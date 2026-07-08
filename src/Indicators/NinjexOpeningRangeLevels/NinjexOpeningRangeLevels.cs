#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class NinjexOpeningRangeLevels : Indicator
    {
        private DateTime activeEasternDate = Core.Globals.MinDate;
        private bool rangeStarted;
        private bool rangeComplete;
        private double rangeHigh;
        private double rangeLow;
        private int rangeEndBarIndex;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Ninjex Opening Range Levels";
                Description = "Marks the high and low of the opening range.";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;

                RangeStartTime = 93000;
                RangeMinutes = 5;
                ConvertChartTimeToEastern = false;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1)
                return;

            var easternBarEnd = ToEastern(Time[0]);
            var easternDate = easternBarEnd.Date;

            if (activeEasternDate != easternDate)
                ResetForNewDay(easternDate);

            var rangeStart = easternDate.Add(ToTimeSpan(RangeStartTime));
            var rangeEnd = rangeStart.AddMinutes(RangeMinutes);

            var easternBarStart = GetApproximateBarStart(easternBarEnd);

            var overlapsOpeningRange =
                easternBarStart < rangeEnd &&
                easternBarEnd > rangeStart;

            if (!rangeComplete && overlapsOpeningRange)
            {
                if (!rangeStarted)
                {
                    rangeStarted = true;
                    rangeHigh = High[0];
                    rangeLow = Low[0];
                }
                else
                {
                    rangeHigh = Math.Max(rangeHigh, High[0]);
                    rangeLow = Math.Min(rangeLow, Low[0]);
                }
            }

            if (!rangeComplete && rangeStarted && easternBarEnd >= rangeEnd)
            {
                rangeComplete = true;
                rangeEndBarIndex = CurrentBar;
            }

            if (rangeComplete)
                DrawLevels();
        }

        private void DrawLevels()
        {
            var barsAgoStart = Math.Max(0, CurrentBar - rangeEndBarIndex);
            var dayKey = activeEasternDate.ToString("yyyyMMdd");

            Draw.Line(
                this,
                "NinjexORHigh_" + dayKey,
                false,
                barsAgoStart,
                rangeHigh,
                0,
                rangeHigh,
                Brushes.DodgerBlue,
                DashStyleHelper.Solid,
                2);

            Draw.Line(
                this,
                "NinjexORLow_" + dayKey,
                false,
                barsAgoStart,
                rangeLow,
                0,
                rangeLow,
                Brushes.OrangeRed,
                DashStyleHelper.Solid,
                2);
        }

        private void ResetForNewDay(DateTime easternDate)
        {
            activeEasternDate = easternDate;
            rangeStarted = false;
            rangeComplete = false;
            rangeHigh = double.MinValue;
            rangeLow = double.MaxValue;
            rangeEndBarIndex = -1;
        }

        private DateTime GetApproximateBarStart(DateTime easternBarEnd)
        {
            if (BarsPeriod.BarsPeriodType == BarsPeriodType.Minute)
                return easternBarEnd.AddMinutes(-BarsPeriod.Value);

            return easternBarEnd;
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
        [Range(0, 235959)]
        [Display(Name = "Range Start Time", GroupName = "Opening Range", Order = 1)]
        public int RangeStartTime { get; set; }

        [NinjaScriptProperty]
        [Range(1, 240)]
        [Display(Name = "Range Minutes", GroupName = "Opening Range", Order = 2)]
        public int RangeMinutes { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Convert Chart Time To Eastern", GroupName = "Time", Order = 10)]
        public bool ConvertChartTimeToEastern { get; set; }
    }
}