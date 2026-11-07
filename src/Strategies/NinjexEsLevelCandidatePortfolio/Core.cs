// NinjexEsLevelCandidatePortfolio.cs
// Research strategy for ES 5-minute charts using CME US Index Futures ETH.
// IMPORTANT: This strategy performs NO timezone conversion. All Time[0] values are assumed to be ET.
//
// Candidate models:
//   1) PDC-SWEEP-RECLAIM-L
//   2) PMH-DELAYED-REJECT-S
//   3) ONH-RECLAIM-L
//   4) RTH-RECLAIM-L  (lower-confidence benchmark candidate)
//
// Initial research risk:
//   - 1 ES contract
//   - Structural stop based on signal-bar extreme, clamped to 8..20 ticks
//   - 2.0R target
//   - Max 3 entries/day
//   - Max 2 winners/day
//   - Max 2 losers/day
//   - Max 1 filled trade/model/day
//   - $500 realized daily loss lockout
//   - 60-minute max hold
//   - 1 tick Strategy Analyzer slippage
//   - No breakeven/trailing logic in research v1, intentionally
//
// Backtest setup:
//   Instrument: ES contract (e.g. ES 12-25)
//   Type: Minute
//   Value: 5
//   Trading hours: CME US Index Futures ETH
//   Calculate: OnBarClose
//   Order fill resolution: Standard
//
// Logs:
//   Documents\NinjaTrader 8\NinjexLogs\EsLevelCandidatePortfolio\<version>\<runId>\events.log
//   Documents\NinjaTrader 8\NinjexLogs\EsLevelCandidatePortfolio\<version>\<runId>\trades.csv
//
// Notes:
//   - "Signal time" parameters refer to the START time of the 5-minute signal candle.
//   - RTH open is captured from the Open of the candle that starts at 09:30 ET.
//   - PMH/PML use 03:00 <= candle start < 09:30 ET.
//   - ONH/ONL use ETH session start (18:00 ET) through < 09:30 ET.
//   - PDC is the last available 5-minute RTH close from the prior session (holiday-safe).

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public enum NinjexInitialStopMode
    {
        SignalBarExtremeCapped,
        PreviousBarExtremeCapped,
        FixedTicks
    }

    public enum NinjexTargetMode
    {
        RewardRisk,
        FixedTicks
    }

    public class NinjexEsLevelCandidatePortfolio : Strategy
    {
        private const string StrategyVersion = "1.0.0-research1";

        private const string SigPdc = "PDC-SWEEP-RECLAIM-L";
        private const string SigPmh = "PMH-DELAYED-REJECT-S";
        private const string SigOnh = "ONH-RECLAIM-L";
        private const string SigRth = "RTH-RECLAIM-L";

        private sealed class CandidateSignal
        {
            public string Id;
            public MarketPosition Direction;
            public int Priority;
            public double Level;
            public string LevelName;
            public int StopTicks;
            public int TargetTicks;
            public string Reason;
            public DateTime SignalBarStart;
            public DateTime SignalBarEnd;
            public double SignalOpen;
            public double SignalHigh;
            public double SignalLow;
            public double SignalClose;
            public double AtrTicks;
            public double Pdc;
            public double Onh;
            public double Onl;
            public double Pmh;
            public double Pml;
            public double RthOpen;
            public double RthVsPdcTicks;
            public double PmhVsOnhTicks;
        }

        private sealed class ActiveTradeContext
        {
            public CandidateSignal Candidate;
            public DateTime EntryTime;
            public double EntryPrice;
            public int EntryQuantity;
            public double RealizedGross;
        }

        private sealed class ModelStats
        {
            public int Qualified;
            public int Taken;
            public int Skipped;
            public int Wins;
            public int Losses;
            public int Flats;
            public double GrossPnl;
        }

        private ATR atr;

        // Reference levels
        private double lastRthClose;
        private DateTime lastRthCloseDate;
        private double currentRthCloseCandidate;
        private DateTime currentRthCloseCandidateDate;
        private double pdc;
        private double onh;
        private double onl;
        private double pmh;
        private double pml;
        private double rthOpen;
        private bool levelsReady;

        // State / validation
        private bool configurationValid;
        private bool rejectionLockout;
        private DateTime currentTradeDate;

        // Daily counters
        private int tradesToday;
        private int winnersToday;
        private int losersToday;
        private double dailyRealizedGross;
        private readonly Dictionary<string, int> modelTradesToday = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Pending / active order state
        private bool entryPending;
        private CandidateSignal pendingCandidate;
        private ActiveTradeContext activeTrade;

        // Cached last bar for thread-safe-ish logging from order callbacks
        private DateTime lastBarTime;
        private double lastBarOpen;
        private double lastBarHigh;
        private double lastBarLow;
        private double lastBarClose;

        // Run-level stats
        private readonly Dictionary<string, ModelStats> stats = new Dictionary<string, ModelStats>(StringComparer.OrdinalIgnoreCase);
        private int runTrades;
        private int runWins;
        private int runLosses;
        private int runFlats;
        private double runGrossPnl;

        // Logging
        private readonly object logSync = new object();
        private StreamWriter eventWriter;
        private StreamWriter tradeWriter;
        private string runId;
        private string runDirectory;
        private bool logFailurePrinted;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Research portfolio for ES 5-minute PDC/PMH/ONH/RTH level candidates.";
                Name = "NinjexEsLevelCandidatePortfolio";
                Calculate = Calculate.OnBarClose;

                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = false;
                ExitOnSessionCloseSeconds = 30;
                IsFillLimitOnTouch = false;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution = OrderFillResolution.Standard;
                Slippage = 1;
                StartBehavior = StartBehavior.WaitUntilFlat;
                TimeInForce = TimeInForce.Gtc;
                TraceOrders = false;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                SetOrderQuantity = SetOrderQuantity.Strategy;
                BarsRequiredToTrade = 50;
                IsInstantiatedOnEachOptimizationIteration = true;

                // Models: all ON by default
                EnablePdcSweepReclaimLong = true;
                EnablePmhDelayedRejectShort = true;
                EnableOnhReclaimLong = true;
                EnableRthReclaimLong = true;

                // Signal windows: START time of the 5-minute signal candle
                PdcStartTime = 103000;
                PmhStartTime = 103000;
                PmhOnhConfluenceStartTime = 110000;
                OnhStartTime = 100000;
                RthStartTime = 100000;
                LastEntryTime = 151500;

                // Signal precision
                RequiredBreakTicks = 1;
                PmhOnhConfluenceToleranceTicks = 1;

                // Initial risk
                Contracts = 1;
                StopMode = NinjexInitialStopMode.SignalBarExtremeCapped;
                StopBufferTicks = 1;
                MinStopTicks = 8;
                MaxStopTicks = 20;
                FixedStopTicks = 16;

                TargetMode = NinjexTargetMode.RewardRisk;
                RewardRisk = 2.0;
                FixedProfitTicks = 40;

                MaxHoldMinutes = 60;
                FlattenAtBarCloseTime = 155500;

                // Daily risk / frequency
                MaxTradesPerDay = 3;
                MaxWinnersPerDay = 2;
                MaxLosersPerDay = 2;
                MaxTradesPerModelPerDay = 1;
                DailyLossLimitCurrency = 500.0;

                // Research telemetry
                AtrPeriod = 14;
                EnableFileLogging = true;
                PrintImportantEvents = true;
            }
            else if (State == State.Configure)
            {
                ResetRuntimeState();
            }
            else if (State == State.DataLoaded)
            {
                atr = ATR(AtrPeriod);
                ValidateConfiguration();
                InitializeLogging();

                LogEvent(TimeOrNow(), "RUN_START", "",
                    string.Format(CultureInfo.InvariantCulture,
                        "Version={0}; Instrument={1}; Bars={2} {3}; TradingHours={4}; Calculate={5}; Fill={6}; SlippageTicks={7}; " +
                        "Models[PDC={8},PMH={9},ONH={10},RTH={11}]; StopMode={12}; MinStop={13}; MaxStop={14}; FixedStop={15}; " +
                        "TargetMode={16}; RR={17:0.00}; FixedTarget={18}; MaxHoldMin={19}; MaxTradesDay={20}; MaxWinsDay={21}; " +
                        "MaxLossesDay={22}; MaxTradesModelDay={23}; DailyLossLimit={24:0.00}",
                        StrategyVersion,
                        Instrument != null ? Instrument.FullName : "n/a",
                        BarsPeriod != null ? BarsPeriod.Value : 0,
                        BarsPeriod != null ? BarsPeriod.BarsPeriodType.ToString() : "n/a",
                        TradingHours != null ? TradingHours.Name : "n/a",
                        Calculate,
                        OrderFillResolution,
                        Slippage,
                        EnablePdcSweepReclaimLong,
                        EnablePmhDelayedRejectShort,
                        EnableOnhReclaimLong,
                        EnableRthReclaimLong,
                        StopMode,
                        MinStopTicks,
                        MaxStopTicks,
                        FixedStopTicks,
                        TargetMode,
                        RewardRisk,
                        FixedProfitTicks,
                        MaxHoldMinutes,
                        MaxTradesPerDay,
                        MaxWinnersPerDay,
                        MaxLosersPerDay,
                        MaxTradesPerModelPerDay,
                        DailyLossLimitCurrency));

                if (!configurationValid)
                    LogEvent(TimeOrNow(), "FATAL_CONFIG", "", "Strategy will not submit entries until configuration is corrected.");
            }
            else if (State == State.Terminated)
            {
                try
                {
                    LogDailySummary(TimeOrNow(), "TERMINATED");
                    LogRunSummary(TimeOrNow());
                }
                catch
                {
                    // Never allow logging cleanup to throw during termination.
                }

                CloseLogging();
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            if (CurrentBar < BarsRequiredToTrade)
                return;

            CacheLastBar();

            if (!configurationValid)
                return;

            DateTime signalBarStart = Time[0].AddMinutes(-BarsPeriod.Value);
            int signalStartTime = ToTime(signalBarStart);
            int barCloseTime = ToTime(Time[0]);

            HandleCalendarDateReset(signalBarStart.Date);
            UpdateReferenceLevels(signalBarStart, signalStartTime);

            // Risk-controlled exits first.
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                if (activeTrade != null &&
                    MaxHoldMinutes > 0 &&
                    Time[0] >= activeTrade.EntryTime.AddMinutes(MaxHoldMinutes))
                {
                    SubmitManagedExit("TIME-EXIT");
                    return;
                }

                if (barCloseTime >= FlattenAtBarCloseTime)
                {
                    SubmitManagedExit("DAY-FLATTEN");
                    return;
                }

                return;
            }

            if (entryPending)
                return;

            // Never open a new trade if the current 5-minute candle would complete at/after the manual flatten.
            if (barCloseTime >= FlattenAtBarCloseTime)
                return;

            if (!levelsReady)
                return;

            List<CandidateSignal> candidates = EvaluateCandidates(signalBarStart, signalStartTime);
            if (candidates.Count == 0)
                return;

            // Log all qualified candidates, even if portfolio arbitration means only one can be traded.
            foreach (CandidateSignal c in candidates)
            {
                GetStats(c.Id).Qualified++;
                LogEvent(Time[0], "SIGNAL_QUALIFIED", c.Id, FormatCandidate(c));
            }

            string gateReason;
            if (!CanSubmitAnyEntry(out gateReason))
            {
                foreach (CandidateSignal c in candidates)
                {
                    GetStats(c.Id).Skipped++;
                    LogEvent(Time[0], "SIGNAL_SKIPPED", c.Id, "PortfolioGate=" + gateReason + "; " + FormatCandidate(c));
                }
                return;
            }

            CandidateSignal chosen = null;
            foreach (CandidateSignal c in candidates.OrderBy(x => x.Priority))
            {
                int modelCount = GetModelTradesToday(c.Id);
                if (modelCount >= MaxTradesPerModelPerDay)
                {
                    GetStats(c.Id).Skipped++;
                    LogEvent(Time[0], "SIGNAL_SKIPPED", c.Id,
                        "ModelDailyLimit; ModelTradesToday=" + modelCount + "; " + FormatCandidate(c));
                    continue;
                }

                if (chosen == null)
                    chosen = c;
                else
                {
                    GetStats(c.Id).Skipped++;
                    LogEvent(Time[0], "SIGNAL_SKIPPED", c.Id,
                        "PortfolioArbitration; Chosen=" + chosen.Id + "; " + FormatCandidate(c));
                }
            }

            if (chosen == null)
                return;

            SubmitCandidate(chosen);
        }

        private void ResetRuntimeState()
        {
            lastRthClose = double.NaN;
            lastRthCloseDate = DateTime.MinValue;
            currentRthCloseCandidate = double.NaN;
            currentRthCloseCandidateDate = DateTime.MinValue;
            pdc = double.NaN;
            onh = double.NaN;
            onl = double.NaN;
            pmh = double.NaN;
            pml = double.NaN;
            rthOpen = double.NaN;
            levelsReady = false;

            configurationValid = false;
            rejectionLockout = false;
            currentTradeDate = DateTime.MinValue;

            tradesToday = 0;
            winnersToday = 0;
            losersToday = 0;
            dailyRealizedGross = 0;
            modelTradesToday.Clear();

            entryPending = false;
            pendingCandidate = null;
            activeTrade = null;

            lastBarTime = DateTime.MinValue;
            lastBarOpen = lastBarHigh = lastBarLow = lastBarClose = double.NaN;

            runTrades = runWins = runLosses = runFlats = 0;
            runGrossPnl = 0;
            stats.Clear();

            EnsureModelDictionaries();
        }

        private void EnsureModelDictionaries()
        {
            string[] ids = { SigPdc, SigPmh, SigOnh, SigRth };
            foreach (string id in ids)
            {
                if (!modelTradesToday.ContainsKey(id))
                    modelTradesToday[id] = 0;
                if (!stats.ContainsKey(id))
                    stats[id] = new ModelStats();
            }
        }

        private void ValidateConfiguration()
        {
            bool barsOk = BarsPeriod != null &&
                          BarsPeriod.BarsPeriodType == BarsPeriodType.Minute &&
                          BarsPeriod.Value == 5;

            bool tradingHoursOk = TradingHours != null &&
                                  string.Equals(TradingHours.Name, "CME US Index Futures ETH", StringComparison.OrdinalIgnoreCase);

            bool instrumentOk = Instrument != null &&
                                Instrument.MasterInstrument != null &&
                                string.Equals(Instrument.MasterInstrument.Name, "ES", StringComparison.OrdinalIgnoreCase);

            configurationValid = barsOk && tradingHoursOk && instrumentOk;

            if (!barsOk)
                Print(Name + ": ERROR - Primary series must be 5 Minute.");

            if (!tradingHoursOk)
                Print(Name + ": ERROR - Trading Hours must be 'CME US Index Futures ETH'.");

            if (!instrumentOk)
                Print(Name + ": ERROR - This research strategy is intended for ES only.");

            // No timezone conversion by design.
            Print(Name + ": Uses Time[0] directly with NO timezone conversion. Ensure NinjaTrader chart timestamps are ET.");
        }

        private void HandleCalendarDateReset(DateTime date)
        {
            if (currentTradeDate == DateTime.MinValue)
            {
                currentTradeDate = date;
                ResetDailyCountersOnly();
                return;
            }

            if (date == currentTradeDate)
                return;

            LogDailySummary(lastBarTime, "DATE_CHANGE");
            currentTradeDate = date;
            ResetDailyCountersOnly();
        }

        private void ResetDailyCountersOnly()
        {
            tradesToday = 0;
            winnersToday = 0;
            losersToday = 0;
            dailyRealizedGross = 0;
            rejectionLockout = false;

            modelTradesToday.Clear();
            modelTradesToday[SigPdc] = 0;
            modelTradesToday[SigPmh] = 0;
            modelTradesToday[SigOnh] = 0;
            modelTradesToday[SigRth] = 0;
        }

        private void UpdateReferenceLevels(DateTime signalBarStart, int signalStartTime)
        {
            // Track the most recent available RTH 5-minute close. This is deliberately not
            // hard-coded to 15:55-16:00 only, so partial-holiday / early-close sessions still
            // produce the correct prior RTH close.
            if (signalStartTime >= 93000 && signalStartTime < 160000)
            {
                currentRthCloseCandidate = Close[0];
                currentRthCloseCandidateDate = signalBarStart.Date;
            }

            // ETH session first bar should start at ~18:00 ET under the required template.
            // Finalize the prior RTH close before beginning the next overnight session.
            if (Bars.IsFirstBarOfSession)
            {
                if (IsFinite(currentRthCloseCandidate))
                {
                    lastRthClose = currentRthCloseCandidate;
                    lastRthCloseDate = currentRthCloseCandidateDate;

                    LogEvent(Time[0], "RTH_CLOSE_CAPTURED", "",
                        "Close=" + Fmt(lastRthClose) + "; SourceDate=" + lastRthCloseDate.ToString("yyyy-MM-dd"));
                }

                currentRthCloseCandidate = double.NaN;
                currentRthCloseCandidateDate = DateTime.MinValue;

                pdc = lastRthClose;
                onh = High[0];
                onl = Low[0];
                pmh = double.NaN;
                pml = double.NaN;
                rthOpen = double.NaN;
                levelsReady = false;

                LogEvent(Time[0], "ETH_SESSION_START", "",
                    string.Format(CultureInfo.InvariantCulture,
                        "SignalBarStart={0:yyyy-MM-dd HH:mm:ss}; CarriedPDC={1}; PDCSourceDate={2:yyyy-MM-dd}",
                        signalBarStart, Fmt(pdc), lastRthCloseDate));
            }

            // Overnight: session open through 09:30 ET.
            // Using signal candle START time makes the 09:25-09:30 bar part of the overnight range.
            bool inOvernight = signalStartTime >= 180000 || signalStartTime < 93000;
            if (inOvernight)
            {
                onh = double.IsNaN(onh) ? High[0] : Math.Max(onh, High[0]);
                onl = double.IsNaN(onl) ? Low[0] : Math.Min(onl, Low[0]);
            }

            // Premarket: 03:00 <= bar start < 09:30 ET.
            if (signalStartTime >= 30000 && signalStartTime < 93000)
            {
                pmh = double.IsNaN(pmh) ? High[0] : Math.Max(pmh, High[0]);
                pml = double.IsNaN(pml) ? Low[0] : Math.Min(pml, Low[0]);
            }

            // First RTH candle starts at 09:30 ET.
            if (signalStartTime == 93000)
            {
                rthOpen = Open[0];
                levelsReady = IsFinite(pdc) &&
                              IsFinite(onh) &&
                              IsFinite(onl) &&
                              IsFinite(pmh) &&
                              IsFinite(pml) &&
                              IsFinite(rthOpen);

                LogEvent(Time[0], "LEVELS_READY", "",
                    string.Format(CultureInfo.InvariantCulture,
                        "Ready={0}; PDC={1}; ONH={2}; ONL={3}; PMH={4}; PML={5}; RTHOpen={6}; " +
                        "RTHvsPDCticks={7}; PMHvsONHticks={8}",
                        levelsReady,
                        Fmt(pdc), Fmt(onh), Fmt(onl), Fmt(pmh), Fmt(pml), Fmt(rthOpen),
                        FmtTicks(rthOpen, pdc),
                        FmtTicks(pmh, onh)));
            }

        }

        private List<CandidateSignal> EvaluateCandidates(DateTime signalBarStart, int signalStartTime)
        {
            List<CandidateSignal> result = new List<CandidateSignal>(4);

            if (signalStartTime > LastEntryTime)
                return result;

            double breakDistance = RequiredBreakTicks * TickSize;

            // 1) PDC late sweep + reclaim LONG
            // Prior candle is at/above PDC; current candle sweeps below and closes back above.
            if (EnablePdcSweepReclaimLong &&
                signalStartTime >= PdcStartTime &&
                Close[1] >= pdc &&
                Low[0] <= pdc - breakDistance &&
                Close[0] >= pdc + breakDistance)
            {
                result.Add(BuildCandidate(
                    SigPdc,
                    MarketPosition.Long,
                    1,
                    pdc,
                    "PDC",
                    signalBarStart,
                    "PrevClose>=PDC; Low swept below PDC; 5m close reclaimed above PDC"));
            }

            // 2) PMH delayed rejection SHORT
            // If PMH and ONH are effectively the same level, wait until the later confluence start.
            bool pmhOnhConfluence = Math.Abs(pmh - onh) <= PmhOnhConfluenceToleranceTicks * TickSize;
            int effectivePmhStart = pmhOnhConfluence ? PmhOnhConfluenceStartTime : PmhStartTime;

            if (EnablePmhDelayedRejectShort &&
                signalStartTime >= effectivePmhStart &&
                High[0] >= pmh + breakDistance &&
                Close[0] <= pmh - breakDistance)
            {
                result.Add(BuildCandidate(
                    SigPmh,
                    MarketPosition.Short,
                    2,
                    pmh,
                    "PMH",
                    signalBarStart,
                    "High probed above PMH; 5m close rejected below PMH; PMH_ONH_Confluence=" + pmhOnhConfluence));
            }

            // 3) ONH reclaim LONG
            // Candle trades below ONH but closes back above it.
            if (EnableOnhReclaimLong &&
                signalStartTime >= OnhStartTime &&
                Low[0] <= onh - breakDistance &&
                Close[0] >= onh + breakDistance)
            {
                result.Add(BuildCandidate(
                    SigOnh,
                    MarketPosition.Long,
                    3,
                    onh,
                    "ONH",
                    signalBarStart,
                    "5m candle traded below ONH and closed back above ONH"));
            }

            // 4) RTH open reclaim LONG - lower-confidence benchmark.
            // Require prior close at/below RTH open to make this an actual reclaim from below.
            if (EnableRthReclaimLong &&
                signalStartTime >= RthStartTime &&
                Close[1] <= rthOpen &&
                Low[0] <= rthOpen - breakDistance &&
                Close[0] >= rthOpen + breakDistance)
            {
                result.Add(BuildCandidate(
                    SigRth,
                    MarketPosition.Long,
                    4,
                    rthOpen,
                    "RTHOPEN",
                    signalBarStart,
                    "PrevClose<=RTHOpen; 5m candle reclaimed and closed above RTH open"));
            }

            return result;
        }

        private CandidateSignal BuildCandidate(
            string id,
            MarketPosition direction,
            int priority,
            double level,
            string levelName,
            DateTime signalBarStart,
            string reason)
        {
            int stopTicks = CalculateInitialStopTicks(direction);
            int targetTicks = CalculateTargetTicks(stopTicks);

            double atrTicks = atr != null && CurrentBar >= AtrPeriod && TickSize > 0
                ? atr[0] / TickSize
                : double.NaN;

            return new CandidateSignal
            {
                Id = id,
                Direction = direction,
                Priority = priority,
                Level = level,
                LevelName = levelName,
                StopTicks = stopTicks,
                TargetTicks = targetTicks,
                Reason = reason,
                SignalBarStart = signalBarStart,
                SignalBarEnd = Time[0],
                SignalOpen = Open[0],
                SignalHigh = High[0],
                SignalLow = Low[0],
                SignalClose = Close[0],
                AtrTicks = atrTicks,
                Pdc = pdc,
                Onh = onh,
                Onl = onl,
                Pmh = pmh,
                Pml = pml,
                RthOpen = rthOpen,
                RthVsPdcTicks = PriceDiffTicks(rthOpen, pdc),
                PmhVsOnhTicks = PriceDiffTicks(pmh, onh)
            };
        }

        private int CalculateInitialStopTicks(MarketPosition direction)
        {
            if (StopMode == NinjexInitialStopMode.FixedTicks)
                return ClampTicks(FixedStopTicks);

            int barsAgo = StopMode == NinjexInitialStopMode.PreviousBarExtremeCapped ? 1 : 0;
            double referenceClose = Close[0];
            double rawTicks;

            if (direction == MarketPosition.Long)
            {
                double structuralStop = Low[barsAgo] - StopBufferTicks * TickSize;
                rawTicks = (referenceClose - structuralStop) / TickSize;
            }
            else
            {
                double structuralStop = High[barsAgo] + StopBufferTicks * TickSize;
                rawTicks = (structuralStop - referenceClose) / TickSize;
            }

            int ticks = (int)Math.Ceiling(Math.Max(1.0, rawTicks));
            return ClampTicks(ticks);
        }

        private int CalculateTargetTicks(int stopTicks)
        {
            if (TargetMode == NinjexTargetMode.FixedTicks)
                return Math.Max(1, FixedProfitTicks);

            return Math.Max(1, (int)Math.Round(stopTicks * RewardRisk, MidpointRounding.AwayFromZero));
        }

        private int ClampTicks(int ticks)
        {
            int min = Math.Max(1, MinStopTicks);
            int max = Math.Max(min, MaxStopTicks);
            return Math.Max(min, Math.Min(max, ticks));
        }

        private bool CanSubmitAnyEntry(out string reason)
        {
            if (rejectionLockout)
            {
                reason = "OrderRejectionLockout";
                return false;
            }

            if (entryPending)
            {
                reason = "EntryPending";
                return false;
            }

            if (Position.MarketPosition != MarketPosition.Flat)
            {
                reason = "PositionNotFlat";
                return false;
            }

            if (tradesToday >= MaxTradesPerDay)
            {
                reason = "MaxTradesPerDay";
                return false;
            }

            if (winnersToday >= MaxWinnersPerDay)
            {
                reason = "MaxWinnersPerDay";
                return false;
            }

            if (losersToday >= MaxLosersPerDay)
            {
                reason = "MaxLosersPerDay";
                return false;
            }

            if (dailyRealizedGross <= -Math.Abs(DailyLossLimitCurrency))
            {
                reason = "DailyLossLimit";
                return false;
            }

            reason = "";
            return true;
        }

        private void SubmitCandidate(CandidateSignal c)
        {
            // Set() methods MUST be called before the entry so protective orders are generated on entry execution.
            SetStopLoss(c.Id, CalculationMode.Ticks, c.StopTicks, false);
            SetProfitTarget(c.Id, CalculationMode.Ticks, c.TargetTicks);

            entryPending = true;
            pendingCandidate = c;

            LogEvent(Time[0], "ENTRY_SUBMIT", c.Id,
                string.Format(CultureInfo.InvariantCulture,
                    "Direction={0}; Qty={1}; StopTicks={2}; TargetTicks={3}; ApproxRiskPerContract=${4:0.00}; " +
                    "ApproxTargetPerContract=${5:0.00}; {6}",
                    c.Direction,
                    Contracts,
                    c.StopTicks,
                    c.TargetTicks,
                    c.StopTicks * TickSize * Instrument.MasterInstrument.PointValue,
                    c.TargetTicks * TickSize * Instrument.MasterInstrument.PointValue,
                    FormatCandidate(c)));

            if (PrintImportantEvents)
            {
                Print(string.Format(CultureInfo.InvariantCulture,
                    "{0:yyyy-MM-dd HH:mm:ss} {1} SUBMIT {2} {3} qty={4} stop={5}t target={6}t",
                    Time[0], Name, c.Id, c.Direction, Contracts, c.StopTicks, c.TargetTicks));
            }

            if (c.Direction == MarketPosition.Long)
                EnterLong(Contracts, c.Id);
            else
                EnterShort(Contracts, c.Id);
        }

        private void SubmitManagedExit(string exitSignal)
        {
            if (Position.MarketPosition == MarketPosition.Flat || activeTrade == null)
                return;

            LogEvent(Time[0], "EXIT_SUBMIT", activeTrade.Candidate.Id,
                "ExitSignal=" + exitSignal +
                "; Position=" + Position.MarketPosition +
                "; Qty=" + Position.Quantity +
                "; AvgPrice=" + Fmt(Position.AveragePrice));

            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong(exitSignal, activeTrade.Candidate.Id);
            else if (Position.MarketPosition == MarketPosition.Short)
                ExitShort(exitSignal, activeTrade.Candidate.Id);
        }

        protected override void OnOrderUpdate(
            Order order,
            double limitPrice,
            double stopPrice,
            int quantity,
            int filled,
            double averageFillPrice,
            OrderState orderState,
            DateTime time,
            ErrorCode error,
            string nativeError)
        {
            if (order == null)
                return;

            string model = IsEntrySignal(order.Name)
                ? order.Name
                : (!string.IsNullOrWhiteSpace(order.FromEntrySignal) ? order.FromEntrySignal : ActiveModelId());

            LogEvent(time, "ORDER_UPDATE", model,
                string.Format(CultureInfo.InvariantCulture,
                    "Name={0}; FromEntry={1}; Action={2}; Type={3}; State={4}; Qty={5}; Filled={6}; AvgFill={7}; " +
                    "Limit={8}; Stop={9}; OrderId={10}; OCO={11}; Error={12}; NativeError={13}",
                    Safe(order.Name),
                    Safe(order.FromEntrySignal),
                    order.OrderAction,
                    order.OrderType,
                    orderState,
                    quantity,
                    filled,
                    Fmt(averageFillPrice),
                    Fmt(limitPrice),
                    Fmt(stopPrice),
                    Safe(order.OrderId),
                    Safe(order.Oco),
                    error,
                    Safe(nativeError)));

            if (orderState == OrderState.Rejected)
            {
                rejectionLockout = true;

                if (IsEntrySignal(order.Name))
                {
                    entryPending = false;
                    pendingCandidate = null;
                }

                if (PrintImportantEvents)
                    Print(time + " " + Name + " ORDER REJECTED: " + order.Name + " / " + nativeError);
            }
            else if (IsEntrySignal(order.Name) &&
                     (orderState == OrderState.Cancelled) &&
                     filled == 0)
            {
                entryPending = false;
                pendingCandidate = null;
            }
        }

        protected override void OnExecutionUpdate(
            Execution execution,
            string executionId,
            double price,
            int quantity,
            MarketPosition marketPosition,
            string orderId,
            DateTime time)
        {
            if (execution == null || execution.Order == null)
                return;

            Order order = execution.Order;
            string orderName = order.Name ?? "";
            string model = IsEntrySignal(orderName)
                ? orderName
                : (!string.IsNullOrWhiteSpace(order.FromEntrySignal) ? order.FromEntrySignal : ActiveModelId());

            LogEvent(time, "EXECUTION", model,
                string.Format(CultureInfo.InvariantCulture,
                    "ExecutionId={0}; OrderId={1}; OrderName={2}; FromEntry={3}; Price={4}; Qty={5}; " +
                    "MarketPositionAfter={6}; OrderState={7}",
                    Safe(executionId),
                    Safe(orderId),
                    Safe(orderName),
                    Safe(order.FromEntrySignal),
                    Fmt(price),
                    quantity,
                    marketPosition,
                    order.OrderState));

            if (IsEntrySignal(orderName))
            {
                HandleEntryExecution(orderName, price, quantity, time);
                return;
            }

            if (activeTrade != null && quantity > 0)
                HandleExitExecution(orderName, price, quantity, time);
        }

        private void HandleEntryExecution(string signal, double price, int quantity, DateTime time)
        {
            CandidateSignal candidate = pendingCandidate;

            // Defensive fallback: should only occur if broker/event sequencing is unusual.
            if (candidate == null || !string.Equals(candidate.Id, signal, StringComparison.OrdinalIgnoreCase))
            {
                LogEvent(time, "WARNING", signal,
                    "Entry execution arrived without matching pending CandidateSignal. Building minimal context.");
                candidate = BuildFallbackCandidate(signal, time);
            }

            if (activeTrade == null)
            {
                activeTrade = new ActiveTradeContext
                {
                    Candidate = candidate,
                    EntryTime = time,
                    EntryPrice = price,
                    EntryQuantity = quantity,
                    RealizedGross = 0
                };

                tradesToday++;
                runTrades++;
                IncrementModelTradesToday(signal);
                GetStats(signal).Taken++;
            }
            else
            {
                int oldQty = activeTrade.EntryQuantity;
                int newQty = oldQty + quantity;
                if (newQty > 0)
                    activeTrade.EntryPrice = ((activeTrade.EntryPrice * oldQty) + (price * quantity)) / newQty;
                activeTrade.EntryQuantity = newQty;
            }

            entryPending = false;
            pendingCandidate = null;

            LogEvent(time, "ENTRY_FILLED", signal,
                string.Format(CultureInfo.InvariantCulture,
                    "FillPrice={0}; FillQty={1}; AvgEntry={2}; TotalEntryQty={3}; TradesToday={4}; ModelTradesToday={5}; " +
                    "StopTicks={6}; TargetTicks={7}",
                    Fmt(price),
                    quantity,
                    Fmt(activeTrade.EntryPrice),
                    activeTrade.EntryQuantity,
                    tradesToday,
                    GetModelTradesToday(signal),
                    activeTrade.Candidate.StopTicks,
                    activeTrade.Candidate.TargetTicks));
        }

        private void HandleExitExecution(string orderName, double exitPrice, int quantity, DateTime time)
        {
            if (activeTrade == null)
                return;

            double pnlPerPoint = Instrument.MasterInstrument.PointValue;
            double gross;

            if (activeTrade.Candidate.Direction == MarketPosition.Long)
                gross = (exitPrice - activeTrade.EntryPrice) * pnlPerPoint * quantity;
            else
                gross = (activeTrade.EntryPrice - exitPrice) * pnlPerPoint * quantity;

            activeTrade.RealizedGross += gross;

            LogEvent(time, "EXIT_EXECUTION", activeTrade.Candidate.Id,
                string.Format(CultureInfo.InvariantCulture,
                    "ExitOrder={0}; ExitPrice={1}; Qty={2}; ExecutionGrossPnL={3:0.00}; TradeGrossPnLSoFar={4:0.00}; PositionAfter={5}",
                    Safe(orderName),
                    Fmt(exitPrice),
                    quantity,
                    gross,
                    activeTrade.RealizedGross,
                    Position.MarketPosition));

            // Position is driven by executions and is updated by OnExecutionUpdate().
            if (Position.MarketPosition == MarketPosition.Flat)
                FinalizeTrade(orderName, exitPrice, time);
        }

        private void FinalizeTrade(string exitOrderName, double exitPrice, DateTime exitTime)
        {
            if (activeTrade == null)
                return;

            ActiveTradeContext closed = activeTrade;
            double pnl = closed.RealizedGross;
            string outcome;

            if (pnl > 0.0000001)
            {
                outcome = "WIN";
                winnersToday++;
                runWins++;
                GetStats(closed.Candidate.Id).Wins++;
            }
            else if (pnl < -0.0000001)
            {
                outcome = "LOSS";
                losersToday++;
                runLosses++;
                GetStats(closed.Candidate.Id).Losses++;
            }
            else
            {
                outcome = "FLAT";
                runFlats++;
                GetStats(closed.Candidate.Id).Flats++;
            }

            dailyRealizedGross += pnl;
            runGrossPnl += pnl;
            GetStats(closed.Candidate.Id).GrossPnl += pnl;

            string exitReason = NormalizeExitReason(exitOrderName);

            LogEvent(exitTime, "TRADE_CLOSED", closed.Candidate.Id,
                string.Format(CultureInfo.InvariantCulture,
                    "Outcome={0}; ExitReason={1}; Direction={2}; EntryTime={3:yyyy-MM-dd HH:mm:ss}; Entry={4}; " +
                    "ExitTime={5:yyyy-MM-dd HH:mm:ss}; Exit={6}; Qty={7}; GrossPnL={8:0.00}; HoldMin={9:0.0}; " +
                    "DailyPnL={10:0.00}; TradesToday={11}; WinnersToday={12}; LosersToday={13}",
                    outcome,
                    exitReason,
                    closed.Candidate.Direction,
                    closed.EntryTime,
                    Fmt(closed.EntryPrice),
                    exitTime,
                    Fmt(exitPrice),
                    closed.EntryQuantity,
                    pnl,
                    (exitTime - closed.EntryTime).TotalMinutes,
                    dailyRealizedGross,
                    tradesToday,
                    winnersToday,
                    losersToday));

            WriteTradeCsv(closed, exitTime, exitPrice, exitReason, outcome, pnl);

            if (PrintImportantEvents)
            {
                Print(string.Format(CultureInfo.InvariantCulture,
                    "{0:yyyy-MM-dd HH:mm:ss} {1} CLOSED {2} {3} gross=${4:0.00} daily=${5:0.00}",
                    exitTime, Name, closed.Candidate.Id, outcome, pnl, dailyRealizedGross));
            }

            activeTrade = null;
        }

        private CandidateSignal BuildFallbackCandidate(string signal, DateTime time)
        {
            MarketPosition direction = signal == SigPmh ? MarketPosition.Short : MarketPosition.Long;
            double level = signal == SigPdc ? pdc :
                           signal == SigPmh ? pmh :
                           signal == SigOnh ? onh : rthOpen;

            int stopTicks = ClampTicks(FixedStopTicks);

            return new CandidateSignal
            {
                Id = signal,
                Direction = direction,
                Priority = 99,
                Level = level,
                LevelName = "UNKNOWN",
                StopTicks = stopTicks,
                TargetTicks = CalculateTargetTicks(stopTicks),
                Reason = "Fallback candidate built from execution callback",
                SignalBarStart = time,
                SignalBarEnd = time,
                SignalOpen = lastBarOpen,
                SignalHigh = lastBarHigh,
                SignalLow = lastBarLow,
                SignalClose = lastBarClose,
                AtrTicks = double.NaN,
                Pdc = pdc,
                Onh = onh,
                Onl = onl,
                Pmh = pmh,
                Pml = pml,
                RthOpen = rthOpen,
                RthVsPdcTicks = PriceDiffTicks(rthOpen, pdc),
                PmhVsOnhTicks = PriceDiffTicks(pmh, onh)
            };
        }

        private string NormalizeExitReason(string orderName)
        {
            if (string.IsNullOrWhiteSpace(orderName))
                return "UNKNOWN";

            if (orderName.IndexOf("Profit target", StringComparison.OrdinalIgnoreCase) >= 0)
                return "PROFIT_TARGET";

            if (orderName.IndexOf("Stop loss", StringComparison.OrdinalIgnoreCase) >= 0)
                return "STOP_LOSS";

            if (orderName.IndexOf("TIME-EXIT", StringComparison.OrdinalIgnoreCase) >= 0)
                return "TIME_EXIT";

            if (orderName.IndexOf("DAY-FLATTEN", StringComparison.OrdinalIgnoreCase) >= 0)
                return "DAY_FLATTEN";

            return orderName.Replace(',', '_');
        }

        private bool IsEntrySignal(string name)
        {
            return string.Equals(name, SigPdc, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, SigPmh, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, SigOnh, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, SigRth, StringComparison.OrdinalIgnoreCase);
        }

        private string ActiveModelId()
        {
            if (activeTrade != null && activeTrade.Candidate != null)
                return activeTrade.Candidate.Id;

            if (pendingCandidate != null)
                return pendingCandidate.Id;

            return "";
        }

        private int GetModelTradesToday(string id)
        {
            int value;
            return modelTradesToday.TryGetValue(id, out value) ? value : 0;
        }

        private void IncrementModelTradesToday(string id)
        {
            modelTradesToday[id] = GetModelTradesToday(id) + 1;
        }

        private ModelStats GetStats(string id)
        {
            ModelStats s;
            if (!stats.TryGetValue(id, out s))
            {
                s = new ModelStats();
                stats[id] = s;
            }
            return s;
        }

        private void CacheLastBar()
        {
            lastBarTime = Time[0];
            lastBarOpen = Open[0];
            lastBarHigh = High[0];
            lastBarLow = Low[0];
            lastBarClose = Close[0];
        }

        private double PriceDiffTicks(double a, double b)
        {
            if (!IsFinite(a) || !IsFinite(b) || TickSize <= 0)
                return double.NaN;

            return (a - b) / TickSize;
        }

        private bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private string FormatCandidate(CandidateSignal c)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "Direction={0}; LevelName={1}; Level={2}; SignalStart={3:yyyy-MM-dd HH:mm:ss}; SignalEnd={4:yyyy-MM-dd HH:mm:ss}; " +
                "O={5}; H={6}; L={7}; C={8}; ATRticks={9}; StopTicks={10}; TargetTicks={11}; " +
                "PDC={12}; ONH={13}; ONL={14}; PMH={15}; PML={16}; RTHOpen={17}; RTHvsPDCticks={18}; PMHvsONHticks={19}; Reason={20}",
                c.Direction,
                c.LevelName,
                Fmt(c.Level),
                c.SignalBarStart,
                c.SignalBarEnd,
                Fmt(c.SignalOpen),
                Fmt(c.SignalHigh),
                Fmt(c.SignalLow),
                Fmt(c.SignalClose),
                Fmt(c.AtrTicks),
                c.StopTicks,
                c.TargetTicks,
                Fmt(c.Pdc),
                Fmt(c.Onh),
                Fmt(c.Onl),
                Fmt(c.Pmh),
                Fmt(c.Pml),
                Fmt(c.RthOpen),
                Fmt(c.RthVsPdcTicks),
                Fmt(c.PmhVsOnhTicks),
                c.Reason);
        }

        private string FmtTicks(double a, double b)
        {
            return Fmt(PriceDiffTicks(a, b));
        }

        private string Fmt(double value)
        {
            if (!IsFinite(value))
                return "";

            return value.ToString("0.########", CultureInfo.InvariantCulture);
        }

        private string Safe(string value)
        {
            return string.IsNullOrEmpty(value) ? "" : value.Replace("\r", " ").Replace("\n", " ").Replace("|", "/");
        }

        private DateTime TimeOrNow()
        {
            if (CurrentBar >= 0 && Bars != null && lastBarTime != DateTime.MinValue)
                return lastBarTime;

            return DateTime.Now;
        }

        private void InitializeLogging()
        {
            if (!EnableFileLogging)
                return;

            try
            {
                string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string safeInstrument = MakeSafeFileName(Instrument != null ? Instrument.FullName : "UnknownInstrument");

                runId = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture) +
                        "_" + Guid.NewGuid().ToString("N").Substring(0, 8);

                runDirectory = Path.Combine(
                    documents,
                    "NinjaTrader 8",
                    "NinjexLogs",
                    "EsLevelCandidatePortfolio",
                    StrategyVersion,
                    runId + "_" + safeInstrument);

                Directory.CreateDirectory(runDirectory);

                string eventPath = Path.Combine(runDirectory, "events.log");
                string tradePath = Path.Combine(runDirectory, "trades.csv");

                eventWriter = new StreamWriter(eventPath, false, new UTF8Encoding(false));
                eventWriter.AutoFlush = true;
                eventWriter.WriteLine("Timestamp|State|Event|Model|Instrument|BarTime|O|H|L|C|PDC|ONH|ONL|PMH|PML|RTHOpen|TradesToday|WinsToday|LossesToday|DailyGrossPnL|Position|ActiveSignal|Detail");

                tradeWriter = new StreamWriter(tradePath, false, new UTF8Encoding(false));
                tradeWriter.AutoFlush = true;
                tradeWriter.WriteLine(
                    "Model,Direction,SignalStart,SignalEnd,EntryTime,EntryPrice,ExitTime,ExitPrice,Quantity,Outcome,ExitReason,GrossPnL,HoldMinutes," +
                    "StopTicks,TargetTicks,ATRticks,SignalOpen,SignalHigh,SignalLow,SignalClose,LevelName,Level,PDC,ONH,ONL,PMH,PML,RTHOpen," +
                    "RTHvsPDCticks,PMHvsONHticks,TradesTodayAtClose,WinnersTodayAtClose,LosersTodayAtClose,DailyGrossPnLAtClose,Reason");

                Print(Name + ": Logging to " + runDirectory);
            }
            catch (Exception ex)
            {
                eventWriter = null;
                tradeWriter = null;
                if (!logFailurePrinted)
                {
                    logFailurePrinted = true;
                    Print(Name + ": File logging initialization failed: " + ex.Message);
                }
            }
        }

        private void CloseLogging()
        {
            lock (logSync)
            {
                try
                {
                    if (eventWriter != null)
                    {
                        eventWriter.Flush();
                        eventWriter.Dispose();
                    }
                }
                catch { }

                try
                {
                    if (tradeWriter != null)
                    {
                        tradeWriter.Flush();
                        tradeWriter.Dispose();
                    }
                }
                catch { }

                eventWriter = null;
                tradeWriter = null;
            }
        }

        private void LogEvent(DateTime timestamp, string eventType, string model, string detail)
        {
            if (!EnableFileLogging || eventWriter == null)
                return;

            lock (logSync)
            {
                try
                {
                    eventWriter.WriteLine(string.Join("|", new[]
                    {
                        timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                        State.ToString(),
                        Safe(eventType),
                        Safe(model),
                        Instrument != null ? Safe(Instrument.FullName) : "",
                        lastBarTime == DateTime.MinValue ? "" : lastBarTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                        Fmt(lastBarOpen),
                        Fmt(lastBarHigh),
                        Fmt(lastBarLow),
                        Fmt(lastBarClose),
                        Fmt(pdc),
                        Fmt(onh),
                        Fmt(onl),
                        Fmt(pmh),
                        Fmt(pml),
                        Fmt(rthOpen),
                        tradesToday.ToString(CultureInfo.InvariantCulture),
                        winnersToday.ToString(CultureInfo.InvariantCulture),
                        losersToday.ToString(CultureInfo.InvariantCulture),
                        dailyRealizedGross.ToString("0.00", CultureInfo.InvariantCulture),
                        Position != null ? Position.MarketPosition.ToString() : "",
                        Safe(ActiveModelId()),
                        Safe(detail)
                    }));
                }
                catch (Exception ex)
                {
                    if (!logFailurePrinted)
                    {
                        logFailurePrinted = true;
                        Print(Name + ": File logging failed: " + ex.Message);
                    }
                }
            }
        }

        private void WriteTradeCsv(
            ActiveTradeContext closed,
            DateTime exitTime,
            double exitPrice,
            string exitReason,
            string outcome,
            double pnl)
        {
            if (!EnableFileLogging || tradeWriter == null || closed == null || closed.Candidate == null)
                return;

            CandidateSignal c = closed.Candidate;

            lock (logSync)
            {
                try
                {
                    string[] fields =
                    {
                        Csv(c.Id),
                        Csv(c.Direction.ToString()),
                        Csv(c.SignalBarStart.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                        Csv(c.SignalBarEnd.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                        Csv(closed.EntryTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)),
                        Csv(Fmt(closed.EntryPrice)),
                        Csv(exitTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)),
                        Csv(Fmt(exitPrice)),
                        Csv(closed.EntryQuantity.ToString(CultureInfo.InvariantCulture)),
                        Csv(outcome),
                        Csv(exitReason),
                        Csv(pnl.ToString("0.00", CultureInfo.InvariantCulture)),
                        Csv((exitTime - closed.EntryTime).TotalMinutes.ToString("0.0", CultureInfo.InvariantCulture)),
                        Csv(c.StopTicks.ToString(CultureInfo.InvariantCulture)),
                        Csv(c.TargetTicks.ToString(CultureInfo.InvariantCulture)),
                        Csv(Fmt(c.AtrTicks)),
                        Csv(Fmt(c.SignalOpen)),
                        Csv(Fmt(c.SignalHigh)),
                        Csv(Fmt(c.SignalLow)),
                        Csv(Fmt(c.SignalClose)),
                        Csv(c.LevelName),
                        Csv(Fmt(c.Level)),
                        Csv(Fmt(c.Pdc)),
                        Csv(Fmt(c.Onh)),
                        Csv(Fmt(c.Onl)),
                        Csv(Fmt(c.Pmh)),
                        Csv(Fmt(c.Pml)),
                        Csv(Fmt(c.RthOpen)),
                        Csv(Fmt(c.RthVsPdcTicks)),
                        Csv(Fmt(c.PmhVsOnhTicks)),
                        Csv(tradesToday.ToString(CultureInfo.InvariantCulture)),
                        Csv(winnersToday.ToString(CultureInfo.InvariantCulture)),
                        Csv(losersToday.ToString(CultureInfo.InvariantCulture)),
                        Csv(dailyRealizedGross.ToString("0.00", CultureInfo.InvariantCulture)),
                        Csv(c.Reason)
                    };

                    tradeWriter.WriteLine(string.Join(",", fields));
                }
                catch (Exception ex)
                {
                    if (!logFailurePrinted)
                    {
                        logFailurePrinted = true;
                        Print(Name + ": Trade CSV logging failed: " + ex.Message);
                    }
                }
            }
        }

        private string Csv(string value)
        {
            if (value == null)
                return "";

            bool quote = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
            string escaped = value.Replace("\"", "\"\"");
            return quote ? "\"" + escaped + "\"" : escaped;
        }

        private string MakeSafeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "unknown";

            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder(value.Length);

            foreach (char ch in value)
                sb.Append(invalid.Contains(ch) ? '_' : ch);

            return sb.ToString().Replace(' ', '_');
        }

        private void LogDailySummary(DateTime time, string reason)
        {
            if (currentTradeDate == DateTime.MinValue)
                return;

            LogEvent(time, "DAILY_SUMMARY", "",
                string.Format(CultureInfo.InvariantCulture,
                    "Reason={0}; Date={1:yyyy-MM-dd}; Trades={2}; Wins={3}; Losses={4}; GrossPnL={5:0.00}; " +
                    "PDCTrades={6}; PMHTrades={7}; ONHTrades={8}; RTHTrades={9}",
                    reason,
                    currentTradeDate,
                    tradesToday,
                    winnersToday,
                    losersToday,
                    dailyRealizedGross,
                    GetModelTradesToday(SigPdc),
                    GetModelTradesToday(SigPmh),
                    GetModelTradesToday(SigOnh),
                    GetModelTradesToday(SigRth)));
        }

        private void LogRunSummary(DateTime time)
        {
            LogEvent(time, "RUN_SUMMARY", "",
                string.Format(CultureInfo.InvariantCulture,
                    "Trades={0}; Wins={1}; Losses={2}; Flats={3}; GrossPnL={4:0.00}",
                    runTrades, runWins, runLosses, runFlats, runGrossPnl));

            foreach (string id in new[] { SigPdc, SigPmh, SigOnh, SigRth })
            {
                ModelStats s = GetStats(id);
                LogEvent(time, "MODEL_SUMMARY", id,
                    string.Format(CultureInfo.InvariantCulture,
                        "Qualified={0}; Taken={1}; Skipped={2}; Wins={3}; Losses={4}; Flats={5}; GrossPnL={6:0.00}",
                        s.Qualified, s.Taken, s.Skipped, s.Wins, s.Losses, s.Flats, s.GrossPnl));
            }
        }

        #region Properties

        [NinjaScriptProperty]
        [Display(Name = "Enable PDC sweep/reclaim long", GroupName = "01. Models", Order = 1)]
        public bool EnablePdcSweepReclaimLong { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable PMH delayed rejection short", GroupName = "01. Models", Order = 2)]
        public bool EnablePmhDelayedRejectShort { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable ONH reclaim long", GroupName = "01. Models", Order = 3)]
        public bool EnableOnhReclaimLong { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable RTH reclaim long", GroupName = "01. Models", Order = 4)]
        public bool EnableRthReclaimLong { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "PDC start time", Description = "Start time of eligible 5m signal candle, ET HHmmss.", GroupName = "02. Signal Windows", Order = 1)]
        public int PdcStartTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "PMH start time", Description = "Normal PMH rejection start time, ET HHmmss.", GroupName = "02. Signal Windows", Order = 2)]
        public int PmhStartTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "PMH=ONH confluence start", Description = "Later PMH start when PMH and ONH overlap.", GroupName = "02. Signal Windows", Order = 3)]
        public int PmhOnhConfluenceStartTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "ONH start time", GroupName = "02. Signal Windows", Order = 4)]
        public int OnhStartTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "RTH reclaim start time", GroupName = "02. Signal Windows", Order = 5)]
        public int RthStartTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Last entry time", Description = "Start time of latest eligible 5m signal candle.", GroupName = "02. Signal Windows", Order = 6)]
        public int LastEntryTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "Required break ticks", Description = "Minimum penetration/close distance through the tested level.", GroupName = "03. Signal Precision", Order = 1)]
        public int RequiredBreakTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "PMH/ONH confluence tolerance ticks", GroupName = "03. Signal Precision", Order = 2)]
        public int PmhOnhConfluenceToleranceTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Contracts", GroupName = "04. Risk", Order = 1)]
        public int Contracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Stop mode", GroupName = "04. Risk", Order = 2)]
        public NinjexInitialStopMode StopMode { get; set; }

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "Stop buffer ticks", GroupName = "04. Risk", Order = 3)]
        public int StopBufferTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Min stop ticks", GroupName = "04. Risk", Order = 4)]
        public int MinStopTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "Max stop ticks", GroupName = "04. Risk", Order = 5)]
        public int MaxStopTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "Fixed stop ticks", GroupName = "04. Risk", Order = 6)]
        public int FixedStopTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Target mode", GroupName = "04. Risk", Order = 7)]
        public NinjexTargetMode TargetMode { get; set; }

        [NinjaScriptProperty]
        [Range(0.25, 10.0)]
        [Display(Name = "Reward/risk", GroupName = "04. Risk", Order = 8)]
        public double RewardRisk { get; set; }

        [NinjaScriptProperty]
        [Range(1, 400)]
        [Display(Name = "Fixed profit ticks", GroupName = "04. Risk", Order = 9)]
        public int FixedProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Range(5, 480)]
        [Display(Name = "Max hold minutes", GroupName = "04. Risk", Order = 10)]
        public int MaxHoldMinutes { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Flatten at bar-close time", Description = "Actual 5m bar close timestamp ET HHmmss.", GroupName = "04. Risk", Order = 11)]
        public int FlattenAtBarCloseTime { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Max trades per day", GroupName = "05. Daily Limits", Order = 1)]
        public int MaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Max winners per day", GroupName = "05. Daily Limits", Order = 2)]
        public int MaxWinnersPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Max losers per day", GroupName = "05. Daily Limits", Order = 3)]
        public int MaxLosersPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Max trades per model per day", GroupName = "05. Daily Limits", Order = 4)]
        public int MaxTradesPerModelPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(50.0, 5000.0)]
        [Display(Name = "Daily loss limit ($)", Description = "Realized gross PnL lockout; intended for research protection, not account-level enforcement.", GroupName = "05. Daily Limits", Order = 5)]
        public double DailyLossLimitCurrency { get; set; }

        [NinjaScriptProperty]
        [Range(2, 100)]
        [Display(Name = "ATR period", GroupName = "06. Research", Order = 1)]
        public int AtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable file logging", GroupName = "06. Research", Order = 2)]
        public bool EnableFileLogging { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Print important events", GroupName = "06. Research", Order = 3)]
        public bool PrintImportantEvents { get; set; }

        #endregion
    }
}
