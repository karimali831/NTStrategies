using System;
using System.IO;
using NinjaTrader.Cbi;
using NinjaTrader.Data;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexEsEvaluationScalper : Strategy
    {
        private EvaluationSignalEngine engine;
        private EvaluationResearchWriter research;
        private EvaluationShadowResearch shadows;
        private DateTime lastContext, lastObserved;
        private double evaluationPeak;
        private long equitySequence;
        private DateTime lastEquityTime;
        private double lastWrittenRealized=double.NaN,lastWrittenLiquidation=double.NaN;
        private int lastWrittenQuantity=-1;
        private string lastWrittenSignal;
        private int lastTickBar = -1, dailyEntries, filledQuantity, requestedEntryQuantity, entryFilledTotal;
        private Order workingEntry;
        private double realized, dayStart, averageEntry, tradeGross, tradeFees, tradeMfe, tradeMae;
        private DateTime day, entryTime, lastExit = DateTime.MinValue;
        private string activeSignal;
        private bool pendingEntry, exiting, faulted;
        private int activeDirection;
        private EvaluationCandidate activeCandidate;
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "NinjexEsEvaluationScalper";
                Description = "ES evaluation research v1.1: tick-driven sweeps and momentum retests. Pass rate unvalidated.";
                Calculate = Calculate.OnEachTick;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                StartBehavior = StartBehavior.WaitUntilFlat;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                BarsRequiredToTrade = 0;
                IsInstantiatedOnEachOptimizationIteration = true;
                Contracts = 2; RiskPerTrade = 400; MinStopTicks = 12; MaxStopTicks = 28;
                RewardRisk = 1.5; DailyLossLimit = 650; DailyProfitLimit = 1200;
                MaxTradesPerDay = 12; CooldownSeconds = 90; MaxHoldSeconds = 300;
                SweepTicks = 3; ReclaimTicks = 2; BreakoutTicks = 4; RetestTicks = 2;
                ConfirmTicks = 2; SetupExpirySeconds = 180;
                CommissionPerSide = 2.5; ExportRawTicks = true; AllowLiveAccounts = false;
                FirstTradeDate = new DateTime(2025, 9, 15); LastTradeDate = new DateTime(2099, 12, 31);
                OutputFolder = "NinjexData"; EnforceEvaluationLimits = false;
                UseConfirmedEntries=true; UseRollingLevelEntries=false; TrendFilterMode=1;
                ExportEveryTickEquity=false; StopAtrMultiplier=.75; ReclaimHoldSeconds=2; QuietSessionGapSeconds=300;
            }
            else if (State == State.Configure) AddDataSeries(BarsPeriodType.Tick, 1);
            else if (State == State.DataLoaded)
            {
                if (Instrument.MasterInstrument.Name != "ES" || TickSize != .25)
                    throw new InvalidOperationException("Use an individual ES contract (not MES/NQ).");
                string zone = NinjaTrader.Core.Globals.GeneralOptions.TimeZoneInfo.Id;
                if (zone != "Eastern Standard Time" && zone != "America/New_York" && zone != "US/Eastern")
                    throw new InvalidOperationException("Set NinjaTrader platform time zone to Eastern (EST/EDT).");
                if (Bars.TradingHours.Name != "CME US Index Futures ETH" || BarsArray[1].TradingHours.Name != "CME US Index Futures ETH")
                    throw new InvalidOperationException("Use CME US Index Futures ETH on the primary chart.");
                if (BarsPeriod.BarsPeriodType != BarsPeriodType.Minute || BarsPeriod.Value != 1)
                    throw new InvalidOperationException("Use a 1-minute primary ES chart. Signals use the added 1-tick series; managed orders use primary Bars context.");
                if (MinStopTicks > MaxStopTicks || FirstTradeDate > LastTradeDate || string.IsNullOrWhiteSpace(OutputFolder))
                    throw new InvalidOperationException("Invalid stop/date/output parameters.");
                engine = new EvaluationSignalEngine(TickSize, SweepTicks, ReclaimTicks, BreakoutTicks,
                    RetestTicks, ConfirmTicks, SetupExpirySeconds,new EvaluationEngineOptions {
                        ConfirmedEntries=UseConfirmedEntries,ReclaimHoldSeconds=ReclaimHoldSeconds,QuietGapSeconds=QuietSessionGapSeconds });
                research = new EvaluationResearchWriter(Path.Combine(NinjaTrader.Core.Globals.UserDataDir, OutputFolder), Instrument.FullName, ExportRawTicks);
                engine.MinuteClosed = m => research.Write("minutes",m.Start,m.End,State,m.Open,m.High,m.Low,m.Close,m.Volume,m.Complete);
                engine.TrendBarClosed = (period,end,open,high,low,close,volume,complete) => research.Write("timeframes",period,end,State,open,high,low,close,volume,complete);
                shadows = new EvaluationShadowResearch(research, TickSize);
                research.Write("manifest", "Version", "1.1.1");
                research.Write("manifest", "PlatformZone", zone);
                research.Write("manifest", "Instrument", Instrument.FullName);
                research.Write("manifest", "PointValue", Instrument.MasterInstrument.PointValue);
                research.Write("manifest", "TradingHours", Bars.TradingHours.Name);
                research.Write("manifest", "Parameters", ParameterSummary());
                Print(Name + " | Research=" + research.DirectoryPath + " | Actual strategy orders enabled; pass rate not yet established.");
            }
            else if (State == State.Realtime)
            {
                // Historical warm-up observations are useful, but must never contaminate replay fills/equity.
                if (Position.MarketPosition != MarketPosition.Flat)
                    throw new InvalidOperationException("Start replay flat; historical strategy position is open. Start earlier or set historical processing flat.");
                realized = dayStart = evaluationPeak = 0; lastExit = DateTime.MinValue; dailyEntries = filledQuantity = 0;
                pendingEntry = exiting = false; activeSignal = null;
                research.Write("manifest", "RealtimeStart", DateTime.UtcNow.ToString("o"));
            }
            else if (State == State.Terminated && research != null)
            {
                shadows.Finish(lastObserved);
                research.Write("manifest", "TerminatedWithOpenQuantity", filledQuantity);
                research.Dispose(); research = null;
            }
        }
        protected override void OnBarUpdate()
        {
            if (engine == null || BarsInProgress != 1 || CurrentBars[1] < 0 || lastTickBar == CurrentBars[1]) return;
            lastTickBar = CurrentBars[1];
            DateTime time = Times[1][0];
            if(lastObserved!=default(DateTime) && time<lastObserved)
            {research.Write("quality",time,State,"OutOfOrder",lastObserved.ToString("o"));return;}
            lastObserved = time; double price = Closes[1][0], volume = Volumes[1][0];
            if (time.Date != day)
            {
                day = time.Date; dailyEntries = 0; dayStart = realized;
                research.Write("sessions", time, State, "Start", realized);
            }
            var candidates = engine.Accept(time, price, volume, (kind, detail) => research.Write("quality", time, State, kind, detail));
            bool inDate = time.Date >= FirstTradeDate.Date && time.Date <= LastTradeDate.Date;
            if (State == State.Realtime && inDate)
            {
                double unrealized = filledQuantity * activeDirection * (price - averageEntry) * Instrument.MasterInstrument.PointValue;
                double equity = realized + unrealized - filledQuantity * CommissionPerSide;
                evaluationPeak = Math.Max(evaluationPeak, equity);
                if (EnforceEvaluationLimits && (evaluationPeak-equity >= 1900 || (realized >= 3000 && filledQuantity == 0)))
                {
                    if (!faulted) research.Write("quality", time, State, "EvaluationLocked", "Pass or 1900 USD trailing guard reached");
                    faulted = true;
                }
                shadows.Accept(time, price, engine.InWindow(time));
                if (lastContext.Date != time.Date || lastContext.Hour != time.Hour || lastContext.Minute != time.Minute)
                {
                    research.Write("context",time,engine.Vwap,engine.AtrTicks,engine.LevelSummary,engine.Ema9,engine.Ema21,
                        engine.Trend15.AsOf,engine.Trend15.IsCurrent(time),engine.Trend15.Direction,engine.Trend15.Strength,
                        engine.Trend30.AsOf,engine.Trend30.IsCurrent(time),engine.Trend30.Direction,engine.Trend30.Strength,
                        engine.Trend60.AsOf,engine.Trend60.IsCurrent(time),engine.Trend60.Direction,engine.Trend60.Strength);
                    lastContext = time;
                }
                if (filledQuantity > 0)
                {
                    tradeMfe = Math.Max(tradeMfe, unrealized); tradeMae = Math.Min(tradeMae, unrealized);
                    if (!engine.InWindow(time) || (time - entryTime).TotalSeconds >= MaxHoldSeconds ||
                        equity - dayStart <= -DailyLossLimit || equity - dayStart >= DailyProfitLimit || faulted) Flatten("RiskOrTime");
                }
                if (engine.InWindow(time) || filledQuantity > 0)
                    WriteEquity(time,realized,unrealized,equity);
                research.Raw(time, price, volume, filledQuantity, engine.InWindow(time));
            }
            foreach (var candidate in candidates)
            {
                int stopTicks = Math.Max(MinStopTicks, Math.Max((int)Math.Ceiling(Math.Abs(price - candidate.Stop) / TickSize),
                    double.IsNaN(engine.AtrTicks)?MinStopTicks:(int)Math.Ceiling(engine.AtrTicks*StopAtrMultiplier)));
                double riskBudget = Math.Min(RiskPerTrade, Math.Max(0, DailyLossLimit + realized - dayStart));
                if (EnforceEvaluationLimits) riskBudget = Math.Min(riskBudget, Math.Max(0, 1900 - (evaluationPeak-realized)));
                int quantity = Math.Min(Contracts, (int)Math.Floor(riskBudget / (stopTicks * TickSize * Instrument.MasterInstrument.PointValue + 2 * CommissionPerSide)));
                string contextReason=EntryContextReason(candidate);
                string reason = State != State.Realtime ? "HistoricalWarmup" : !inDate ? "OutsideDate" :
                    faulted ? "Faulted" : contextReason.Length>0 ? contextReason : (!AllowLiveAccounts && Account != null && Account.Name.IndexOf("Playback", StringComparison.OrdinalIgnoreCase) < 0 && Account.Name.IndexOf("Sim", StringComparison.OrdinalIgnoreCase) < 0) ? "LiveAccountDisabled" :
                    pendingEntry || filledQuantity > 0 || Position.MarketPosition != MarketPosition.Flat ? "Occupied" :
                    dailyEntries >= MaxTradesPerDay ? "TradeCap" : realized - dayStart <= -DailyLossLimit ? "DailyLoss" :
                    realized - dayStart >= DailyProfitLimit ? "DailyProfit" : (time - lastExit).TotalSeconds < CooldownSeconds ? "Cooldown" :
                    stopTicks > MaxStopTicks ? "StopTooWide" : quantity < 1 ? "RiskBudget" : "Submitted";
                research.Write("candidates", time, State, candidate.Id, candidate.Model, candidate.Direction, candidate.LevelName,
                    candidate.Level, price, candidate.Stop, stopTicks, quantity, engine.Vwap, engine.AtrTicks, reason,candidate.ConfirmationTime,engine.Ema9,engine.Ema21,
                    engine.Trend15.AsOf,engine.Trend15.IsCurrent(time),engine.Trend15.Direction,
                    engine.Trend30.AsOf,engine.Trend30.IsCurrent(time),engine.Trend30.Direction,
                    engine.Trend60.AsOf,engine.Trend60.IsCurrent(time),engine.Trend60.Direction);
                if (State == State.Realtime && inDate) shadows.Register(candidate,time,price);
                if (reason == "Submitted") Submit(candidate, time, stopTicks, quantity);
            }
            research.Checkpoint(time);
        }
        private string EntryContextReason(EvaluationCandidate candidate)
        {
            if(!UseRollingLevelEntries && candidate.LevelName.StartsWith("Rolling",StringComparison.Ordinal))return "RollingLevelDisabled";
            int d=candidate.Direction;
            if(TrendFilterMode==1 && engine.Trend15.IsCurrent(lastObserved) && engine.Trend30.IsCurrent(lastObserved) && engine.Trend15.Direction==-d && engine.Trend30.Direction==-d)return "Opposing15m30mTrend";
            if(TrendFilterMode==2 && (!engine.Trend15.IsCurrent(lastObserved) || engine.Trend15.Direction!=d))return "15mNotAligned";
            if(TrendFilterMode==3 && (!engine.Trend15.IsCurrent(lastObserved) || !engine.Trend30.IsCurrent(lastObserved) || !engine.Trend60.IsCurrent(lastObserved) || engine.Trend15.Direction!=d || engine.Trend30.Direction!=d || engine.Trend60.Direction!=d))return "AllTimeframesNotAligned";
            return "";
        }
        private void WriteEquity(DateTime executionTime,double realizedNet,double unrealized,double liquidation)
        {
            // Ordered observation clock is distinct from provider execution timestamps,
            // which Playback may round ahead of the current tick by up to one second.
            DateTime observed=lastObserved==default(DateTime)?executionTime:lastObserved;
            string signal=activeSignal ?? "";
            bool heartbeat=observed.Date!=lastEquityTime.Date || observed.Hour!=lastEquityTime.Hour || observed.Minute!=lastEquityTime.Minute;
            bool changed=realizedNet!=lastWrittenRealized || liquidation!=lastWrittenLiquidation || filledQuantity!=lastWrittenQuantity || signal!=lastWrittenSignal;
            if(!ExportEveryTickEquity && !heartbeat && !changed)return;
            research.Write("equity",observed,State,realizedNet,unrealized,liquidation,filledQuantity,signal,++equitySequence,executionTime);
            lastEquityTime=observed;lastWrittenRealized=realizedNet;lastWrittenLiquidation=liquidation;lastWrittenQuantity=filledQuantity;lastWrittenSignal=signal;
        }
    }
}
