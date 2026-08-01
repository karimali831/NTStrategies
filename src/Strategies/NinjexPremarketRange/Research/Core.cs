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
        private const string ResearchVersion = "2.0.0";
        private const int ContextSeriesIndex = 0;
        private const int EntrySeriesIndex = 1;
        private const int TickSeriesIndex = 2;

        private NinjexPremarketRangeEngine premarketRangeEngine;
        private readonly List<IEntryModel> entryModels = new List<IEntryModel>();
        private readonly List<CandleSnapshot> entryCandleHistory = new List<CandleSnapshot>();
        private readonly List<BreakoutEvent> breakoutEvents = new List<BreakoutEvent>();
        private readonly List<EntryCandidate> entryCandidates = new List<EntryCandidate>();
        private readonly List<HypotheticalTrade> activeTrades = new List<HypotheticalTrade>();
        private readonly BreakoutEventDetector breakoutDetector = new BreakoutEventDetector();
        private readonly CandleMetricsCalculator metricsCalculator = new CandleMetricsCalculator();

        private RangeSessionContext sessionContext;
        private SessionDataQuality sessionQuality;
        private DateTime activeTradingDate = Core.Globals.MinDate;
        private DateTime lastMarketTime = Core.Globals.MinDate;
        private int lastProcessedContextBar = -1;
        private int lastProcessedEntryBar = -1;
        private double lastKnownTickPrice = double.NaN;
        private string runId;
        private string strategyInstanceId;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Ninjex Premarket Range Research";
                Description = "Research-only 5-minute context, 1-minute entries and optional tick-precision management analyser.";
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
                AddDataSeries(BarsPeriodType.Minute, 1);
                AddDataSeries(BarsPeriodType.Tick, 1);
            }
            else if (State == State.DataLoaded)
            {
                runId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                strategyInstanceId = Guid.NewGuid().ToString("N").Substring(0, 8);
                premarketRangeEngine = new NinjexPremarketRangeEngine();
                ConfigureModels();
                InitializeExport();
                ExportManifest();
            }
            else if (State == State.Terminated)
            {
                DateTime time = lastMarketTime != Core.Globals.MinDate ? lastMarketTime : DateTime.Now;
                FinalizeOpenBreakoutEvents(time, "StrategyTerminated");
                ForceCloseAllHypotheticalTrades(time, lastKnownTickPrice, "StrategyTerminated");
                FinalizeSessionDataQuality(time, "StrategyTerminated");
                FlushAndDisposeExport();
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress == TickSeriesIndex)
            {
                ProcessTickSeries();
                return;
            }

            if (BarsInProgress == EntrySeriesIndex)
            {
                ProcessOneMinuteEntrySeries();
                return;
            }

            if (BarsInProgress == ContextSeriesIndex)
                ProcessFiveMinuteContextSeries();
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
                MinimumConfirmationBodyPercent = MinimumRetestConfirmationBodyPercent
            });
        }

        private void ProcessFiveMinuteContextSeries()
        {
            if (CurrentBars[ContextSeriesIndex] < 2 || lastProcessedContextBar == CurrentBars[ContextSeriesIndex])
                return;

            lastProcessedContextBar = CurrentBars[ContextSeriesIndex];
            DateTime barTime = Times[ContextSeriesIndex][1];
            DateTime tradingDate = barTime.Date;
            lastMarketTime = barTime;

            EnsureResearchDay(tradingDate, barTime);
            TrackFiveMinuteData(barTime);

            bool finalizedNow = premarketRangeEngine.ProcessCompletedBar(
                barTime,
                Highs[ContextSeriesIndex][1],
                Lows[ContextSeriesIndex][1],
                RangeStartTime,
                MarketOpenTime);

            if (finalizedNow)
            {
                EnsureSessionContext(tradingDate, barTime);
                Diagnostic(barTime,
                    "Premarket range finalized. Date={0:yyyy-MM-dd}, High={1}, Low={2}, HighTime={3:HH:mm}, LowTime={4:HH:mm}, Bars={5}",
                    premarketRangeEngine.LatestRangeDate,
                    premarketRangeEngine.LatestHigh,
                    premarketRangeEngine.LatestLow,
                    premarketRangeEngine.HighBarTime,
                    premarketRangeEngine.LowBarTime,
                    premarketRangeEngine.RangeBarCount);
            }
        }

        private void ProcessOneMinuteEntrySeries()
        {
            if (CurrentBars[EntrySeriesIndex] < Math.Max(BarsRequiredToTrade, 2)
                || lastProcessedEntryBar == CurrentBars[EntrySeriesIndex])
                return;

            lastProcessedEntryBar = CurrentBars[EntrySeriesIndex];
            DateTime barTime = Times[EntrySeriesIndex][1];
            DateTime tradingDate = barTime.Date;
            lastMarketTime = barTime;

            EnsureResearchDay(tradingDate, barTime);
            TrackOneMinuteData(barTime);

            CandleSnapshot bar = CaptureBar(EntrySeriesIndex, 1);
            CandleSnapshot previousBar = entryCandleHistory.Count > 0
                ? entryCandleHistory[entryCandleHistory.Count - 1]
                : null;

            CandleMetrics metrics = metricsCalculator.Calculate(
                bar,
                entryCandleHistory,
                RelativeBodyLookback,
                TickSize);

            entryCandleHistory.Add(bar);
            TrimEntryHistory();

            if (ToTime(barTime) >= FlattenTime)
            {
                FinalizeOpenBreakoutEvents(barTime, "FlattenTime");
                ForceCloseAllHypotheticalTrades(barTime, Closes[EntrySeriesIndex][1], "FlattenTime");
                FinalizeSessionDataQuality(barTime, "FlattenTime");
                return;
            }

            if (premarketRangeEngine == null
                || !premarketRangeEngine.IsRangeComplete
                || premarketRangeEngine.LatestRangeDate.Date != tradingDate)
                return;

            EnsureSessionContext(tradingDate, barTime);

            if (!IsInsideEntryWindow(barTime))
                return;

            var context = new ModelBarContext
            {
                Session = sessionContext,
                Bar = bar,
                PreviousBar = previousBar,
                Metrics = metrics,
                History = entryCandleHistory
            };

            IList<BreakoutEvent> newBreakouts = breakoutDetector.Detect(context, MinimumBreakoutDistanceTicks);
            foreach (BreakoutEvent breakout in newBreakouts)
                RegisterBreakout(breakout);

            foreach (var model in entryModels)
            {
                var generated = model.Evaluate(context);
                foreach (var candidate in generated)
                    RegisterCandidate(candidate);
            }

            UpdateBreakoutExcursionsFromBar(bar);
            UpdateRawRetestObservations(context);
            UpdateBreakoutReturnInside(bar);

            // OnEachTick means this method is running at the first tick of the new
            // 1-minute bar; Open[0] is therefore the requested next-bar-open entry.
            FillPendingCandidates(
                Times[EntrySeriesIndex][0],
                Opens[EntrySeriesIndex][0]);
        }
        
        private void ProcessTickSeries()
        {
            if (CurrentBars[TickSeriesIndex] < 0)
                return;

            var tickTime = Times[TickSeriesIndex][0];
            var tickPrice = Closes[TickSeriesIndex][0];
            lastMarketTime = tickTime;
            lastKnownTickPrice = tickPrice;

            EnsureResearchDay(tickTime.Date, tickTime);
            TrackTickData(tickTime);

            if (!EnablePrecisionTickAnalysis || !RequiresTickProcessing())
                return;

            foreach (var trade in activeTrades.ToList())
            {
                trade.ProcessTick(tickTime, tickPrice);
                if (trade.IsClosed)
                {
                    ExportTrade(trade);
                    FlushExportWriters();
                    
                    activeTrades.Remove(trade);
                }
            }

            UpdateBreakoutExcursions(tickTime, tickPrice);

            if (ToTime(tickTime) >= FlattenTime)
            {
                FinalizeOpenBreakoutEvents(tickTime, "FlattenTime");
                ForceCloseAllHypotheticalTrades(tickTime, tickPrice, "FlattenTime");
                FinalizeSessionDataQuality(tickTime, "FlattenTime");
            }
        }

        private bool RequiresTickProcessing()
        {
            return activeTrades.Count > 0 || breakoutEvents.Any(x => !x.IsResolved);
        }

        private void EnsureResearchDay(DateTime tradingDate, DateTime eventTime)
        {
            if (activeTradingDate == tradingDate)
                return;

            if (activeTradingDate != Core.Globals.MinDate)
            {
                FinalizeOpenBreakoutEvents(eventTime, "NewTradingDate");
                ForceCloseAllHypotheticalTrades(eventTime, lastKnownTickPrice, "NewTradingDate");
                FinalizeSessionDataQuality(eventTime, "NewTradingDate");
                ExportDailySummary(activeTradingDate);
                FlushExportWriters();
            }

            activeTradingDate = tradingDate;
            entryCandleHistory.Clear();
            breakoutEvents.Clear();
            entryCandidates.Clear();
            breakoutDetector.Reset(tradingDate);
            sessionContext = null;
            sessionQuality = new SessionDataQuality { TradingDate = tradingDate };

            foreach (IEntryModel model in entryModels)
                model.Reset(null);

            Diagnostic(eventTime, "New research day: {0:yyyy-MM-dd}", tradingDate);
        }

        private void EnsureSessionContext(DateTime tradingDate, DateTime eventTime)
        {
            if (sessionContext != null && sessionContext.TradingDate == tradingDate)
                return;

            sessionContext = new RangeSessionContext
            {
                TradingDate = tradingDate,
                Instrument = Instrument.MasterInstrument.Name,
                Contract = Instrument.FullName,
                PremarketHigh = premarketRangeEngine.LatestHigh,
                PremarketLow = premarketRangeEngine.LatestLow,
                HighFormationTime = premarketRangeEngine.HighBarTime,
                LowFormationTime = premarketRangeEngine.LowBarTime,
                TickSize = TickSize,
                PointValue = Instrument.MasterInstrument.PointValue
            };

            foreach (IEntryModel model in entryModels)
                model.Reset(sessionContext);

            ExportSession("Created", sessionContext, sessionQuality);
            Diagnostic(eventTime,
                "Range ready. Contract={0}, Date={1:yyyy-MM-dd}, High={2}, Low={3}, Width={4:0.0} ticks",
                Instrument.FullName,
                tradingDate,
                sessionContext.PremarketHigh,
                sessionContext.PremarketLow,
                sessionContext.RangeTicks);
        }

        private CandleSnapshot CaptureBar(int seriesIndex, int barsAgo)
        {
            return new CandleSnapshot
            {
                Time = Times[seriesIndex][barsAgo],
                BarIndex = CurrentBars[seriesIndex] - barsAgo,
                Open = Opens[seriesIndex][barsAgo],
                High = Highs[seriesIndex][barsAgo],
                Low = Lows[seriesIndex][barsAgo],
                Close = Closes[seriesIndex][barsAgo],
                Volume = Volumes[seriesIndex][barsAgo]
            };
        }
        
        private void ForceCloseAllHypotheticalTrades(DateTime time, double price, string reason)
        {
            if (double.IsNaN(price) || price <= 0)
                return;

            foreach (var trade in activeTrades.ToList())
            {
                trade.ForceClose(time, price, reason);
                
                ExportTrade(trade);
                FlushExportWriters();
                
                activeTrades.Remove(trade);
            }
        }
       
        private void FinalizeSessionDataQuality(DateTime time, string reason)
        {
            if (sessionQuality == null || sessionQuality.IsFinalized)
                return;

            var rangeComplete = premarketRangeEngine != null
                                && premarketRangeEngine.IsRangeComplete
                                && premarketRangeEngine.LatestRangeDate.Date == sessionQuality.TradingDate;

            var tickOkay = !EnablePrecisionTickAnalysis || sessionQuality.HasTickData;
            sessionQuality.Status = rangeComplete
                && sessionQuality.HasFiveMinuteData
                && sessionQuality.HasOneMinuteData
                && tickOkay
                    ? "Complete"
                    : "Incomplete";
            sessionQuality.IsFinalized = true;

            if (sessionContext != null)
            {
                ExportSession(
                    "Final",
                    sessionContext,
                    sessionQuality);

                FlushExportWriters();
            }

            Diagnostic(time,
                "DATA QUALITY Date={0:yyyy-MM-dd} Status={1} 5mBars={2} 1mBars={3} Ticks={4} Reason={5}",
                sessionQuality.TradingDate,
                sessionQuality.Status,
                sessionQuality.FiveMinuteRangeBarCount,
                sessionQuality.OneMinuteEntryWindowBarCount,
                sessionQuality.TickCount,
                reason);
        }

        private bool IsInsideEntryWindow(DateTime time)
        {
            var value = ToTime(time);
            return value >= EntryStartTime && value < EntryEndTime && value < FlattenTime;
        }

        private void TrimEntryHistory()
        {
            const int maximumHistory = 1000;
            if (entryCandleHistory.Count > maximumHistory)
                entryCandleHistory.RemoveRange(0, entryCandleHistory.Count - maximumHistory);
        }
    }
}
