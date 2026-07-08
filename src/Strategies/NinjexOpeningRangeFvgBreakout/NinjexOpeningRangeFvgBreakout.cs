#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript.DrawingTools;

#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class NinjexOpeningRangeFvgBreakout : Strategy
    {
        private DateTime activeEasternDate = Core.Globals.MinDate;

        private bool openingRangeStarted;
        private bool openingRangeComplete;
        private double openingRangeHigh;
        private double openingRangeLow;
        private bool printedRangeComplete;

        private int losingTradesToday;
        private int winningTradesToday;

        private bool pendingEntry;
        private bool pendingLong;
        private double pendingStopPrice;

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
                Calculate = Calculate.OnBarClose;

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
                EnableDiagnostics = true;
                RecentFvgLookbackBars = 3;
                ConvertChartTimeToEastern = false;
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Minute, 5);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBars[0] < 3 || CurrentBars[1] < 1)
                return;

            if (BarsInProgress == 1)
            {
                UpdateOpeningRangeFromFiveMinuteSeries();
                return;
            }

            if (BarsInProgress != 0)
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

        private void HandleOneMinuteEntryModel()
        {
            var easternBarTime = ToEastern(Time[0]);

            if (activeEasternDate != easternBarTime.Date)
                ResetForNewDay(easternBarTime.Date);

            if (!openingRangeComplete)
            {
                LogDiag("Blocked: opening range not complete.");
                return;
            }

            if (!printedRangeComplete)
            {
                printedRangeComplete = true;
                LogDiag($"Opening range complete. High={openingRangeHigh}, Low={openingRangeLow}");
            }

            if (!CanTradeToday())
            {
                LogDiag($"Blocked: daily limits reached. Wins={winningTradesToday}, Losses={losingTradesToday}");
                return;
            }

            if (pendingEntry)
            {
                LogDiag("Blocked: pending entry already active.");
                return;
            }

            if (Position.MarketPosition != MarketPosition.Flat)
            {
                LogDiag($"Blocked: position not flat. Position={Position.MarketPosition}");
                return;
            }

            var rangeStart = activeEasternDate.Add(ToTimeSpan(RangeStartTime));
            var rangeEnd = rangeStart.AddMinutes(RangeMinutes);

            if (easternBarTime <= rangeEnd)
            {
                LogDiag($"Blocked: still inside opening range window. Time={easternBarTime:HH:mm:ss}, RangeEnd={rangeEnd:HH:mm:ss}");
                return;
            }

            if (CurrentBar < 4)
                return;

            var minGap = MinFvgGapTicks * TickSize;

            // Standard bullish 3-candle FVG:
            // Candle [2] = impulse / breakout candle
            // Candle [0] = FVG confirmation candle
            // Gap is between High[2] and Low[0]
            var bullishFvg =
                Low[0] > High[2] &&
                (Low[0] - High[2]) >= minGap;

            // Standard bearish 3-candle FVG:
            // Candle [2] = impulse / breakout candle
            // Candle [0] = FVG confirmation candle
            // Gap is between Low[2] and High[0]
            var bearishFvg =
                High[0] < Low[2] &&
                (Low[2] - High[0]) >= minGap;

            // Long model:
            // Candle [2] must be the first candle that closed above the opening range high.
            // The actual FVG gap must be above / through the opening range high.
            var breakoutCandleFirstCloseAboveRange =
                Close[2] > openingRangeHigh &&
                Close[3] <= openingRangeHigh;

            var bullishFvgThroughOpeningHigh =
                bullishFvg &&
                breakoutCandleFirstCloseAboveRange &&
                High[2] >= openingRangeHigh;

            // Short model:
            // Candle [2] must be the first candle that closed below the opening range low.
            // The actual FVG gap must be below / through the opening range low.
            var breakoutCandleFirstCloseBelowRange =
                Close[2] < openingRangeLow &&
                Close[3] >= openingRangeLow;

            var bearishFvgThroughOpeningLow =
                bearishFvg &&
                breakoutCandleFirstCloseBelowRange &&
                Low[2] <= openingRangeLow;

            LogDiag(
                $"Check: Close0={Close[0]}, ORH={openingRangeHigh}, ORL={openingRangeLow}, " +
                $"BullFVG={bullishFvg}, BearFVG={bearishFvg}, " +
                $"BreakoutCandleAbove={breakoutCandleFirstCloseAboveRange}, " +
                $"BreakoutCandleBelow={breakoutCandleFirstCloseBelowRange}, " +
                $"BullThroughORH={bullishFvgThroughOpeningHigh}, BearThroughORL={bearishFvgThroughOpeningLow}");

            if (bullishFvgThroughOpeningHigh)
            {
                pendingEntry = true;
                pendingLong = true;

                // Stop goes at the first candle that closed outside the range.
                pendingStopPrice = Low[2];

                DrawDiag("LONG_SIGNAL", "LONG", Low[0] - 4 * TickSize);
                LogDiag($"LONG submitted. BreakoutCandle={Time[2]:HH:mm:ss}, Stop={pendingStopPrice}");

                EnterLong(Quantity, LongEntryName);
                return;
            }

            if (bearishFvgThroughOpeningLow)
            {
                pendingEntry = true;
                pendingLong = false;

                // Stop goes at the first candle that closed outside the range.
                pendingStopPrice = High[2];

                DrawDiag("SHORT_SIGNAL", "SHORT", High[0] + 4 * TickSize);
                LogDiag($"SHORT submitted. BreakoutCandle={Time[2]:HH:mm:ss}, Stop={pendingStopPrice}");

                EnterShort(Quantity, ShortEntryName);
                return;
            }

            if (Close[0] > openingRangeHigh && !bullishFvgThroughOpeningHigh)
                DrawDiag("BLOCK_LONG_STRICT", "No strict bull FVG", High[0] + 4 * TickSize);

            if (Close[0] < openingRangeLow && !bearishFvgThroughOpeningLow)
                DrawDiag("BLOCK_SHORT_STRICT", "No strict bear FVG", Low[0] - 4 * TickSize);
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
                SubmitLongBracket(price, quantity);
                return;
            }

            if (orderName == ShortEntryName)
            {
                SubmitShortBracket(price, quantity);
                return;
            }

            if (orderName == LongTargetName || orderName == ShortTargetName)
            {
                winningTradesToday++;
                pendingEntry = false;
                return;
            }

            if (orderName == LongStopName || orderName == ShortStopName)
            {
                losingTradesToday++;
                pendingEntry = false;
            }
        }

        private void SubmitLongBracket(double entryPrice, int quantity)
        {
            var stopPrice = Instrument.MasterInstrument.RoundToTickSize(pendingStopPrice);
            var risk = entryPrice - stopPrice;

            if (risk <= TickSize)
            {
                ExitLong("InvalidLongRiskExit", LongEntryName);
                pendingEntry = false;
                return;
            }

            var targetPrice = Instrument.MasterInstrument.RoundToTickSize(entryPrice + (risk * 2.0));

            ExitLongStopMarket(0, true, quantity, stopPrice, LongStopName, LongEntryName);
            ExitLongLimit(0, true, quantity, targetPrice, LongTargetName, LongEntryName);
        }

        private void SubmitShortBracket(double entryPrice, int quantity)
        {
            var stopPrice = Instrument.MasterInstrument.RoundToTickSize(pendingStopPrice);
            var risk = stopPrice - entryPrice;

            if (risk <= TickSize)
            {
                ExitShort("InvalidShortRiskExit", ShortEntryName);
                pendingEntry = false;
                return;
            }

            var targetPrice = Instrument.MasterInstrument.RoundToTickSize(entryPrice - (risk * 2.0));

            ExitShortStopMarket(0, true, quantity, stopPrice, ShortStopName, ShortEntryName);
            ExitShortLimit(0, true, quantity, targetPrice, ShortTargetName, ShortEntryName);
        }

        private bool CanTradeToday()
        {
            if (MaxLosingTradesPerDay > 0 && losingTradesToday >= MaxLosingTradesPerDay)
                return false;

            if (MaxWinningTradesPerDay > 0 && winningTradesToday >= MaxWinningTradesPerDay)
                return false;

            return true;
        }
        
        private void DrawDiag(string tag, string text, double price)
        {
            if (!EnableDiagnostics)
                return;

            Draw.Text(
                this,
                tag + "_" + CurrentBar,
                false,
                text,
                0,
                price,
                0,
                Brushes.Gray,
                new SimpleFont("Arial", 10),
                TextAlignment.Center,
                Brushes.Transparent,
                Brushes.Transparent,
                0);
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

            printedRangeComplete = false;
        }
        
        private DateTime ToEastern(DateTime chartTime)
        {
            if (!ConvertChartTimeToEastern)
                return chartTime;

            try
            {
                var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                return TimeZoneInfo.ConvertTime(chartTime, TimeZoneInfo.Local, eastern);
            }
            catch
            {
                return chartTime;
            }
        }

        private TimeSpan ToTimeSpan(int hhmmss)
        {
            var hours = hhmmss / 10000;
            var minutes = (hhmmss % 10000) / 100;
            var seconds = hhmmss % 100;

            return new TimeSpan(hours, minutes, seconds);
        }
        
        [NinjaScriptProperty]
        [Display(Name = "Enable Diagnostics", GroupName = "Diagnostics", Order = 100)]
        public bool EnableDiagnostics { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Quantity", GroupName = "Risk", Order = 1)]
        public int Quantity { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Max Losing Trades Per Day", GroupName = "Risk", Order = 2)]
        public int MaxLosingTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Max Winning Trades Per Day", GroupName = "Risk", Order = 3)]
        public int MaxWinningTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Range Start Time", GroupName = "Opening Range", Order = 10)]
        public int RangeStartTime { get; set; }

        [NinjaScriptProperty]
        [Range(1, 240)]
        [Display(Name = "Range Minutes", GroupName = "Opening Range", Order = 11)]
        public int RangeMinutes { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Min FVG Gap Ticks", GroupName = "FVG", Order = 20)]
        public int MinFvgGapTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "Recent FVG Lookback Bars", GroupName = "FVG", Order = 21)]
        public int RecentFvgLookbackBars { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Convert Chart Time To Eastern", GroupName = "Time", Order = 30)]
        public bool ConvertChartTimeToEastern { get; set; }
    }
}