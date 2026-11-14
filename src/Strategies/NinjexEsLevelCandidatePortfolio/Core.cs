// NinjexEsLevelCandidatePortfolio.cs
// Version: 1.4.0-research5
//
// Research5 goals:
//   - Preserve all four Research3-validated contextual entry models and filters unchanged.
//   - Long-horizon validation from ES 12-23 through present tick data.
//   - Keep Research4 RTH/PDC/ONH exits, but revert PMH to the Research3 structural 8..20t / 2R exit.
//   - Add an actual-fill anchored bracket matrix so counterfactual exits are measured from NinjaTrader's exact fill price.
//   - PDC baseline remains SHADOW ONLY; it can never submit an actual order.
//   - The FIRST qualified signal for each trade model consumes that model for the trading day,
//     whether the portfolio trades it or a portfolio/risk gate blocks it. Later same-day retests
//     remain shadow-tracked but are never eligible for actual entry.
//   - Shadow-track every qualified signal for 5/15/30/60m returns and MFE/MAE.
//   - On each model's first qualified signal/day, simultaneously evaluate a counterfactual
//     bracket matrix using Playback tick sequencing:
//       stops 12/16/20/24 ticks x RR targets 1.5R/2.0R/2.5R,
//       plus stops 12/16/20/24 ticks x fixed targets 20/30/40/50 ticks.
//   - Playback ticks are TELEMETRY ONLY. Actual trading decisions remain 5-minute OnBarClose.
//   - NO timezone conversion. NinjaTrader timestamps must be ET.
//
// Actual trade candidates (all ON by default):
//   1) RTH-CONTEXT-RECLAIM-L
//   2) PDC-CONTEXT-SWEEP-RECLAIM-L
//   3) PMH-CONTEXT-REJECT-S
//   4) ONH-CONTEXT-RECLAIM-L
//
// Shadow-only comparators:
//   RAW-PDC-BASE-SWEEP-RECLAIM-L
//   RAW-PMH-DELAYED-REJECT-S
//   RAW-ONH-RECLAIM-L
//   RAW-RTH-RECLAIM-L
//
// Research5 actual exits (entry/context logic unchanged from Research3):
//   RTH 24t stop / 40t target
//   PDC 12t stop / 50t target
//   PMH structural signal-bar stop clamped to 8..20t / 2R target
//   ONH 20t stop / 40t target
// Generic structural-stop / RR settings also remain available for raw/shadow comparators.
// Daily controls remain unchanged: max 3 trades/day, max 2 winners/day, max 2 losers/day,
// max 1 trade/model/day, max 1 trade/family/day, $500 realized daily loss lockout,
// and exactly 12 completed 5-minute bars max hold.
//
// Logs:
//   Documents\NinjaTrader 8\NinjexLogs\EsLevelCandidatePortfolio\1.4.0-research5\<run>\events.log
//   ...\trades.csv     actual portfolio trades
//   ...\signals.csv    every shadow signal + 5/15/30/60m + MFE/MAE telemetry
//   ...\brackets.csv          first-signal/day matrix anchored to first Playback tick
//   ...\actual_brackets.csv   same 28-way matrix anchored to exact NinjaTrader fills
//   ...\equity.csv     5-minute equity + Playback tick high-water / drawdown state
//   ...\sessions.csv   RTH session-length / reduced-session classification
//
// IMPORTANT: Signal-time parameters refer to the START time of the 5-minute candle.

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
        private const string StrategyVersion = "1.4.0-research5";

        private const string SigPdcContext = "PDC-CONTEXT-SWEEP-RECLAIM-L";
        private const string SigRthContext = "RTH-CONTEXT-RECLAIM-L";
        private const string SigPmhContext = "PMH-CONTEXT-REJECT-S";
        private const string SigOnhContext = "ONH-CONTEXT-RECLAIM-L";
        private const string RawPdc = "RAW-PDC-BASE-SWEEP-RECLAIM-L";

        private const string RawPmh = "RAW-PMH-DELAYED-REJECT-S";
        private const string RawOnh = "RAW-ONH-RECLAIM-L";
        private const string RawRth = "RAW-RTH-RECLAIM-L";

        private const string FamilyPdc = "PDC";
        private const string FamilyPmh = "PMH";
        private const string FamilyOnh = "ONH";
        private const string FamilyRth = "RTH";

        private sealed class CandidateSignal
        {
            public string Id;
            public string Family;
            public MarketPosition Direction;
            public int Priority;
            public bool TradeCandidate;
            public bool Contextual;
            public double Level;
            public string LevelName;
            public int StopTicks;
            public int TargetTicks;
            public string Reason;
            public DateTime TradingDate;
            public DateTime SignalBarStart;
            public DateTime SignalBarEnd;
            public int SignalBarNumber;
            public int SignalOrdinalToday;
            public double SignalOpen;
            public double SignalHigh;
            public double SignalLow;
            public double SignalClose;
            public double SignalRangeTicks;
            public double ClosePositionInBar;
            public double DirectionalCloseStrength;
            public double AtrTicks;
            public double Pdc;
            public double Onh;
            public double Onl;
            public double Pmh;
            public double Pml;
            public double RthOpen;
            public double RthVsPdcTicks;
            public double PmhVsOnhTicks;
            public double OnhMinusPmhTicks;
            public int PriorRthBarCount;
            public bool PriorRthReducedSession;
            public string ContextSnapshot;
        }

        private sealed class BracketOutcome
        {
            public string BracketId;
            public string TargetType;
            public int StopTicks;
            public double RewardRisk;
            public int TargetTicks;
            public bool OutcomeResolved;
            public string Outcome;
            public string ExitReason;
            public DateTime OutcomeTime;
            public double OutcomePrice;
            public double OutcomePnlTicks;
            public bool AmbiguousBoth;
        }

        private sealed class ShadowObservation
        {
            public long Sequence;
            public CandidateSignal Candidate;
            public bool EntryInitialized;
            public bool TickDriven;
            public string EntrySource;
            public DateTime EntryTime;
            public double EntryPrice;
            public int BarsObserved;
            public double MaxPrice;
            public double MinPrice;
            public double MfeTicks;
            public double MaeTicks;
            public double Forward5Ticks;
            public double Forward15Ticks;
            public double Forward30Ticks;
            public double Forward60Ticks;
            public bool OutcomeResolved;
            public string Outcome;
            public string ExitReason;
            public DateTime OutcomeTime;
            public double OutcomePrice;
            public double OutcomePnlTicks;
            public bool AmbiguousBoth;
            public bool Finalized;
            public List<BracketOutcome> Brackets;
        }

        private sealed class ActualFillBracketObservation
        {
            public long Sequence;
            public CandidateSignal Candidate;
            public DateTime EntryTime;
            public double EntryPrice;
            public int SignalBarNumber;
            public int BarsObserved;
            public bool TickDriven;
            public bool Finalized;
            public List<BracketOutcome> Brackets;
        }

        private sealed class ActiveTradeContext
        {
            public CandidateSignal Candidate;
            public DateTime EntryTime;
            public double EntryPrice;
            public int EntryQuantity;
            public int SignalBarNumber;
            public double RealizedGross;
            public double TickMfeTicks;
            public double TickMaeTicks;
            public double TickMaxUnrealized;
            public double TickMinUnrealized;
            public double BarMfeTicks;
            public double BarMaeTicks;
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
            public int ShadowCompleted;
            public int ShadowTargetFirst;
            public int ShadowStopFirst;
            public int ShadowTimeExit;
            public int ShadowAmbiguous;
            public int ShadowIncomplete;
            public double ShadowPnlTicks;
        }

        private ATR atr;

        // Reference levels
        private double lastRthClose;
        private DateTime lastRthCloseDate;
        private double currentRthCloseCandidate;
        private DateTime currentRthCloseCandidateDate;
        private int currentRthBarCount;
        private int priorRthBarCount;
        private bool priorRthReducedSession;
        private DateTime lastWrittenSessionDate;

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
        private DateTime currentTradingDate;

        // Daily counters
        private int tradesToday;
        private int winnersToday;
        private int losersToday;
        private double dailyRealizedGross;
        private readonly Dictionary<string, int> modelTradesToday = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> familyTradesToday = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> qualifiedSignalsToday = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Pending / active order state
        private bool entryPending;
        private CandidateSignal pendingCandidate;
        private ActiveTradeContext activeTrade;

        // Shadow research state
        private readonly object shadowSync = new object();
        private readonly List<ShadowObservation> shadowObservations = new List<ShadowObservation>();
        private long nextShadowSequence;

        private readonly object actualFillBracketSync = new object();
        private readonly List<ActualFillBracketObservation> actualFillBracketObservations = new List<ActualFillBracketObservation>();
        private long nextActualFillBracketSequence;

        private bool playbackTickTelemetrySeen;

        // Cached last primary bar for callbacks/logging
        private DateTime lastBarTime;
        private double lastBarOpen;
        private double lastBarHigh;
        private double lastBarLow;
        private double lastBarClose;

        // Run-level actual-trade stats
        private readonly Dictionary<string, ModelStats> stats = new Dictionary<string, ModelStats>(StringComparer.OrdinalIgnoreCase);
        private int runTrades;
        private int runWins;
        private int runLosses;
        private int runFlats;
        private double runGrossPnl;

        // Equity telemetry
        private double equityPeakAtBarClose;
        private double maxDrawdownAtBarClose;
        private DateTime maxDrawdownAtBarCloseTime;
        private double equityPeakPlayback;
        private double maxDrawdownPlayback;
        private DateTime maxDrawdownPlaybackTime;
        private double lastPlaybackEquity;

        // Logging
        private readonly object logSync = new object();
        private StreamWriter eventWriter;
        private StreamWriter tradeWriter;
        private StreamWriter signalWriter;
        private StreamWriter bracketWriter;
        private StreamWriter actualBracketWriter;
        private StreamWriter equityWriter;
        private StreamWriter sessionWriter;
        private string runId;
        private string runDirectory;
        private bool logFailurePrinted;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Research5 long-horizon ES validation with hybrid exits and actual-fill bracket telemetry.";
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

                // Research5 trade candidates: all ON by default.
                EnablePdcContextLong = true;
                EnableRthContextLong = true;
                EnablePmhContextShort = true;
                EnableOnhContextLong = true;
                EnableRawPdcBaseShadow = true;
                EnableRawComparatorShadowSignals = true;

                // Base signal windows (START time of 5-minute signal candle, ET).
                PdcStartTime = 103000;
                PmhStartTime = 103000;
                PmhOnhConfluenceStartTime = 110000;
                OnhStartTime = 100000;
                RthStartTime = 100000;
                LastEntryTime = 151500;

                // Signal precision.
                RequiredBreakTicks = 1;
                PmhOnhConfluenceToleranceTicks = 1;

                // Research3-validated context filters; intentionally unchanged in Research5.
                PdcContextMaxRthVsPdcTicks = 80;
                PdcContextMaxOnhMinusPmhTicks = 60;

                PmhContextEndTimeExclusive = 123000;
                PmhContextMinSignalRangeTicks = 20;
                PmhContextMaxRthVsPdcTicks = 80;

                OnhContextMinAtrTicks = 30;
                OnhContextMaxAtrTicks = 55;
                OnhContextMaxRthVsPdcTicks = 40;

                RthContextMaxRthVsPdcTicks = 0;
                RthContextMaxOnhMinusPmhTicks = 60;

                // Generic/raw comparator risk; actual contextual trades use the Research5 brackets below.
                Contracts = 1;
                StopMode = NinjexInitialStopMode.SignalBarExtremeCapped;
                StopBufferTicks = 1;
                MinStopTicks = 8;
                MaxStopTicks = 20;
                FixedStopTicks = 16;

                TargetMode = NinjexTargetMode.RewardRisk;
                RewardRisk = 2.0;
                FixedProfitTicks = 40;

                // Research5 hybrid ACTUAL exits.
                // PMH intentionally uses the generic structural 8..20t / 2R calculation above.
                RthActualStopTicks = 24;
                RthActualTargetTicks = 40;
                PdcActualStopTicks = 12;
                PdcActualTargetTicks = 50;
                OnhActualStopTicks = 20;
                OnhActualTargetTicks = 40;

                MaxHoldBars = 12;
                FlattenAtBarCloseTime = 155500;

                // Daily risk / frequency.
                MaxTradesPerDay = 3;
                MaxWinnersPerDay = 2;
                MaxLosersPerDay = 2;
                MaxTradesPerModelPerDay = 1;
                MaxTradesPerFamilyPerDay = 1;
                DailyLossLimitCurrency = 500.0;

                // Research telemetry.
                AtrPeriod = 14;
                ExpectedFullRthBars = 78;
                ReducedSessionMaxRthBars = 70;
                EnableFileLogging = true;
                EnableEquityCsv = true;
                EnableBracketMatrix = true;
                EnableActualFillBracketMatrix = true;
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
                    "Version=" + StrategyVersion +
                    "; Instrument=" + (Instrument != null ? Instrument.FullName : "n/a") +
                    "; Bars=" + (BarsPeriod != null ? BarsPeriod.Value.ToString(CultureInfo.InvariantCulture) : "0") +
                    " " + (BarsPeriod != null ? BarsPeriod.BarsPeriodType.ToString() : "n/a") +
                    "; TradingHours=" + (TradingHours != null ? TradingHours.Name : "n/a") +
                    "; Calculate=" + Calculate +
                    "; Fill=" + OrderFillResolution +
                    "; SlippageTicks=" + Slippage +
                    "; Models[PDCCTX=" + EnablePdcContextLong +
                    ",RTHCTX=" + EnableRthContextLong +
                    ",PMHCTX=" + EnablePmhContextShort +
                    ",ONHCTX=" + EnableOnhContextLong +
                    ",PDCBaseShadow=" + EnableRawPdcBaseShadow +
                    ",RawShadow=" + EnableRawComparatorShadowSignals + "]" +
                    "; FirstQualifiedConsumesModel=True" +
                    "; PDCContext[RTHvsPDC<=" + PdcContextMaxRthVsPdcTicks +
                    ",ONHminusPMH<=" + PdcContextMaxOnhMinusPmhTicks + "]" +
                    "; PMHContext[EndExclusive=" + PmhContextEndTimeExclusive +
                    ",Range>=" + PmhContextMinSignalRangeTicks +
                    ",RTHvsPDC<=" + PmhContextMaxRthVsPdcTicks + "]" +
                    "; ONHContext[ATR=" + OnhContextMinAtrTicks + "-" + OnhContextMaxAtrTicks +
                    ",RTHvsPDC<=" + OnhContextMaxRthVsPdcTicks + "]" +
                    "; RTHContext[RTHvsPDC<=" + RthContextMaxRthVsPdcTicks +
                    ",ONHminusPMH<=" + RthContextMaxOnhMinusPmhTicks + "]" +
                    "; StopMode=" + StopMode +
                    "; MinStop=" + MinStopTicks +
                    "; MaxStop=" + MaxStopTicks +
                    "; TargetMode=" + TargetMode +
                    "; RR=" + RewardRisk.ToString("0.00", CultureInfo.InvariantCulture) +
                    "; ActualExits[RTH=" + RthActualStopTicks + "/" + RthActualTargetTicks +
                    ",PDC=" + PdcActualStopTicks + "/" + PdcActualTargetTicks +
                    ",PMH=Structural" + MinStopTicks + "-" + MaxStopTicks + "x" + RewardRisk.ToString("0.0", CultureInfo.InvariantCulture) + "R" +
                    ",ONH=" + OnhActualStopTicks + "/" + OnhActualTargetTicks + "]" +
                    "; MaxHoldBars=" + MaxHoldBars +
                    "; MaxTradesDay=" + MaxTradesPerDay +
                    "; MaxWinsDay=" + MaxWinnersPerDay +
                    "; MaxLossesDay=" + MaxLosersPerDay +
                    "; DailyLossLimit=" + DailyLossLimitCurrency.ToString("0.00", CultureInfo.InvariantCulture) +
                    "; BracketMatrix=" + EnableBracketMatrix +
                    "; ActualFillBracketMatrix=" + EnableActualFillBracketMatrix +
                    "; Brackets=Stops[12,16,20,24]xRR[1.5,2.0,2.5]+FixedTargets[20,30,40,50]");

                if (!configurationValid)
                    LogEvent(TimeOrNow(), "FATAL_CONFIG", "", "Strategy will not submit entries until configuration is corrected.");
            }
            else if (State == State.Terminated)
            {
                try
                {
                    FinalizeAllShadowObservations("TERMINATED");
                    FinalizeAllActualFillBracketObservations("TERMINATED");
                    WritePendingSessionSummary();
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
            DateTime tradingDate = GetTradingDate(signalBarStart);

            HandleTradingDateReset(tradingDate);
            UpdateReferenceLevels(signalBarStart, signalStartTime);

            // IMPORTANT Research5 ordering:
            // 1) Advance all previously-created shadow signals with this completed bar.
            // 2) Update actual-position telemetry / risk exits.
            // 3) Evaluate and register EVERY current signal, even if a trade is already open.
            AdvanceShadowObservationsOnBar();
            AdvanceActualFillBracketObservationsOnBar();
            UpdateActiveTradeBarTelemetry();
            UpdateBarCloseEquityTelemetry();

            if (Position.MarketPosition != MarketPosition.Flat && activeTrade != null)
            {
                int completedPostSignalBars = CurrentBar - activeTrade.SignalBarNumber;
                if (MaxHoldBars > 0 && completedPostSignalBars >= MaxHoldBars)
                    SubmitManagedExit("TIME-EXIT");
                else if (barCloseTime >= FlattenAtBarCloseTime)
                    SubmitManagedExit("DAY-FLATTEN");
            }

            List<CandidateSignal> candidates = levelsReady
                ? EvaluateCandidates(signalBarStart, signalStartTime, tradingDate)
                : new List<CandidateSignal>();

            // Every qualified candidate is registered for unbiased shadow analysis BEFORE portfolio gates.
            foreach (CandidateSignal c in candidates)
            {
                GetStats(c.Id).Qualified++;
                c.SignalOrdinalToday = IncrementQualifiedSignalOrdinal(c.Id);
                RegisterShadowObservation(c);
                LogEvent(Time[0], "SIGNAL_QUALIFIED", c.Id, FormatCandidate(c));
            }

            // Research5: the first qualified signal consumes the model for this trading day.
            // A blocked/arbitrated first signal remains shadow-tracked, but later retests can never
            // become actual entries that day.
            List<CandidateSignal> tradeCandidates = new List<CandidateSignal>();
            foreach (CandidateSignal c in candidates.Where(x => x.TradeCandidate))
            {
                if (c.SignalOrdinalToday == 1)
                    tradeCandidates.Add(c);
                else
                    MarkActualSkip(c, "ModelConsumedByFirstQualifiedSignal; FirstQualifiedOrdinal=1");
            }

            if (tradeCandidates.Count == 0)
                return;

            // Do not open new trades after the latest signal time or at/after manual flatten.
            if (signalStartTime > LastEntryTime || barCloseTime >= FlattenAtBarCloseTime)
            {
                foreach (CandidateSignal c in tradeCandidates)
                    MarkActualSkip(c, "OutsideActualEntryWindow");
                return;
            }

            string portfolioGate;
            if (!CanSubmitAnyEntry(out portfolioGate))
            {
                foreach (CandidateSignal c in tradeCandidates)
                    MarkActualSkip(c, "PortfolioGate=" + portfolioGate);
                return;
            }

            // Generic family arbitration remains as a safety net. In Research5 there is only one
            // actual-trade model per family; raw comparators are shadow-only.
            List<CandidateSignal> familyWinners = new List<CandidateSignal>();
            foreach (IGrouping<string, CandidateSignal> familyGroup in tradeCandidates.GroupBy(x => x.Family, StringComparer.OrdinalIgnoreCase))
            {
                CandidateSignal familyWinner = familyGroup.OrderBy(x => x.Priority).First();
                familyWinners.Add(familyWinner);

                foreach (CandidateSignal suppressed in familyGroup.Where(x => x != familyWinner))
                    MarkActualSkip(suppressed, "SupersededSameFamilyBy=" + familyWinner.Id);
            }

            CandidateSignal chosen = null;
            foreach (CandidateSignal c in familyWinners.OrderBy(x => x.Priority))
            {
                if (GetModelTradesToday(c.Id) >= MaxTradesPerModelPerDay)
                {
                    MarkActualSkip(c, "ModelDailyLimit");
                    continue;
                }

                if (GetFamilyTradesToday(c.Family) >= MaxTradesPerFamilyPerDay)
                {
                    MarkActualSkip(c, "FamilyDailyLimit");
                    continue;
                }

                if (chosen == null)
                    chosen = c;
                else
                    MarkActualSkip(c, "PortfolioArbitration; Chosen=" + chosen.Id);
            }

            if (chosen != null)
                SubmitCandidate(chosen);
        }

        // Playback / real-time market data is used ONLY for telemetry.
        // Entry/exit decisions remain 5-minute OnBarClose.
        protected override void OnMarketData(MarketDataEventArgs marketDataUpdate)
        {
            if (marketDataUpdate == null || marketDataUpdate.MarketDataType != MarketDataType.Last)
                return;

            double price = marketDataUpdate.Price;
            if (!IsFinite(price) || price <= 0)
                return;

            playbackTickTelemetrySeen = true;

            UpdateShadowObservationsOnTick(marketDataUpdate.Time, price);
            UpdateActualFillBracketObservationsOnTick(marketDataUpdate.Time, price);
            UpdateActiveTradeTickTelemetry(marketDataUpdate.Time, price);
            UpdatePlaybackEquityTelemetry(marketDataUpdate.Time, price);
        }

        private void ResetRuntimeState()
        {
            lastRthClose = double.NaN;
            lastRthCloseDate = DateTime.MinValue;
            currentRthCloseCandidate = double.NaN;
            currentRthCloseCandidateDate = DateTime.MinValue;
            currentRthBarCount = 0;
            priorRthBarCount = 0;
            priorRthReducedSession = false;
            lastWrittenSessionDate = DateTime.MinValue;

            pdc = onh = onl = pmh = pml = rthOpen = double.NaN;
            levelsReady = false;

            configurationValid = false;
            rejectionLockout = false;
            currentTradingDate = DateTime.MinValue;

            tradesToday = winnersToday = losersToday = 0;
            dailyRealizedGross = 0;
            modelTradesToday.Clear();
            familyTradesToday.Clear();
            qualifiedSignalsToday.Clear();

            entryPending = false;
            pendingCandidate = null;
            activeTrade = null;

            lock (shadowSync)
                shadowObservations.Clear();
            nextShadowSequence = 0;

            lock (actualFillBracketSync)
                actualFillBracketObservations.Clear();
            nextActualFillBracketSequence = 0;

            playbackTickTelemetrySeen = false;

            lastBarTime = DateTime.MinValue;
            lastBarOpen = lastBarHigh = lastBarLow = lastBarClose = double.NaN;

            runTrades = runWins = runLosses = runFlats = 0;
            runGrossPnl = 0;
            stats.Clear();

            equityPeakAtBarClose = 0;
            maxDrawdownAtBarClose = 0;
            maxDrawdownAtBarCloseTime = DateTime.MinValue;
            equityPeakPlayback = 0;
            maxDrawdownPlayback = 0;
            maxDrawdownPlaybackTime = DateTime.MinValue;
            lastPlaybackEquity = 0;

            EnsureDictionaries();
        }

        private void EnsureDictionaries()
        {
            foreach (string id in AllModelIds())
            {
                if (!modelTradesToday.ContainsKey(id))
                    modelTradesToday[id] = 0;
                if (!qualifiedSignalsToday.ContainsKey(id))
                    qualifiedSignalsToday[id] = 0;
                if (!stats.ContainsKey(id))
                    stats[id] = new ModelStats();
            }

            foreach (string family in new[] { FamilyPdc, FamilyPmh, FamilyOnh, FamilyRth })
                if (!familyTradesToday.ContainsKey(family))
                    familyTradesToday[family] = 0;
        }

        private IEnumerable<string> AllModelIds()
        {
            return new[]
            {
                SigPdcContext, SigRthContext, SigPmhContext, SigOnhContext,
                RawPdc, RawPmh, RawOnh, RawRth
            };
        }

        private void ValidateConfiguration()
        {
            bool barsOk = BarsPeriod != null && BarsPeriod.BarsPeriodType == BarsPeriodType.Minute && BarsPeriod.Value == 5;
            bool tradingHoursOk = TradingHours != null &&
                                  string.Equals(TradingHours.Name, "CME US Index Futures ETH", StringComparison.OrdinalIgnoreCase);
            bool instrumentOk = Instrument != null && Instrument.MasterInstrument != null &&
                                string.Equals(Instrument.MasterInstrument.Name, "ES", StringComparison.OrdinalIgnoreCase);

            configurationValid = barsOk && tradingHoursOk && instrumentOk;

            if (!barsOk)
                Print(Name + ": ERROR - Primary series must be 5 Minute.");
            if (!tradingHoursOk)
                Print(Name + ": ERROR - Trading Hours must be 'CME US Index Futures ETH'.");
            if (!instrumentOk)
                Print(Name + ": ERROR - This research strategy is intended for ES only.");

            Print(Name + ": Uses Time[0] directly with NO timezone conversion. Ensure NinjaTrader timestamps are ET.");
            Print(Name + ": Playback/OnMarketData ticks are telemetry only; all trading decisions remain 5-minute OnBarClose.");
        }

        private DateTime GetTradingDate(DateTime signalBarStart)
        {
            // CME equity-index ETH session begins 18:00 ET on the prior calendar date.
            return ToTime(signalBarStart) >= 180000 ? signalBarStart.Date.AddDays(1) : signalBarStart.Date;
        }

        private void HandleTradingDateReset(DateTime tradingDate)
        {
            if (currentTradingDate == DateTime.MinValue)
            {
                currentTradingDate = tradingDate;
                ResetDailyCountersOnly();
                return;
            }

            if (tradingDate == currentTradingDate)
                return;

            LogDailySummary(lastBarTime, "TRADING_DATE_CHANGE");
            currentTradingDate = tradingDate;
            ResetDailyCountersOnly();
        }

        private void ResetDailyCountersOnly()
        {
            tradesToday = winnersToday = losersToday = 0;
            dailyRealizedGross = 0;
            rejectionLockout = false;

            modelTradesToday.Clear();
            familyTradesToday.Clear();
            qualifiedSignalsToday.Clear();
            EnsureDictionaries();
        }

        private void UpdateReferenceLevels(DateTime signalBarStart, int signalStartTime)
        {
            // Track current RTH so the next ETH session can finalize PDC and session classification.
            if (signalStartTime >= 93000 && signalStartTime < 160000)
            {
                currentRthCloseCandidate = Close[0];
                currentRthCloseCandidateDate = signalBarStart.Date;
                currentRthBarCount++;
            }

            if (Bars.IsFirstBarOfSession)
            {
                FinalizePriorRthSession();

                pdc = lastRthClose;
                onh = High[0];
                onl = Low[0];
                pmh = double.NaN;
                pml = double.NaN;
                rthOpen = double.NaN;
                levelsReady = false;

                LogEvent(Time[0], "ETH_SESSION_START", "",
                    string.Format(CultureInfo.InvariantCulture,
                        "SignalBarStart={0:yyyy-MM-dd HH:mm:ss}; TradingDate={1:yyyy-MM-dd}; CarriedPDC={2}; PDCSourceDate={3:yyyy-MM-dd}; " +
                        "PriorRthBars={4}; PriorReduced={5}",
                        signalBarStart,
                        GetTradingDate(signalBarStart),
                        Fmt(pdc),
                        lastRthCloseDate,
                        priorRthBarCount,
                        priorRthReducedSession));
            }

            bool inOvernight = signalStartTime >= 180000 || signalStartTime < 93000;
            if (inOvernight)
            {
                onh = double.IsNaN(onh) ? High[0] : Math.Max(onh, High[0]);
                onl = double.IsNaN(onl) ? Low[0] : Math.Min(onl, Low[0]);
            }

            if (signalStartTime >= 30000 && signalStartTime < 93000)
            {
                pmh = double.IsNaN(pmh) ? High[0] : Math.Max(pmh, High[0]);
                pml = double.IsNaN(pml) ? Low[0] : Math.Min(pml, Low[0]);
            }

            if (signalStartTime == 93000)
            {
                rthOpen = Open[0];
                levelsReady = IsFinite(pdc) && IsFinite(onh) && IsFinite(onl) &&
                              IsFinite(pmh) && IsFinite(pml) && IsFinite(rthOpen);

                LogEvent(Time[0], "LEVELS_READY", "",
                    string.Format(CultureInfo.InvariantCulture,
                        "Ready={0}; PDC={1}; ONH={2}; ONL={3}; PMH={4}; PML={5}; RTHOpen={6}; " +
                        "RTHvsPDCticks={7}; ONHminusPMHticks={8}; PriorRthBars={9}; PriorReduced={10}",
                        levelsReady,
                        Fmt(pdc), Fmt(onh), Fmt(onl), Fmt(pmh), Fmt(pml), Fmt(rthOpen),
                        Fmt(PriceDiffTicks(rthOpen, pdc)),
                        Fmt(PriceDiffTicks(onh, pmh)),
                        priorRthBarCount,
                        priorRthReducedSession));
            }
        }

        private void FinalizePriorRthSession()
        {
            if (IsFinite(currentRthCloseCandidate))
            {
                lastRthClose = currentRthCloseCandidate;
                lastRthCloseDate = currentRthCloseCandidateDate;
                priorRthBarCount = currentRthBarCount;
                priorRthReducedSession = priorRthBarCount > 0 && priorRthBarCount <= ReducedSessionMaxRthBars;

                WriteSessionCsv(lastRthCloseDate, priorRthBarCount, priorRthReducedSession, lastRthClose);

                LogEvent(Time[0], "RTH_SESSION_FINALIZED", "",
                    string.Format(CultureInfo.InvariantCulture,
                        "Date={0:yyyy-MM-dd}; RthBars={1}; ExpectedFull={2}; ReducedThreshold={3}; Reduced={4}; Close={5}",
                        lastRthCloseDate,
                        priorRthBarCount,
                        ExpectedFullRthBars,
                        ReducedSessionMaxRthBars,
                        priorRthReducedSession,
                        Fmt(lastRthClose)));
            }

            currentRthCloseCandidate = double.NaN;
            currentRthCloseCandidateDate = DateTime.MinValue;
            currentRthBarCount = 0;
        }

        private void WritePendingSessionSummary()
        {
            if (!IsFinite(currentRthCloseCandidate) || currentRthCloseCandidateDate == DateTime.MinValue)
                return;

            bool reduced = currentRthBarCount > 0 && currentRthBarCount <= ReducedSessionMaxRthBars;
            WriteSessionCsv(currentRthCloseCandidateDate, currentRthBarCount, reduced, currentRthCloseCandidate);
        }

        private List<CandidateSignal> EvaluateCandidates(DateTime signalBarStart, int signalStartTime, DateTime tradingDate)
        {
            List<CandidateSignal> result = new List<CandidateSignal>(8);
            if (signalStartTime > LastEntryTime)
                return result;

            double breakDistance = RequiredBreakTicks * TickSize;
            double rthVsPdc = PriceDiffTicks(rthOpen, pdc);
            double onhMinusPmh = PriceDiffTicks(onh, pmh);
            double signalRangeTicks = PriceDiffTicks(High[0], Low[0]);
            double atrTicks = atr != null && CurrentBar >= AtrPeriod && TickSize > 0 ? atr[0] / TickSize : double.NaN;

            bool pdcBase = signalStartTime >= PdcStartTime &&
                           Close[1] >= pdc &&
                           Low[0] <= pdc - breakDistance &&
                           Close[0] >= pdc + breakDistance;

            if (pdcBase)
            {
                if (EnableRawPdcBaseShadow)
                    result.Add(BuildCandidate(RawPdc, FamilyPdc, MarketPosition.Long, 900, false, false,
                        pdc, "PDC", signalBarStart, tradingDate,
                        "Raw PDC sweep/reclaim shadow comparator: PrevClose>=PDC; low swept below; 5m close reclaimed above."));

                bool contextPass = rthVsPdc <= PdcContextMaxRthVsPdcTicks &&
                                   onhMinusPmh <= PdcContextMaxOnhMinusPmhTicks;

                if (EnablePdcContextLong && contextPass)
                    result.Add(BuildCandidate(SigPdcContext, FamilyPdc, MarketPosition.Long, 2, true, true,
                        pdc, "PDC", signalBarStart, tradingDate,
                        "PDC context pass: RTHvsPDC<=" + PdcContextMaxRthVsPdcTicks +
                        "t; ONHminusPMH<=" + PdcContextMaxOnhMinusPmhTicks + "t."));
            }

            bool pmhOnhConfluence = Math.Abs(pmh - onh) <= PmhOnhConfluenceToleranceTicks * TickSize;
            int effectivePmhStart = pmhOnhConfluence ? PmhOnhConfluenceStartTime : PmhStartTime;
            bool pmhBase = signalStartTime >= effectivePmhStart &&
                           High[0] >= pmh + breakDistance &&
                           Close[0] <= pmh - breakDistance;

            if (pmhBase)
            {
                if (EnableRawComparatorShadowSignals)
                    result.Add(BuildCandidate(RawPmh, FamilyPmh, MarketPosition.Short, 900, false, false,
                        pmh, "PMH", signalBarStart, tradingDate,
                        "Raw delayed PMH rejection comparator; PMH_ONH_Confluence=" + pmhOnhConfluence));

                bool contextPass = signalStartTime < PmhContextEndTimeExclusive &&
                                   signalRangeTicks >= PmhContextMinSignalRangeTicks &&
                                   rthVsPdc <= PmhContextMaxRthVsPdcTicks;

                if (EnablePmhContextShort && contextPass)
                    result.Add(BuildCandidate(SigPmhContext, FamilyPmh, MarketPosition.Short, 3, true, true,
                        pmh, "PMH", signalBarStart, tradingDate,
                        "PMH context pass: Start<" + PmhContextEndTimeExclusive +
                        "; Range>=" + PmhContextMinSignalRangeTicks +
                        "t; RTHvsPDC<=" + PmhContextMaxRthVsPdcTicks +
                        "t; PMH_ONH_Confluence=" + pmhOnhConfluence));
            }

            bool onhBase = signalStartTime >= OnhStartTime &&
                           Low[0] <= onh - breakDistance &&
                           Close[0] >= onh + breakDistance;

            if (onhBase)
            {
                if (EnableRawComparatorShadowSignals)
                    result.Add(BuildCandidate(RawOnh, FamilyOnh, MarketPosition.Long, 900, false, false,
                        onh, "ONH", signalBarStart, tradingDate,
                        "Raw ONH reclaim comparator."));

                bool contextPass = atrTicks >= OnhContextMinAtrTicks &&
                                   atrTicks <= OnhContextMaxAtrTicks &&
                                   rthVsPdc <= OnhContextMaxRthVsPdcTicks;
                if (EnableOnhContextLong && contextPass)
                    result.Add(BuildCandidate(SigOnhContext, FamilyOnh, MarketPosition.Long, 4, true, true,
                        onh, "ONH", signalBarStart, tradingDate,
                        "ONH context pass: ATRticks=" + Fmt(atrTicks) +
                        " within [" + OnhContextMinAtrTicks + "," + OnhContextMaxAtrTicks +
                        "]; RTHvsPDC<=" + OnhContextMaxRthVsPdcTicks + "t."));
            }

            bool rthBase = signalStartTime >= RthStartTime &&
                           Close[1] <= rthOpen &&
                           Low[0] <= rthOpen - breakDistance &&
                           Close[0] >= rthOpen + breakDistance;

            if (rthBase)
            {
                if (EnableRawComparatorShadowSignals)
                    result.Add(BuildCandidate(RawRth, FamilyRth, MarketPosition.Long, 900, false, false,
                        rthOpen, "RTHOPEN", signalBarStart, tradingDate,
                        "Raw RTH reclaim comparator."));

                bool contextPass = rthVsPdc <= RthContextMaxRthVsPdcTicks &&
                                   onhMinusPmh <= RthContextMaxOnhMinusPmhTicks;

                if (EnableRthContextLong && contextPass)
                    result.Add(BuildCandidate(SigRthContext, FamilyRth, MarketPosition.Long, 1, true, true,
                        rthOpen, "RTHOPEN", signalBarStart, tradingDate,
                        "RTH context pass: RTHvsPDC<=" + RthContextMaxRthVsPdcTicks +
                        "t; ONHminusPMH<=" + RthContextMaxOnhMinusPmhTicks + "t."));
            }

            return result;
        }

        private CandidateSignal BuildCandidate(
            string id,
            string family,
            MarketPosition direction,
            int priority,
            bool tradeCandidate,
            bool contextual,
            double level,
            string levelName,
            DateTime signalBarStart,
            DateTime tradingDate,
            string reason)
        {
            int stopTicks = CalculateInitialStopTicks(direction);
            int targetTicks = CalculateTargetTicks(stopTicks);
            ApplyResearch5ActualBracket(id, tradeCandidate, ref stopTicks, ref targetTicks);

            double range = High[0] - Low[0];
            double closePos = range > 0 ? (Close[0] - Low[0]) / range : 0.5;
            double directionalClose = direction == MarketPosition.Long ? closePos : 1.0 - closePos;
            double atrTicks = atr != null && CurrentBar >= AtrPeriod && TickSize > 0 ? atr[0] / TickSize : double.NaN;

            return new CandidateSignal
            {
                Id = id,
                Family = family,
                Direction = direction,
                Priority = priority,
                TradeCandidate = tradeCandidate,
                Contextual = contextual,
                Level = level,
                LevelName = levelName,
                StopTicks = stopTicks,
                TargetTicks = targetTicks,
                Reason = reason,
                TradingDate = tradingDate,
                SignalBarStart = signalBarStart,
                SignalBarEnd = Time[0],
                SignalBarNumber = CurrentBar,
                SignalOpen = Open[0],
                SignalHigh = High[0],
                SignalLow = Low[0],
                SignalClose = Close[0],
                SignalRangeTicks = PriceDiffTicks(High[0], Low[0]),
                ClosePositionInBar = closePos,
                DirectionalCloseStrength = directionalClose,
                AtrTicks = atrTicks,
                Pdc = pdc,
                Onh = onh,
                Onl = onl,
                Pmh = pmh,
                Pml = pml,
                RthOpen = rthOpen,
                RthVsPdcTicks = PriceDiffTicks(rthOpen, pdc),
                PmhVsOnhTicks = PriceDiffTicks(pmh, onh),
                OnhMinusPmhTicks = PriceDiffTicks(onh, pmh),
                PriorRthBarCount = priorRthBarCount,
                PriorRthReducedSession = priorRthReducedSession,
                ContextSnapshot = BuildContextSnapshot()
            };
        }

        private string BuildContextSnapshot()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "PDC={0};ONH={1};PMH={2};RTH={3};RTHvsPDC={4};ONHminusPMH={5};PriorRthBars={6};PriorReduced={7}",
                Fmt(pdc), Fmt(onh), Fmt(pmh), Fmt(rthOpen),
                Fmt(PriceDiffTicks(rthOpen, pdc)),
                Fmt(PriceDiffTicks(onh, pmh)),
                priorRthBarCount,
                priorRthReducedSession);
        }

        private void ApplyResearch5ActualBracket(string id, bool tradeCandidate, ref int stopTicks, ref int targetTicks)
        {
            // Research5 keeps fixed actual exits for RTH/PDC/ONH.
            // PMH deliberately retains the generic structural-stop / 2R calculation.
            // Raw/shadow comparators also retain the generic calculation.
            if (!tradeCandidate)
                return;

            if (string.Equals(id, SigRthContext, StringComparison.OrdinalIgnoreCase))
            {
                stopTicks = Math.Max(1, RthActualStopTicks);
                targetTicks = Math.Max(1, RthActualTargetTicks);
            }
            else if (string.Equals(id, SigPdcContext, StringComparison.OrdinalIgnoreCase))
            {
                stopTicks = Math.Max(1, PdcActualStopTicks);
                targetTicks = Math.Max(1, PdcActualTargetTicks);
            }
            else if (string.Equals(id, SigOnhContext, StringComparison.OrdinalIgnoreCase))
            {
                stopTicks = Math.Max(1, OnhActualStopTicks);
                targetTicks = Math.Max(1, OnhActualTargetTicks);
            }
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

            return ClampTicks((int)Math.Ceiling(Math.Max(1.0, rawTicks)));
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

        // -------------------- Shadow telemetry --------------------

        private void RegisterShadowObservation(CandidateSignal c)
        {
            ShadowObservation obs = new ShadowObservation
            {
                Sequence = ++nextShadowSequence,
                Candidate = c,
                EntryInitialized = false,
                TickDriven = false,
                EntrySource = "",
                BarsObserved = 0,
                MaxPrice = double.NaN,
                MinPrice = double.NaN,
                MfeTicks = 0,
                MaeTicks = 0,
                Forward5Ticks = double.NaN,
                Forward15Ticks = double.NaN,
                Forward30Ticks = double.NaN,
                Forward60Ticks = double.NaN,
                OutcomeResolved = false,
                Outcome = "",
                ExitReason = "",
                OutcomeTime = DateTime.MinValue,
                OutcomePrice = double.NaN,
                OutcomePnlTicks = double.NaN,
                AmbiguousBoth = false,
                Finalized = false,
                Brackets = EnableBracketMatrix && c.SignalOrdinalToday == 1
                    ? BuildBracketMatrix()
                    : new List<BracketOutcome>()
            };

            lock (shadowSync)
                shadowObservations.Add(obs);
        }

        private void UpdateShadowObservationsOnTick(DateTime time, double price)
        {
            lock (shadowSync)
            {
                foreach (ShadowObservation obs in shadowObservations)
                {
                    if (obs.Finalized)
                        continue;

                    if (!obs.EntryInitialized)
                    {
                        // Only initialize from ticks that occur at/after the signal-bar close.
                        if (time < obs.Candidate.SignalBarEnd)
                            continue;

                        double adjusted = ApplyShadowSlippage(price, obs.Candidate.Direction);
                        InitializeShadow(obs, time, adjusted, true, "PlaybackTick");
                    }

                    if (!obs.TickDriven)
                        continue;

                    UpdateShadowExcursion(obs, price);
                    ResolveShadowOutcomeAtPrice(obs, time, price);
                    ResolveBracketOutcomesAtPrice(obs, time, price);
                }
            }
        }

        private void AdvanceShadowObservationsOnBar()
        {
            List<ShadowObservation> finalize = new List<ShadowObservation>();

            lock (shadowSync)
            {
                foreach (ShadowObservation obs in shadowObservations)
                {
                    if (obs.Finalized)
                        continue;

                    // Current completed bar must be after the signal bar.
                    if (CurrentBar <= obs.Candidate.SignalBarNumber)
                        continue;

                    if (!obs.EntryInitialized)
                    {
                        double adjustedOpen = ApplyShadowSlippage(Open[0], obs.Candidate.Direction);
                        InitializeShadow(obs, Time[0].AddMinutes(-BarsPeriod.Value), adjustedOpen, false, "NextBarOpen5mFallback");
                    }

                    obs.BarsObserved++;

                    if (!obs.TickDriven)
                    {
                        UpdateShadowExcursion(obs, High[0]);
                        UpdateShadowExcursion(obs, Low[0]);
                        ResolveShadowOutcomeFromBar(obs, Time[0], High[0], Low[0]);
                        ResolveBracketOutcomesFromBar(obs, Time[0], High[0], Low[0]);
                    }

                    SetForwardReturnIfDue(obs);

                    if (obs.BarsObserved >= MaxHoldBars)
                    {
                        if (!obs.OutcomeResolved)
                        {
                            obs.OutcomeResolved = true;
                            obs.Outcome = "TIME";
                            obs.ExitReason = "SHADOW_TIME_EXIT";
                            obs.OutcomeTime = Time[0];
                            obs.OutcomePrice = Close[0];
                            obs.OutcomePnlTicks = DirectionalTicks(obs.Candidate.Direction, obs.EntryPrice, Close[0]);
                        }

                        ResolveBracketTimeExits(obs, Time[0], Close[0]);
                        obs.Finalized = true;
                        finalize.Add(obs);
                    }
                }

                foreach (ShadowObservation obs in finalize)
                    shadowObservations.Remove(obs);
            }

            foreach (ShadowObservation obs in finalize)
                FinalizeShadowObservation(obs, "COMPLETE");
        }

        private void InitializeShadow(ShadowObservation obs, DateTime time, double price, bool tickDriven, string source)
        {
            obs.EntryInitialized = true;
            obs.TickDriven = tickDriven;
            obs.EntrySource = source;
            obs.EntryTime = time;
            obs.EntryPrice = price;
            obs.MaxPrice = price;
            obs.MinPrice = price;
            obs.MfeTicks = 0;
            obs.MaeTicks = 0;
        }

        private double ApplyShadowSlippage(double price, MarketPosition direction)
        {
            double slip = Math.Max(0, Slippage) * TickSize;
            return direction == MarketPosition.Long ? price + slip : price - slip;
        }

        private void UpdateShadowExcursion(ShadowObservation obs, double price)
        {
            if (!obs.EntryInitialized || !IsFinite(price))
                return;

            obs.MaxPrice = double.IsNaN(obs.MaxPrice) ? price : Math.Max(obs.MaxPrice, price);
            obs.MinPrice = double.IsNaN(obs.MinPrice) ? price : Math.Min(obs.MinPrice, price);

            if (obs.Candidate.Direction == MarketPosition.Long)
            {
                obs.MfeTicks = Math.Max(obs.MfeTicks, (obs.MaxPrice - obs.EntryPrice) / TickSize);
                obs.MaeTicks = Math.Max(obs.MaeTicks, (obs.EntryPrice - obs.MinPrice) / TickSize);
            }
            else
            {
                obs.MfeTicks = Math.Max(obs.MfeTicks, (obs.EntryPrice - obs.MinPrice) / TickSize);
                obs.MaeTicks = Math.Max(obs.MaeTicks, (obs.MaxPrice - obs.EntryPrice) / TickSize);
            }
        }

        private void ResolveShadowOutcomeAtPrice(ShadowObservation obs, DateTime time, double price)
        {
            if (obs.OutcomeResolved || !obs.EntryInitialized)
                return;

            double stopPrice = ShadowStopPrice(obs);
            double targetPrice = ShadowTargetPrice(obs);

            bool stopHit = obs.Candidate.Direction == MarketPosition.Long ? price <= stopPrice : price >= stopPrice;
            bool targetHit = obs.Candidate.Direction == MarketPosition.Long ? price >= targetPrice : price <= targetPrice;

            if (stopHit)
                ResolveShadow(obs, "STOP", "SHADOW_STOP_FIRST", time, stopPrice, -obs.Candidate.StopTicks, false);
            else if (targetHit)
                ResolveShadow(obs, "TARGET", "SHADOW_TARGET_FIRST", time, targetPrice, obs.Candidate.TargetTicks, false);
        }

        private void ResolveShadowOutcomeFromBar(ShadowObservation obs, DateTime time, double high, double low)
        {
            if (obs.OutcomeResolved || !obs.EntryInitialized)
                return;

            double stopPrice = ShadowStopPrice(obs);
            double targetPrice = ShadowTargetPrice(obs);

            bool stopHit;
            bool targetHit;

            if (obs.Candidate.Direction == MarketPosition.Long)
            {
                stopHit = low <= stopPrice;
                targetHit = high >= targetPrice;
            }
            else
            {
                stopHit = high >= stopPrice;
                targetHit = low <= targetPrice;
            }

            if (stopHit && targetHit)
                ResolveShadow(obs, "AMBIGUOUS", "SHADOW_BOTH_SAME_5M_BAR_CONSERVATIVE_STOP", time, stopPrice, -obs.Candidate.StopTicks, true);
            else if (stopHit)
                ResolveShadow(obs, "STOP", "SHADOW_STOP_FIRST", time, stopPrice, -obs.Candidate.StopTicks, false);
            else if (targetHit)
                ResolveShadow(obs, "TARGET", "SHADOW_TARGET_FIRST", time, targetPrice, obs.Candidate.TargetTicks, false);
        }

        private void ResolveShadow(ShadowObservation obs, string outcome, string reason, DateTime time, double price, double pnlTicks, bool ambiguous)
        {
            obs.OutcomeResolved = true;
            obs.Outcome = outcome;
            obs.ExitReason = reason;
            obs.OutcomeTime = time;
            obs.OutcomePrice = price;
            obs.OutcomePnlTicks = pnlTicks;
            obs.AmbiguousBoth = ambiguous;
        }

        private List<BracketOutcome> BuildBracketMatrix()
        {
            List<BracketOutcome> matrix = new List<BracketOutcome>(28);
            int[] stops = { 12, 16, 20, 24 };
            double[] rewardRisks = { 1.5, 2.0, 2.5 };
            int[] fixedTargets = { 20, 30, 40, 50 };

            foreach (int stop in stops)
            {
                foreach (double rr in rewardRisks)
                {
                    int target = Math.Max(1, (int)Math.Round(stop * rr, MidpointRounding.AwayFromZero));
                    matrix.Add(new BracketOutcome
                    {
                        BracketId = "S" + stop + "_RR" + rr.ToString("0.0", CultureInfo.InvariantCulture) + "_T" + target,
                        TargetType = "RR",
                        StopTicks = stop,
                        RewardRisk = rr,
                        TargetTicks = target,
                        OutcomeResolved = false,
                        Outcome = "",
                        ExitReason = "",
                        OutcomeTime = DateTime.MinValue,
                        OutcomePrice = double.NaN,
                        OutcomePnlTicks = double.NaN,
                        AmbiguousBoth = false
                    });
                }

                foreach (int target in fixedTargets)
                {
                    matrix.Add(new BracketOutcome
                    {
                        BracketId = "S" + stop + "_FIXED_T" + target,
                        TargetType = "FIXED",
                        StopTicks = stop,
                        RewardRisk = double.NaN,
                        TargetTicks = target,
                        OutcomeResolved = false,
                        Outcome = "",
                        ExitReason = "",
                        OutcomeTime = DateTime.MinValue,
                        OutcomePrice = double.NaN,
                        OutcomePnlTicks = double.NaN,
                        AmbiguousBoth = false
                    });
                }
            }

            return matrix;
        }

        private void ResolveBracketOutcomesAtPrice(ShadowObservation obs, DateTime time, double price)
        {
            if (obs == null || !obs.EntryInitialized || obs.Brackets == null || obs.Brackets.Count == 0)
                return;

            foreach (BracketOutcome bracket in obs.Brackets)
            {
                if (bracket.OutcomeResolved)
                    continue;

                double stopPrice = obs.Candidate.Direction == MarketPosition.Long
                    ? obs.EntryPrice - bracket.StopTicks * TickSize
                    : obs.EntryPrice + bracket.StopTicks * TickSize;
                double targetPrice = obs.Candidate.Direction == MarketPosition.Long
                    ? obs.EntryPrice + bracket.TargetTicks * TickSize
                    : obs.EntryPrice - bracket.TargetTicks * TickSize;

                bool stopHit = obs.Candidate.Direction == MarketPosition.Long ? price <= stopPrice : price >= stopPrice;
                bool targetHit = obs.Candidate.Direction == MarketPosition.Long ? price >= targetPrice : price <= targetPrice;

                if (stopHit)
                    ResolveBracket(bracket, "STOP", "BRACKET_STOP_FIRST", time, stopPrice, -bracket.StopTicks, false);
                else if (targetHit)
                    ResolveBracket(bracket, "TARGET", "BRACKET_TARGET_FIRST", time, targetPrice, bracket.TargetTicks, false);
            }
        }

        private void ResolveBracketOutcomesFromBar(ShadowObservation obs, DateTime time, double high, double low)
        {
            if (obs == null || !obs.EntryInitialized || obs.Brackets == null || obs.Brackets.Count == 0)
                return;

            foreach (BracketOutcome bracket in obs.Brackets)
            {
                if (bracket.OutcomeResolved)
                    continue;

                double stopPrice = obs.Candidate.Direction == MarketPosition.Long
                    ? obs.EntryPrice - bracket.StopTicks * TickSize
                    : obs.EntryPrice + bracket.StopTicks * TickSize;
                double targetPrice = obs.Candidate.Direction == MarketPosition.Long
                    ? obs.EntryPrice + bracket.TargetTicks * TickSize
                    : obs.EntryPrice - bracket.TargetTicks * TickSize;

                bool stopHit;
                bool targetHit;
                if (obs.Candidate.Direction == MarketPosition.Long)
                {
                    stopHit = low <= stopPrice;
                    targetHit = high >= targetPrice;
                }
                else
                {
                    stopHit = high >= stopPrice;
                    targetHit = low <= targetPrice;
                }

                if (stopHit && targetHit)
                    ResolveBracket(bracket, "AMBIGUOUS", "BRACKET_BOTH_SAME_5M_BAR_CONSERVATIVE_STOP", time, stopPrice, -bracket.StopTicks, true);
                else if (stopHit)
                    ResolveBracket(bracket, "STOP", "BRACKET_STOP_FIRST", time, stopPrice, -bracket.StopTicks, false);
                else if (targetHit)
                    ResolveBracket(bracket, "TARGET", "BRACKET_TARGET_FIRST", time, targetPrice, bracket.TargetTicks, false);
            }
        }

        private void ResolveBracketTimeExits(ShadowObservation obs, DateTime time, double closePrice)
        {
            if (obs == null || !obs.EntryInitialized || obs.Brackets == null)
                return;

            foreach (BracketOutcome bracket in obs.Brackets)
            {
                if (bracket.OutcomeResolved)
                    continue;

                bracket.OutcomeResolved = true;
                bracket.Outcome = "TIME";
                bracket.ExitReason = "BRACKET_TIME_EXIT";
                bracket.OutcomeTime = time;
                bracket.OutcomePrice = closePrice;
                bracket.OutcomePnlTicks = DirectionalTicks(obs.Candidate.Direction, obs.EntryPrice, closePrice);
                bracket.AmbiguousBoth = false;
            }
        }

        private void ResolveBracket(BracketOutcome bracket, string outcome, string reason, DateTime time, double price, double pnlTicks, bool ambiguous)
        {
            bracket.OutcomeResolved = true;
            bracket.Outcome = outcome;
            bracket.ExitReason = reason;
            bracket.OutcomeTime = time;
            bracket.OutcomePrice = price;
            bracket.OutcomePnlTicks = pnlTicks;
            bracket.AmbiguousBoth = ambiguous;
        }

        private double ShadowStopPrice(ShadowObservation obs)
        {
            return obs.Candidate.Direction == MarketPosition.Long
                ? obs.EntryPrice - obs.Candidate.StopTicks * TickSize
                : obs.EntryPrice + obs.Candidate.StopTicks * TickSize;
        }

        private double ShadowTargetPrice(ShadowObservation obs)
        {
            return obs.Candidate.Direction == MarketPosition.Long
                ? obs.EntryPrice + obs.Candidate.TargetTicks * TickSize
                : obs.EntryPrice - obs.Candidate.TargetTicks * TickSize;
        }

        private void SetForwardReturnIfDue(ShadowObservation obs)
        {
            double directional = DirectionalTicks(obs.Candidate.Direction, obs.EntryPrice, Close[0]);

            if (obs.BarsObserved == 1)
                obs.Forward5Ticks = directional;
            else if (obs.BarsObserved == 3)
                obs.Forward15Ticks = directional;
            else if (obs.BarsObserved == 6)
                obs.Forward30Ticks = directional;
            else if (obs.BarsObserved == 12)
                obs.Forward60Ticks = directional;
        }

        private double DirectionalTicks(MarketPosition direction, double entry, double exit)
        {
            if (!IsFinite(entry) || !IsFinite(exit) || TickSize <= 0)
                return double.NaN;

            return direction == MarketPosition.Long ? (exit - entry) / TickSize : (entry - exit) / TickSize;
        }

        private void FinalizeShadowObservation(ShadowObservation obs, string status)
        {
            if (obs == null || obs.Candidate == null)
                return;

            ModelStats s = GetStats(obs.Candidate.Id);
            if (string.Equals(status, "COMPLETE", StringComparison.OrdinalIgnoreCase))
                s.ShadowCompleted++;
            else
                s.ShadowIncomplete++;
            if (obs.Outcome == "TARGET") s.ShadowTargetFirst++;
            else if (obs.Outcome == "STOP") s.ShadowStopFirst++;
            else if (obs.Outcome == "TIME") s.ShadowTimeExit++;
            else if (obs.Outcome == "AMBIGUOUS") s.ShadowAmbiguous++;
            if (IsFinite(obs.OutcomePnlTicks)) s.ShadowPnlTicks += obs.OutcomePnlTicks;

            WriteSignalCsv(obs, status);
            WriteBracketCsv(obs, status);
        }

        private void FinalizeAllShadowObservations(string reason)
        {
            List<ShadowObservation> remaining;
            lock (shadowSync)
            {
                remaining = shadowObservations.ToList();
                shadowObservations.Clear();
            }

            foreach (ShadowObservation obs in remaining)
            {
                obs.Finalized = true;
                FinalizeShadowObservation(obs, "INCOMPLETE_" + reason);
            }
        }

        // -------------------- Actual-fill anchored bracket telemetry --------------------

        private void RegisterActualFillBracketObservation(CandidateSignal candidate, DateTime entryTime, double entryPrice)
        {
            if (!EnableActualFillBracketMatrix || candidate == null || !candidate.TradeCandidate || !IsFinite(entryPrice))
                return;

            ActualFillBracketObservation obs = new ActualFillBracketObservation
            {
                Sequence = ++nextActualFillBracketSequence,
                Candidate = candidate,
                EntryTime = entryTime,
                EntryPrice = entryPrice,
                SignalBarNumber = candidate.SignalBarNumber,
                BarsObserved = 0,
                TickDriven = false,
                Finalized = false,
                Brackets = BuildBracketMatrix()
            };

            lock (actualFillBracketSync)
                actualFillBracketObservations.Add(obs);

            LogEvent(entryTime, "ACTUAL_FILL_MATRIX_START", candidate.Id,
                "ActualEntry=" + Fmt(entryPrice) + "; MatrixRows=" + obs.Brackets.Count +
                "; SignalStart=" + candidate.SignalBarStart.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        }

        private void UpdateActualFillBracketObservationsOnTick(DateTime time, double price)
        {
            lock (actualFillBracketSync)
            {
                foreach (ActualFillBracketObservation obs in actualFillBracketObservations)
                {
                    if (obs.Finalized || time < obs.EntryTime)
                        continue;

                    obs.TickDriven = true;
                    ResolveActualFillBracketOutcomesAtPrice(obs, time, price);
                }
            }
        }

        private void AdvanceActualFillBracketObservationsOnBar()
        {
            List<ActualFillBracketObservation> finalize = new List<ActualFillBracketObservation>();

            lock (actualFillBracketSync)
            {
                foreach (ActualFillBracketObservation obs in actualFillBracketObservations)
                {
                    if (obs.Finalized || CurrentBar <= obs.SignalBarNumber)
                        continue;

                    obs.BarsObserved++;

                    if (!obs.TickDriven)
                        ResolveActualFillBracketOutcomesFromBar(obs, Time[0], High[0], Low[0]);

                    if (obs.BarsObserved >= MaxHoldBars)
                    {
                        ResolveActualFillBracketTimeExits(obs, Time[0], Close[0]);
                        obs.Finalized = true;
                        finalize.Add(obs);
                    }
                }

                foreach (ActualFillBracketObservation obs in finalize)
                    actualFillBracketObservations.Remove(obs);
            }

            foreach (ActualFillBracketObservation obs in finalize)
                WriteActualBracketCsv(obs, "COMPLETE");
        }

        private void ResolveActualFillBracketOutcomesAtPrice(ActualFillBracketObservation obs, DateTime time, double price)
        {
            if (obs == null || obs.Candidate == null || obs.Brackets == null)
                return;

            foreach (BracketOutcome bracket in obs.Brackets)
            {
                if (bracket.OutcomeResolved)
                    continue;

                double stopPrice = obs.Candidate.Direction == MarketPosition.Long
                    ? obs.EntryPrice - bracket.StopTicks * TickSize
                    : obs.EntryPrice + bracket.StopTicks * TickSize;
                double targetPrice = obs.Candidate.Direction == MarketPosition.Long
                    ? obs.EntryPrice + bracket.TargetTicks * TickSize
                    : obs.EntryPrice - bracket.TargetTicks * TickSize;

                bool stopHit = obs.Candidate.Direction == MarketPosition.Long ? price <= stopPrice : price >= stopPrice;
                bool targetHit = obs.Candidate.Direction == MarketPosition.Long ? price >= targetPrice : price <= targetPrice;

                if (stopHit)
                    ResolveBracket(bracket, "STOP", "ACTUAL_FILL_BRACKET_STOP_FIRST", time, stopPrice, -bracket.StopTicks, false);
                else if (targetHit)
                    ResolveBracket(bracket, "TARGET", "ACTUAL_FILL_BRACKET_TARGET_FIRST", time, targetPrice, bracket.TargetTicks, false);
            }
        }

        private void ResolveActualFillBracketOutcomesFromBar(ActualFillBracketObservation obs, DateTime time, double high, double low)
        {
            if (obs == null || obs.Candidate == null || obs.Brackets == null)
                return;

            foreach (BracketOutcome bracket in obs.Brackets)
            {
                if (bracket.OutcomeResolved)
                    continue;

                double stopPrice = obs.Candidate.Direction == MarketPosition.Long
                    ? obs.EntryPrice - bracket.StopTicks * TickSize
                    : obs.EntryPrice + bracket.StopTicks * TickSize;
                double targetPrice = obs.Candidate.Direction == MarketPosition.Long
                    ? obs.EntryPrice + bracket.TargetTicks * TickSize
                    : obs.EntryPrice - bracket.TargetTicks * TickSize;

                bool stopHit;
                bool targetHit;
                if (obs.Candidate.Direction == MarketPosition.Long)
                {
                    stopHit = low <= stopPrice;
                    targetHit = high >= targetPrice;
                }
                else
                {
                    stopHit = high >= stopPrice;
                    targetHit = low <= targetPrice;
                }

                if (stopHit && targetHit)
                    ResolveBracket(bracket, "AMBIGUOUS", "ACTUAL_FILL_BRACKET_BOTH_SAME_5M_BAR_CONSERVATIVE_STOP", time, stopPrice, -bracket.StopTicks, true);
                else if (stopHit)
                    ResolveBracket(bracket, "STOP", "ACTUAL_FILL_BRACKET_STOP_FIRST", time, stopPrice, -bracket.StopTicks, false);
                else if (targetHit)
                    ResolveBracket(bracket, "TARGET", "ACTUAL_FILL_BRACKET_TARGET_FIRST", time, targetPrice, bracket.TargetTicks, false);
            }
        }

        private void ResolveActualFillBracketTimeExits(ActualFillBracketObservation obs, DateTime time, double closePrice)
        {
            if (obs == null || obs.Candidate == null || obs.Brackets == null)
                return;

            foreach (BracketOutcome bracket in obs.Brackets)
            {
                if (bracket.OutcomeResolved)
                    continue;

                bracket.OutcomeResolved = true;
                bracket.Outcome = "TIME";
                bracket.ExitReason = "ACTUAL_FILL_BRACKET_TIME_EXIT";
                bracket.OutcomeTime = time;
                bracket.OutcomePrice = closePrice;
                bracket.OutcomePnlTicks = DirectionalTicks(obs.Candidate.Direction, obs.EntryPrice, closePrice);
                bracket.AmbiguousBoth = false;
            }
        }

        private void FinalizeAllActualFillBracketObservations(string reason)
        {
            List<ActualFillBracketObservation> remaining;
            lock (actualFillBracketSync)
            {
                remaining = actualFillBracketObservations.ToList();
                actualFillBracketObservations.Clear();
            }

            foreach (ActualFillBracketObservation obs in remaining)
            {
                obs.Finalized = true;
                WriteActualBracketCsv(obs, "INCOMPLETE_" + reason);
            }
        }

        // -------------------- Actual portfolio --------------------

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

        private void MarkActualSkip(CandidateSignal c, string reason)
        {
            GetStats(c.Id).Skipped++;
            LogEvent(Time[0], "SIGNAL_SKIPPED_ACTUAL", c.Id, reason + "; " + FormatCandidate(c));
        }

        private void SubmitCandidate(CandidateSignal c)
        {
            SetStopLoss(c.Id, CalculationMode.Ticks, c.StopTicks, false);
            SetProfitTarget(c.Id, CalculationMode.Ticks, c.TargetTicks);

            entryPending = true;
            pendingCandidate = c;

            LogEvent(Time[0], "ENTRY_SUBMIT", c.Id,
                string.Format(CultureInfo.InvariantCulture,
                    "Family={0}; Direction={1}; Qty={2}; StopTicks={3}; TargetTicks={4}; ApproxRiskPerContract=${5:0.00}; " +
                    "ApproxTargetPerContract=${6:0.00}; {7}",
                    c.Family,
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
                "; AvgPrice=" + Fmt(Position.AveragePrice) +
                "; CompletedPostSignalBars=" + (CurrentBar - activeTrade.SignalBarNumber));

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

            string model = IsActualEntrySignal(order.Name)
                ? order.Name
                : (!string.IsNullOrWhiteSpace(order.FromEntrySignal) ? order.FromEntrySignal : ActiveModelId());

            LogEvent(time, "ORDER_UPDATE", model,
                string.Format(CultureInfo.InvariantCulture,
                    "Name={0}; FromEntry={1}; Action={2}; Type={3}; State={4}; Qty={5}; Filled={6}; AvgFill={7}; " +
                    "Limit={8}; Stop={9}; OrderId={10}; OCO={11}; Error={12}; NativeError={13}",
                    Safe(order.Name), Safe(order.FromEntrySignal), order.OrderAction, order.OrderType, orderState,
                    quantity, filled, Fmt(averageFillPrice), Fmt(limitPrice), Fmt(stopPrice),
                    Safe(order.OrderId), Safe(order.Oco), error, Safe(nativeError)));

            if (orderState == OrderState.Rejected)
            {
                rejectionLockout = true;
                if (IsActualEntrySignal(order.Name))
                {
                    entryPending = false;
                    pendingCandidate = null;
                }

                if (PrintImportantEvents)
                    Print(time + " " + Name + " ORDER REJECTED: " + order.Name + " / " + nativeError);
            }
            else if (IsActualEntrySignal(order.Name) && orderState == OrderState.Cancelled && filled == 0)
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
            string model = IsActualEntrySignal(orderName)
                ? orderName
                : (!string.IsNullOrWhiteSpace(order.FromEntrySignal) ? order.FromEntrySignal : ActiveModelId());

            LogEvent(time, "EXECUTION", model,
                string.Format(CultureInfo.InvariantCulture,
                    "ExecutionId={0}; OrderId={1}; OrderName={2}; FromEntry={3}; Price={4}; Qty={5}; " +
                    "MarketPositionAfter={6}; OrderState={7}",
                    Safe(executionId), Safe(orderId), Safe(orderName), Safe(order.FromEntrySignal),
                    Fmt(price), quantity, marketPosition, order.OrderState));

            if (IsActualEntrySignal(orderName))
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
            if (candidate == null || !string.Equals(candidate.Id, signal, StringComparison.OrdinalIgnoreCase))
            {
                LogEvent(time, "WARNING", signal, "Entry execution without matching pending candidate; building fallback context.");
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
                    SignalBarNumber = candidate.SignalBarNumber,
                    RealizedGross = 0,
                    TickMfeTicks = 0,
                    TickMaeTicks = 0,
                    TickMaxUnrealized = 0,
                    TickMinUnrealized = 0,
                    BarMfeTicks = 0,
                    BarMaeTicks = 0
                };

                tradesToday++;
                runTrades++;
                IncrementModelTradesToday(signal);
                IncrementFamilyTradesToday(candidate.Family);
                GetStats(signal).Taken++;

                RegisterActualFillBracketObservation(candidate, time, price);
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

            UpdatePlaybackEquityTelemetry(time, price);

            LogEvent(time, "ENTRY_FILLED", signal,
                string.Format(CultureInfo.InvariantCulture,
                    "FillPrice={0}; FillQty={1}; AvgEntry={2}; TotalEntryQty={3}; TradesToday={4}; ModelTradesToday={5}; " +
                    "FamilyTradesToday={6}; StopTicks={7}; TargetTicks={8}; SignalBarNumber={9}",
                    Fmt(price), quantity, Fmt(activeTrade.EntryPrice), activeTrade.EntryQuantity,
                    tradesToday, GetModelTradesToday(signal), GetFamilyTradesToday(candidate.Family),
                    candidate.StopTicks, candidate.TargetTicks, candidate.SignalBarNumber));
        }

        private void HandleExitExecution(string orderName, double exitPrice, int quantity, DateTime time)
        {
            if (activeTrade == null)
                return;

            double pnlPerPoint = Instrument.MasterInstrument.PointValue;
            double gross = activeTrade.Candidate.Direction == MarketPosition.Long
                ? (exitPrice - activeTrade.EntryPrice) * pnlPerPoint * quantity
                : (activeTrade.EntryPrice - exitPrice) * pnlPerPoint * quantity;

            activeTrade.RealizedGross += gross;

            LogEvent(time, "EXIT_EXECUTION", activeTrade.Candidate.Id,
                string.Format(CultureInfo.InvariantCulture,
                    "ExitOrder={0}; ExitPrice={1}; Qty={2}; ExecutionGrossPnL={3:0.00}; TradeGrossPnLSoFar={4:0.00}; PositionAfter={5}",
                    Safe(orderName), Fmt(exitPrice), quantity, gross, activeTrade.RealizedGross, Position.MarketPosition));

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
                    "Outcome={0}; ExitReason={1}; Family={2}; Direction={3}; EntryTime={4:yyyy-MM-dd HH:mm:ss.fff}; Entry={5}; " +
                    "ExitTime={6:yyyy-MM-dd HH:mm:ss.fff}; Exit={7}; Qty={8}; GrossPnL={9:0.00}; HoldMin={10:0.0}; " +
                    "TickMFE={11:0.0}; TickMAE={12:0.0}; BarMFE={13:0.0}; BarMAE={14:0.0}; DailyPnL={15:0.00}; " +
                    "RunGross={16:0.00}; PlaybackMaxDD={17:0.00}; BarCloseMaxDD={18:0.00}",
                    outcome, exitReason, closed.Candidate.Family, closed.Candidate.Direction,
                    closed.EntryTime, Fmt(closed.EntryPrice), exitTime, Fmt(exitPrice), closed.EntryQuantity, pnl,
                    (exitTime - closed.EntryTime).TotalMinutes,
                    closed.TickMfeTicks, closed.TickMaeTicks, closed.BarMfeTicks, closed.BarMaeTicks,
                    dailyRealizedGross, runGrossPnl, maxDrawdownPlayback, maxDrawdownAtBarClose));

            WriteTradeCsv(closed, exitTime, exitPrice, exitReason, outcome, pnl);

            if (PrintImportantEvents)
            {
                Print(string.Format(CultureInfo.InvariantCulture,
                    "{0:yyyy-MM-dd HH:mm:ss} {1} CLOSED {2} {3} gross=${4:0.00} run=${5:0.00} tickDD=${6:0.00}",
                    exitTime, Name, closed.Candidate.Id, outcome, pnl, runGrossPnl, maxDrawdownPlayback));
            }

            activeTrade = null;
            UpdatePlaybackEquityTelemetry(exitTime, exitPrice);
        }

        private CandidateSignal BuildFallbackCandidate(string signal, DateTime time)
        {
            MarketPosition direction = signal == SigPmhContext ? MarketPosition.Short : MarketPosition.Long;
            string family = signal.IndexOf("PDC", StringComparison.OrdinalIgnoreCase) >= 0 ? FamilyPdc :
                            signal.IndexOf("PMH", StringComparison.OrdinalIgnoreCase) >= 0 ? FamilyPmh :
                            signal.IndexOf("ONH", StringComparison.OrdinalIgnoreCase) >= 0 ? FamilyOnh : FamilyRth;
            double level = family == FamilyPdc ? pdc : family == FamilyPmh ? pmh : family == FamilyOnh ? onh : rthOpen;
            int stopTicks = ClampTicks(FixedStopTicks);
            int targetTicks = CalculateTargetTicks(stopTicks);
            ApplyResearch5ActualBracket(signal, true, ref stopTicks, ref targetTicks);

            return new CandidateSignal
            {
                Id = signal,
                Family = family,
                Direction = direction,
                Priority = 99,
                TradeCandidate = true,
                Contextual = signal.IndexOf("CONTEXT", StringComparison.OrdinalIgnoreCase) >= 0 || signal.IndexOf("ATR", StringComparison.OrdinalIgnoreCase) >= 0,
                Level = level,
                LevelName = family,
                StopTicks = stopTicks,
                TargetTicks = targetTicks,
                Reason = "Fallback candidate built from execution callback",
                TradingDate = currentTradingDate,
                SignalBarStart = time,
                SignalBarEnd = time,
                SignalBarNumber = CurrentBar,
                SignalOpen = lastBarOpen,
                SignalHigh = lastBarHigh,
                SignalLow = lastBarLow,
                SignalClose = lastBarClose,
                SignalRangeTicks = PriceDiffTicks(lastBarHigh, lastBarLow),
                ClosePositionInBar = 0.5,
                DirectionalCloseStrength = 0.5,
                AtrTicks = double.NaN,
                Pdc = pdc,
                Onh = onh,
                Onl = onl,
                Pmh = pmh,
                Pml = pml,
                RthOpen = rthOpen,
                RthVsPdcTicks = PriceDiffTicks(rthOpen, pdc),
                PmhVsOnhTicks = PriceDiffTicks(pmh, onh),
                OnhMinusPmhTicks = PriceDiffTicks(onh, pmh),
                PriorRthBarCount = priorRthBarCount,
                PriorRthReducedSession = priorRthReducedSession,
                ContextSnapshot = BuildContextSnapshot()
            };
        }

        private string NormalizeExitReason(string orderName)
        {
            if (string.IsNullOrWhiteSpace(orderName)) return "UNKNOWN";
            if (orderName.IndexOf("Profit target", StringComparison.OrdinalIgnoreCase) >= 0) return "PROFIT_TARGET";
            if (orderName.IndexOf("Stop loss", StringComparison.OrdinalIgnoreCase) >= 0) return "STOP_LOSS";
            if (orderName.IndexOf("TIME-EXIT", StringComparison.OrdinalIgnoreCase) >= 0) return "TIME_EXIT";
            if (orderName.IndexOf("DAY-FLATTEN", StringComparison.OrdinalIgnoreCase) >= 0) return "DAY_FLATTEN";
            return orderName.Replace(',', '_');
        }

        private bool IsActualEntrySignal(string name)
        {
            return string.Equals(name, SigPdcContext, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, SigRthContext, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, SigPmhContext, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, SigOnhContext, StringComparison.OrdinalIgnoreCase);
        }

        // -------------------- Equity and MFE/MAE telemetry --------------------

        private void UpdateActiveTradeTickTelemetry(DateTime time, double price)
        {
            if (activeTrade == null || !IsFinite(price))
                return;

            double pnlTicks = DirectionalTicks(activeTrade.Candidate.Direction, activeTrade.EntryPrice, price);
            activeTrade.TickMfeTicks = Math.Max(activeTrade.TickMfeTicks, pnlTicks);
            activeTrade.TickMaeTicks = Math.Max(activeTrade.TickMaeTicks, -pnlTicks);

            double unrealized = pnlTicks * TickSize * Instrument.MasterInstrument.PointValue * activeTrade.EntryQuantity;
            activeTrade.TickMaxUnrealized = Math.Max(activeTrade.TickMaxUnrealized, unrealized);
            activeTrade.TickMinUnrealized = Math.Min(activeTrade.TickMinUnrealized, unrealized);
        }

        private void UpdateActiveTradeBarTelemetry()
        {
            if (activeTrade == null)
                return;

            double mfe;
            double mae;
            if (activeTrade.Candidate.Direction == MarketPosition.Long)
            {
                mfe = (High[0] - activeTrade.EntryPrice) / TickSize;
                mae = (activeTrade.EntryPrice - Low[0]) / TickSize;
            }
            else
            {
                mfe = (activeTrade.EntryPrice - Low[0]) / TickSize;
                mae = (High[0] - activeTrade.EntryPrice) / TickSize;
            }

            activeTrade.BarMfeTicks = Math.Max(activeTrade.BarMfeTicks, mfe);
            activeTrade.BarMaeTicks = Math.Max(activeTrade.BarMaeTicks, mae);
        }

        private void UpdatePlaybackEquityTelemetry(DateTime time, double price)
        {
            double equity = runGrossPnl;
            if (activeTrade != null && IsFinite(price))
            {
                double ticks = DirectionalTicks(activeTrade.Candidate.Direction, activeTrade.EntryPrice, price);
                equity += ticks * TickSize * Instrument.MasterInstrument.PointValue * activeTrade.EntryQuantity;
            }

            lastPlaybackEquity = equity;
            if (equity > equityPeakPlayback)
                equityPeakPlayback = equity;

            double dd = equityPeakPlayback - equity;
            if (dd > maxDrawdownPlayback)
            {
                maxDrawdownPlayback = dd;
                maxDrawdownPlaybackTime = time;
            }
        }

        private void UpdateBarCloseEquityTelemetry()
        {
            double equity = runGrossPnl;
            if (activeTrade != null)
            {
                double ticks = DirectionalTicks(activeTrade.Candidate.Direction, activeTrade.EntryPrice, Close[0]);
                equity += ticks * TickSize * Instrument.MasterInstrument.PointValue * activeTrade.EntryQuantity;
            }

            if (equity > equityPeakAtBarClose)
                equityPeakAtBarClose = equity;

            double dd = equityPeakAtBarClose - equity;
            if (dd > maxDrawdownAtBarClose)
            {
                maxDrawdownAtBarClose = dd;
                maxDrawdownAtBarCloseTime = Time[0];
            }

            if (EnableEquityCsv)
                WriteEquityCsv(Time[0], equity);
        }

        // -------------------- Dictionaries / helpers --------------------

        private string ActiveModelId()
        {
            if (activeTrade != null && activeTrade.Candidate != null) return activeTrade.Candidate.Id;
            if (pendingCandidate != null) return pendingCandidate.Id;
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

        private int GetFamilyTradesToday(string family)
        {
            int value;
            return familyTradesToday.TryGetValue(family, out value) ? value : 0;
        }

        private void IncrementFamilyTradesToday(string family)
        {
            familyTradesToday[family] = GetFamilyTradesToday(family) + 1;
        }

        private int IncrementQualifiedSignalOrdinal(string id)
        {
            int current;
            qualifiedSignalsToday.TryGetValue(id, out current);
            current++;
            qualifiedSignalsToday[id] = current;
            return current;
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
                "Family={0}; TradeCandidate={1}; Contextual={2}; Direction={3}; LevelName={4}; Level={5}; " +
                "TradingDate={6:yyyy-MM-dd}; SignalStart={7:yyyy-MM-dd HH:mm:ss}; SignalEnd={8:yyyy-MM-dd HH:mm:ss}; OrdinalToday={9}; " +
                "O={10}; H={11}; L={12}; C={13}; RangeTicks={14}; ClosePos={15}; DirectionalCloseStrength={16}; ATRticks={17}; " +
                "StopTicks={18}; TargetTicks={19}; PDC={20}; ONH={21}; ONL={22}; PMH={23}; PML={24}; RTHOpen={25}; " +
                "RTHvsPDCticks={26}; PMHvsONHticks={27}; ONHminusPMHticks={28}; PriorRthBars={29}; PriorReduced={30}; Reason={31}",
                c.Family, c.TradeCandidate, c.Contextual, c.Direction, c.LevelName, Fmt(c.Level),
                c.TradingDate, c.SignalBarStart, c.SignalBarEnd, c.SignalOrdinalToday,
                Fmt(c.SignalOpen), Fmt(c.SignalHigh), Fmt(c.SignalLow), Fmt(c.SignalClose), Fmt(c.SignalRangeTicks),
                Fmt(c.ClosePositionInBar), Fmt(c.DirectionalCloseStrength), Fmt(c.AtrTicks), c.StopTicks, c.TargetTicks,
                Fmt(c.Pdc), Fmt(c.Onh), Fmt(c.Onl), Fmt(c.Pmh), Fmt(c.Pml), Fmt(c.RthOpen),
                Fmt(c.RthVsPdcTicks), Fmt(c.PmhVsOnhTicks), Fmt(c.OnhMinusPmhTicks),
                c.PriorRthBarCount, c.PriorRthReducedSession, c.Reason);
        }

        private string Fmt(double value)
        {
            return IsFinite(value) ? value.ToString("0.########", CultureInfo.InvariantCulture) : "";
        }

        private string Safe(string value)
        {
            return string.IsNullOrEmpty(value) ? "" : value.Replace("\r", " ").Replace("\n", " ").Replace("|", "/");
        }

        private DateTime TimeOrNow()
        {
            return lastBarTime != DateTime.MinValue ? lastBarTime : DateTime.Now;
        }

        // -------------------- Logging --------------------

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

                runDirectory = Path.Combine(documents, "NinjaTrader 8", "NinjexLogs", "EsLevelCandidatePortfolio",
                    StrategyVersion, runId + "_" + safeInstrument);
                Directory.CreateDirectory(runDirectory);

                eventWriter = OpenWriter(Path.Combine(runDirectory, "events.log"));
                tradeWriter = OpenWriter(Path.Combine(runDirectory, "trades.csv"));
                signalWriter = OpenWriter(Path.Combine(runDirectory, "signals.csv"));
                bracketWriter = OpenWriter(Path.Combine(runDirectory, "brackets.csv"));
                actualBracketWriter = OpenWriter(Path.Combine(runDirectory, "actual_brackets.csv"));
                equityWriter = OpenWriter(Path.Combine(runDirectory, "equity.csv"));
                sessionWriter = OpenWriter(Path.Combine(runDirectory, "sessions.csv"));

                eventWriter.WriteLine("Timestamp|State|Event|Model|Instrument|BarTime|O|H|L|C|PDC|ONH|ONL|PMH|PML|RTHOpen|TradesToday|WinsToday|LossesToday|DailyGrossPnL|RunGrossPnL|Position|ActiveSignal|PlaybackMaxDD|BarCloseMaxDD|Detail");

                tradeWriter.WriteLine(
                    "Model,Family,Contextual,Direction,TradingDate,SignalStart,SignalEnd,SignalOrdinalToday,EntryTime,EntryPrice,ExitTime,ExitPrice,Quantity,Outcome,ExitReason,GrossPnL,HoldMinutes," +
                    "StopTicks,TargetTicks,ATRticks,SignalRangeTicks,ClosePositionInBar,DirectionalCloseStrength,SignalOpen,SignalHigh,SignalLow,SignalClose,LevelName,Level,PDC,ONH,ONL,PMH,PML,RTHOpen," +
                    "RTHvsPDCticks,PMHvsONHticks,ONHminusPMHticks,PriorRthBarCount,PriorRthReducedSession,TickMFEticks,TickMAEticks,BarMFEticks,BarMAEticks,TickMaxUnrealized,TickMinUnrealized," +
                    "RunGrossPnLAtClose,PlaybackMaxDDAtClose,BarCloseMaxDDAtClose,PlaybackTickTelemetrySeen,Reason");

                signalWriter.WriteLine(
                    "ShadowSequence,Status,Model,Family,TradeCandidate,Contextual,Direction,TradingDate,SignalStart,SignalEnd,SignalOrdinalToday,EntrySource,EntryTime,EntryPrice,BarsObserved," +
                    "ShadowOutcome,ShadowExitReason,OutcomeTime,OutcomePrice,OutcomePnLTicks,OutcomePnLCurrencyPerContract,AmbiguousBoth,MFE60Ticks,MAE60Ticks,Fwd5Ticks,Fwd15Ticks,Fwd30Ticks,Fwd60Ticks," +
                    "StopTicks,TargetTicks,ATRticks,SignalRangeTicks,ClosePositionInBar,DirectionalCloseStrength,SignalOpen,SignalHigh,SignalLow,SignalClose,LevelName,Level,PDC,ONH,ONL,PMH,PML,RTHOpen," +
                    "RTHvsPDCticks,PMHvsONHticks,ONHminusPMHticks,PriorRthBarCount,PriorRthReducedSession,TickDriven,ContextSnapshot,Reason");

                bracketWriter.WriteLine(
                    "ShadowSequence,Status,Model,Family,TradingDate,SignalStart,SignalOrdinalToday,EntrySource,EntryTime,EntryPrice,TickDriven," +
                    "BracketId,StopTicks,TargetType,RewardRisk,TargetTicks,Outcome,ExitReason,OutcomeTime,OutcomePrice,OutcomePnLTicks,OutcomePnLCurrencyPerContract,AmbiguousBoth," +
                    "ATRticks,RTHvsPDCticks,ONHminusPMHticks,SignalRangeTicks,ContextSnapshot");

                actualBracketWriter.WriteLine(
                    "ActualSequence,Status,Model,Family,TradingDate,SignalStart,SignalOrdinalToday,EntrySource,EntryTime,EntryPrice,TickDriven,BarsObserved," +
                    "BracketId,StopTicks,TargetType,RewardRisk,TargetTicks,Outcome,ExitReason,OutcomeTime,OutcomePrice,OutcomePnLTicks,OutcomePnLCurrencyPerContract,AmbiguousBoth," +
                    "ActualTradeStopTicks,ActualTradeTargetTicks,ATRticks,RTHvsPDCticks,ONHminusPMHticks,SignalRangeTicks,ContextSnapshot");
                equityWriter.WriteLine("BarTime,TradingDate,RunRealizedGross,OpenUnrealizedAtClose,EquityAtClose,PeakEquityAtClose,DrawdownAtClose,MaxDrawdownAtClose,PlaybackTickTelemetrySeen,LastPlaybackEquity,PlaybackPeakEquity,PlaybackMaxDrawdown,Position,ActiveModel");
                sessionWriter.WriteLine("RthDate,RthBars,ExpectedFullRthBars,ReducedThreshold,ReducedSession,LastRthClose");

                Print(Name + ": Logging to " + runDirectory);
            }
            catch (Exception ex)
            {
                eventWriter = tradeWriter = signalWriter = bracketWriter = actualBracketWriter = equityWriter = sessionWriter = null;
                if (!logFailurePrinted)
                {
                    logFailurePrinted = true;
                    Print(Name + ": File logging initialization failed: " + ex.Message);
                }
            }
        }

        private StreamWriter OpenWriter(string path)
        {
            StreamWriter w = new StreamWriter(path, false, new UTF8Encoding(false));
            w.AutoFlush = true;
            return w;
        }

        private void CloseLogging()
        {
            lock (logSync)
            {
                CloseWriter(eventWriter);
                CloseWriter(tradeWriter);
                CloseWriter(signalWriter);
                CloseWriter(bracketWriter);
                CloseWriter(actualBracketWriter);
                CloseWriter(equityWriter);
                CloseWriter(sessionWriter);
                eventWriter = tradeWriter = signalWriter = bracketWriter = actualBracketWriter = equityWriter = sessionWriter = null;
            }
        }

        private void CloseWriter(StreamWriter writer)
        {
            try
            {
                if (writer != null)
                {
                    writer.Flush();
                    writer.Dispose();
                }
            }
            catch { }
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
                        Fmt(lastBarOpen), Fmt(lastBarHigh), Fmt(lastBarLow), Fmt(lastBarClose),
                        Fmt(pdc), Fmt(onh), Fmt(onl), Fmt(pmh), Fmt(pml), Fmt(rthOpen),
                        tradesToday.ToString(CultureInfo.InvariantCulture),
                        winnersToday.ToString(CultureInfo.InvariantCulture),
                        losersToday.ToString(CultureInfo.InvariantCulture),
                        dailyRealizedGross.ToString("0.00", CultureInfo.InvariantCulture),
                        runGrossPnl.ToString("0.00", CultureInfo.InvariantCulture),
                        Position != null ? Position.MarketPosition.ToString() : "",
                        Safe(ActiveModelId()),
                        maxDrawdownPlayback.ToString("0.00", CultureInfo.InvariantCulture),
                        maxDrawdownAtBarClose.ToString("0.00", CultureInfo.InvariantCulture),
                        Safe(detail)
                    }));
                }
                catch (Exception ex)
                {
                    ReportLoggingFailure("events.log", ex);
                }
            }
        }

        private void WriteTradeCsv(ActiveTradeContext closed, DateTime exitTime, double exitPrice, string exitReason, string outcome, double pnl)
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
                        Csv(c.Id), Csv(c.Family), Csv(c.Contextual.ToString()), Csv(c.Direction.ToString()),
                        Csv(c.TradingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                        Csv(c.SignalBarStart.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                        Csv(c.SignalBarEnd.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                        Csv(c.SignalOrdinalToday.ToString(CultureInfo.InvariantCulture)),
                        Csv(closed.EntryTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)), Csv(Fmt(closed.EntryPrice)),
                        Csv(exitTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)), Csv(Fmt(exitPrice)),
                        Csv(closed.EntryQuantity.ToString(CultureInfo.InvariantCulture)), Csv(outcome), Csv(exitReason),
                        Csv(pnl.ToString("0.00", CultureInfo.InvariantCulture)), Csv((exitTime - closed.EntryTime).TotalMinutes.ToString("0.0", CultureInfo.InvariantCulture)),
                        Csv(c.StopTicks.ToString(CultureInfo.InvariantCulture)), Csv(c.TargetTicks.ToString(CultureInfo.InvariantCulture)),
                        Csv(Fmt(c.AtrTicks)), Csv(Fmt(c.SignalRangeTicks)), Csv(Fmt(c.ClosePositionInBar)), Csv(Fmt(c.DirectionalCloseStrength)),
                        Csv(Fmt(c.SignalOpen)), Csv(Fmt(c.SignalHigh)), Csv(Fmt(c.SignalLow)), Csv(Fmt(c.SignalClose)),
                        Csv(c.LevelName), Csv(Fmt(c.Level)), Csv(Fmt(c.Pdc)), Csv(Fmt(c.Onh)), Csv(Fmt(c.Onl)), Csv(Fmt(c.Pmh)), Csv(Fmt(c.Pml)), Csv(Fmt(c.RthOpen)),
                        Csv(Fmt(c.RthVsPdcTicks)), Csv(Fmt(c.PmhVsOnhTicks)), Csv(Fmt(c.OnhMinusPmhTicks)),
                        Csv(c.PriorRthBarCount.ToString(CultureInfo.InvariantCulture)), Csv(c.PriorRthReducedSession.ToString()),
                        Csv(closed.TickMfeTicks.ToString("0.0", CultureInfo.InvariantCulture)), Csv(closed.TickMaeTicks.ToString("0.0", CultureInfo.InvariantCulture)),
                        Csv(closed.BarMfeTicks.ToString("0.0", CultureInfo.InvariantCulture)), Csv(closed.BarMaeTicks.ToString("0.0", CultureInfo.InvariantCulture)),
                        Csv(closed.TickMaxUnrealized.ToString("0.00", CultureInfo.InvariantCulture)), Csv(closed.TickMinUnrealized.ToString("0.00", CultureInfo.InvariantCulture)),
                        Csv(runGrossPnl.ToString("0.00", CultureInfo.InvariantCulture)),
                        Csv(maxDrawdownPlayback.ToString("0.00", CultureInfo.InvariantCulture)),
                        Csv(maxDrawdownAtBarClose.ToString("0.00", CultureInfo.InvariantCulture)),
                        Csv(playbackTickTelemetrySeen.ToString()), Csv(c.Reason)
                    };
                    tradeWriter.WriteLine(string.Join(",", fields));
                }
                catch (Exception ex)
                {
                    ReportLoggingFailure("trades.csv", ex);
                }
            }
        }

        private void WriteSignalCsv(ShadowObservation obs, string status)
        {
            if (!EnableFileLogging || signalWriter == null || obs == null || obs.Candidate == null)
                return;

            CandidateSignal c = obs.Candidate;
            double pnlCurrency = IsFinite(obs.OutcomePnlTicks)
                ? obs.OutcomePnlTicks * TickSize * Instrument.MasterInstrument.PointValue
                : double.NaN;

            lock (logSync)
            {
                try
                {
                    string[] fields =
                    {
                        Csv(obs.Sequence.ToString(CultureInfo.InvariantCulture)), Csv(status), Csv(c.Id), Csv(c.Family),
                        Csv(c.TradeCandidate.ToString()), Csv(c.Contextual.ToString()), Csv(c.Direction.ToString()),
                        Csv(c.TradingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                        Csv(c.SignalBarStart.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                        Csv(c.SignalBarEnd.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                        Csv(c.SignalOrdinalToday.ToString(CultureInfo.InvariantCulture)), Csv(obs.EntrySource),
                        Csv(obs.EntryTime == DateTime.MinValue ? "" : obs.EntryTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)),
                        Csv(Fmt(obs.EntryPrice)), Csv(obs.BarsObserved.ToString(CultureInfo.InvariantCulture)),
                        Csv(obs.Outcome), Csv(obs.ExitReason),
                        Csv(obs.OutcomeTime == DateTime.MinValue ? "" : obs.OutcomeTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)),
                        Csv(Fmt(obs.OutcomePrice)), Csv(Fmt(obs.OutcomePnlTicks)), Csv(Fmt(pnlCurrency)), Csv(obs.AmbiguousBoth.ToString()),
                        Csv(Fmt(obs.MfeTicks)), Csv(Fmt(obs.MaeTicks)), Csv(Fmt(obs.Forward5Ticks)), Csv(Fmt(obs.Forward15Ticks)), Csv(Fmt(obs.Forward30Ticks)), Csv(Fmt(obs.Forward60Ticks)),
                        Csv(c.StopTicks.ToString(CultureInfo.InvariantCulture)), Csv(c.TargetTicks.ToString(CultureInfo.InvariantCulture)), Csv(Fmt(c.AtrTicks)),
                        Csv(Fmt(c.SignalRangeTicks)), Csv(Fmt(c.ClosePositionInBar)), Csv(Fmt(c.DirectionalCloseStrength)),
                        Csv(Fmt(c.SignalOpen)), Csv(Fmt(c.SignalHigh)), Csv(Fmt(c.SignalLow)), Csv(Fmt(c.SignalClose)),
                        Csv(c.LevelName), Csv(Fmt(c.Level)), Csv(Fmt(c.Pdc)), Csv(Fmt(c.Onh)), Csv(Fmt(c.Onl)), Csv(Fmt(c.Pmh)), Csv(Fmt(c.Pml)), Csv(Fmt(c.RthOpen)),
                        Csv(Fmt(c.RthVsPdcTicks)), Csv(Fmt(c.PmhVsOnhTicks)), Csv(Fmt(c.OnhMinusPmhTicks)),
                        Csv(c.PriorRthBarCount.ToString(CultureInfo.InvariantCulture)), Csv(c.PriorRthReducedSession.ToString()),
                        Csv(obs.TickDriven.ToString()), Csv(c.ContextSnapshot), Csv(c.Reason)
                    };
                    signalWriter.WriteLine(string.Join(",", fields));
                }
                catch (Exception ex)
                {
                    ReportLoggingFailure("signals.csv", ex);
                }
            }
        }

        private void WriteBracketCsv(ShadowObservation obs, string status)
        {
            if (!EnableFileLogging || !EnableBracketMatrix || bracketWriter == null ||
                obs == null || obs.Candidate == null || obs.Brackets == null || obs.Brackets.Count == 0)
                return;

            CandidateSignal c = obs.Candidate;
            lock (logSync)
            {
                try
                {
                    foreach (BracketOutcome bracket in obs.Brackets)
                    {
                        double pnlCurrency = IsFinite(bracket.OutcomePnlTicks)
                            ? bracket.OutcomePnlTicks * TickSize * Instrument.MasterInstrument.PointValue
                            : double.NaN;

                        bracketWriter.WriteLine(string.Join(",", new[]
                        {
                            Csv(obs.Sequence.ToString(CultureInfo.InvariantCulture)),
                            Csv(status),
                            Csv(c.Id),
                            Csv(c.Family),
                            Csv(c.TradingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                            Csv(c.SignalBarStart.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                            Csv(c.SignalOrdinalToday.ToString(CultureInfo.InvariantCulture)),
                            Csv(obs.EntrySource),
                            Csv(obs.EntryTime == DateTime.MinValue ? "" : obs.EntryTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)),
                            Csv(Fmt(obs.EntryPrice)),
                            Csv(obs.TickDriven.ToString()),
                            Csv(bracket.BracketId),
                            Csv(bracket.StopTicks.ToString(CultureInfo.InvariantCulture)),
                            Csv(bracket.TargetType),
                            Csv(IsFinite(bracket.RewardRisk) ? bracket.RewardRisk.ToString("0.0", CultureInfo.InvariantCulture) : ""),
                            Csv(bracket.TargetTicks.ToString(CultureInfo.InvariantCulture)),
                            Csv(bracket.Outcome),
                            Csv(bracket.ExitReason),
                            Csv(bracket.OutcomeTime == DateTime.MinValue ? "" : bracket.OutcomeTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)),
                            Csv(Fmt(bracket.OutcomePrice)),
                            Csv(Fmt(bracket.OutcomePnlTicks)),
                            Csv(Fmt(pnlCurrency)),
                            Csv(bracket.AmbiguousBoth.ToString()),
                            Csv(Fmt(c.AtrTicks)),
                            Csv(Fmt(c.RthVsPdcTicks)),
                            Csv(Fmt(c.OnhMinusPmhTicks)),
                            Csv(Fmt(c.SignalRangeTicks)),
                            Csv(c.ContextSnapshot)
                        }));
                    }
                }
                catch (Exception ex)
                {
                    ReportLoggingFailure("brackets.csv", ex);
                }
            }
        }

        private void WriteActualBracketCsv(ActualFillBracketObservation obs, string status)
        {
            if (!EnableFileLogging || !EnableActualFillBracketMatrix || actualBracketWriter == null ||
                obs == null || obs.Candidate == null || obs.Brackets == null || obs.Brackets.Count == 0)
                return;

            CandidateSignal c = obs.Candidate;
            lock (logSync)
            {
                try
                {
                    foreach (BracketOutcome bracket in obs.Brackets)
                    {
                        double pnlCurrency = IsFinite(bracket.OutcomePnlTicks)
                            ? bracket.OutcomePnlTicks * TickSize * Instrument.MasterInstrument.PointValue
                            : double.NaN;

                        actualBracketWriter.WriteLine(string.Join(",", new[]
                        {
                            Csv(obs.Sequence.ToString(CultureInfo.InvariantCulture)),
                            Csv(status),
                            Csv(c.Id),
                            Csv(c.Family),
                            Csv(c.TradingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                            Csv(c.SignalBarStart.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                            Csv(c.SignalOrdinalToday.ToString(CultureInfo.InvariantCulture)),
                            Csv("ActualNinjaTraderFill"),
                            Csv(obs.EntryTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)),
                            Csv(Fmt(obs.EntryPrice)),
                            Csv(obs.TickDriven.ToString()),
                            Csv(obs.BarsObserved.ToString(CultureInfo.InvariantCulture)),
                            Csv(bracket.BracketId),
                            Csv(bracket.StopTicks.ToString(CultureInfo.InvariantCulture)),
                            Csv(bracket.TargetType),
                            Csv(IsFinite(bracket.RewardRisk) ? bracket.RewardRisk.ToString("0.0", CultureInfo.InvariantCulture) : ""),
                            Csv(bracket.TargetTicks.ToString(CultureInfo.InvariantCulture)),
                            Csv(bracket.Outcome),
                            Csv(bracket.ExitReason),
                            Csv(bracket.OutcomeTime == DateTime.MinValue ? "" : bracket.OutcomeTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)),
                            Csv(Fmt(bracket.OutcomePrice)),
                            Csv(Fmt(bracket.OutcomePnlTicks)),
                            Csv(Fmt(pnlCurrency)),
                            Csv(bracket.AmbiguousBoth.ToString()),
                            Csv(c.StopTicks.ToString(CultureInfo.InvariantCulture)),
                            Csv(c.TargetTicks.ToString(CultureInfo.InvariantCulture)),
                            Csv(Fmt(c.AtrTicks)),
                            Csv(Fmt(c.RthVsPdcTicks)),
                            Csv(Fmt(c.OnhMinusPmhTicks)),
                            Csv(Fmt(c.SignalRangeTicks)),
                            Csv(c.ContextSnapshot)
                        }));
                    }
                }
                catch (Exception ex)
                {
                    ReportLoggingFailure("actual_brackets.csv", ex);
                }
            }
        }

        private void WriteEquityCsv(DateTime barTime, double equityAtClose)
        {
            if (!EnableFileLogging || !EnableEquityCsv || equityWriter == null)
                return;

            double openUnrealized = equityAtClose - runGrossPnl;
            double dd = equityPeakAtBarClose - equityAtClose;

            lock (logSync)
            {
                try
                {
                    equityWriter.WriteLine(string.Join(",", new[]
                    {
                        Csv(barTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                        Csv(currentTradingDate == DateTime.MinValue ? "" : currentTradingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                        Csv(runGrossPnl.ToString("0.00", CultureInfo.InvariantCulture)),
                        Csv(openUnrealized.ToString("0.00", CultureInfo.InvariantCulture)),
                        Csv(equityAtClose.ToString("0.00", CultureInfo.InvariantCulture)),
                        Csv(equityPeakAtBarClose.ToString("0.00", CultureInfo.InvariantCulture)),
                        Csv(dd.ToString("0.00", CultureInfo.InvariantCulture)),
                        Csv(maxDrawdownAtBarClose.ToString("0.00", CultureInfo.InvariantCulture)),
                        Csv(playbackTickTelemetrySeen.ToString()),
                        Csv(lastPlaybackEquity.ToString("0.00", CultureInfo.InvariantCulture)),
                        Csv(equityPeakPlayback.ToString("0.00", CultureInfo.InvariantCulture)),
                        Csv(maxDrawdownPlayback.ToString("0.00", CultureInfo.InvariantCulture)),
                        Csv(Position != null ? Position.MarketPosition.ToString() : ""),
                        Csv(ActiveModelId())
                    }));
                }
                catch (Exception ex)
                {
                    ReportLoggingFailure("equity.csv", ex);
                }
            }
        }

        private void WriteSessionCsv(DateTime rthDate, int rthBars, bool reduced, double close)
        {
            if (!EnableFileLogging || sessionWriter == null || rthDate == DateTime.MinValue)
                return;
            if (rthDate == lastWrittenSessionDate)
                return;

            lock (logSync)
            {
                try
                {
                    sessionWriter.WriteLine(string.Join(",", new[]
                    {
                        Csv(rthDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                        Csv(rthBars.ToString(CultureInfo.InvariantCulture)),
                        Csv(ExpectedFullRthBars.ToString(CultureInfo.InvariantCulture)),
                        Csv(ReducedSessionMaxRthBars.ToString(CultureInfo.InvariantCulture)),
                        Csv(reduced.ToString()),
                        Csv(Fmt(close))
                    }));
                    lastWrittenSessionDate = rthDate;
                }
                catch (Exception ex)
                {
                    ReportLoggingFailure("sessions.csv", ex);
                }
            }
        }

        private void ReportLoggingFailure(string file, Exception ex)
        {
            if (!logFailurePrinted)
            {
                logFailurePrinted = true;
                Print(Name + ": File logging failed for " + file + ": " + ex.Message);
            }
        }

        private string Csv(string value)
        {
            if (value == null) return "";
            bool quote = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
            string escaped = value.Replace("\"", "\"\"");
            return quote ? "\"" + escaped + "\"" : escaped;
        }

        private string MakeSafeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "unknown";
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder(value.Length);
            foreach (char ch in value)
                sb.Append(invalid.Contains(ch) ? '_' : ch);
            return sb.ToString().Replace(' ', '_');
        }

        private void LogDailySummary(DateTime time, string reason)
        {
            if (currentTradingDate == DateTime.MinValue)
                return;

            LogEvent(time, "DAILY_SUMMARY", "",
                string.Format(CultureInfo.InvariantCulture,
                    "Reason={0}; TradingDate={1:yyyy-MM-dd}; Trades={2}; Wins={3}; Losses={4}; DailyGross={5:0.00}; " +
                    "RTHCTX={6}; PDCCTX={7}; PMHCTX={8}; ONHCTX={9}; PlaybackMaxDD={10:0.00}; BarCloseMaxDD={11:0.00}",
                    reason, currentTradingDate, tradesToday, winnersToday, losersToday, dailyRealizedGross,
                    GetModelTradesToday(SigRthContext), GetModelTradesToday(SigPdcContext),
                    GetModelTradesToday(SigPmhContext), GetModelTradesToday(SigOnhContext),
                    maxDrawdownPlayback, maxDrawdownAtBarClose));
        }

        private void LogRunSummary(DateTime time)
        {
            LogEvent(time, "RUN_SUMMARY", "",
                string.Format(CultureInfo.InvariantCulture,
                    "Trades={0}; Wins={1}; Losses={2}; Flats={3}; GrossPnL={4:0.00}; PlaybackTickTelemetrySeen={5}; " +
                    "PlaybackMaxDD={6:0.00}@{7}; BarCloseMaxDD={8:0.00}@{9}",
                    runTrades, runWins, runLosses, runFlats, runGrossPnl, playbackTickTelemetrySeen,
                    maxDrawdownPlayback, maxDrawdownPlaybackTime == DateTime.MinValue ? "" : maxDrawdownPlaybackTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    maxDrawdownAtBarClose, maxDrawdownAtBarCloseTime == DateTime.MinValue ? "" : maxDrawdownAtBarCloseTime.ToString("yyyy-MM-dd HH:mm:ss")));

            foreach (string id in AllModelIds())
            {
                ModelStats s = GetStats(id);
                LogEvent(time, "MODEL_SUMMARY", id,
                    string.Format(CultureInfo.InvariantCulture,
                        "Qualified={0}; Taken={1}; SkippedActual={2}; Wins={3}; Losses={4}; Flats={5}; GrossPnL={6:0.00}; " +
                        "ShadowCompleted={7}; ShadowIncomplete={8}; ShadowTargetFirst={9}; ShadowStopFirst={10}; ShadowTimeExit={11}; ShadowAmbiguous={12}; ShadowPnLTicks={13:0.0}",
                        s.Qualified, s.Taken, s.Skipped, s.Wins, s.Losses, s.Flats, s.GrossPnl,
                        s.ShadowCompleted, s.ShadowIncomplete, s.ShadowTargetFirst, s.ShadowStopFirst, s.ShadowTimeExit, s.ShadowAmbiguous, s.ShadowPnlTicks));
            }
        }

        #region Properties

        [NinjaScriptProperty]
        [Display(Name = "Enable PDC contextual long", GroupName = "01. Models", Order = 1)]
        public bool EnablePdcContextLong { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable RTH contextual long", GroupName = "01. Models", Order = 2)]
        public bool EnableRthContextLong { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable PMH contextual short", GroupName = "01. Models", Order = 3)]
        public bool EnablePmhContextShort { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable ONH contextual long", GroupName = "01. Models", Order = 4)]
        public bool EnableOnhContextLong { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Shadow PDC baseline comparator", GroupName = "01. Models", Order = 5)]
        public bool EnableRawPdcBaseShadow { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Shadow raw PMH/ONH/RTH comparators", GroupName = "01. Models", Order = 6)]
        public bool EnableRawComparatorShadowSignals { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "PDC start time", Description = "Eligible signal-candle START time, ET HHmmss.", GroupName = "02. Signal Windows", Order = 1)]
        public int PdcStartTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "PMH start time", GroupName = "02. Signal Windows", Order = 2)]
        public int PmhStartTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "PMH=ONH confluence start", GroupName = "02. Signal Windows", Order = 3)]
        public int PmhOnhConfluenceStartTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "ONH start time", GroupName = "02. Signal Windows", Order = 4)]
        public int OnhStartTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "RTH start time", GroupName = "02. Signal Windows", Order = 5)]
        public int RthStartTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Last signal time", GroupName = "02. Signal Windows", Order = 6)]
        public int LastEntryTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "Required break ticks", GroupName = "03. Signal Precision", Order = 1)]
        public int RequiredBreakTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "PMH/ONH confluence tolerance ticks", GroupName = "03. Signal Precision", Order = 2)]
        public int PmhOnhConfluenceToleranceTicks { get; set; }

        [NinjaScriptProperty]
        [Range(-500, 500)]
        [Display(Name = "PDC context max RTH-PDC ticks", GroupName = "04. Context Filters", Order = 1)]
        public int PdcContextMaxRthVsPdcTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "PDC context max ONH-PMH ticks", GroupName = "04. Context Filters", Order = 2)]
        public int PdcContextMaxOnhMinusPmhTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "PMH context end time (exclusive)", GroupName = "04. Context Filters", Order = 3)]
        public int PmhContextEndTimeExclusive { get; set; }

        [NinjaScriptProperty]
        [Range(0, 300)]
        [Display(Name = "PMH min rejection range ticks", GroupName = "04. Context Filters", Order = 4)]
        public int PmhContextMinSignalRangeTicks { get; set; }

        [NinjaScriptProperty]
        [Range(-500, 500)]
        [Display(Name = "PMH context max RTH-PDC ticks", GroupName = "04. Context Filters", Order = 5)]
        public int PmhContextMaxRthVsPdcTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 300)]
        [Display(Name = "ONH min ATR ticks", GroupName = "04. Context Filters", Order = 6)]
        public int OnhContextMinAtrTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "ONH max ATR ticks", GroupName = "04. Context Filters", Order = 7)]
        public int OnhContextMaxAtrTicks { get; set; }

        [NinjaScriptProperty]
        [Range(-500, 500)]
        [Display(Name = "ONH context max RTH-PDC ticks", GroupName = "04. Context Filters", Order = 8)]
        public int OnhContextMaxRthVsPdcTicks { get; set; }

        [NinjaScriptProperty]
        [Range(-500, 500)]
        [Display(Name = "RTH context max RTH-PDC ticks", GroupName = "04. Context Filters", Order = 9)]
        public int RthContextMaxRthVsPdcTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "RTH context max ONH-PMH ticks", GroupName = "04. Context Filters", Order = 10)]
        public int RthContextMaxOnhMinusPmhTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Contracts", GroupName = "05. Baseline / Raw Risk", Order = 1)]
        public int Contracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Stop mode", GroupName = "05. Baseline / Raw Risk", Order = 2)]
        public NinjexInitialStopMode StopMode { get; set; }

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "Stop buffer ticks", GroupName = "05. Baseline / Raw Risk", Order = 3)]
        public int StopBufferTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Min stop ticks", GroupName = "05. Baseline / Raw Risk", Order = 4)]
        public int MinStopTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "Max stop ticks", GroupName = "05. Baseline / Raw Risk", Order = 5)]
        public int MaxStopTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "Fixed stop ticks", GroupName = "05. Baseline / Raw Risk", Order = 6)]
        public int FixedStopTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Target mode", GroupName = "05. Baseline / Raw Risk", Order = 7)]
        public NinjexTargetMode TargetMode { get; set; }

        [NinjaScriptProperty]
        [Range(0.25, 10.0)]
        [Display(Name = "Reward/risk", GroupName = "05. Baseline / Raw Risk", Order = 8)]
        public double RewardRisk { get; set; }

        [NinjaScriptProperty]
        [Range(1, 400)]
        [Display(Name = "Fixed profit ticks", GroupName = "05. Baseline / Raw Risk", Order = 9)]
        public int FixedProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "RTH actual stop ticks", Description = "Research5 fixed RTH exit.", GroupName = "05A. Research5 Actual Exits", Order = 1)]
        public int RthActualStopTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 400)]
        [Display(Name = "RTH actual target ticks", Description = "Research5 fixed RTH exit.", GroupName = "05A. Research5 Actual Exits", Order = 2)]
        public int RthActualTargetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "PDC actual stop ticks", Description = "Research5 fixed PDC exit.", GroupName = "05A. Research5 Actual Exits", Order = 3)]
        public int PdcActualStopTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 400)]
        [Display(Name = "PDC actual target ticks", Description = "Research5 fixed PDC exit.", GroupName = "05A. Research5 Actual Exits", Order = 4)]
        public int PdcActualTargetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "ONH actual stop ticks", Description = "Research5 fixed ONH exit.", GroupName = "05A. Research5 Actual Exits", Order = 7)]
        public int OnhActualStopTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 400)]
        [Display(Name = "ONH actual target ticks", Description = "Research5 fixed ONH exit.", GroupName = "05A. Research5 Actual Exits", Order = 8)]
        public int OnhActualTargetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 48)]
        [Display(Name = "Max hold bars (5m)", Description = "12 bars = exactly 60 completed minutes after the signal bar.", GroupName = "05. Baseline / Raw Risk", Order = 10)]
        public int MaxHoldBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Flatten at bar-close time", GroupName = "05. Baseline / Raw Risk", Order = 11)]
        public int FlattenAtBarCloseTime { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Max trades per day", GroupName = "06. Daily Limits", Order = 1)]
        public int MaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Max winners per day", GroupName = "06. Daily Limits", Order = 2)]
        public int MaxWinnersPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Max losers per day", GroupName = "06. Daily Limits", Order = 3)]
        public int MaxLosersPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Max trades per model per day", GroupName = "06. Daily Limits", Order = 4)]
        public int MaxTradesPerModelPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Max trades per family per day", Description = "Safety cap; Research5 has one actual-trade model per level family.", GroupName = "06. Daily Limits", Order = 5)]
        public int MaxTradesPerFamilyPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(50.0, 5000.0)]
        [Display(Name = "Daily loss limit ($)", GroupName = "06. Daily Limits", Order = 6)]
        public double DailyLossLimitCurrency { get; set; }

        [NinjaScriptProperty]
        [Range(2, 100)]
        [Display(Name = "ATR period", GroupName = "07. Research", Order = 1)]
        public int AtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Expected full RTH bars", Description = "Normally 78 five-minute bars from 09:30 through 16:00.", GroupName = "07. Research", Order = 2)]
        public int ExpectedFullRthBars { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Reduced session max RTH bars", Description = "Prior RTH session is flagged reduced when bar count is <= this value.", GroupName = "07. Research", Order = 3)]
        public int ReducedSessionMaxRthBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable file logging", GroupName = "07. Research", Order = 4)]
        public bool EnableFileLogging { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable equity CSV", GroupName = "07. Research", Order = 5)]
        public bool EnableEquityCsv { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable bracket matrix CSV", Description = "Tracks 28 counterfactual stop/target combinations from the first Playback tick of each first signal/day.", GroupName = "07. Research", Order = 6)]
        public bool EnableBracketMatrix { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable actual-fill bracket matrix CSV", Description = "Tracks the same 28 combinations from exact NinjaTrader entry fills for actual trades.", GroupName = "07. Research", Order = 7)]
        public bool EnableActualFillBracketMatrix { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Print important events", GroupName = "07. Research", Order = 8)]
        public bool PrintImportantEvents { get; set; }

        #endregion
    }
}