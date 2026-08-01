#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class NinjexPremarketRange : Indicator
    {
        private const string PanelTag = "NinjexPremarketRange_Panel";

        private DateTime activeDate = Core.Globals.MinDate;
        private DateTime highBarTime = Core.Globals.MinDate;
        private DateTime lowBarTime = Core.Globals.MinDate;

        private double premarketHigh = double.NaN;
        private double premarketLow = double.NaN;

        private bool hasRangeData;
        private bool rangeFinalized;
        private bool isFiveMinuteChart;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Ninjex Premarket Range";
                Description = "Marks the 03:00-09:30 ET premarket high and low on a 5-minute chart.";

                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = false;
                DrawOnPricePanel = true;
                IsSuspendedWhileInactive = true;

                RangeStartTime = 30000;
                MarketOpenTime = 93000;
                MarketCloseTime = 160000;

                DrawVerticalLines = true;
                DrawHorizontalLines = true;
                DisplayPanel = true;

                VerticalLineBrush = Brushes.DimGray;
                HighLineBrush = Brushes.RoyalBlue;
                LowLineBrush = Brushes.RoyalBlue;

                VerticalLineWidth = 1;
                HorizontalLineWidth = 2;

                AddPlot(Brushes.Transparent, "PremarketHigh");
                AddPlot(Brushes.Transparent, "PremarketLow");
            }
            else if (State == State.DataLoaded)
            {
                isFiveMinuteChart =
                    BarsPeriod.BarsPeriodType == BarsPeriodType.Minute
                    && BarsPeriod.Value == 5;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1)
                return;

            DateTime barTime = Time[0];
            DateTime barDate = barTime.Date;

            if (activeDate != barDate)
                StartNewDay(barDate);

            DateTime rangeStart = CombineDateAndTime(barDate, RangeStartTime);
            DateTime marketOpen = CombineDateAndTime(barDate, MarketOpenTime);

            // NinjaTrader minute bars use the bar's closing timestamp.
            // On a 5-minute chart, the 03:00-09:30 range is represented by
            // bars stamped 03:05, 03:10, ... 09:30.
            bool isPremarketRangeBar =
                barTime > rangeStart
                && barTime <= marketOpen;

            if (isPremarketRangeBar)
            {
                if (!hasRangeData)
                {
                    hasRangeData = true;

                    if (DrawVerticalLines)
                        DrawSessionVerticalLines(barDate);
                }

                UpdateRange(barTime);
            }

            if (!rangeFinalized && hasRangeData && barTime >= marketOpen)
                FinalizeRange(barDate);

            UpdateOutputSeries();

            if (DisplayPanel)
                UpdatePanel(barTime);
            else
                RemoveDrawObject(PanelTag);
        }

        private void StartNewDay(DateTime date)
        {
            activeDate = date;
            highBarTime = Core.Globals.MinDate;
            lowBarTime = Core.Globals.MinDate;

            premarketHigh = double.NaN;
            premarketLow = double.NaN;

            hasRangeData = false;
            rangeFinalized = false;
        }

        private void UpdateRange(DateTime barTime)
        {
            if (!hasRangeData || double.IsNaN(premarketHigh) || High[0] > premarketHigh)
            {
                premarketHigh = High[0];
                highBarTime = barTime;
            }

            if (!hasRangeData || double.IsNaN(premarketLow) || Low[0] < premarketLow)
            {
                premarketLow = Low[0];
                lowBarTime = barTime;
            }
        }

        private void FinalizeRange(DateTime date)
        {
            rangeFinalized =
                hasRangeData
                && IsValidLevel(premarketHigh)
                && IsValidLevel(premarketLow);

            if (!rangeFinalized)
                return;

            if (DrawHorizontalLines)
                DrawRangeLines(date);
        }

        private void DrawSessionVerticalLines(DateTime date)
        {
            string dateKey = date.ToString("yyyyMMdd");

            DateTime startTime = CombineDateAndTime(date, RangeStartTime);
            DateTime openTime = CombineDateAndTime(date, MarketOpenTime);

            Draw.VerticalLine(
                this,
                "NinjexPremarketRange_Start_" + dateKey,
                startTime,
                VerticalLineBrush,
                DashStyleHelper.Dash,
                VerticalLineWidth,
                true);

            Draw.VerticalLine(
                this,
                "NinjexPremarketRange_Open_" + dateKey,
                openTime,
                VerticalLineBrush,
                DashStyleHelper.Dash,
                VerticalLineWidth,
                true);
        }

        private void DrawRangeLines(DateTime date)
        {
            string dateKey = date.ToString("yyyyMMdd");
            DateTime marketClose = CombineDateAndTime(date, MarketCloseTime);

            Draw.Line(
                this,
                "NinjexPremarketRange_High_" + dateKey,
                false,
                highBarTime,
                premarketHigh,
                marketClose,
                premarketHigh,
                HighLineBrush,
                DashStyleHelper.Solid,
                HorizontalLineWidth);

            Draw.Line(
                this,
                "NinjexPremarketRange_Low_" + dateKey,
                false,
                lowBarTime,
                premarketLow,
                marketClose,
                premarketLow,
                LowLineBrush,
                DashStyleHelper.Solid,
                HorizontalLineWidth);
        }

        private void UpdateOutputSeries()
        {
            Values[0][0] = rangeFinalized ? premarketHigh : double.NaN;
            Values[1][0] = rangeFinalized ? premarketLow : double.NaN;
        }

        private void UpdatePanel(DateTime barTime)
        {
            string chartStatus = isFiveMinuteChart
                ? "Chart: 5-minute"
                : "WARNING: use a 5-minute chart";

            string rangeStatus;
            if (rangeFinalized)
                rangeStatus = "Status: Complete";
            else if (hasRangeData)
                rangeStatus = "Status: Building";
            else
                rangeStatus = "Status: Waiting for 03:00";

            string highText = IsValidLevel(premarketHigh)
                ? premarketHigh.ToString("0.00")
                : "-";

            string lowText = IsValidLevel(premarketLow)
                ? premarketLow.ToString("0.00")
                : "-";

            string highTimeText = highBarTime != Core.Globals.MinDate
                ? highBarTime.ToString("HH:mm")
                : "-";

            string lowTimeText = lowBarTime != Core.Globals.MinDate
                ? lowBarTime.ToString("HH:mm")
                : "-";

            string text =
                "Premarket Range 03:00-09:30 ET\n" +
                chartStatus + "\n" +
                "Date: " + activeDate.ToString("dd MMM yyyy") + "\n" +
                rangeStatus + "\n" +
                "High: " + highText + "  (" + highTimeText + ")\n" +
                "Low:  " + lowText + "  (" + lowTimeText + ")";

            Draw.TextFixed(
                this,
                PanelTag,
                text,
                TextPosition.TopRight);
        }

        private static DateTime CombineDateAndTime(DateTime date, int hhmmss)
        {
            int hour = hhmmss / 10000;
            int minute = (hhmmss / 100) % 100;
            int second = hhmmss % 100;

            return date.Date.AddHours(hour).AddMinutes(minute).AddSeconds(second);
        }

        private static bool IsValidLevel(double value)
        {
            return !double.IsNaN(value)
                   && !double.IsInfinity(value)
                   && value > 0;
        }

        #region Inputs

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Range Start Time", Order = 1, GroupName = "Time")]
        public int RangeStartTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Market Open Time", Order = 2, GroupName = "Time")]
        public int MarketOpenTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Market Close Time", Order = 3, GroupName = "Time")]
        public int MarketCloseTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Draw Vertical Lines", Order = 10, GroupName = "Visual")]
        public bool DrawVerticalLines { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Draw Horizontal Lines", Order = 11, GroupName = "Visual")]
        public bool DrawHorizontalLines { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Display Panel", Order = 12, GroupName = "Visual")]
        public bool DisplayPanel { get; set; }

        [XmlIgnore]
        [Display(Name = "Vertical Line Brush", Order = 20, GroupName = "Style")]
        public Brush VerticalLineBrush { get; set; }

        [Browsable(false)]
        public string VerticalLineBrushSerializable
        {
            get { return Serialize.BrushToString(VerticalLineBrush); }
            set { VerticalLineBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "High Line Brush", Order = 21, GroupName = "Style")]
        public Brush HighLineBrush { get; set; }

        [Browsable(false)]
        public string HighLineBrushSerializable
        {
            get { return Serialize.BrushToString(HighLineBrush); }
            set { HighLineBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Low Line Brush", Order = 22, GroupName = "Style")]
        public Brush LowLineBrush { get; set; }

        [Browsable(false)]
        public string LowLineBrushSerializable
        {
            get { return Serialize.BrushToString(LowLineBrush); }
            set { LowLineBrush = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Vertical Line Width", Order = 23, GroupName = "Style")]
        public int VerticalLineWidth { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Horizontal Line Width", Order = 24, GroupName = "Style")]
        public int HorizontalLineWidth { get; set; }

        #endregion

        #region Output Series

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> PremarketHigh
        {
            get { return Values[0]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> PremarketLow
        {
            get { return Values[1]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public double LatestPremarketHigh
        {
            get { return rangeFinalized ? premarketHigh : double.NaN; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public double LatestPremarketLow
        {
            get { return rangeFinalized ? premarketLow : double.NaN; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public bool IsRangeComplete
        {
            get { return rangeFinalized; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public DateTime LatestRangeDate
        {
            get { return activeDate; }
        }

        #endregion
    }
}
