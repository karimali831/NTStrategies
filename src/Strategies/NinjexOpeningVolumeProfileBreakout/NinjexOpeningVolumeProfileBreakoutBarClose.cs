#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class NinjexOpeningVolumeProfileBreakoutBarClose : Strategy
    {
        private NinjexOpeningVolumeProfile openingProfile;

        private TimeZoneInfo sourceTimeZone;
        private TimeZoneInfo easternTimeZone;

        private DateTime activeTradeDate = Core.Globals.MinDate;

        private bool tradingLockedForDay;
        private int tradesToday;

        private bool longBreakoutArmed;
        private bool shortBreakoutArmed;

        private double activeVAH = double.NaN;
        private double activeVAL = double.NaN;
        private double activePOC = double.NaN;

        private const string LongSignal = "OVP_Long";
        private const string ShortSignal = "OVP_Short";

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Ninjex Opening Volume Profile Breakout Bar Close";
                Description = "Backtestable bar-close breakout strategy using VAH/VAL/POC from Ninjex Opening Volume Profile.";

                Calculate = Calculate.OnBarClose;

                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;

                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;

                IsInstantiatedOnEachOptimizationIteration = false;

                ProfileStartTime = 930;
                ProfileEndTime = 945;

                EntryStartTime = 950;
                EntryEndTime = 1100;

                RowSizeTicks = 1;
                ValueAreaPercent = 70;
                UseTickDataForProfile = true;

                ConvertChartTimeToEastern = true;
                SourceTimeZoneId = "GMT Standard Time";

                EntryOffsetTicks = 0;
                MinRetracementTicks = 15;

                StopOffsetTicksFromPOC = 2;
                RewardRiskRatio = 2.0;

                Quantity = 1;
                MaxTradesPerDay = 1;

                AddProfileIndicatorToChart = true;
                ShowProfilePanel = true;
                ShowProfileHorizontalLines = true;

                EnableDebug = false;
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

                openingProfile = NinjexOpeningVolumeProfile(
                    ProfileStartTime,
                    ProfileEndTime,
                    RowSizeTicks,
                    ValueAreaPercent,
                    UseTickDataForProfile,
                    ConvertChartTimeToEastern,
                    SourceTimeZoneId,
                    ShowProfilePanel,
                    ShowProfileHorizontalLines,
                    true,
                    true,
                    true);

                if (AddProfileIndicatorToChart)
                    AddChartIndicator(openingProfile);
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            if (CurrentBar < 20)
                return;

            ResetDailyStateIfNeeded();

            if (tradingLockedForDay)
                return;

            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            if (!LoadActiveProfileLevels())
                return;

            DateTime easternNow = ConvertChartTimeToEastern
                ? ConvertTime(Time[0], sourceTimeZone, easternTimeZone)
                : Time[0];

            int timeValue = ToTime(easternNow);

            int profileEnd = NormalizeTimeInput(ProfileEndTime);
            int entryStart = NormalizeTimeInput(EntryStartTime);
            int entryEnd = NormalizeTimeInput(EntryEndTime);

            UpdateBreakoutArming(timeValue, profileEnd);

            if (timeValue < entryStart || timeValue > entryEnd)
                return;

            TrySubmitBarCloseEntry();
        }

        private void UpdateBreakoutArming(int timeValue, int profileEnd)
        {
            if (timeValue <= profileEnd)
                return;

            double retraceDistance = Math.Max(0, MinRetracementTicks) * TickSize;

            double longArmPrice = GetLongTrigger() - retraceDistance;
            double shortArmPrice = GetShortTrigger() + retraceDistance;

            bool bodyInsideRange =
                IsInsideProfileRange(Open[0]) ||
                IsInsideProfileRange(Close[0]) ||
                (Low[0] <= activeVAH && High[0] >= activeVAL);

            if (!bodyInsideRange)
                return;

            if (!longBreakoutArmed && Low[0] <= longArmPrice && longArmPrice >= activeVAL)
            {
                longBreakoutArmed = true;
                DebugPrint("Long armed. Bar retraced inside range. ArmPrice=" + longArmPrice);
            }

            if (!shortBreakoutArmed && High[0] >= shortArmPrice && shortArmPrice <= activeVAH)
            {
                shortBreakoutArmed = true;
                DebugPrint("Short armed. Bar retraced inside range. ArmPrice=" + shortArmPrice);
            }
        }

        private void TrySubmitBarCloseEntry()
        {
            if (tradesToday >= MaxTradesPerDay)
                return;

            double longTrigger = GetLongTrigger();
            double shortTrigger = GetShortTrigger();

            double bodyHigh = Math.Max(Open[0], Close[0]);
            double bodyLow = Math.Min(Open[0], Close[0]);

            bool previousCloseInsideRange = CurrentBar > 0 && IsInsideProfileRange(Close[1]);
            bool currentOpenedInsideRange = IsInsideProfileRange(Open[0]);

            bool originatedInsideRange = previousCloseInsideRange || currentOpenedInsideRange;

            bool longBodyBreak =
                originatedInsideRange &&
                bodyLow <= longTrigger &&
                bodyHigh >= longTrigger &&
                Close[0] > longTrigger;

            bool shortBodyBreak =
                originatedInsideRange &&
                bodyHigh >= shortTrigger &&
                bodyLow <= shortTrigger &&
                Close[0] < shortTrigger;

            DebugPrint(
                "Bar close check"
                + " Time=" + Time[0]
                + " Open=" + Open[0]
                + " Close=" + Close[0]
                + " VAH=" + activeVAH
                + " VAL=" + activeVAL
                + " POC=" + activePOC
                + " LongArmed=" + longBreakoutArmed
                + " ShortArmed=" + shortBreakoutArmed
                + " LongBodyBreak=" + longBodyBreak
                + " ShortBodyBreak=" + shortBodyBreak
                + " OriginatedInside=" + originatedInsideRange);

            if (longBodyBreak)
            {
                if (!longBreakoutArmed)
                {
                    DebugPrint("Long blocked: minimum retracement inside range not satisfied.");
                    return;
                }

                SubmitManagedLong();
                return;
            }

            if (shortBodyBreak)
            {
                if (!shortBreakoutArmed)
                {
                    DebugPrint("Short blocked: minimum retracement inside range not satisfied.");
                    return;
                }

                SubmitManagedShort();
            }
        }

        private void SubmitManagedLong()
        {
            double expectedEntry = Close[0];
            double stopPrice = activePOC - StopOffsetTicksFromPOC * TickSize;

            stopPrice = Instrument.MasterInstrument.RoundToTickSize(stopPrice);

            if (stopPrice >= expectedEntry)
            {
                DebugPrint("Long skipped: invalid stop. Stop=" + stopPrice + " Entry=" + expectedEntry);
                return;
            }

            double risk = expectedEntry - stopPrice;
            double targetPrice = expectedEntry + risk * RewardRiskRatio;

            targetPrice = Instrument.MasterInstrument.RoundToTickSize(targetPrice);

            SetStopLoss(LongSignal, CalculationMode.Price, stopPrice, false);
            SetProfitTarget(LongSignal, CalculationMode.Price, targetPrice);

            longBreakoutArmed = false;
            shortBreakoutArmed = false;

            EnterLong(Quantity, LongSignal);
            tradesToday++;

            DebugPrint("Long submitted. Entry=" + expectedEntry + " Stop=" + stopPrice + " Target=" + targetPrice);
        }

        private void SubmitManagedShort()
        {
            double expectedEntry = Close[0];
            double stopPrice = activePOC + StopOffsetTicksFromPOC * TickSize;

            stopPrice = Instrument.MasterInstrument.RoundToTickSize(stopPrice);

            if (stopPrice <= expectedEntry)
            {
                DebugPrint("Short skipped: invalid stop. Stop=" + stopPrice + " Entry=" + expectedEntry);
                return;
            }

            double risk = stopPrice - expectedEntry;
            double targetPrice = expectedEntry - risk * RewardRiskRatio;

            targetPrice = Instrument.MasterInstrument.RoundToTickSize(targetPrice);

            SetStopLoss(ShortSignal, CalculationMode.Price, stopPrice, false);
            SetProfitTarget(ShortSignal, CalculationMode.Price, targetPrice);

            longBreakoutArmed = false;
            shortBreakoutArmed = false;

            EnterShort(Quantity, ShortSignal);
            tradesToday++;

            DebugPrint("Short submitted. Entry=" + expectedEntry + " Stop=" + stopPrice + " Target=" + targetPrice);
        }

        private bool LoadActiveProfileLevels()
        {
            if (openingProfile == null)
                return false;

            activeVAH = openingProfile.VAH[0];
            activeVAL = openingProfile.VAL[0];
            activePOC = openingProfile.POC[0];

            return IsValidLevel(activeVAH)
                && IsValidLevel(activeVAL)
                && IsValidLevel(activePOC)
                && activeVAH > activeVAL;
        }

        private double GetLongTrigger()
        {
            return Instrument.MasterInstrument.RoundToTickSize(activeVAH + EntryOffsetTicks * TickSize);
        }

        private double GetShortTrigger()
        {
            return Instrument.MasterInstrument.RoundToTickSize(activeVAL - EntryOffsetTicks * TickSize);
        }

        private void ResetDailyStateIfNeeded()
        {
            DateTime checkTime = ConvertChartTimeToEastern
                ? ConvertTime(Time[0], sourceTimeZone, easternTimeZone)
                : Time[0];

            DateTime tradeDate = checkTime.Date;

            if (activeTradeDate == tradeDate)
                return;

            activeTradeDate = tradeDate;

            tradingLockedForDay = false;
            tradesToday = 0;

            longBreakoutArmed = false;
            shortBreakoutArmed = false;

            activeVAH = double.NaN;
            activeVAL = double.NaN;
            activePOC = double.NaN;

            DebugPrint("New trading day: " + activeTradeDate.ToString("yyyyMMdd"));
        }

        private bool IsInsideProfileRange(double price)
        {
            return IsValidLevel(price)
                && price >= activeVAL
                && price <= activeVAH;
        }

        private bool IsValidLevel(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
        }

        private void DebugPrint(string message)
        {
            if (!EnableDebug)
                return;

            Print(Time[0] + " | " + Name + " | " + message);
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
        [Range(0, 235959)]
        [Display(Name = "Entry Start Time", Order = 10, GroupName = "Entry")]
        public int EntryStartTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Entry End Time", Order = 11, GroupName = "Entry")]
        public int EntryEndTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Entry Offset Ticks", Order = 12, GroupName = "Entry")]
        public int EntryOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 200)]
        [Display(Name = "Min Retracement Ticks", Order = 13, GroupName = "Entry")]
        public int MinRetracementTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "Stop Offset Ticks From POC", Order = 20, GroupName = "Bracket")]
        public int StopOffsetTicksFromPOC { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 20)]
        [Display(Name = "Reward Risk Ratio", Order = 21, GroupName = "Bracket")]
        public double RewardRiskRatio { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Quantity", Order = 30, GroupName = "Order")]
        public int Quantity { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Max Trades Per Day", Order = 31, GroupName = "Order")]
        public int MaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Convert Chart Time To Eastern", Order = 40, GroupName = "Time")]
        public bool ConvertChartTimeToEastern { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Source Time Zone Id", Order = 41, GroupName = "Time")]
        public string SourceTimeZoneId { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Add Profile Indicator To Chart", Order = 50, GroupName = "Visual")]
        public bool AddProfileIndicatorToChart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Profile Panel", Order = 51, GroupName = "Visual")]
        public bool ShowProfilePanel { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Profile Horizontal Lines", Order = 52, GroupName = "Visual")]
        public bool ShowProfileHorizontalLines { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Debug", Order = 100, GroupName = "Debug")]
        public bool EnableDebug { get; set; }

        #endregion
    }
}