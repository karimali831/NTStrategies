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

        private int winningTradesToday;
        private int losingTradesToday;
        private int breakEvenTradesToday;
        
        private const string LongSignal = "OVP_Long";
        private const string ShortSignal = "OVP_Short";

        private bool breakEvenMoved;

        private string activeTradeDirection = string.Empty;
        private double activeTradeEntryPrice = double.NaN;
        private int activeTradeQuantity;

        private double previousPrice = double.NaN;
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
                Description = "Managed-order opening volume profile breakout strategy using VAH/VAL from Ninjex Opening Volume Profile.";

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

                Quantity = 1;

                EntryOffsetTicks = 0;
                MinRetracementTicks = 15;
				StopLossUsd = 250;
				ProfitTargetUsd = 500;
				BreakEvenProfitTriggerUsd = 250;
				BreakEvenPlusUsd = 10;

				MaxLosingTradesPerDay = 1;
				MaxWinningTradesPerDay = 2;

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
                
                profileEngine = new NinjexOpeningVolumeProfileEngine();
            }
        }

        protected override void OnBarUpdate()
        {
	        if (UseTickDataForProfile && BarsInProgress == 1)
	        {
		        ProcessProfileTickForStrategy();
		        return;
	        }

	        if (BarsInProgress != 0)
		        return;

	        if (CurrentBar < 20)
		        return;

	        ResetDailyStateIfNeeded();
		
	        ManageBreakEven();

	        if (tradingLockedForDay)
	        {
		        previousPrice = Close[0];
		        return;
	        }

	        if (Position.MarketPosition != MarketPosition.Flat)
	        {
		        previousPrice = Close[0];
		        return;
	        }
		
		    DateTime easternNow = ConvertChartTimeToEastern
			    ? ConvertTime(Time[0], sourceTimeZone, easternTimeZone)
			    : Time[0];

		    if (!LoadActiveProfileLevels(easternNow.Date))
		    {
			    previousPrice = Close[0];
			    return;
		    }
		
		    var timeValue = ToTime(easternNow);
		
		    var profileEnd = NormalizeTimeInput(ProfileEndTime);
		    var entryStart = NormalizeTimeInput(EntryStartTime);
		    var entryEnd = NormalizeTimeInput(EntryEndTime);
		
		    var price = Close[0];

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
        
        private bool LoadActiveProfileLevels(DateTime expectedProfileDate)
        {
	        if (profileEngine == null || !profileEngine.HasCompletedProfile)
		        return false;

	        if (profileEngine.LatestProfileDate.Date != expectedProfileDate.Date)
		        return false;

	        activeVAH = profileEngine.LatestVAH;
	        activeVAL = profileEngine.LatestVAL;
	        activePOC = profileEngine.LatestPOC;

	        return IsValidLevel(activeVAH)
	               && IsValidLevel(activeVAL)
	               && IsValidLevel(activePOC)
	               && activeVAH > activeVAL;
        }

        
        private void ProcessProfileTickForStrategy()
        {
	        if (profileEngine == null)
		        return;

	        if (CurrentBars.Length <= 1 || CurrentBars[1] < 1)
		        return;

	        var tickChartTime = Times[1][0];

	        var profileTime = ConvertChartTimeToEastern
		        ? ConvertTime(tickChartTime, sourceTimeZone, easternTimeZone)
		        : tickChartTime;

	        profileEngine.ProcessTick(
		        profileTime,
		        Closes[1][0],
		        Volumes[1][0],
		        TickSize,
		        ProfileStartTime,
		        ProfileEndTime,
		        RowSizeTicks,
		        ValueAreaPercent);
        }

        private void TrackPreEntrySweepsAndRetraces(int timeValue, int profileEnd, int entryStart, double price)
		{
		    var longTrigger = GetLongTrigger();
		    var shortTrigger = GetShortTrigger();
		
		    var retraceDistance = Math.Max(0, MinRetracementTicks) * TickSize;
		
		    // Important:
			// Do not treat the exact profile end bar/tick as a pre-entry sweep.
			// On a 5-minute chart, the 09:45 timestamp belongs to the final profile bar.
			// Pre-entry sweep tracking should only begin AFTER the profile has completed.
			var beforeEntryWindow = timeValue > profileEnd && timeValue < entryStart;
		
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
		        var insideRange = price <= activeVAH && price >= activeVAL;
		        var enoughRetrace = price <= longTrigger - retraceDistance;
		
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
		        var insideRange = price >= activeVAL && price <= activeVAH;
		        var enoughRetrace = price >= shortTrigger + retraceDistance;
		
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
			if (!CanTakeNewTrade())
			{
				DebugPrintThrottled(
					"No entry: daily risk lock or active position."
					+ " WinsToday=" + winningTradesToday
					+ " LossesToday=" + losingTradesToday
					+ " BEToday=" + breakEvenTradesToday
					+ " Locked=" + tradingLockedForDay
					+ " Position=" + Position.MarketPosition);

				return;
			}
			
		    var longTrigger = GetLongTrigger();
		    var shortTrigger = GetShortTrigger();
		
		   	var previousWasInsideRange = IsInsideProfileRange(previousPrice);

			var crossedLong = !double.IsNaN(previousPrice)
			                  && previousWasInsideRange
			                  && previousPrice < longTrigger
			                  && price >= longTrigger;
			
			var crossedShort = !double.IsNaN(previousPrice)
			                   && previousWasInsideRange
			                   && previousPrice > shortTrigger
			                   && price <= shortTrigger;
			
			var longBlockedByRetrace = preEntryLongSweep && !longRetraceSatisfied;
			var shortBlockedByRetrace = preEntryShortSweep && !shortRetraceSatisfied;
			
			// Extra safety:
			// If retrace was satisfied on THIS tick, do not enter on this same tick.
			// Wait for a later tick to cross VAH/VAL again.
			if (longRetraceArmedThisTick)
			{
			    DebugPrint("Long retrace armed this tick. No entry allowed until fresh VAH cross.");
			    return;
			}
			
			if (shortRetraceArmedThisTick)
			{
			    DebugPrint("Short retrace armed this tick. No entry allowed until fresh VAL cross.");
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
			    + " PreLongSweep=" + preEntryLongSweep
			    + " LongRetraceOk=" + longRetraceSatisfied
			    + " PreShortSweep=" + preEntryShortSweep
			    + " ShortRetraceOk=" + shortRetraceSatisfied
			    + " WinsToday=" + winningTradesToday
			    + " LossesToday=" + losingTradesToday
			    + " BEToday=" + breakEvenTradesToday);
			
			if (crossedLong)
			{
			    if (longBlockedByRetrace)
			    {
			        DebugPrint("Long blocked: retrace required before fresh VAH breakout.");
			        return;
			    }
			
			    SubmitManagedLong();
			    return;
			}
			
			if (crossedShort)
			{
			    if (shortBlockedByRetrace)
			    {
			        DebugPrint("Short blocked: retrace required before fresh VAL breakout.");
			        return;
			    }
			
			    SubmitManagedShort();
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
        
        private void SubmitManagedLong()
        {
	        var stopTicks = CurrencyToTicks(StopLossUsd, Quantity);
	        var targetTicks = CurrencyToTicks(ProfitTargetUsd, Quantity);

	        if (stopTicks <= 0 || targetTicks <= 0)
	        {
		        DebugPrint("Long skipped: invalid bracket ticks. StopTicks=" + stopTicks + " TargetTicks=" + targetTicks);
		        return;
	        }

	        SetStopLoss(LongSignal, CalculationMode.Ticks, stopTicks, false);
	        SetProfitTarget(LongSignal, CalculationMode.Ticks, targetTicks);

	        breakEvenMoved = false;

	        EnterLong(Quantity, LongSignal);

	        DebugPrint("Long submitted. StopTicks=" + stopTicks + " TargetTicks=" + targetTicks);
        }

        private void SubmitManagedShort()
        {
	        var stopTicks = CurrencyToTicks(StopLossUsd, Quantity);
	        var targetTicks = CurrencyToTicks(ProfitTargetUsd, Quantity);

	        if (stopTicks <= 0 || targetTicks <= 0)
	        {
		        DebugPrint("Short skipped: invalid bracket ticks. StopTicks=" + stopTicks + " TargetTicks=" + targetTicks);
		        return;
	        }

	        SetStopLoss(ShortSignal, CalculationMode.Ticks, stopTicks, false);
	        SetProfitTarget(ShortSignal, CalculationMode.Ticks, targetTicks);

	        breakEvenMoved = false;

	        EnterShort(Quantity, ShortSignal);

	        DebugPrint("Short submitted. StopTicks=" + stopTicks + " TargetTicks=" + targetTicks);
        }

        private int CurrencyToTicks(double currencyAmount, int quantity)
        {
	        var tickValue = Instrument.MasterInstrument.PointValue * TickSize;
	        var safeQuantity = Math.Max(1, quantity);

	        if (currencyAmount <= 0 || tickValue <= 0)
		        return 0;

	        return Math.Max(1, (int)Math.Round(currencyAmount / (tickValue * safeQuantity), MidpointRounding.AwayFromZero));
        }
        
        private bool CanTakeNewTrade()
        {
	        if (tradingLockedForDay)
		        return false;

	        if (losingTradesToday >= MaxLosingTradesPerDay)
		        return false;

	        if (winningTradesToday >= MaxWinningTradesPerDay)
		        return false;

	        if (Position.MarketPosition != MarketPosition.Flat)
		        return false;

	        return true;
        }
        
        private void ManageBreakEven()
        {
	        if (BreakEvenProfitTriggerUsd <= 0)
		        return;

	        if (breakEvenMoved)
		        return;

	        if (Position.MarketPosition == MarketPosition.Flat)
		        return;

	        var pointValue = Instrument.MasterInstrument.PointValue;
	        var safeQuantity = Math.Max(1, Position.Quantity);

	        if (pointValue <= 0)
		        return;

	        var triggerDistance = BreakEvenProfitTriggerUsd / (pointValue * safeQuantity);
	        var plusDistance = BreakEvenPlusUsd / (pointValue * safeQuantity);

	        if (Position.MarketPosition == MarketPosition.Long)
	        {
		        var triggerPrice = Position.AveragePrice + triggerDistance;

		        if (Close[0] < triggerPrice)
			        return;

		        var newStopPrice = Instrument.MasterInstrument.RoundToTickSize(Position.AveragePrice + plusDistance);

		        SetStopLoss(LongSignal, CalculationMode.Price, newStopPrice, false);
		        breakEvenMoved = true;

		        DebugPrint("Long BE moved. Stop=" + newStopPrice);
		        return;
	        }

	        if (Position.MarketPosition == MarketPosition.Short)
	        {
		        var triggerPrice = Position.AveragePrice - triggerDistance;

		        if (Close[0] > triggerPrice)
			        return;

		        var newStopPrice = Instrument.MasterInstrument.RoundToTickSize(Position.AveragePrice - plusDistance);

		        SetStopLoss(ShortSignal, CalculationMode.Price, newStopPrice, false);
		        breakEvenMoved = true;

		        DebugPrint("Short BE moved. Stop=" + newStopPrice);
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
	        if (execution == null || execution.Order == null)
		        return;

	        var order = execution.Order;
	        var orderName = order.Name ?? string.Empty;

	        if (quantity <= 0)
		        return;

	        var isLongEntry = orderName == LongSignal;
	        var isShortEntry = orderName == ShortSignal;

	        if (isLongEntry || isShortEntry)
	        {
		        activeTradeDirection = isLongEntry ? "LONG" : "SHORT";
		        activeTradeEntryPrice = price;
		        activeTradeQuantity = quantity;

		        DebugPrint(
			        "Entry fill. Direction=" + activeTradeDirection
			                                 + " Price=" + price
			                                 + " Qty=" + quantity);

		        return;
	        }

	        if (string.IsNullOrWhiteSpace(activeTradeDirection) || double.IsNaN(activeTradeEntryPrice))
		        return;

	        var isExitAction =
		        order.OrderAction == OrderAction.Sell ||
		        order.OrderAction == OrderAction.BuyToCover;

	        if (!isExitAction)
		        return;

	        var realizedPnl = CalculateTradePnlUsd(
		        activeTradeDirection,
		        activeTradeEntryPrice,
		        price,
		        activeTradeQuantity);

	        RegisterCompletedTrade(realizedPnl);

	        activeTradeDirection = string.Empty;
	        activeTradeEntryPrice = double.NaN;
	        activeTradeQuantity = 0;
	        breakEvenMoved = false;
        }
        
        private double CalculateTradePnlUsd(string direction, double entryPrice, double exitPrice, int quantity)
        {
	        var pointValue = Instrument.MasterInstrument.PointValue;
	        var safeQuantity = Math.Max(1, quantity);

	        if (direction == "LONG")
		        return (exitPrice - entryPrice) * pointValue * safeQuantity;

	        if (direction == "SHORT")
		        return (entryPrice - exitPrice) * pointValue * safeQuantity;

	        return 0;
        }

        private void RegisterCompletedTrade(double realizedPnl)
        {
	        var lossThreshold = -Math.Abs(StopLossUsd) * 0.75;
	        var winThreshold = Math.Abs(ProfitTargetUsd) * 0.75;

	        if (realizedPnl <= lossThreshold)
	        {
		        losingTradesToday++;

		        if (losingTradesToday >= MaxLosingTradesPerDay)
			        tradingLockedForDay = true;

		        DebugPrint(
			        "Losing trade registered. PnL=" + realizedPnl
			                                        + " LossesToday=" + losingTradesToday
			                                        + " Locked=" + tradingLockedForDay);

		        return;
	        }

	        if (realizedPnl >= winThreshold)
	        {
		        winningTradesToday++;

		        if (winningTradesToday >= MaxWinningTradesPerDay)
			        tradingLockedForDay = true;

		        DebugPrint(
			        "Winning trade registered. PnL=" + realizedPnl
			                                         + " WinsToday=" + winningTradesToday
			                                         + " Locked=" + tradingLockedForDay);

		        return;
	        }

	        breakEvenTradesToday++;

	        DebugPrint(
		        "Break-even trade registered. PnL=" + realizedPnl
		                                            + " BEToday=" + breakEvenTradesToday);
        }
        
        private void ResetDailyStateIfNeeded()
        {
            var chartTime = Time[0];

            var checkTime = ConvertChartTimeToEastern
                ? ConvertTime(chartTime, sourceTimeZone, easternTimeZone)
                : chartTime;

            var tradeDate = checkTime.Date;

            if (activeTradeDate == tradeDate)
                return;

            activeTradeDate = tradeDate;
            tradingLockedForDay = false;

            winningTradesToday = 0;
            losingTradesToday = 0;
            breakEvenTradesToday = 0;

            breakEvenMoved = false;

            activeTradeDirection = string.Empty;
            activeTradeEntryPrice = double.NaN;
            activeTradeQuantity = 0;

            previousPrice = double.NaN;
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
        [Range(1, 100)]
        [Display(Name = "Quantity", Order = 19, GroupName = "Risk Management")]
        public int Quantity { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100000)]
        [Display(Name = "Profit Target USD", Order = 20, GroupName = "Risk Management")]
        public double ProfitTargetUsd { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100000)]
        [Display(Name = "Stop Loss USD", Order = 21, GroupName = "Risk Management")]
        public double StopLossUsd { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100000)]
        [Display(Name = "BE Profit Trigger USD", Order = 22, GroupName = "Risk Management")]
        public double BreakEvenProfitTriggerUsd { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100000)]
        [Display(Name = "BE Plus USD", Order = 23, GroupName = "Risk Management")]
        public double BreakEvenPlusUsd { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Max Losing Trades Per Day", Order = 24, GroupName = "Risk Management")]
        public int MaxLosingTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Max Winning Trades Per Day", Order = 25, GroupName = "Risk Management")]
        public int MaxWinningTradesPerDay { get; set; }

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

