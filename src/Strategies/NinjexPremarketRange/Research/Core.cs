#region Using declarations
using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Analysis;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Models;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Risk;
using NinjaTrader.NinjaScript.Ninjex;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexPremarketRangeResearch : Strategy
    {
        private NinjexPremarketRangeEngine premarketRangeEngine;

        private readonly List<IEntryModel> entryModels =
            new List<IEntryModel>();

        private readonly List<CandleSnapshot> candleHistory =
            new List<CandleSnapshot>();

        private readonly List<BreakoutEvent> breakoutEvents =
            new List<BreakoutEvent>();

        private readonly List<EntryCandidate> entryCandidates =
            new List<EntryCandidate>();

        private readonly List<HypotheticalTrade> activeTrades =
            new List<HypotheticalTrade>();

        private readonly BreakoutEventDetector breakoutDetector =
            new BreakoutEventDetector();

        private readonly CandleMetricsCalculator metricsCalculator =
            new CandleMetricsCalculator();

        private RangeSessionContext sessionContext;
        private DateTime activeTradingDate = Core.Globals.MinDate;
        private int lastProcessedPrimaryBar = -1;
        private double lastKnownTickPrice = double.NaN;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Ninjex Premarket Range Research";
                Description = "Research-only premarket range breakout and retest analyser. This strategy never submits orders.";

                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
                IsUnmanaged = false;
                BarsRequiredToTrade = 20;
                IsExitOnSessionCloseStrategy = false;
                                StartBehavior = StartBehavior.WaitUntilFlat;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;

                ApplyPropertyDefaults();
            }
            else if (State == State.Configure)
            {
                // The 1-tick series drives deterministic hypothetical fills,
                // MFE/MAE, stop, target, break-even and trailing simulations.
                AddDataSeries(BarsPeriodType.Tick, 1);
            }
            else if (State == State.DataLoaded)
            {
                premarketRangeEngine = new NinjexPremarketRangeEngine();

                ConfigureModels();
                InitializeExport();
            }
            else if (State == State.Terminated)
            {
                ForceCloseAllHypotheticalTrades(
                    DateTime.Now,
                    lastKnownTickPrice,
                    "StrategyTerminated");

                FlushAndDisposeExport();
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress == 1)
            {
                ProcessTickSeries();
                return;
            }

            if (BarsInProgress != 0 || CurrentBar < BarsRequiredToTrade)
                return;

            ProcessPrimarySeries();
        }

        private void ConfigureModels()
        {
            entryModels.Clear();

            entryModels.Add(new BreakoutConfirmationModel
            {
                IsEnabled = EnableBreakoutModel,
                MinimumBodyPercent = MinimumStrongBodyPercent,
                MinimumCloseLocationPercent = MinimumCloseLocationPercent,
                MinimumRelativeBodyMultiple = MinimumRelativeBodyMultiple
            });

            entryModels.Add(new RetestEntryModel
            {
                IsEnabled = EnableRetestModel,
                MaximumBarsAfterBreakout = MaximumRetestBars,
                OutsideDistanceTicks = RetestOutsideDistanceTicks,
                InsideDistanceTicks = RetestInsideDistanceTicks,
                MinimumConfirmationBodyPercent =
                    MinimumRetestConfirmationBodyPercent
            });
        }

       private void ProcessPrimarySeries()
        {
            if (lastProcessedPrimaryBar == CurrentBar)
                return;

            lastProcessedPrimaryBar = CurrentBar;

            var barTime = Time[1];
            var tradingDate = barTime.Date;

            var isRangeCompleteNow =
                premarketRangeEngine.ProcessCompletedBar(
                    barTime,
                    High[1],
                    Low[1],
                    30000,
                    93000);

            if (activeTradingDate != tradingDate)
                StartResearchDay(tradingDate);

            var bar = CapturePrimaryBar(1);

            var previousBar =
                candleHistory.Count > 0
                    ? candleHistory[candleHistory.Count - 1]
                    : null;

            var metrics =
                metricsCalculator.Calculate(
                    bar,
                    candleHistory,
                    RelativeBodyLookback,
                    TickSize);

            candleHistory.Add(bar);
            TrimHistory();

            if (isRangeCompleteNow)
            {
                Diagnostic(
                    barTime,
                    "Premarket range finalized. " +
                    "Date={0:yyyy-MM-dd}, High={1}, Low={2}, " +
                    "HighTime={3:HH:mm}, LowTime={4:HH:mm}",
                    premarketRangeEngine.LatestRangeDate,
                    premarketRangeEngine.LatestHigh,
                    premarketRangeEngine.LatestLow,
                    premarketRangeEngine.HighBarTime,
                    premarketRangeEngine.LowBarTime);
            }

            if (ToTime(barTime) >= FlattenTime)
            {
                ForceCloseAllHypotheticalTrades(
                    barTime,
                    Close[0],
                    "FlattenTime");
            }

            if (!premarketRangeEngine.IsRangeComplete
                || premarketRangeEngine.LatestRangeDate.Date
                    != tradingDate)
            {
                return;
            }

            EnsureSessionContext(tradingDate, barTime);

            if (!IsInsideEntryWindow(barTime))
                return;

            var context = new ModelBarContext
            {
                Session = sessionContext,
                Bar = bar,
                PreviousBar = previousBar,
                Metrics = metrics,
                History = candleHistory
            };

            var newBreakouts = breakoutDetector.Detect(
                context,
                MinimumBreakoutDistanceTicks);

            foreach (BreakoutEvent breakout in newBreakouts)
            {
                breakoutEvents.Add(breakout);
                ExportBreakout(breakout);

                Diagnostic(
                    breakout.BreakoutTime,
                    "BREAKOUT {0} {1} Level={2} Close={3} " +
                    "Distance={4:0.0} ticks",
                    breakout.EventId,
                    breakout.Direction,
                    breakout.RangeLevel,
                    breakout.BreakoutClose,
                    breakout.DistanceOutsideTicks);

                foreach (IEntryModel model in entryModels)
                    model.OnBreakout(breakout);
            }

            foreach (var model in entryModels)
            {
                var generated =
                    model.Evaluate(context);

                foreach (var candidate in generated)
                    RegisterCandidate(candidate);
            }

            UpdateBreakoutReturnInside(bar);
        }

        private void ProcessTickSeries()
        {
            if (CurrentBars[1] < 0)
                return;

            var tickTime = Times[1][0];
            var tickPrice = Closes[1][0];
            lastKnownTickPrice = tickPrice;

            if (activeTradingDate == Core.Globals.MinDate)
                return;

            FillPendingCandidates(tickTime, tickPrice);

            foreach (var trade in activeTrades.ToList())
            {
                trade.ProcessTick(tickTime, tickPrice);

                if (trade.IsClosed)
                {
                    ExportTrade(trade);
                    activeTrades.Remove(trade);
                }
            }

            UpdateBreakoutExcursions(tickTime, tickPrice);

            if (ToTime(tickTime) >= FlattenTime)
                ForceCloseAllHypotheticalTrades(
                    tickTime,
                    tickPrice,
                    "FlattenTime");
        }

        private void StartResearchDay(DateTime tradingDate)
        {
            if (activeTradingDate != Core.Globals.MinDate)
                ExportDailySummary(activeTradingDate);

            ForceCloseAllHypotheticalTrades(
                tradingDate,
                lastKnownTickPrice,
                "NewTradingDate");

            activeTradingDate = tradingDate;
            candleHistory.Clear();
            breakoutEvents.Clear();
            entryCandidates.Clear();

            breakoutDetector.Reset(tradingDate);
            sessionContext = null;

            foreach (var model in entryModels)
                model.Reset(null);

            Diagnostic(
                tradingDate,
                "New research day: {0:yyyy-MM-dd}",
                tradingDate);
        }

        private void EnsureSessionContext(DateTime tradingDate, DateTime eventTime)
        {
            var high = premarketRangeEngine.LatestHigh;
            var low = premarketRangeEngine.LatestLow;

            if (sessionContext != null
                && sessionContext.TradingDate == tradingDate
                && Math.Abs(sessionContext.PremarketHigh - high) < TickSize / 2.0
                && Math.Abs(sessionContext.PremarketLow - low) < TickSize / 2.0)
                return;

            sessionContext = new RangeSessionContext
            {
                TradingDate = tradingDate,
                PremarketHigh = high,
                PremarketLow = low,
                TickSize = TickSize,
                PointValue = Instrument.MasterInstrument.PointValue
            };

            foreach (var model in entryModels)
                model.Reset(sessionContext);

            ExportSession(sessionContext);

            Diagnostic(
                eventTime,
                "Range ready. Date={0:yyyy-MM-dd}, " +
                "High={1}, Low={2}, Width={3:0.0} ticks",
                tradingDate,
                high,
                low,
                sessionContext.RangeTicks);
        }

        private CandleSnapshot CapturePrimaryBar(int barsAgo)
        {
            return new CandleSnapshot
            {
                Time = Time[barsAgo],
                BarIndex = CurrentBar - barsAgo,
                Open = Open[barsAgo],
                High = High[barsAgo],
                Low = Low[barsAgo],
                Close = Close[barsAgo],
                Volume = Volume[barsAgo]
            };
        }

        private void RegisterCandidate(EntryCandidate candidate)
        {
            if (candidate == null)
                return;

            entryCandidates.Add(candidate);
            ExportCandidate(candidate);

            Diagnostic(
                candidate.SignalTime,
                "CANDIDATE {0} Qualified={1} Reason={2}",
                candidate.CandidateId,
                candidate.StrongCandleQualified,
                candidate.QualificationReason);
        }

        private void FillPendingCandidates(DateTime tickTime, double tickPrice)
        {
            if (sessionContext == null || double.IsNaN(tickPrice))
                return;

            foreach (var candidate in entryCandidates.ToList())
            {
                if (!candidate.StrongCandleQualified)
                    continue;

                if (candidate.PlannedEntryPrice > 0)
                    continue;

                if (tickTime <= candidate.SignalTime)
                    continue;

                if (ToTime(tickTime) >= FlattenTime)
                    continue;

                var entryDistanceTicks = candidate.Direction == TradeDirection.Long
                    ? (tickPrice - candidate.RangeLevel) / TickSize
                    : (candidate.RangeLevel - tickPrice) / TickSize;

                if (entryDistanceTicks < EntryMinimumDistanceTicksFromRange
                    || entryDistanceTicks > EntryMaximumDistanceTicksFromRange)
                {
                    candidate.StrongCandleQualified = false;
                    candidate.QualificationReason += string.Format(
                        " Entry rejected at next tick because distance from range was {0:0.0} ticks; permitted range is {1}-{2} ticks.",
                        entryDistanceTicks,
                        EntryMinimumDistanceTicksFromRange,
                        EntryMaximumDistanceTicksFromRange);
                    ExportCandidateUpdate(candidate);
                    continue;
                }

                PrepareCandidateRisk(candidate, tickPrice);

                if (candidate.ActualRiskTicks <= 0)
                {
                    candidate.QualificationReason += " Rejected because calculated risk was zero or negative.";
                    ExportCandidateUpdate(candidate);
                    continue;
                }

                CreateTradeVariants(candidate, tickTime, tickPrice);
                ExportCandidateUpdate(candidate);
            }
        }

        private void PrepareCandidateRisk(
            EntryCandidate candidate,
            double entryPrice)
        {
            candidate.PlannedEntryPrice = entryPrice;

            var structuralRiskTicks =
                candidate.Direction == TradeDirection.Long
                    ? (entryPrice - candidate.StructuralStopPrice) / TickSize
                    : (candidate.StructuralStopPrice - entryPrice) / TickSize;

            structuralRiskTicks = Math.Max(0, structuralRiskTicks);

            candidate.StructuralRiskTicks = structuralRiskTicks;
            candidate.ActualRiskTicks =
                Math.Min(MaximumInitialStopTicks, structuralRiskTicks);

            candidate.StopWasCapped =
                structuralRiskTicks > MaximumInitialStopTicks;

            candidate.PlannedStopPrice =
                candidate.Direction == TradeDirection.Long
                    ? entryPrice - candidate.ActualRiskTicks * TickSize
                    : entryPrice + candidate.ActualRiskTicks * TickSize;
        }

        private void CreateTradeVariants(
            EntryCandidate candidate,
            DateTime entryTime,
            double entryPrice)
        {
            var settings =
                BuildTradeManagementSettings();

            activeTrades.Add(new HypotheticalTrade(
                candidate,
                entryTime,
                entryPrice,
                settings,
                "FixedTarget",
                false,
                false));

            if (BEProfitTriggerTicks > 0)
            {
                activeTrades.Add(new HypotheticalTrade(
                    candidate,
                    entryTime,
                    entryPrice,
                    settings,
                    "BreakEven",
                    true,
                    false));
            }

            if (IsAnyTrailStepEnabled())
            {
                activeTrades.Add(new HypotheticalTrade(
                    candidate,
                    entryTime,
                    entryPrice,
                    settings,
                    "ThreeStepTrail",
                    false,
                    true));
            }

            if (BEProfitTriggerTicks > 0 && IsAnyTrailStepEnabled())
            {
                activeTrades.Add(new HypotheticalTrade(
                    candidate,
                    entryTime,
                    entryPrice,
                    settings,
                    "BreakEvenPlusTrail",
                    true,
                    true));
            }
        }

        private TradeManagementSettings BuildTradeManagementSettings()
        {
            return new TradeManagementSettings
            {
                TickSize = TickSize,
                PointValue = Instrument.MasterInstrument.PointValue,
                Quantity = Quantity,
                RiskRewardRatio = RiskRewardRatio,
                BreakEvenTriggerTicks = BEProfitTriggerTicks,
                BreakEvenPlusTicks = BEPlusTicks,
                Step1 = new TrailStepSettings
                {
                    ProfitTriggerTicks = Step1ProfitTriggerTicks,
                    StopLossTicks = Step1StopLossTicks,
                    FrequencyTicks = Step1FrequencyTicks
                },
                Step2 = new TrailStepSettings
                {
                    ProfitTriggerTicks = Step2ProfitTriggerTicks,
                    StopLossTicks = Step2StopLossTicks,
                    FrequencyTicks = Step2FrequencyTicks
                },
                Step3 = new TrailStepSettings
                {
                    ProfitTriggerTicks = Step3ProfitTriggerTicks,
                    StopLossTicks = Step3StopLossTicks,
                    FrequencyTicks = Step3FrequencyTicks
                }
            };
        }

        private bool IsAnyTrailStepEnabled()
        {
            return Step1ProfitTriggerTicks > 0
                || Step2ProfitTriggerTicks > 0
                || Step3ProfitTriggerTicks > 0;
        }

        private void UpdateBreakoutExcursions(
            DateTime tickTime,
            double tickPrice)
        {
            if (sessionContext == null)
                return;

            foreach (var breakout in breakoutEvents)
            {
                if (breakout.IsResolved)
                    continue;

                var favorable = breakout.Direction == TradeDirection.Long
                    ? (tickPrice - breakout.RangeLevel) / TickSize
                    : (breakout.RangeLevel - tickPrice) / TickSize;

                var adverse = breakout.Direction == TradeDirection.Long
                    ? (breakout.RangeLevel - tickPrice) / TickSize
                    : (tickPrice - breakout.RangeLevel) / TickSize;

                breakout.MfeTicks = Math.Max(breakout.MfeTicks, favorable);
                breakout.MaeTicks = Math.Max(breakout.MaeTicks, adverse);

                breakout.Reached10Ticks |= breakout.MfeTicks >= 10;
                breakout.Reached20Ticks |= breakout.MfeTicks >= 20;
                breakout.Reached30Ticks |= breakout.MfeTicks >= 30;
                breakout.Reached40Ticks |= breakout.MfeTicks >= 40;
                breakout.Reached60Ticks |= breakout.MfeTicks >= 60;
                breakout.Reached100Ticks |= breakout.MfeTicks >= 100;
            }
        }

        private void UpdateBreakoutReturnInside(CandleSnapshot bar)
        {
            foreach (var breakout in breakoutEvents)
            {
                if (breakout.ReturnedInside)
                    continue;

                var returned = breakout.Direction == TradeDirection.Long
                    ? bar.Close < breakout.RangeLevel
                    : bar.Close > breakout.RangeLevel;

                if (!returned)
                    continue;

                breakout.ReturnedInside = true;
                breakout.ReturnedInsideTime = bar.Time;
                breakout.BarsUntilReturnInside =
                    bar.BarIndex - breakout.BreakoutBarIndex;

                ExportBreakoutUpdate(breakout);
            }
        }

        private void ForceCloseAllHypotheticalTrades(
            DateTime time,
            double price,
            string reason)
        {
            if (double.IsNaN(price) || price <= 0)
                return;

            foreach (var trade in activeTrades.ToList())
            {
                trade.ForceClose(time, price, reason);
                ExportTrade(trade);
                activeTrades.Remove(trade);
            }
        }

        private bool IsInsideEntryWindow(DateTime time)
        {
            var value = ToTime(time);
            return value >= EntryStartTime
                   && value < EntryEndTime
                   && value < FlattenTime;
        }

        private void TrimHistory()
        {
            const int maximumHistory = 500;

            if (candleHistory.Count > maximumHistory)
                candleHistory.RemoveRange(
                    0,
                    candleHistory.Count - maximumHistory);
        }
    }
}
