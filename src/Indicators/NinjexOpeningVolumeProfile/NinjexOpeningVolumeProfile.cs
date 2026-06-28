#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Ninjex;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class NinjexOpeningVolumeProfile : Indicator
    {
        private NinjexOpeningVolumeProfileEngine profileEngine;

        private TimeZoneInfo sourceTimeZone;
        private TimeZoneInfo easternTimeZone;

        private const string PanelTag = "NinjexOpeningVolumeProfile_Panel";
        private const string VAHLineTag = "NinjexOpeningVolumeProfile_VAH";
        private const string VALLineTag = "NinjexOpeningVolumeProfile_VAL";
        private const string POCLineTag = "NinjexOpeningVolumeProfile_POC";

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Ninjex Opening Volume Profile";
                Description = "Opening fixed range volume profile using 1-tick volume-at-price data. Calculates VAH, VAL and POC for 09:30-09:45 ET.";

                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
                DisplayInDataBox = false;
                DrawOnPricePanel = true;
                IsSuspendedWhileInactive = true;

                ProfileStartTime = 930;
                ProfileEndTime = 945;

                RowSizeTicks = 1;
                ValueAreaPercent = 70;

                UseTickDataForProfile = true;

                ConvertChartTimeToEastern = true;
                SourceTimeZoneId = "GMT Standard Time";

                ShowPanel = true;
                ShowHorizontalLines = true;

                ShowVAH = true;
                ShowVAL = true;
                ShowPOC = true;

                AddPlot(Brushes.Transparent, "VAH");
                AddPlot(Brushes.Transparent, "VAL");
                AddPlot(Brushes.Transparent, "POC");
            }
            else if (State == State.Configure)
            {
                if (UseTickDataForProfile)
                    AddDataSeries(BarsPeriodType.Tick, 1);
            }
            else if (State == State.DataLoaded)
            {
                easternTimeZone = FindTimeZoneOrLocal("Eastern Standard Time");
                sourceTimeZone = FindTimeZoneOrLocal(SourceTimeZoneId);

                profileEngine = new NinjexOpeningVolumeProfileEngine();
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress == 0)
            {
                if (CurrentBar < 1)
                    return;

                UpdateOutputSeries();
                UpdatePanel();

                if (ShowHorizontalLines)
                    DrawHorizontalLevels();

                return;
            }

            if (UseTickDataForProfile && BarsInProgress == 1)
                ProcessTickProfile();
        }

        private void ProcessTickProfile()
        {
            if (profileEngine == null)
                return;

            if (CurrentBars[1] < 1)
                return;

            var tickChartTime = Times[1][0];
            var profileTime = ConvertChartTimeToProfileTime(tickChartTime);

            var finalizedNow = profileEngine.ProcessTick(
                profileTime,
                Closes[1][0],
                Volumes[1][0],
                TickSize,
                ProfileStartTime,
                ProfileEndTime,
                RowSizeTicks,
                ValueAreaPercent);

            if (finalizedNow)
            {
                UpdateOutputSeries();
                UpdatePanel();

                if (ShowHorizontalLines)
                    DrawHorizontalLevels();
            }
        }

        private void UpdateOutputSeries()
        {
            Values[0][0] = double.NaN;
            Values[1][0] = double.NaN;
            Values[2][0] = double.NaN;

            if (profileEngine == null || !profileEngine.HasCompletedProfile)
                return;

            if (ShowVAH && IsValidLevel(profileEngine.LatestVAH))
                Values[0][0] = profileEngine.LatestVAH;

            if (ShowVAL && IsValidLevel(profileEngine.LatestVAL))
                Values[1][0] = profileEngine.LatestVAL;

            if (ShowPOC && IsValidLevel(profileEngine.LatestPOC))
                Values[2][0] = profileEngine.LatestPOC;
        }

        private void UpdatePanel()
        {
            if (!ShowPanel)
            {
                RemoveDrawObject(PanelTag);
                return;
            }

            var hasProfile = profileEngine != null && profileEngine.HasCompletedProfile;

            var dateText = hasProfile
                ? profileEngine.LatestProfileDate.ToString("dd MMM yyyy")
                : "No completed profile";

            var vahText = hasProfile ? profileEngine.LatestVAH.ToString("0.00") : "-";
            var valText = hasProfile ? profileEngine.LatestVAL.ToString("0.00") : "-";
            var pocText = hasProfile ? profileEngine.LatestPOC.ToString("0.00") : "-";

            var text =
                "Opening Volume Profile 09:30-09:45 ET\n" +
                "Mode: Tick volume-at-price\n" +
                "Date: " + dateText + "\n" +
                "VAH: " + vahText + "\n" +
                "VAL: " + valText + "\n" +
                "POC: " + pocText;

            Draw.TextFixed(
                this,
                PanelTag,
                text,
                TextPosition.TopRight);
        }

        private void DrawHorizontalLevels()
        {
            if (profileEngine == null || !profileEngine.HasCompletedProfile)
            {
                RemoveDrawObject(VAHLineTag);
                RemoveDrawObject(VALLineTag);
                RemoveDrawObject(POCLineTag);
                return;
            }

            if (ShowVAH && IsValidLevel(profileEngine.LatestVAH))
                Draw.HorizontalLine(this, VAHLineTag, profileEngine.LatestVAH, Brushes.RoyalBlue);
            else
                RemoveDrawObject(VAHLineTag);

            if (ShowVAL && IsValidLevel(profileEngine.LatestVAL))
                Draw.HorizontalLine(this, VALLineTag, profileEngine.LatestVAL, Brushes.RoyalBlue);
            else
                RemoveDrawObject(VALLineTag);

            if (ShowPOC && IsValidLevel(profileEngine.LatestPOC))
                Draw.HorizontalLine(this, POCLineTag, profileEngine.LatestPOC, Brushes.Red);
            else
                RemoveDrawObject(POCLineTag);
        }

        private DateTime ConvertChartTimeToProfileTime(DateTime chartTime)
        {
            if (!ConvertChartTimeToEastern)
                return chartTime;

            return ConvertTime(chartTime, sourceTimeZone, easternTimeZone);
        }

        private static bool IsValidLevel(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
        }

        private static TimeZoneInfo FindTimeZoneOrLocal(string timeZoneId)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(timeZoneId))
                    return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
            }
            catch
            {
                // ignored
            }

            return TimeZoneInfo.Local;
        }

        private static DateTime ConvertTime(DateTime sourceTime, TimeZoneInfo sourceZone, TimeZoneInfo destinationZone)
        {
            DateTime unspecified = DateTime.SpecifyKind(sourceTime, DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTime(unspecified, sourceZone, destinationZone);
        }

        #region Inputs

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Profile Start Time", Order = 1, GroupName = "Profile")]
        public int ProfileStartTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Profile End Time", Order = 2, GroupName = "Profile")]
        public int ProfileEndTime { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Row Size Ticks", Order = 3, GroupName = "Profile")]
        public int RowSizeTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Value Area Volume %", Order = 4, GroupName = "Profile")]
        public int ValueAreaPercent { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Tick Data For Profile", Order = 5, GroupName = "Profile")]
        public bool UseTickDataForProfile { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Convert Chart Time To Eastern", Order = 10, GroupName = "Time")]
        public bool ConvertChartTimeToEastern { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Source Time Zone Id", Order = 11, GroupName = "Time")]
        public string SourceTimeZoneId { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Panel", Order = 20, GroupName = "Visual")]
        public bool ShowPanel { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Horizontal Lines", Order = 21, GroupName = "Visual")]
        public bool ShowHorizontalLines { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show VAH", Order = 30, GroupName = "Levels")]
        public bool ShowVAH { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show VAL", Order = 31, GroupName = "Levels")]
        public bool ShowVAL { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show POC", Order = 32, GroupName = "Levels")]
        public bool ShowPOC { get; set; }

        #endregion

        #region Output Series

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> VAH
        {
            get { return Values[0]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> VAL
        {
            get { return Values[1]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> POC
        {
            get { return Values[2]; }
        }
        
        [Browsable(false)]
        [XmlIgnore]
        public double LatestVAH
        {
            get { return profileEngine != null ? profileEngine.LatestVAH : double.NaN; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public double LatestVAL
        {
            get { return profileEngine != null ? profileEngine.LatestVAL : double.NaN; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public double LatestPOC
        {
            get { return profileEngine != null ? profileEngine.LatestPOC : double.NaN; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public DateTime LatestProfileDate
        {
            get { return profileEngine != null ? profileEngine.LatestProfileDate : Core.Globals.MinDate; }
        }

        #endregion
    }
}