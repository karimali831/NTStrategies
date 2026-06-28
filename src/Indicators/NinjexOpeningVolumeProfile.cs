#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class NinjexOpeningVolumeProfile : Indicator
    {
        private readonly Dictionary<int, double> volumeByBucket = new Dictionary<int, double>();

        private TimeZoneInfo sourceTimeZone;
        private TimeZoneInfo easternTimeZone;

        private DateTime activeProfileDate = Core.Globals.MinDate;
        private DateTime completedProfileDate = Core.Globals.MinDate;

        private bool activeProfileFinalized;

        private double currentVAH = double.NaN;
        private double currentVAL = double.NaN;
        private double currentPOC = double.NaN;

        private double displayedVAH = double.NaN;
        private double displayedVAL = double.NaN;
        private double displayedPOC = double.NaN;

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
            if (CurrentBars[1] < 1)
                return;

            DateTime tickChartTime = Times[1][0];
            DateTime profileTime = ConvertChartTimeToProfileTime(tickChartTime);
            DateTime profileDate = profileTime.Date;

            if (activeProfileDate != profileDate)
                StartNewProfile(profileDate);

            if (activeProfileFinalized)
                return;

            var timeValue = ToTime(profileTime);
            var startTime = NormalizeTimeInput(ProfileStartTime);
            var endTime = NormalizeTimeInput(ProfileEndTime);

            if (timeValue >= startTime && timeValue < endTime)
            {
                var price = Closes[1][0];
                var volume = Volumes[1][0];

                AddVolumeAtPrice(price, volume);
                return;
            }

            if (timeValue >= endTime && volumeByBucket.Count > 0)
            {
                FinalizeProfile();

                displayedVAH = currentVAH;
                displayedVAL = currentVAL;
                displayedPOC = currentPOC;

                completedProfileDate = activeProfileDate;
                activeProfileFinalized = true;
            }
        }

        private void StartNewProfile(DateTime profileDate)
        {
            activeProfileDate = profileDate;
            activeProfileFinalized = false;

            volumeByBucket.Clear();

            currentVAH = double.NaN;
            currentVAL = double.NaN;
            currentPOC = double.NaN;

            // Do not clear displayed levels here.
            // This keeps the previous completed profile visible until the new one completes.
        }

        private void AddVolumeAtPrice(double price, double volume)
        {
            if (price <= 0 || volume <= 0)
                return;

            int safeRowSizeTicks = Math.Max(1, RowSizeTicks);
            double bucketSize = TickSize * safeRowSizeTicks;

            int bucket = (int)Math.Round(price / bucketSize, MidpointRounding.AwayFromZero);

            if (!volumeByBucket.ContainsKey(bucket))
                volumeByBucket[bucket] = 0;

            volumeByBucket[bucket] += volume;
        }

        private void FinalizeProfile()
        {
            if (volumeByBucket.Count == 0)
                return;

            var safeRowSizeTicks = Math.Max(1, RowSizeTicks);
            var bucketSize = TickSize * safeRowSizeTicks;

            var pocBucket = volumeByBucket
                .OrderByDescending(x => x.Value)
                .ThenBy(x => x.Key)
                .First()
                .Key;

            var totalVolume = volumeByBucket.Values.Sum();

            var safeValueArea = Math.Max(1, Math.Min(100, ValueAreaPercent));
            var targetVolume = totalVolume * (safeValueArea / 100.0);

            var sortedBuckets = volumeByBucket.Keys.OrderBy(x => x).ToList();

            var pocIndex = sortedBuckets.IndexOf(pocBucket);
            var lowerIndex = pocIndex;
            var upperIndex = pocIndex;

            var accumulatedVolume = volumeByBucket[pocBucket];

            while (accumulatedVolume < targetVolume && (lowerIndex > 0 || upperIndex < sortedBuckets.Count - 1))
            {
                var lowerVolume = lowerIndex > 0
                    ? volumeByBucket[sortedBuckets[lowerIndex - 1]]
                    : -1;

                var upperVolume = upperIndex < sortedBuckets.Count - 1
                    ? volumeByBucket[sortedBuckets[upperIndex + 1]]
                    : -1;

                if (upperVolume >= lowerVolume && upperIndex < sortedBuckets.Count - 1)
                {
                    upperIndex++;
                    accumulatedVolume += Math.Max(0, upperVolume);
                }
                else if (lowerIndex > 0)
                {
                    lowerIndex--;
                    accumulatedVolume += Math.Max(0, lowerVolume);
                }
                else
                {
                    break;
                }
            }

            currentPOC = Instrument.MasterInstrument.RoundToTickSize(pocBucket * bucketSize);
            currentVAH = Instrument.MasterInstrument.RoundToTickSize(sortedBuckets[upperIndex] * bucketSize);
            currentVAL = Instrument.MasterInstrument.RoundToTickSize(sortedBuckets[lowerIndex] * bucketSize);
        }

        private void UpdateOutputSeries()
        {
            Values[0][0] = double.NaN;
            Values[1][0] = double.NaN;
            Values[2][0] = double.NaN;

            if (ShowVAH && IsValidLevel(displayedVAH))
                Values[0][0] = displayedVAH;

            if (ShowVAL && IsValidLevel(displayedVAL))
                Values[1][0] = displayedVAL;

            if (ShowPOC && IsValidLevel(displayedPOC))
                Values[2][0] = displayedPOC;
        }

        private void UpdatePanel()
        {
            if (!ShowPanel)
            {
                RemoveDrawObject(PanelTag);
                return;
            }

            var dateText = completedProfileDate == Core.Globals.MinDate
                ? "No completed profile"
                : completedProfileDate.ToString("dd MMM yyyy");

            var vahText = IsValidLevel(displayedVAH) ? displayedVAH.ToString("0.00") : "-";
            var valText = IsValidLevel(displayedVAL) ? displayedVAL.ToString("0.00") : "-";
            var pocText = IsValidLevel(displayedPOC) ? displayedPOC.ToString("0.00") : "-";

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
            if (ShowVAH && IsValidLevel(displayedVAH))
                Draw.HorizontalLine(this, VAHLineTag, displayedVAH, Brushes.RoyalBlue);
            else
                RemoveDrawObject(VAHLineTag);

            if (ShowVAL && IsValidLevel(displayedVAL))
                Draw.HorizontalLine(this, VALLineTag, displayedVAL, Brushes.RoyalBlue);
            else
                RemoveDrawObject(VALLineTag);

            if (ShowPOC && IsValidLevel(displayedPOC))
                Draw.HorizontalLine(this, POCLineTag, displayedPOC, Brushes.Red);
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

        private static int NormalizeTimeInput(int value)
        {
            if (value > 0 && value < 2400)
                return value * 100;

            return value;
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
            var unspecified = DateTime.SpecifyKind(sourceTime, DateTimeKind.Unspecified);
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

        #endregion
    }
}