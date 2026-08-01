#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Ninjex;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class NinjexPremarketRange : Indicator
    {
        private NinjexPremarketRangeEngine rangeEngine;
        private const string PanelTag = "NinjexPremarketRange_Panel";
        
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
                rangeEngine = new NinjexPremarketRangeEngine();
                
                isFiveMinuteChart =
                    BarsPeriod.BarsPeriodType == BarsPeriodType.Minute
                    && BarsPeriod.Value == 5;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1 || rangeEngine == null)
                return;

            bool previouslyHadRangeData = rangeEngine.HasRangeData;

            bool finalizedNow = rangeEngine.ProcessCompletedBar(
                Time[0],
                High[0],
                Low[0],
                RangeStartTime,
                MarketOpenTime);

            // Draw the session markers when the first qualifying premarket
            // bar has actually been processed.
            if (!previouslyHadRangeData
                && rangeEngine.HasRangeData
                && DrawVerticalLines)
            {
                DrawSessionVerticalLines(
                    rangeEngine.ActiveRangeDate);
            }

            if (finalizedNow && DrawHorizontalLines)
                DrawRangeLines();

            UpdateOutputSeries();

            if (DisplayPanel)
                UpdatePanel();
            else
                RemoveDrawObject(PanelTag);
        }
        
        private void DrawSessionVerticalLines(DateTime date)
        {
            var dateKey = date.ToString("yyyyMMdd");

            var startTime = CombineDateAndTime(date, RangeStartTime);
            var openTime = CombineDateAndTime(date, MarketOpenTime);

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

        private void DrawRangeLines()
        {
            if (rangeEngine == null
                || !rangeEngine.IsRangeComplete)
            {
                return;
            }

            DateTime date = rangeEngine.LatestRangeDate;
            string dateKey = date.ToString("yyyyMMdd");

            DateTime marketClose =
                CombineDateAndTime(date, MarketCloseTime);

            Draw.Line(
                this,
                "NinjexPremarketRange_High_" + dateKey,
                false,
                rangeEngine.HighBarTime,
                rangeEngine.LatestHigh,
                marketClose,
                rangeEngine.LatestHigh,
                HighLineBrush,
                DashStyleHelper.Solid,
                HorizontalLineWidth);

            Draw.Line(
                this,
                "NinjexPremarketRange_Low_" + dateKey,
                false,
                rangeEngine.LowBarTime,
                rangeEngine.LatestLow,
                marketClose,
                rangeEngine.LatestLow,
                LowLineBrush,
                DashStyleHelper.Solid,
                HorizontalLineWidth);
        }

        private void UpdateOutputSeries()
        {
            Values[0][0] = rangeEngine.IsRangeComplete ? rangeEngine.LatestHigh : double.NaN;
            Values[1][0] = rangeEngine.IsRangeComplete ? rangeEngine.LatestLow : double.NaN;
        }

        private void UpdatePanel()
        {
            var chartStatus = isFiveMinuteChart
                ? "Chart: 5-minute"
                : "WARNING: use a 5-minute chart";

            string rangeStatus;
            if (rangeEngine.IsRangeComplete)
                rangeStatus = "Status: Complete";
            else if (rangeEngine.HasRangeData)
                rangeStatus = "Status: Building";
            else
                rangeStatus = "Status: Waiting for 03:00";

            var highText = IsValidLevel(rangeEngine.LatestHigh)
                ? rangeEngine.LatestHigh.ToString("0.00")
                : "-";

            var lowText = IsValidLevel(rangeEngine.LatestLow)
                ? rangeEngine.LatestLow.ToString("0.00")
                : "-";

            var highTimeText = rangeEngine.HighBarTime != Core.Globals.MinDate
                ? rangeEngine.HighBarTime.ToString("HH:mm")
                : "-";

            var lowTimeText = rangeEngine.LowBarTime != Core.Globals.MinDate
                ? rangeEngine.LowBarTime.ToString("HH:mm")
                : "-";

            var text =
                "Premarket Range 03:00-09:30 ET\n" +
                chartStatus + "\n" +
                "Date: " + rangeEngine.ActiveRangeDate.ToString("dd MMM yyyy") + "\n" +
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
            var hour = hhmmss / 10000;
            var minute = hhmmss / 100 % 100;
            var second = hhmmss % 100;

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
        public Series<double> PremarketHigh => Values[0];

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> PremarketLow => Values[1];

        [Browsable(false)]
        [XmlIgnore]
        public double LatestPremarketHigh =>
            rangeEngine != null && rangeEngine.IsRangeComplete
                ? rangeEngine.LatestHigh
                : double.NaN;

        [Browsable(false)]
        [XmlIgnore]
        public double LatestPremarketLow =>
            rangeEngine != null && rangeEngine.IsRangeComplete
                ? rangeEngine.LatestLow
                : double.NaN;

        [Browsable(false)]
        [XmlIgnore]
        public bool IsRangeComplete =>
            rangeEngine != null
            && rangeEngine.IsRangeComplete;

        [Browsable(false)]
        [XmlIgnore]
        public DateTime LatestRangeDate =>
            rangeEngine != null
                ? rangeEngine.LatestRangeDate
                : Core.Globals.MinDate;

        #endregion
    }
}