#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript.Ninjex;

#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class NinjexOpeningVolumeProfileBreakoutOnEachTick : Strategy
    {
	    private NinjexOpeningVolumeProfileEngine profileEngine;
	    
        private TimeZoneInfo sourceTimeZone;
        private TimeZoneInfo easternTimeZone;

        private DateTime activeTradeDate = Core.Globals.MinDate;

        private bool tradingLockedForDay;

        private int tradesToday;
        private int breakEvenTradesToday;

        private bool atmCreationPending;
        private bool atmActive;
        private bool atmPositionWasOpen;

        private string atmStrategyId = string.Empty;
        private string atmOrderId = string.Empty;
		private int lastEntryDirection;

        private double previousPrice = double.NaN;
		private bool hasSeenRealtimeEligibleTick;
		private DateTime lastDebugPrintTime = Core.Globals.MinDate;
		
        private bool preEntryLongSweep;
        private bool preEntryShortSweep;

        private bool longRetraceSatisfied;
        private bool shortRetraceSatisfied;
		
		private bool longRetraceArmedThisTick;
		private bool shortRetraceArmedThisTick;

        private double activeVAH = double.NaN;
        private double activeVAL = double.NaN;
        private double activePOC = double.NaN;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Ninjex Opening Volume Profile Breakout";
                Description = "ATM-based opening volume profile breakout strategy using VAH/VAL from Ninjex Opening Volume Profile.";

                Calculate = Calculate.OnEachTick;

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

                AtmTemplateName = "Your ATM Template Name";
                Quantity = 1;

                EntryOffsetTicks = 0;
                MinRetracementTicks = 15;
				AllowCatchUpEntryOnFirstRealtimeTick = true;
                MaxTradesIfBreakEven = 2;
                BreakEvenMinCurrency = -100;
                BreakEvenMaxCurrency = 300;

                AddProfileIndicatorToChart = true;
                ShowProfilePanel = true;
                ShowProfileHorizontalLines = true;

                EnableDebug = false;
				DebugEverySeconds = 5;
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
                
                if (AddProfileIndicatorToChart)
	                profileEngine = new NinjexOpeningVolumeProfileEngine();
            }
        }

       	protected override void OnBarUpdate()
		{
		    if (BarsInProgress != 0)
		        return;
		
		    if (CurrentBar < 20)
		        return;
		
		    ResetDailyStateIfNeeded();
		
		    // ATM methods can only be used in realtime.
		    // Historical processing may still calculate/reset state, but must never submit ATM orders.
		    if (State != State.Realtime)
		    {
		        previousPrice = Close[0];
		        return;
		    }
		
		    UpdateAtmState();
		
		    if (tradingLockedForDay)
		    {
		        previousPrice = Close[0];
		        return;
		    }
		
		    if (atmCreationPending || atmActive)
		    {
		        previousPrice = Close[0];
		        return;
		    }
		
		    if (!LoadActiveProfileLevels())
		    {
		        previousPrice = Close[0];
		        return;
		    }
		
		    DateTime easternNow = ConvertChartTimeToEastern
		        ? ConvertTime(Time[0], sourceTimeZone, easternTimeZone)
		        : Time[0];
		
		    int timeValue = ToTime(easternNow);
		
		    int profileEnd = NormalizeTimeInput(ProfileEndTime);
		    int entryStart = NormalizeTimeInput(EntryStartTime);
		    int entryEnd = NormalizeTimeInput(EntryEndTime);
		
		    double price = Close[0];

			longRetraceArmedThisTick = false;
			shortRetraceArmedThisTick = false;
			
			TrackPreEntrySweepsAndRetraces(timeValue, profileEnd, entryStart, price);
		
		    if (timeValue < entryStart || timeValue > entryEnd)
		    {
		        previousPrice = price;
		        return;
		    }
		
		    TrySubmitEntry(price);
		
		    previousPrice = price;
		}

        private bool LoadActiveProfileLevels()
        {
            if (profileEngine == null)
                return false;

            activeVAH = profileEngine.LatestVAH;
            activeVAL = profileEngine.LatestVAL;
            activePOC = profileEngine.LatestPOC;

            return IsValidLevel(activeVAH)
                && IsValidLevel(activeVAL)
                && IsValidLevel(activePOC)
                && activeVAH > activeVAL;
        }

        private void TrackPreEntrySweepsAndRetraces(int timeValue, int profileEnd, int entryStart, double price)
		{
		    double longTrigger = GetLongTrigger();
		    double shortTrigger = GetShortTrigger();
		
		    double retraceDistance = Math.Max(0, MinRetracementTicks) * TickSize;
		
		    // Important:
			// Do not treat the exact profile end bar/tick as a pre-entry sweep.
			// On a 5-minute chart, the 09:45 timestamp belongs to the final profile bar.
			// Pre-entry sweep tracking should only begin AFTER the profile has completed.
			bool beforeEntryWindow = timeValue > profileEnd && timeValue < entryStart;
		
		    if (beforeEntryWindow)
			{
			    if (!preEntryLongSweep && price >= longTrigger)
			    {
			        preEntryLongSweep = true;
			        longRetraceSatisfied = false;
			        DebugPrint("Pre-entry long sweep detected. Waiting for retrace into range.");
			    }
			
			    if (!preEntryShortSweep && price <= shortTrigger)
			    {
			        preEntryShortSweep = true;
			        shortRetraceSatisfied = false;
			        DebugPrint("Pre-entry short sweep detected. Waiting for retrace into range.");
			    }
			}
		
		    if (preEntryLongSweep && !longRetraceSatisfied)
		    {
		        bool insideRange = price <= activeVAH && price >= activeVAL;
		        bool enoughRetrace = price <= longTrigger - retraceDistance;
		
		        if (insideRange && enoughRetrace)
		        {
		            longRetraceSatisfied = true;
		            longRetraceArmedThisTick = true;
		
		            // Critical:
		            // This prevents the retracement tick from being treated as an entry-cross tick.
		            previousPrice = double.NaN;
		
		            DebugPrint("Long retrace satisfied. Long side armed. Waiting for fresh VAH cross.");
		        }
		    }
		
		    if (preEntryShortSweep && !shortRetraceSatisfied)
		    {
		        bool insideRange = price >= activeVAL && price <= activeVAH;
		        bool enoughRetrace = price >= shortTrigger + retraceDistance;
		
		        if (insideRange && enoughRetrace)
		        {
		            shortRetraceSatisfied = true;
		            shortRetraceArmedThisTick = true;
		
		            // Critical:
		            // This prevents the retracement tick from being treated as an entry-cross tick.
		            previousPrice = double.NaN;
		
		            DebugPrint("Short retrace satisfied. Short side armed. Waiting for fresh VAL cross.");
		        }
		    }
		}

       	private void TrySubmitEntry(double price)
		{
		    if (tradesToday >= MaxTradesIfBreakEven)
		    {
		        DebugPrintThrottled("No entry: max trades reached. TradesToday=" + tradesToday);
		        return;
		    }
		
		    if (string.IsNullOrWhiteSpace(AtmTemplateName) || AtmTemplateName == "Your ATM Template Name")
		    {
		        DebugPrintThrottled("No entry: ATM template name is not set.");
		        return;
		    }
		
		    double longTrigger = GetLongTrigger();
		    double shortTrigger = GetShortTrigger();
		
		   	bool previousWasInsideRange = IsInsideProfileRange(previousPrice);

			bool crossedLong = !double.IsNaN(previousPrice)
			    && previousWasInsideRange
			    && previousPrice < longTrigger
			    && price >= longTrigger;
			
			bool crossedShort = !double.IsNaN(previousPrice)
			    && previousWasInsideRange
			    && previousPrice > shortTrigger
			    && price <= shortTrigger;
			
			// Disabled by design:
			// A valid entry must break out FROM INSIDE the VAH/VAL range.
			// Catch-up entries cannot prove that the move came from inside the range.
			bool catchUpLong = false;
			bool catchUpShort = false;
			
			bool longBlockedByRetrace = preEntryLongSweep && !longRetraceSatisfied;
			bool shortBlockedByRetrace = preEntryShortSweep && !shortRetraceSatisfied;
			
			// Extra safety:
			// If retrace was satisfied on THIS tick, do not enter on this same tick.
			// Wait for a later tick to cross VAH/VAL again.
			if (longRetraceArmedThisTick)
			{
			    DebugPrint("Long retrace armed this tick. No entry allowed until fresh VAH cross.");
			    hasSeenRealtimeEligibleTick = true;
			    return;
			}
			
			if (shortRetraceArmedThisTick)
			{
			    DebugPrint("Short retrace armed this tick. No entry allowed until fresh VAL cross.");
			    hasSeenRealtimeEligibleTick = true;
			    return;
			}
			
			DebugPrintThrottled(
			    "Entry check"
			    + " Price=" + price
			    + " Prev=" + previousPrice
			    + " PrevInsideRange=" + previousWasInsideRange
			    + " VAH=" + activeVAH
			    + " VAL=" + activeVAL
			    + " LongTrigger=" + longTrigger
			    + " ShortTrigger=" + shortTrigger
			    + " CrossLong=" + crossedLong
			    + " CrossShort=" + crossedShort
			    + " CatchUpLong=" + catchUpLong
			    + " CatchUpShort=" + catchUpShort
			    + " PreLongSweep=" + preEntryLongSweep
			    + " LongRetraceOk=" + longRetraceSatisfied
			    + " PreShortSweep=" + preEntryShortSweep
			    + " ShortRetraceOk=" + shortRetraceSatisfied
			    + " TradesToday=" + tradesToday);
			
			hasSeenRealtimeEligibleTick = true;
			
			if (crossedLong || catchUpLong)
			{
			    if (longBlockedByRetrace)
			    {
			        DebugPrint("Long blocked: retrace required before fresh VAH breakout.");
			        return;
			    }
			
			    SubmitAtmEntry(OrderAction.Buy);
			    return;
			}
			
			if (crossedShort || catchUpShort)
			{
			    if (shortBlockedByRetrace)
			    {
			        DebugPrint("Short blocked: retrace required before fresh VAL breakout.");
			        return;
			    }
			
			    SubmitAtmEntry(OrderAction.SellShort);
			    return;
			}
		}

        private double GetLongTrigger()
        {
            return Instrument.MasterInstrument.RoundToTickSize(
                activeVAH + EntryOffsetTicks * TickSize);
        }

        private double GetShortTrigger()
        {
            return Instrument.MasterInstrument.RoundToTickSize(
                activeVAL - EntryOffsetTicks * TickSize);
        }

        private void SubmitAtmEntry(OrderAction action)
		{
		    if (State != State.Realtime)
		        return;
		
		    if (atmCreationPending || atmActive)
		        return;
		
		    if (tradingLockedForDay)
		    return;
		
			lastEntryDirection = action == OrderAction.Buy ? 1 : -1;
			
			atmStrategyId = GetAtmStrategyUniqueId();
			atmOrderId = GetAtmStrategyUniqueId();

            atmCreationPending = true;
            atmActive = false;
            atmPositionWasOpen = false;

            DebugPrint("Submitting ATM entry. Action=" + action + " Template=" + AtmTemplateName);

            AtmStrategyCreate(
                action,
                OrderType.Market,
                0,
                0,
                TimeInForce.Day,
                atmOrderId,
                AtmTemplateName,
                atmStrategyId,
                (atmCallbackErrorCode, atmCallbackId) =>
                {
                    if (atmCallbackId != atmStrategyId)
                        return;

                    if (atmCallbackErrorCode == ErrorCode.NoError)
                    {
                        atmCreationPending = false;
                        atmActive = true;
                        tradesToday++;

                        DebugPrint("ATM created. TradesToday=" + tradesToday);
                    }
                    else
                    {
                        atmCreationPending = false;
                        atmActive = false;
                        tradingLockedForDay = true;

                        DebugPrint("ATM creation failed. Error=" + atmCallbackErrorCode);
                    }
                });
        }
		
		private void ArmRetraceAfterBreakEven()
		{
		    previousPrice = double.NaN;
		
		    if (lastEntryDirection > 0)
		    {
		        preEntryLongSweep = true;
		        longRetraceSatisfied = false;
		        longRetraceArmedThisTick = false;
		
		        DebugPrint("Long BE completed. Long side now requires retrace before next VAH breakout.");
		        return;
		    }
		
		    if (lastEntryDirection < 0)
		    {
		        preEntryShortSweep = true;
		        shortRetraceSatisfied = false;
		        shortRetraceArmedThisTick = false;
		
		        DebugPrint("Short BE completed. Short side now requires retrace before next VAL breakout.");
		        return;
		    }
		}

        private void UpdateAtmState()
        {
            if (!atmActive || string.IsNullOrWhiteSpace(atmStrategyId))
                return;

            MarketPosition atmPosition = GetAtmStrategyMarketPosition(atmStrategyId);

            if (atmPosition != MarketPosition.Flat)
            {
                atmPositionWasOpen = true;
                return;
            }

            if (!atmPositionWasOpen)
                return;

            double realizedPnL = GetAtmStrategyRealizedProfitLoss(atmStrategyId);

            bool wasBreakEven = realizedPnL >= BreakEvenMinCurrency
                && realizedPnL <= BreakEvenMaxCurrency;

            DebugPrint("ATM completed. RealizedPnL=" + realizedPnL + " WasBreakEven=" + wasBreakEven);

            atmActive = false;
            atmPositionWasOpen = false;
            atmStrategyId = string.Empty;
            atmOrderId = string.Empty;

            if (wasBreakEven)
			{
			    breakEvenTradesToday++;
			
			    if (tradesToday >= MaxTradesIfBreakEven)
			    {
			        tradingLockedForDay = true;
			        return;
			    }
			
			    ArmRetraceAfterBreakEven();
			    return;
			}

            tradingLockedForDay = true;
        }

        private void ResetDailyStateIfNeeded()
        {
            DateTime chartTime = Time[0];

            DateTime checkTime = ConvertChartTimeToEastern
                ? ConvertTime(chartTime, sourceTimeZone, easternTimeZone)
                : chartTime;

            DateTime tradeDate = checkTime.Date;

            if (activeTradeDate == tradeDate)
                return;

            activeTradeDate = tradeDate;

            tradingLockedForDay = false;

            tradesToday = 0;
            breakEvenTradesToday = 0;

            atmCreationPending = false;
            atmActive = false;
            atmPositionWasOpen = false;

           	atmStrategyId = string.Empty;
			atmOrderId = string.Empty;
			lastEntryDirection = 0;

            previousPrice = double.NaN;
			hasSeenRealtimeEligibleTick = false;
			lastDebugPrintTime = Core.Globals.MinDate;

            preEntryLongSweep = false;
            preEntryShortSweep = false;

            longRetraceSatisfied = false;
            shortRetraceSatisfied = false;
			
			longRetraceArmedThisTick = false;
			shortRetraceArmedThisTick = false;

            activeVAH = double.NaN;
            activeVAL = double.NaN;
            activePOC = double.NaN;

            DebugPrint("New trading day: " + activeTradeDate.ToString("yyyyMMdd"));
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
		
		private void DebugPrintThrottled(string message)
		{
		    if (!EnableDebug)
		        return;
		
		    if (DebugEverySeconds <= 0)
		    {
		        DebugPrint(message);
		        return;
		    }
		
		    if (lastDebugPrintTime != Core.Globals.MinDate
		        && (Time[0] - lastDebugPrintTime).TotalSeconds < DebugEverySeconds)
		        return;
		
		    lastDebugPrintTime = Time[0];
		    DebugPrint(message);
		}

        private static int NormalizeTimeInput(int value)
        {
            if (value > 0 && value < 2400)
                return value * 100;

            return value;
        }
		
		private bool IsInsideProfileRange(double price)
		{
		    return IsValidLevel(price)
		        && price >= activeVAL
		        && price <= activeVAH;
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
		[Display(Name = "Allow Catch-Up Entry On First Realtime Tick", Order = 14, GroupName = "Entry")]
		public bool AllowCatchUpEntryOnFirstRealtimeTick { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ATM Template Name", Order = 20, GroupName = "ATM")]
        public string AtmTemplateName { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Quantity", Order = 21, GroupName = "ATM")]
        public int Quantity { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Max Trades If Break Even", Order = 30, GroupName = "Trade Limits")]
        public int MaxTradesIfBreakEven { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Break Even Min Currency", Order = 31, GroupName = "Trade Limits")]
        public double BreakEvenMinCurrency { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Break Even Max Currency", Order = 32, GroupName = "Trade Limits")]
        public double BreakEvenMaxCurrency { get; set; }

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
		
		[NinjaScriptProperty]
		[Range(0, 60)]
		[Display(Name = "Debug Every Seconds", Order = 101, GroupName = "Debug")]
		public int DebugEverySeconds { get; set; }

        #endregion
    }
}

