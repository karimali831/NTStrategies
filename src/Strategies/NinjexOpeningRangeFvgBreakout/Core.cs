#region Using declarations
using System;
using NinjaTrader.Cbi;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOpeningRangeFvgBreakout : Strategy
    {
        private DateTime activeEasternDate = Core.Globals.MinDate;

        private bool openingRangeStarted;
        private bool openingRangeComplete;
        private double openingRangeHigh;
        private double openingRangeLow;
        private bool printedRangeComplete;

        private bool pendingEntry;
        private bool pendingLong;
        private double pendingStopPrice;

        private MarketPosition lastKnownMarketPosition = MarketPosition.Flat;

        private const string LongEntryName = "LongFvgBreakout";
        private const string ShortEntryName = "ShortFvgBreakout";

        private double lastTradeEntryPrice;
        private int lastEntrySignalBar = -1;

        private bool dailyPnlLimitHit;
        private double dailyStartCumProfit;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Ninjex Opening Range FVG Breakout";
                Description = "Trades FVG breakouts through the opening range high/low.";
                Calculate = Calculate.OnEachTick;
                BarsRequiredToTrade = 5;
                
                EnableRangeFilter = true;
                EnableDiagnostics = false;
                EntryStartTime = 93500;
                EntryEndTime = 110000;
                MaxEntryDistanceFromRangeTicks = 50;
                
                MaxStopTicks = 0;
                MinOpeningRangeTicks = 4;

                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IncludeCommission = true;

                Quantity = 1;

                // 0 = disabled
                MaxDailyProfit = 1000;
                MaxDailyLoss = 500;

                RangeStartTime = 93000;
                RangeMinutes = 5;

                MinFvgGapTicks = 20;
                MaxFvgGapTicks = 80;

                MinFvgDistanceFromRangeTicks = 0;
                MaxFvgDistanceFromRangeTicks = 40;

                AutoBreakevenProfitTriggerTicks = 0;
                AutoBreakevenPlusTicks = 0;

                Trail1ProfitTriggerTicks = 0;
                Trail1StopLossTicks = 0;
                Trail1FrequencyTicks = 1;

                Trail2ProfitTriggerTicks = 0;
                Trail2StopLossTicks = 0;
                Trail2FrequencyTicks = 1;

                Trail3ProfitTriggerTicks = 0;
                Trail3StopLossTicks = 0;
                Trail3FrequencyTicks = 1;

                ConvertChartTimeToEastern = false;
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            var easternBarTime = ToEastern(Time[0]);

            if (activeEasternDate != easternBarTime.Date)
                ResetForNewDay(easternBarTime.Date);

            if (EnableRangeFilter && CurrentBar >= 1)
                UpdateOpeningRangeFromOneMinuteSeries();

            SyncFlatState();
            ManageActiveBracket();

            EnforceDailyPnlLimits();

            if (CurrentBar < 5)
                return;

            if (IsFirstTickOfBar)
                HandleOneMinuteEntryModel();
        }

        private void UpdateOpeningRangeFromOneMinuteSeries()
        {
            var easternBarTime = ToEastern(Time[0]);
            var easternDate = easternBarTime.Date;

            if (activeEasternDate != easternDate)
                ResetForNewDay(easternDate);

            if (openingRangeComplete)
                return;

            if (!IsFirstTickOfBar)
                return;

            if (CurrentBar < 1)
                return;

            var includedBars = string.Empty;
            var rangeStart = easternDate.Add(ToTimeSpan(RangeStartTime));
            var rangeEnd = rangeStart.AddMinutes(RangeMinutes);

            // Do not complete OR until the range window has finished.
            var closedBarTime = ToEastern(Time[1]);

            if (closedBarTime < rangeEnd)
            {
                LogDiagOncePerBar(
                    "OR_NOT_READY",
                    $"Opening range waiting. ClosedBar={closedBarTime:HH:mm:ss}, RangeEnd={rangeEnd:HH:mm:ss}");

                return;
            }

            var foundBars = 0;
            var high = double.MinValue;
            var low = double.MaxValue;

            // Scan completed bars only.
            // barsAgo=1 is the candle that just closed.
            for (var barsAgo = 1; barsAgo <= CurrentBar; barsAgo++)
            {
                var barTime = ToEastern(Time[barsAgo]);

                if (barTime.Date != easternDate)
                    break;

                // NinjaTrader minute bars are being treated here as completed-bar timestamps.
                // For a 09:30-09:35 opening range, include bars stamped:
                // 09:31, 09:32, 09:33, 09:34, 09:35.
                if (barTime > rangeStart && barTime <= rangeEnd)
                {
                    foundBars++;
                    high = Math.Max(high, High[barsAgo]);
                    low = Math.Min(low, Low[barsAgo]);
                    includedBars += $"{barTime:HH:mm:ss}(H={High[barsAgo]},L={Low[barsAgo]}) ";
                }

                // Once we have moved to/before the range start, no need to continue.
                if (barTime <= rangeStart)
                    break;
            }

            if (foundBars <= 0)
            {
                LogDiagOncePerBar(
                    "OR_NO_BARS_FOUND",
                    $"Blocked: no opening range bars found. Range={rangeStart:HH:mm:ss}-{rangeEnd:HH:mm:ss}");

                return;
            }

            openingRangeStarted = true;
            openingRangeComplete = true;
            openingRangeHigh = high;
            openingRangeLow = low;

            LogDiag(
                $"Opening range complete. BarsFound={foundBars}, High={openingRangeHigh}, Low={openingRangeLow}, " +
                $"RangeTicks={(openingRangeHigh - openingRangeLow) / TickSize}, Bars={includedBars}");
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
            if (execution?.Order == null)
                return;

            if (execution.Order.OrderState != OrderState.Filled)
                return;

            var orderName = execution.Order.Name;

            if (orderName == LongEntryName)
            {
                activeEntryPrice = price;
                lastTradeEntryPrice = price;
                lastKnownMarketPosition = MarketPosition.Long;

                var risk = activeEntryPrice - activeStopPrice;
                if (risk > TickSize)
                {
                    activeTargetPrice = Instrument.MasterInstrument.RoundToTickSize(activeEntryPrice + risk * 2.0);
                    SetStopLoss(LongEntryName, CalculationMode.Price, activeStopPrice, false);
                    SetProfitTarget(LongEntryName, CalculationMode.Price, activeTargetPrice);
                }

                pendingEntry = false;

                LogDiag($"LONG filled. Entry={activeEntryPrice}, Stop={activeStopPrice}, Target={activeTargetPrice}");
                return;
            }

            if (orderName == ShortEntryName)
            {
                activeEntryPrice = price;
                lastTradeEntryPrice = price;
                lastKnownMarketPosition = MarketPosition.Short;

                var risk = activeStopPrice - activeEntryPrice;
                if (risk > TickSize)
                {
                    activeTargetPrice = Instrument.MasterInstrument.RoundToTickSize(activeEntryPrice - risk * 2.0);
                    SetStopLoss(ShortEntryName, CalculationMode.Price, activeStopPrice, false);
                    SetProfitTarget(ShortEntryName, CalculationMode.Price, activeTargetPrice);
                }

                pendingEntry = false;

                LogDiag($"SHORT filled. Entry={activeEntryPrice}, Stop={activeStopPrice}, Target={activeTargetPrice}");
            }
        }

        private void SyncFlatState()
        {
            var currentPosition = Position.MarketPosition;

            if (lastKnownMarketPosition != MarketPosition.Flat &&
                currentPosition == MarketPosition.Flat)
            {
                LogDiag(
                    $"Position flat detected. DailyPnL={GetDailyTotalPnl():0.00}");

                pendingEntry = false;
                pendingLong = false;
                lastTradeEntryPrice = 0;

                ResetActiveBracketState();
            }

            lastKnownMarketPosition = currentPosition;
        }

        private double GetDailyRealizedPnl()
        {
            var currentCumProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
            return currentCumProfit - dailyStartCumProfit;
        }
        private void ResetForNewDay(DateTime easternDate)
        {
            activeEasternDate = easternDate;

            openingRangeStarted = false;
            openingRangeComplete = false;
            openingRangeHigh = double.MinValue;
            openingRangeLow = double.MaxValue;

            pendingEntry = false;
            pendingLong = false;
            pendingStopPrice = 0;

            printedRangeComplete = false;

            lastKnownMarketPosition = MarketPosition.Flat;
            lastTradeEntryPrice = 0;
            lastEntrySignalBar = -1;

            dailyPnlLimitHit = false;
            dailyStartCumProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;

            ResetActiveBracketState();

            LogDiag(
                $"New trading day reset. Date={easternDate:yyyy-MM-dd}, DailyStartCumProfit={dailyStartCumProfit:0.00}");
            
            lastDiagnosticBar = -1;
            lastBlockDiagnosticBar = -1;
            lastBlockDiagnosticKey = string.Empty;
        }
    }
}