#region Using declarations
using System;
using NinjaTrader.Cbi;
using NinjaTrader.Data;

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
        
        private bool shortBreakoutArmed;
        private int shortBreakoutBar;
        private double shortBreakoutStop;

        private bool longBreakoutArmed;
        private int longBreakoutBar;
        private double longBreakoutStop;
        
        private int losingTradesToday;
        private int winningTradesToday;

        private bool pendingEntry;
        private bool pendingLong;
        private double pendingStopPrice;
        
        private MarketPosition lastKnownMarketPosition = MarketPosition.Flat;
        private double lastTradeEntryPrice;
        private int tradesCompletedToday;
        
        private const string LongEntryName = "LongFvgBreakout";
        private const string ShortEntryName = "ShortFvgBreakout";

        private const string LongStopName = "LongStop";
        private const string LongTargetName = "LongTarget";
        private const string ShortStopName = "ShortStop";
        private const string ShortTargetName = "ShortTarget";

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Ninjex Opening Range FVG Breakout";
                Description = "Trades FVG breakouts through the opening range high/low.";
                Calculate = Calculate.OnEachTick;

                EnableDiagnostics = false;
                EntryStartTime = 93500;
                EntryEndTime = 110000;

                MaxStopTicks = 0;

                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IncludeCommission = true;

                Quantity = 1;
                MaxLosingTradesPerDay = 2;
                MaxWinningTradesPerDay = 2;

                RangeStartTime = 93000;
                RangeMinutes = 5;

                MinFvgGapTicks = 1;
                FvgConfirmBarsAfterBreakout = 3;
                
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
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Minute, 5);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBars[0] < 5 || CurrentBars[1] < 1)
                return;

            if (BarsInProgress == 1)
            {
                UpdateOpeningRangeFromFiveMinuteSeries();
                return;
            }

            if (BarsInProgress != 0)
                return;

            SyncFlatState();
            ManageActiveBracket();

            if (!IsFirstTickOfBar)
                return;

            HandleOneMinuteEntryModel();
        }

        private void UpdateOpeningRangeFromFiveMinuteSeries()
        {
            var easternBarEnd = ToEastern(Times[1][0]);
            var easternDate = easternBarEnd.Date;

            if (activeEasternDate != easternDate)
                ResetForNewDay(easternDate);

            var rangeStart = easternDate.Add(ToTimeSpan(RangeStartTime));
            var rangeEnd = rangeStart.AddMinutes(RangeMinutes);

            var easternBarStart = easternBarEnd.AddMinutes(-5);

            var overlapsOpeningRange =
                easternBarStart < rangeEnd &&
                easternBarEnd > rangeStart;

            if (!openingRangeComplete && overlapsOpeningRange)
            {
                if (!openingRangeStarted)
                {
                    openingRangeStarted = true;
                    openingRangeHigh = Highs[1][0];
                    openingRangeLow = Lows[1][0];
                }
                else
                {
                    openingRangeHigh = Math.Max(openingRangeHigh, Highs[1][0]);
                    openingRangeLow = Math.Min(openingRangeLow, Lows[1][0]);
                }
            }

            if (!openingRangeComplete && openingRangeStarted && easternBarEnd >= rangeEnd)
                openingRangeComplete = true;
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
                var exitPrice = Close[0];

                if (activeDirection == 1)
                {
                    if (exitPrice >= activeEntryPrice)
                        winningTradesToday++;
                    else
                        losingTradesToday++;
                }
                else if (activeDirection == -1)
                {
                    if (exitPrice <= activeEntryPrice)
                        winningTradesToday++;
                    else
                        losingTradesToday++;
                }

                tradesCompletedToday++;

                LogDiag(
                    $"Position flat detected. Wins={winningTradesToday}, Losses={losingTradesToday}, Completed={tradesCompletedToday}");

                pendingEntry = false;
                pendingLong = false;

                longBreakoutArmed = false;
                shortBreakoutArmed = false;

                ResetActiveBracketState();
            }

            lastKnownMarketPosition = currentPosition;
        }

        private void LogDiag(string message)
        {
            if (!EnableDiagnostics)
                return;

            Print($"{Time[0]:yyyy-MM-dd HH:mm:ss} | {Name} | {message}");
        }

        private void ResetForNewDay(DateTime easternDate)
        {
            activeEasternDate = easternDate;

            openingRangeStarted = false;
            openingRangeComplete = false;
            openingRangeHigh = double.MinValue;
            openingRangeLow = double.MaxValue;

            losingTradesToday = 0;
            winningTradesToday = 0;

            pendingEntry = false;
            pendingLong = false;
            pendingStopPrice = 0;
            
            longBreakoutArmed = false;
            longBreakoutBar = -1;
            longBreakoutStop = 0;

            shortBreakoutArmed = false;
            shortBreakoutBar = -1;
            shortBreakoutStop = 0;

            printedRangeComplete = false;
            
            lastKnownMarketPosition = MarketPosition.Flat;
            lastTradeEntryPrice = 0;
            tradesCompletedToday = 0;
            
            ResetActiveBracketState();
        }
    }
}