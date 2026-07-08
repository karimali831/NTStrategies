#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
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
                MaxLosingTradesPerDay = 1;
                MaxWinningTradesPerDay = 2;

                RangeStartTime = 93000;
                RangeMinutes = 5;

                MinFvgGapTicks = 1;
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
                return;

            if (!CanTradeToday())
                return;

            if (pendingEntry)
                return;

            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            DateTime rangeStart = activeEasternDate.Add(ToTimeSpan(RangeStartTime));
            DateTime rangeEnd = rangeStart.AddMinutes(RangeMinutes);

            if (easternBarTime <= rangeEnd)
                return;

            var minGap = MinFvgGapTicks * TickSize;

            var bullishFvg =
                Low[0] > High[2] &&
                (Low[0] - High[2]) >= minGap;

            var bearishFvg =
                High[0] < Low[2] &&
                (Low[2] - High[0]) >= minGap;

            var bullishFvgThroughOpeningHigh =
                bullishFvg &&
                High[2] < openingRangeHigh &&
                Low[0] > openingRangeHigh;

            var bearishFvgThroughOpeningLow =
                bearishFvg &&
                Low[2] > openingRangeLow &&
                High[0] < openingRangeLow;

            var firstCloseAboveRange =
                Close[0] > openingRangeHigh &&
                Close[1] <= openingRangeHigh;

            var firstCloseBelowRange =
                Close[0] < openingRangeLow &&
                Close[1] >= openingRangeLow;

            if (bullishFvgThroughOpeningHigh && firstCloseAboveRange)
            {
                pendingEntry = true;
                pendingLong = true;
                pendingStopPrice = Low[0];

                EnterLong(Quantity, LongEntryName);
                return;
            }

            if (bearishFvgThroughOpeningLow && firstCloseBelowRange)
            {
                pendingEntry = true;
                pendingLong = false;
                pendingStopPrice = High[0];

                EnterShort(Quantity, ShortEntryName);
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
            if (execution?.Order == null)
                return;

            if (execution.Order.OrderState != OrderState.Filled)
                return;

            string orderName = execution.Order.Name;

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
        [Display(Name = "Convert Chart Time To Eastern", GroupName = "Time", Order = 30)]
        public bool ConvertChartTimeToEastern { get; set; }
    }
}