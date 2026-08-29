using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Ninjex;

namespace NinjaTrader.NinjaScript.Indicators
{
    public class NinjexPremarketRange : Indicator
    {
        private const string PanelTag = "NinjexPremarketRange_Panel";

        private NinjexPremarketRangeEngine rangeEngine;
        private DateTime verticalLinesDate = Core.Globals.MinDate;
        private bool isFiveMinuteChart;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Ninjex Premarket / Overnight Range";
                Description = "Marks either the 03:00-09:30 ET premarket range or the 18:00-09:30 ET overnight range.";

                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = false;
                DrawOnPricePanel = true;
                IsSuspendedWhileInactive = true;

                Mode = KeyLevelsMode.Premarket;
                RangeStartTime = 30000;
                OvernightStartTime = 180000;
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

                // Retain the original plot names for templates and consumers.
                // KeyLevelHigh/KeyLevelLow below are mode-neutral aliases.
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

            var hadRangeData = rangeEngine.HasRangeData;
            var previousRangeDate = rangeEngine.ActiveRangeDate;

            var completedNow = rangeEngine.ProcessCompletedBar(
                Time[0], 
                High[0], 
                Low[0], 
                Mode,
                RangeStartTime, 
                OvernightStartTime, 
                MarketOpenTime);

            var startedNewRange =
                rangeEngine.ActiveRangeDate != previousRangeDate;

            if (rangeEngine.HasRangeData
                && (!hadRangeData || startedNewRange)
                && DrawVerticalLines
                && verticalLinesDate != rangeEngine.ActiveRangeDate)
            {
                DrawSessionVerticalLines(rangeEngine.ActiveRangeDate);
                verticalLinesDate = rangeEngine.ActiveRangeDate;
            }

            if (completedNow && DrawHorizontalLines)
                DrawRangeLines(rangeEngine.LatestRangeDate);

            Values[0][0] = rangeEngine.IsRangeComplete
                ? rangeEngine.LatestHigh
                : double.NaN;

            Values[1][0] = rangeEngine.IsRangeComplete
                ? rangeEngine.LatestLow
                : double.NaN;

            if (DisplayPanel)
                UpdatePanel();
            else
                RemoveDrawObject(PanelTag);
        }

        private void DrawSessionVerticalLines(DateTime rangeDate)
        {
            var key = Mode + "_" + rangeDate.ToString("yyyyMMdd");
            var startValue = Mode == KeyLevelsMode.Overnight
                ? OvernightStartTime
                : RangeStartTime;

            var startDate = Mode == KeyLevelsMode.Overnight
                ? rangeDate.AddDays(-1)
                : rangeDate;

            Draw.VerticalLine(
                this,
                "NinjexKeyLevels_Start_" + key,
                CombineDateAndTime(startDate, startValue),
                VerticalLineBrush,
                DashStyleHelper.Dash,
                VerticalLineWidth,
                true);

            Draw.VerticalLine(
                this,
                "NinjexKeyLevels_Open_" + key,
                CombineDateAndTime(rangeDate, MarketOpenTime),
                VerticalLineBrush,
                DashStyleHelper.Dash,
                VerticalLineWidth,
                true);
        }

        private void DrawRangeLines(DateTime rangeDate)
        {
            string key = Mode + "_" + rangeDate.ToString("yyyyMMdd");
            DateTime marketClose = CombineDateAndTime(rangeDate, MarketCloseTime);

            Draw.Line(
                this,
                "NinjexKeyLevels_High_" + key,
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
                "NinjexKeyLevels_Low_" + key,
                false,
                rangeEngine.LowBarTime,
                rangeEngine.LatestLow,
                marketClose,
                rangeEngine.LatestLow,
                LowLineBrush,
                DashStyleHelper.Solid,
                HorizontalLineWidth);
        }

