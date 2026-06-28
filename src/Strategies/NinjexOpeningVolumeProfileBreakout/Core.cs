#region Using declarations
using System;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOpeningVolumeProfileBreakout : Strategy
    {
        private NinjexOpeningVolumeProfile openingProfile;

        private TimeZoneInfo sourceTimeZone;
        private TimeZoneInfo easternTimeZone;

        private DateTime activeTradeDate = NinjaTrader.Core.Globals.MinDate;

        private bool tradingLockedForDay;
        private int tradesToday;

        private bool longBreakoutArmed;
        private bool shortBreakoutArmed;

        private bool breakEvenMoved;

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
                Description = "Backtestable bar-close breakout strategy using VAH/VAL from Ninjex Opening Volume Profile.";

                Calculate = Calculate.OnBarClose;

                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;

                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;

                IsInstantiatedOnEachOptimizationIteration = false;

                // Profile
                ProfileStartTime = 930;
                ProfileEndTime = 945;
                RowSizeTicks = 1;
                ValueAreaPercent = 70;
                UseTickDataForProfile = true;

                // Entry
                EnableLongs = true;
                EnableShorts = false;
                EntryStartTime = 950;
                EntryEndTime = 1100;
                EntryOffsetTicks = 0;
                MinRetracementTicks = 15;

                // Risk management
                ProfitTargetUsd = 700;
                StopLossUsd = 350;
                BreakEvenProfitTriggerUsd = 100;
                BreakEvenPlusUsd = 50;
                MaxTradesPerDay = 1;

                // Order
                Quantity = 1;

                // Time
                ConvertChartTimeToEastern = true;
                SourceTimeZoneId = "GMT Standard Time";

                // Visual
                AddProfileIndicatorToChart = true;
                ShowProfilePanel = true;
                ShowProfileHorizontalLines = true;

                // Debug
                EnableDebug = false;
                
                // Data analysis
                EnableDataCollection = true;
                DataDirectoryName = "NinjexData";
                DataFilePrefix = "ovp_barclose_setups";
                LogDailyProfileRows = true;
                LogRejectedSetups = true;
                TrackForwardOutcome = true;
                ForwardBarsToTrack = 48;
                SameBarStopFirst = true;
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

                openingProfile = CreateOpeningProfileIndicator();

                if (AddProfileIndicatorToChart)
                    AddChartIndicator(openingProfile);

                ConfigureDataCollection();
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            if (CurrentBar < 20)
                return;

            ResetDailyStateIfNeeded();
            
            UpdatePendingSetupOutcome();

            ManageBreakEven();

            if (tradingLockedForDay)
                return;

            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            if (!LoadActiveProfileLevels())
                return;
            
            var easternNow = ConvertChartTimeToEastern
                ? ConvertTime(Time[0], sourceTimeZone, easternTimeZone)
                : Time[0];

            LogDailyProfileIfNeeded(easternNow);

            var timeValue = ToTime(easternNow);

            var profileEnd = NormalizeTimeInput(ProfileEndTime);
            var entryStart = NormalizeTimeInput(EntryStartTime);
            var entryEnd = NormalizeTimeInput(EntryEndTime);

            UpdateBreakoutArming(timeValue, profileEnd);

            if (timeValue < entryStart || timeValue > entryEnd)
                return;

            TrySubmitBarCloseEntry();
        }

        private NinjexOpeningVolumeProfile CreateOpeningProfileIndicator()
        {
#if RIDER
            return new NinjexOpeningVolumeProfile
            {
                ProfileStartTime = ProfileStartTime,
                ProfileEndTime = ProfileEndTime,
                RowSizeTicks = RowSizeTicks,
                ValueAreaPercent = ValueAreaPercent,
                UseTickDataForProfile = UseTickDataForProfile,
                ConvertChartTimeToEastern = ConvertChartTimeToEastern,
                SourceTimeZoneId = SourceTimeZoneId,
                ShowPanel = ShowProfilePanel,
                ShowHorizontalLines = ShowProfileHorizontalLines,
                ShowVAH = true,
                ShowVAL = true,
                ShowPOC = true
            };
#else
            return NinjexOpeningVolumeProfile(
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
#endif
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
                   && activeVAH > activeVAL;
        }

        private void UpdateBreakoutArming(int timeValue, int profileEnd)
        {
            if (timeValue <= profileEnd)
                return;

            if (!IsValidLevel(activeVAH) || !IsValidLevel(activeVAL))
                return;

            var retraceDistance = Math.Max(0, MinRetracementTicks) * TickSize;

            var longArmPrice = GetLongTrigger() - retraceDistance;
            var shortArmPrice = GetShortTrigger() + retraceDistance;

            var barTouchedRange = Low[0] <= activeVAH && High[0] >= activeVAL;

            if (!barTouchedRange)
                return;

            if (!longBreakoutArmed && longArmPrice >= activeVAL && Low[0] <= longArmPrice)
            {
                longBreakoutArmed = true;
                DebugPrint("Long armed. Bar retraced inside range. ArmPrice=" + longArmPrice);
            }

            if (!shortBreakoutArmed && shortArmPrice <= activeVAH && High[0] >= shortArmPrice)
            {
                shortBreakoutArmed = true;
                DebugPrint("Short armed. Bar retraced inside range. ArmPrice=" + shortArmPrice);
            }
        }
        

        private double GetLongTrigger()
        {
            return Instrument.MasterInstrument.RoundToTickSize(activeVAH + EntryOffsetTicks * TickSize);
        }

        private double GetShortTrigger()
        {
            return Instrument.MasterInstrument.RoundToTickSize(activeVAL - EntryOffsetTicks * TickSize);
        }

        private double CurrencyToPriceDistance(double currencyAmount, int quantity)
        {
            if (currencyAmount <= 0)
                return 0;

            double pointValue = Instrument.MasterInstrument.PointValue;
            int safeQuantity = Math.Max(1, quantity);

            if (pointValue <= 0)
                return 0;

            return currencyAmount / (pointValue * safeQuantity);
        }

        private void ResetDailyStateIfNeeded()
        {
            var checkTime = ConvertChartTimeToEastern
                ? ConvertTime(Time[0], sourceTimeZone, easternTimeZone)
                : Time[0];

            var tradeDate = checkTime.Date;

            if (activeTradeDate == tradeDate)
                return;
            
            if (activeTradeDate != NinjaTrader.Core.Globals.MinDate && activeTradeDate != tradeDate && pendingSetup != null)
                FinalizePendingSetup("NewDay", Time[0], Close[0], "New trading date reached before stop/target outcome.");

            activeTradeDate = tradeDate;

            tradingLockedForDay = false;
            tradesToday = 0;

            longBreakoutArmed = false;
            shortBreakoutArmed = false;
            breakEvenMoved = false;

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
                // ignored
            }

            return TimeZoneInfo.Local;
        }

        private static DateTime ConvertTime(DateTime sourceTime, TimeZoneInfo sourceZone, TimeZoneInfo destinationZone)
        {
            DateTime unspecified = DateTime.SpecifyKind(sourceTime, DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTime(unspecified, sourceZone, destinationZone);
        }
    }
}