        private void UpdatePanel()
        {
            var chartStatus = isFiveMinuteChart
                ? "Chart: 5-minute"
                : "WARNING: use a 5-minute chart";

            string status = rangeEngine.IsRangeComplete
                ? "Status: Complete"
                : rangeEngine.HasRangeData
                    ? "Status: Building"
                    : "Status: Waiting for " + FormatTime(SelectedStartTime);

            var dateText = rangeEngine.ActiveRangeDate == Core.Globals.MinDate
                ? "-"
                : rangeEngine.ActiveRangeDate.ToString("dd MMM yyyy");

            string highText = IsValidLevel(rangeEngine.LatestHigh)
                ? rangeEngine.LatestHigh.ToString("0.00")
                : "-";

            var lowText = IsValidLevel(rangeEngine.LatestLow)
                ? rangeEngine.LatestLow.ToString("0.00")
                : "-";

            var highTimeText = rangeEngine.HighBarTime == Core.Globals.MinDate
                ? "-"
                : rangeEngine.HighBarTime.ToString("dd MMM HH:mm");

            var lowTimeText = rangeEngine.LowBarTime == Core.Globals.MinDate
                ? "-"
                : rangeEngine.LowBarTime.ToString("dd MMM HH:mm");

            var text =
                ModeTitle + "\n" +
                chartStatus + "\n" +
                "Range date: " + dateText + "\n" +
                status + "\n" +
                "High: " + highText + "  (" + highTimeText + ")\n" +
                "Low:  " + lowText + "  (" + lowTimeText + ")";

            Draw.TextFixed(this, PanelTag, text, TextPosition.TopRight);
        }

        private int SelectedStartTime =>
            Mode == KeyLevelsMode.Overnight
                ? OvernightStartTime
                : RangeStartTime;

        private string ModeTitle =>
            (Mode == KeyLevelsMode.Overnight
                ? "Overnight Range "
                : "Premarket Range ")
            + FormatTime(SelectedStartTime)
            + "-"
            + FormatTime(MarketOpenTime)
            + " ET";

        private static string FormatTime(int hhmmss)
        {
            var hour = hhmmss / 10000;
            var minute = (hhmmss / 100) % 100;
            return hour.ToString("00") + ":" + minute.ToString("00");
        }

        private static DateTime CombineDateAndTime(DateTime date, int hhmmss)
        {
            var hour = hhmmss / 10000;
            var minute = (hhmmss / 100) % 100;
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
        [Display(Name = "Key Levels Mode", Order = 0, GroupName = "Key Levels")]
        public KeyLevelsMode Mode { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Premarket Start Time", Order = 1, GroupName = "Time")]
        public int RangeStartTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Overnight Start Time", Order = 2, GroupName = "Time")]
        public int OvernightStartTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Market Open Time", Order = 3, GroupName = "Time")]
        public int MarketOpenTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Market Close Time", Order = 4, GroupName = "Time")]
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
        public Series<double> KeyLevelHigh => Values[0];

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> KeyLevelLow => Values[1];

        // Compatibility aliases for existing indicator consumers.
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> PremarketHigh => Values[0];

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> PremarketLow => Values[1];

        [Browsable(false)]
        [XmlIgnore]
        public double LatestHigh =>
            rangeEngine != null && rangeEngine.IsRangeComplete
                ? rangeEngine.LatestHigh
                : double.NaN;

        [Browsable(false)]
        [XmlIgnore]
        public double LatestLow =>
            rangeEngine != null && rangeEngine.IsRangeComplete
                ? rangeEngine.LatestLow
                : double.NaN;

        [Browsable(false)]
        [XmlIgnore]
        public double LatestPremarketHigh => LatestHigh;

        [Browsable(false)]
        [XmlIgnore]
        public double LatestPremarketLow => LatestLow;

        [Browsable(false)]
        [XmlIgnore]
        public bool IsRangeComplete =>
            rangeEngine != null && rangeEngine.IsRangeComplete;

        [Browsable(false)]
        [XmlIgnore]
        public DateTime LatestRangeDate =>
            rangeEngine != null
                ? rangeEngine.LatestRangeDate
                : Core.Globals.MinDate;

        #endregion
    }
}