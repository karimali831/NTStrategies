#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// ApexMicroPBLiveV3 — NQ 5m micro pullback trend strategy with safety exits:
    /// - Hard emergency exit by ticks (independent of protective orders)
    /// - Protective-order watchdog (if stop/target missing or rejected -> flatten)
    /// - Intrabar unrealized PnL kill-switch (via 1-tick data series)
    /// - Exit on session close enabled
    ///
    /// Hardened:
    /// - Duplicate instance detection (same strategy + same account + same instrument)
    ///   -> block trading + alert + cancel working orders + flatten instrument.
    /// </summary>
    public class ApexMicroPBLiveV3 : Strategy
    {
        public enum ConsistencyRuleMode
        {
            None = 0,
            ThirtyPercent = 1,
            FiftyPercent = 2
        }

        public enum DayLocked
        {
            NoLock = 0,
            DailyProfitReached = 1,
            MaxLossReached = 2,
            ConsistencyRule = 3
        }

        // ========== 01 – General / Mode ==========
        [NinjaScriptProperty]
        [Display(Name = "Debug mode", Order = 1, GroupName = "01-General")]
        public bool DebugMode { get; set; } = false;
		
		[NinjaScriptProperty]
        [Display(Name = "Is Historical Run", Order = 2, GroupName = "01-General")]
        public bool IsHistorical { get; set; } = false;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contracts", Order = 3, GroupName = "01-General")]
        public int Contracts { get; set; } = 1;

        [NinjaScriptProperty]
        [Display(Name = "Enable longs", Order = 4, GroupName = "01-General")]
        public bool EnableLongs { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Enable shorts", Order = 5, GroupName = "01-General")]
        public bool EnableShorts { get; set; } = true;

        // ---- Duplicate-instance guard (Option A: same strategy + same account + same instrument) ----
        [NinjaScriptProperty]
        [Display(Name = "Block duplicates on same account+instrument", Order = 90, GroupName = "01-General")]
        public bool BlockDuplicateInstances { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "On duplicate: Cancel working orders", Order = 91, GroupName = "01-General")]
        public bool DuplicateCancelWorkingOrders { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "On duplicate: Flatten instrument", Order = 92, GroupName = "01-General")]
        public bool DuplicateFlattenInstrument { get; set; } = true;

        // ========== 02 – Session / Time Filters ==========
        [NinjaScriptProperty]
        [Display(Name = "Min minutes from open", Order = 2, GroupName = "02-Session")]
        public int MinMinutesFromOpen { get; set; } = 20;

        [NinjaScriptProperty]
        [Display(Name = "Max minutes from open", Order = 3, GroupName = "02-Session")]
        public int MaxMinutesFromOpen { get; set; } = 120;

        [NinjaScriptProperty]
        [Display(Name = "Mid-break start (min from open)", Order = 4, GroupName = "02-Session")]
        public int MidBreakStartMin { get; set; } = 0;

        [NinjaScriptProperty]
        [Display(Name = "Mid-break end (min from open)", Order = 5, GroupName = "02-Session")]
        public int MidBreakEndMin { get; set; } = 0;

        // ========== 03 – Trend / Pullback / Confirmation ==========
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA fast", Order = 1, GroupName = "03-Trend & Filters")]
        public int EMAFast { get; set; } = 14;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA slow", Order = 2, GroupName = "03-Trend & Filters")]
        public int EMASlow { get; set; } = 50;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "ADX period", Order = 3, GroupName = "03-Trend & Filters")]
        public int ADXPeriod { get; set; } = 14;

        [NinjaScriptProperty]
        [Display(Name = "ADX min", Order = 4, GroupName = "03-Trend & Filters")]
        public double ADXMin { get; set; } = 15.0;

        [NinjaScriptProperty]
        [Display(Name = "ADX max", Order = 5, GroupName = "03-Trend & Filters")]
        public double ADXMax { get; set; } = 45.0;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Pullback lookback bars", Order = 6, GroupName = "03-Trend & Filters")]
        public int LookbackBars { get; set; } = 6;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Confirm bars", Order = 7, GroupName = "03-Trend & Filters")]
        public int ConfirmBars { get; set; } = 1;

        [NinjaScriptProperty]
        [Display(Name = "Confirm above EMA ticks", Order = 8, GroupName = "03-Trend & Filters")]
        public int ConfirmAboveEmaTicks { get; set; } = 5;

        [NinjaScriptProperty]
        [Display(Name = "Strong body ticks", Order = 9, GroupName = "03-Trend & Filters")]
        public int StrongBodyTicks { get; set; } = 4;

        [NinjaScriptProperty]
        [Display(Name = "Long pullback near-touch ticks", Order = 10, GroupName = "03-Trend & Filters")]
        public int LongTouchTicks { get; set; } = 2;

        [NinjaScriptProperty]
        [Display(Name = "Short pullback near-touch ticks", Order = 11, GroupName = "03-Trend & Filters")]
        public int ShortTouchTicks { get; set; } = 2;

        [NinjaScriptProperty]
        [Display(Name = "Max ATR (ticks, 0=disabled)", Order = 12, GroupName = "03-Trend & Filters")]
        public double MaxAtrTicks { get; set; } = 0.0;

        // ========== 04 – Risk / Money Management ==========
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max stop (ticks)", Order = 1, GroupName = "04-Risk")]
        public int MaxStopTicks { get; set; } = 40;

        [NinjaScriptProperty]
        [Display(Name = "Max profit (ticks, 0=none)", Order = 2, GroupName = "04-Risk")]
        public int MaxProfitTicks { get; set; } = 60;

        [NinjaScriptProperty]
        [Display(Name = "Stop multiplier ATR", Order = 3, GroupName = "04-Risk")]
        public double StopMultATR { get; set; } = 0.75;

        [NinjaScriptProperty]
        [Display(Name = "Reward/Risk ratio", Order = 4, GroupName = "04-Risk")]
        public double R_Ratio { get; set; } = 1.8;

        [NinjaScriptProperty]
        [Display(Name = "Min stop offset ticks", Order = 5, GroupName = "04-Risk")]
        public int MinStopOffsetTicks { get; set; } = 2;

        [NinjaScriptProperty]
        [Display(Name = "Max daily loss per contract (USD, 0=disabled)", Order = 6, GroupName = "04-Risk")]
        public double MaxDailyLossPerContractUSD { get; set; } = 300.0;

        [NinjaScriptProperty]
        [Display(Name = "Max trades per day", Order = 7, GroupName = "04-Risk")]
        public int MaxTradesPerDay { get; set; } = 8;

        [NinjaScriptProperty]
        [Display(Name = "Max daily profit per contrat (USD, 0=none)", Order = 9, GroupName = "04-Risk")]
        public double MaxDailyProfitPerContractUSD { get; set; } = 600.0;

        [NinjaScriptProperty]
        [Display(Name = "Consistency rule", Order = 10, GroupName = "04-Risk")]
        public ConsistencyRuleMode ConsistencyRule { get; set; } = ConsistencyRuleMode.None;

        [NinjaScriptProperty]
        [Display(Name = "Min minutes between trades (0=none)", Order = 11, GroupName = "04-Risk")]
        public int MinMinutesBetweenTrades { get; set; } = 15;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Emergency stop ticks (hard)", Order = 12, GroupName = "04-Risk")]
        public int EmergencyStopTicks { get; set; } = 45;

        [NinjaScriptProperty]
        [Range(0, 60)]
        [Display(Name = "Protective order watchdog seconds", Order = 13, GroupName = "04-Risk")]
        public int ProtectiveWatchdogSeconds { get; set; } = 0;

        [NinjaScriptProperty]
        [Display(Name = "Auto emergency = MaxStop+5", Order = 14, GroupName = "04-Risk")]
        public bool AutoEmergencyStop { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Enable dynamic contract sizing", Order = 15, GroupName = "04-Risk")]
        public bool DynamicContractSizing { get; set; } = false;

        // ========== 05 – Break-even / Trade Management ==========
        [NinjaScriptProperty]
        [Display(Name = "Use break-even", Order = 1, GroupName = "05-BE & Management")]
        public bool UseBreakEven { get; set; } = false;
        
        [NinjaScriptProperty]
        [Display(Name = "BE trigger (ticks in favour)", Order = 3, GroupName = "05-BE & Management")]
        public int BE_TriggerTicks { get; set; } = 12;

        [NinjaScriptProperty]
        [Display(Name = "BE plus ticks", Order = 4, GroupName = "05-BE & Management")]
        public int BE_PlusTicks { get; set; } = 6;

        // ========== 99 – API Reporter ==========
        private TrailingDdApiReporter _tddApi;
		private TradeApiReporter _tradeApi;
		private int _lastReportedTradeCount = 0;
		
		[NinjaScriptProperty]
		[Display(Name = "API Reporter Enabled", Order = 100, GroupName = "99-API Reporter")]
		public bool ApiEnabled { get; set; } = false;

		[NinjaScriptProperty]
        [Display(Name = "API Base URL", Order = 101, GroupName = "99-API Reporter")]
        public string ApiBaseUrl { get; set; } = "https://karimali.uk";
				
		[NinjaScriptProperty]
		[Range(1000, 30000)]
		[Display(Name = "API Timeout (ms)", Order = 102, GroupName = "99-API Reporter")]
		public int ApiTimeoutMs { get; set; } = 7000;
        
        [NinjaScriptProperty]
        [Display(Name = "TDD PlanContextId (PropFirmPlans.Id Default Apex 150k Funded)", Order = 201, GroupName = "99-API Reporter")]
        public int TddPlanContextId { get; set; } = 12;

        [NinjaScriptProperty]
        [Range(1, 60)]
        [Display(Name = "TDD Send interval seconds", Order = 103, GroupName = "99-API Reporter")]
        public int TddSendIntervalSeconds { get; set; } = 1;

		[NinjaScriptProperty]
		[Display(Name = "Override Trailing DD (USD, 0 = none)", Order = 104, GroupName = "99-API Reporter")]
		public double OverrideTrailingDrawdown { get; set; } = 0;

        // ===== Indicators =====
        private EMA emaFast;
        private EMA emaSlow;
        private ADX adx;
        private ATR atr;

        // ===== Session / state =====
        private DateTime sessionStart = DateTime.MinValue;
        private DateTime prevSessionDate = DateTime.MinValue;

        private int tradesToday;
        private DayLocked dayLocked = DayLocked.NoLock;
        private double cumAtSessionOpen;

        // Break-even throttle
        private int lastBEBar = -1;
        private double lastBEPrice = 0.0;
        private string lastEntryTag = string.Empty;

        // Cooldown
        private DateTime lastEntryExecutionTime = DateTime.MinValue;

        // ===== Consistency rule state =====
        private bool strategyStartSet = false;
        private double cumAtStrategyStart = 0.0;

        // ===== hard stop + watchdog state =====
        private double entryPriceHard = 0.0;
        private bool hardStopTriggered = false;
        private DateTime entryFillTime = DateTime.MinValue;
        private bool protectiveSeenSinceEntry = false;

        private int entrySeq = 0;
        private int entryBarIdx = -1;

        // --- OnEachTick safety ---
        private int lastEntryAttemptBar = -1;
        private DateTime lastDiagTime = DateTime.MinValue;
        private int diagThrottleSeconds = 10;

        private int dynamicReduceMonFriBy = 1;

        // ===== Duplicate instance lock state =====
        private static readonly object _dupLock = new object();
        private static readonly HashSet<string> _activeInstanceKeys = new HashSet<string>();

        private string _instanceKey = null;
        private bool _duplicateBlocked = false;
        private bool _duplicateCleanupDone = false;
        private string _instanceGuid = null;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "ApexMicroPBLiveV3";
                Calculate = Calculate.OnEachTick;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.UniqueEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 10;
                IsInstantiatedOnEachOptimizationIteration = false;
                SetOrderQuantity = SetOrderQuantity.Strategy;

                // keep your previous behaviour
                EmergencyStopTicks = MaxStopTicks + 5;

                _instanceGuid = Guid.NewGuid().ToString("N").Substring(0, 8);
            }
            else if (State == State.Configure)
            {
                if (AutoEmergencyStop)
                    EmergencyStopTicks = MaxStopTicks + 5;

                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelCloseIgnoreRejects;
                
                if (EmergencyStopTicks < 1)
                    EmergencyStopTicks = MaxStopTicks + 5;
            }
            else if (State == State.DataLoaded)
            {
                emaFast = EMA(EMAFast);
                emaSlow = EMA(EMASlow);
                adx = ADX(ADXPeriod);
                atr = ATR(14);
				
				if (ApiEnabled)
				{
					 _tradeApi = new TradeApiReporter(
				        apiBaseUrl: ApiBaseUrl,
				        timeoutMs: ApiTimeoutMs,
				        debug: DebugMode,
						isHistorical: IsHistorical
				    );
				
				    // Start from "current count" so you don't blast historical trades when enabling.
				    _lastReportedTradeCount = (SystemPerformance != null && SystemPerformance.AllTrades != null)
				        ? SystemPerformance.AllTrades.Count
				        : 0;
					
	                _tddApi = new TrailingDdApiReporter(
					    apiBaseUrl: ApiBaseUrl,
						timeoutMs: ApiTimeoutMs,
					    planContextId: TddPlanContextId,
					    sendIntervalSeconds: TddSendIntervalSeconds,
					    enableDebug: DebugMode,
						OverrideTrailingDrawdown
					);
				}
            }
            else if (State == State.Realtime)
            {
                // Duplicate guard only on real accounts
                if (BlockDuplicateInstances && Account != null && Instrument != null && !IsExcludedAccountForDupLock())
                {
                    string strat = GetType().FullName ?? Name;
                    string acc = Account.Name ?? "N/A";
                    string inst = Instrument.FullName ?? "N/A";
                    _instanceKey = strat + "|" + acc + "|" + inst;

                    bool alreadyRunning;
                    lock (_dupLock)
                    {
                        alreadyRunning = _activeInstanceKeys.Contains(_instanceKey);
                        if (!alreadyRunning)
                            _activeInstanceKeys.Add(_instanceKey);
                    }

                    if (DebugMode)
                        Print($"[INSTANCE] {_instanceGuid} key={_instanceKey} alreadyRunning={alreadyRunning}");

                    if (alreadyRunning)
                    {
                        _duplicateBlocked = true;

                        string msg =
                            "DUPLICATE STRATEGY INSTANCE DETECTED\n" +
                            $"Strategy={strat}\nAccount={acc}\nInstrument={inst}\n" +
                            $"Instance={_instanceGuid}\n" +
                            "Trading is BLOCKED for this instance.";

                        Print("[DUPLICATE] " + msg);
                        Log(msg, LogLevel.Error);

                        try { Alert("DUPLICATE_" + _instanceGuid, Priority.High, msg, null, 0, null, null); }
                        catch { }

                        TryDuplicateSafetyCleanup("Duplicate detected at realtime start");
                    }
                }
                else if (BlockDuplicateInstances && DebugMode && Account != null && Instrument != null && IsExcludedAccountForDupLock())
                {
                    Print($"[INSTANCE] {_instanceGuid} duplicate-lock SKIPPED for excluded account: {Account.Name}");
                }
            }
            else if (State == State.Terminated)
            {
                if (!string.IsNullOrEmpty(_instanceKey))
                {
                    lock (_dupLock)
                        _activeInstanceKeys.Remove(_instanceKey);
                }

                if (DebugMode)
                    Print($"[INSTANCE] {_instanceGuid} terminated key={_instanceKey}");
            }
        }

        protected override void OnOrderUpdate(
            Order order,
            double limitPrice,
            double stopPrice,
            int quantity,
            int filled,
            double averageFillPrice,
            OrderState orderState,
            DateTime time,
            ErrorCode error,
            string comment)
        {
            if (order == null)
                return;

            if (_duplicateBlocked)
            {
                TryDuplicateSafetyCleanup("OrderUpdate while duplicate-blocked");
                return;
            }

            bool looksProtective =
                (order.Name != null) &&
                (order.Name.IndexOf("stop", StringComparison.OrdinalIgnoreCase) >= 0
                 || order.Name.IndexOf("target", StringComparison.OrdinalIgnoreCase) >= 0
                 || order.Name.IndexOf("profit", StringComparison.OrdinalIgnoreCase) >= 0);

            if (looksProtective)
            {
                if (orderState == OrderState.Accepted
                    || orderState == OrderState.Working
                    || orderState == OrderState.PartFilled
                    || orderState == OrderState.Filled)
                {
                    protectiveSeenSinceEntry = true;
                }

                if (orderState == OrderState.Rejected && Position.MarketPosition != MarketPosition.Flat)
                {
                    if (DebugMode)
                    {
                        Print(string.Format(
                            "[PROTECTIVE REJECT -> FLATTEN] {0:yyyy-MM-dd HH:mm:ss.fff} Name={1} State={2} Err={3} Msg={4}",
                            time, order.Name, orderState, error, comment));
                    }

                    CancelWorkingOrders();
                    ForceFlatten("PROTECTIVE_REJECT");
                    return;
                }
            }

            if (orderState == OrderState.Rejected && DebugMode)
            {
                double bid = GetCurrentBid();
                double ask = GetCurrentAsk();

                Print(string.Format(
                    "[ORDER REJECTED] {0:yyyy-MM-dd HH:mm:ss.fff} Name={1}, Action={2}, Type={3}, Qty={4}, Stop={5}, Limit={6}, Bid={7}, Ask={8}, ErrorCode={9}, Msg={10}",
                    time, order.Name, order.OrderAction, order.OrderType, quantity, stopPrice, limitPrice, bid, ask, error, comment));
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

            if (_duplicateBlocked)
            {
                TryDuplicateSafetyCleanup("ExecutionUpdate while duplicate-blocked");
                return;
            }

            string name = execution.Order.Name ?? string.Empty;

            bool isLongEntry =
                name.StartsWith("MPB_LONG_", StringComparison.OrdinalIgnoreCase)
                && execution.Order.OrderAction == OrderAction.Buy;

            bool isShortEntry =
                name.StartsWith("MPB_SHORT_", StringComparison.OrdinalIgnoreCase)
                && execution.Order.OrderAction == OrderAction.SellShort;

            if (isLongEntry || isShortEntry)
            {
                entryBarIdx = CurrentBar;
                lastEntryExecutionTime = time;

                entryPriceHard = execution.Price;
                entryFillTime = time;
                hardStopTriggered = false;

                protectiveSeenSinceEntry = false;

                if (DebugMode)
                {
                    Print(string.Format("[ENTRY FILL] {0:yyyy-MM-dd HH:mm:ss.fff} name={1} price={2} qty={3} emergencyTicks={4}",
                        time, name, execution.Price, execution.Quantity, EmergencyStopTicks));

                    lastDiagTime = DateTime.MinValue;
                }
            }

            if (Position.MarketPosition == MarketPosition.Flat)
            {
                entryPriceHard = 0.0;
                hardStopTriggered = false;
                entryFillTime = DateTime.MinValue;
                protectiveSeenSinceEntry = false;
                entryBarIdx = -1;
            }
        }

        private void TryDuplicateSafetyCleanup(string reason)
        {
            if (_duplicateCleanupDone)
                return;

            _duplicateCleanupDone = true;

            string acc = Account != null ? Account.Name : "N/A";
            string inst = Instrument != null ? Instrument.FullName : "N/A";

            if (DebugMode)
                Print($"[DUPLICATE CLEANUP] {_instanceGuid} reason={reason} acc={acc} inst={inst}");

            try
            {
                if (DuplicateCancelWorkingOrders)
                    CancelWorkingOrders();
            }
            catch { }

            if (DuplicateFlattenInstrument)
            {
                bool flattened = false;

                try
                {
                    if (Account != null && Instrument != null)
                    {
                        Account.Flatten(new[] { Instrument });
                        flattened = true;
                    }
                }
                catch { }

                if (!flattened)
                {
                    try
                    {
                        if (Position.MarketPosition != MarketPosition.Flat)
                            ForceFlatten("DUPLICATE_INSTANCE");
                    }
                    catch { }
                }
            }
        }

        private void ResetDay(DateTime time)
        {
            if (!strategyStartSet)
            {
                cumAtStrategyStart = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
                strategyStartSet = true;
            }

            tradesToday = 0;
            dayLocked = DayLocked.NoLock;

            lastBEBar = -1;
            lastBEPrice = 0.0;
            lastEntryTag = string.Empty;
            entrySeq = 0;

            sessionStart = time;
            prevSessionDate = time.Date;
            lastEntryExecutionTime = DateTime.MinValue;

            entryBarIdx = -1;
            entryPriceHard = 0.0;
            hardStopTriggered = false;
            entryFillTime = DateTime.MinValue;
            protectiveSeenSinceEntry = false;

            cumAtSessionOpen = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
        }

        protected override void OnBarUpdate()
        {
            if (_duplicateBlocked)
            {
                TryDuplicateSafetyCleanup("OnBarUpdate while duplicate-blocked");
                return;
            }

            // API Reporter: only primary series, realtime only, and not Strategy Analyzer
			if (BarsInProgress == 0 && ApiEnabled && State == State.Realtime && !IsInStrategyAnalyzer)
			{
				// Trade reporter: only primary series, realtime only, and not Strategy Analyzer
				if (_tradeApi != null)
				{
				    TryReportNewClosedTrades();
				}
				
				// TDD reporter
				if (_tddApi != null) 
				{
				    bool inWindow = IsWithinTradingWindow();
				    //bool hasRisk  = HasRiskOn();
				
				    if (Bars.IsFirstBarOfSession)
				        _tddApi.ResetPeak(); // intraday peak
				
				    _tddApi.OnHeartbeat(this, inWindow);
				}
			}
			
            // --- Main logic (5m) ---
            if (BarsInProgress != 0)
                return;

            if (CurrentBar < 50)
                return;

            // ===== POSITION MANAGEMENT (RUNS INTRABAR) =====
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                if (EnforceDailyKill())
                    return;

                if (EnforceDailyProfitLock())
                    return;

                if (EnforceConsistencyRule())
                    return;

                TryHardStopByTicks();
                TryProtectiveWatchdog();
                ManageBreakEven();

                return;
            }

            // ===== ENTRY EVALUATION GATE (ONLY WHEN FLAT) =====
            var isRealtime = State == State.Realtime;
            var evalEntriesNow = !isRealtime || IsFirstTickOfBar;

            if (!evalEntriesNow)
            {
                ManageBreakEven();
                return;
            }
            
            if (isRealtime)
            {
                if (CurrentBar == lastEntryAttemptBar)
                {
                    ManageBreakEven();
                    return;
                }
            }

            var sig = Sig();
            var now = Time[sig];

            if (sessionStart == DateTime.MinValue)
            {
                ResetDay(now);

                if (EnforceDailyProfitLock())
                    return;

                if (EnforceDailyKill())
                    return;

                if (EnforceConsistencyRule())
                    return;
            }
            else
            {
                bool newSessionByNT = Bars.IsFirstBarOfSession;
                bool newDayByDate = (prevSessionDate != DateTime.MinValue && now.Date != prevSessionDate);

                if (newSessionByNT || newDayByDate)
                {
                    if (prevSessionDate != DateTime.MinValue && DebugMode)
                    {
                        double cum = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
                        double realizedPrevDay = cum - cumAtSessionOpen;

                        Print(string.Format("[{0:yyyy-MM-dd}] realized={1,8:C2} trades={2} locked={3}",
                            prevSessionDate, realizedPrevDay, tradesToday, dayLocked.ToString()));
                    }

                    ResetDay(now);
                }
            }

            int minFromOpen = (int)Math.Floor(Time[sig].Subtract(sessionStart).TotalMinutes);

            PrintDiagnostics(minFromOpen);

            if (EnforceDailyProfitLock()) return;
            if (EnforceDailyKill()) return;
            if (EnforceConsistencyRule()) return;

            // ===== TIME FILTERS =====
            if (MidBreakEndMin > MidBreakStartMin)
            {
                if (minFromOpen >= MidBreakStartMin && minFromOpen < MidBreakEndMin)
                {
                    ManageBreakEven();
                    return;
                }
            }

            if (minFromOpen < MinMinutesFromOpen || minFromOpen > MaxMinutesFromOpen)
            {
                ManageBreakEven();
                return;
            }

            if (!DayNotLocked())
            {
                ManageBreakEven();
                return;
            }

            if (tradesToday >= MaxTradesPerDay)
            {
                ManageBreakEven();
                return;
            }

            if (MinMinutesBetweenTrades > 0 && lastEntryExecutionTime != DateTime.MinValue)
            {
                double minsSinceLast = Time[sig].Subtract(lastEntryExecutionTime).TotalMinutes;
                if (minsSinceLast < MinMinutesBetweenTrades)
                {
                    ManageBreakEven();
                    return;
                }
            }

            // ===== FILTERS (USING LAST CLOSED BAR INDEX = sig) =====
            double atrNow = Math.Max(atr[sig], TickSize);
            if (atrNow <= 0)
                return;

            double atrTicksNow = atrNow / TickSize;
            if (MaxAtrTicks > 0 && atrTicksNow > MaxAtrTicks)
            {
                ManageBreakEven();
                return;
            }

            bool trendUp = emaFast[sig] > emaSlow[sig];
            bool trendDown = emaFast[sig] < emaSlow[sig];

            if (!trendUp && !trendDown)
            {
                ManageBreakEven();
                return;
            }

            double adxNow = adx[sig];
            bool adxOK = adxNow >= ADXMin && adxNow <= ADXMax;
            if (!adxOK)
            {
                ManageBreakEven();
                return;
            }

            bool submitted = false;
            double buf = TickSize;
            int qty = GetEntryQty(sig);

            // ===== LONG =====
            if (!submitted && EnableLongs && trendUp)
            {
                bool pulledBack = Touched(emaFast, LookbackBars, true);
                bool reclaimed = Close[sig] > emaFast[sig];

                if (pulledBack && reclaimed && TrendConfirm(ConfirmBars, true, sig))
                {
                    string tag = "MPB_LONG_" + (++entrySeq);
                    PrepareBracket(tag, atrNow);

                    double rawTrigger = Instrument.MasterInstrument.RoundToTickSize(Math.Max(Close[sig] + buf, High[sig] + buf));
                    double trigger = NormalizeBuyStopPrice(rawTrigger);

                    double ask = GetCurrentAsk();
                    if (State == State.Realtime && ask > 0 && !double.IsNaN(ask) && !double.IsInfinity(ask))
                    {
                        double distanceTicks = (trigger - ask) / TickSize;
                        if (distanceTicks < MinStopOffsetTicks)
                            return;
                    }

                    lastEntryAttemptBar = CurrentBar;
                    EnterLongStopMarket(qty, trigger, tag);
                    tradesToday++;
                    lastEntryTag = tag;
                    submitted = true;
                }
            }

            // ===== SHORT =====
            if (!submitted && EnableShorts && trendDown)
            {
                bool pulledBack = Touched(emaFast, LookbackBars, false);
                bool reclaimed = Close[sig] < emaFast[sig];

                if (pulledBack && reclaimed && TrendConfirm(ConfirmBars, false, sig))
                {
                    string tag = "MPB_SHORT_" + (++entrySeq);
                    PrepareBracket(tag, atrNow);

                    double rawTrigger = Instrument.MasterInstrument.RoundToTickSize(Math.Min(Close[sig] - buf, Low[sig] - buf));
                    double trigger = NormalizeSellStopPrice(rawTrigger);

                    double bid = GetCurrentBid();
                    if (State == State.Realtime && bid > 0 && !double.IsNaN(bid) && !double.IsInfinity(bid))
                    {
                        double distanceTicks = (bid - trigger) / TickSize;
                        if (distanceTicks < MinStopOffsetTicks)
                            return;
                    }

                    lastEntryAttemptBar = CurrentBar;
                    EnterShortStopMarket(qty, trigger, tag);
                    tradesToday++;
                    lastEntryTag = tag;
                    submitted = true;
                }
            }

            ManageBreakEven();
        }

        private void TryHardStopByTicks()
        {
            if (hardStopTriggered)
                return;

            if (EmergencyStopTicks < 1)
                return;

            if (entryPriceHard <= 0)
                return;

            if (entryBarIdx >= 0 && CurrentBar <= entryBarIdx)
                return;

            double adverseTicks;

            if (Position.MarketPosition == MarketPosition.Long)
                adverseTicks = (entryPriceHard - Low[0]) / TickSize;
            else
                adverseTicks = (High[0] - entryPriceHard) / TickSize;

            if (adverseTicks < 0)
                adverseTicks = 0;

            if (adverseTicks >= EmergencyStopTicks)
            {
                if (DebugMode)
                {
                    Print(string.Format(
                        "[HARD STOP] {0:yyyy-MM-dd HH:mm:ss} pos={1} entry={2:F2} adverseTicks={3:F1} >= {4} -> FLATTEN",
                        Time[0], Position.MarketPosition, entryPriceHard, adverseTicks, EmergencyStopTicks));
                }

                CancelWorkingOrders();
                ForceFlatten("HARD_STOP_TICKS");
            }
        }

        private void TryProtectiveWatchdog()
        {
            if (ProtectiveWatchdogSeconds < 1)
                return;

            // watchdog only makes sense in LIVE (not market replay / historical)
            if (State != State.Realtime || IsInStrategyAnalyzer)
                return;

            if (entryFillTime == DateTime.MinValue)
                return;

            double secsSinceEntry = (Time[0] - entryFillTime).TotalSeconds;
            if (secsSinceEntry < ProtectiveWatchdogSeconds)
                return;

            if (!protectiveSeenSinceEntry)
            {
                if (DebugMode)
                {
                    Print(string.Format(
                        "[WATCHDOG] {0:yyyy-MM-dd HH:mm:ss} No protective stop/target seen within {1}s after entry -> FLATTEN",
                        Time[0], ProtectiveWatchdogSeconds));
                }

                CancelWorkingOrders();
                ForceFlatten("WATCHDOG_NO_PROTECTIVE");
            }
        }

        private void ForceFlatten(string reason)
        {
            if (hardStopTriggered)
                return;

            hardStopTriggered = true;

            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (!string.IsNullOrEmpty(lastEntryTag))
                    ExitLong(reason, lastEntryTag);
                else
                    ExitLong(reason);
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                if (!string.IsNullOrEmpty(lastEntryTag))
                    ExitShort(reason, lastEntryTag);
                else
                    ExitShort(reason);
            }

            lastEntryTag = string.Empty;
            lastBEBar = -1;
            lastBEPrice = 0.0;
            entryBarIdx = -1;

            entryPriceHard = 0.0;
            entryFillTime = DateTime.MinValue;
            protectiveSeenSinceEntry = false;
        }

        private void CancelWorkingOrders()
        {
            try
            {
                if (Account == null)
                    return;

                foreach (Order o in Account.Orders)
                {
                    if (o == null)
                        continue;

                    if (o.Instrument == null || Instrument == null)
                        continue;

                    if (o.Instrument.FullName != Instrument.FullName)
                        continue;

                    if (o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted || o.OrderState == OrderState.PartFilled)
                        Account.Cancel(new[] { o });
                }
            }
            catch (Exception ex)
            {
                if (DebugMode)
                    Print("[WARN] CancelWorkingOrders failed: " + ex.Message);
            }
        }

        private double NormalizeBuyStopPrice(double desiredPrice)
        {
            double ask = GetCurrentAsk();
            if (ask <= 0 || double.IsNaN(ask) || double.IsInfinity(ask))
                return desiredPrice;

            double minPrice = Instrument.MasterInstrument.RoundToTickSize(ask + MinStopOffsetTicks * TickSize);
            return Math.Max(desiredPrice, minPrice);
        }

        private double NormalizeSellStopPrice(double desiredPrice)
        {
            double bid = GetCurrentBid();
            if (bid <= 0 || double.IsNaN(bid) || double.IsInfinity(bid))
                return desiredPrice;

            double maxPrice = Instrument.MasterInstrument.RoundToTickSize(bid - MinStopOffsetTicks * TickSize);
            return Math.Min(desiredPrice, maxPrice);
        }

        private bool Touched(EMA ema, int lookback, bool longSide)
        {
            int look = Math.Min(lookback, CurrentBar - 1);
            if (look <= 0)
                return false;

            if (longSide)
            {
                double proximity = LongTouchTicks > 0 ? LongTouchTicks * TickSize : 0.0;
                for (int i = 1; i <= look; i++)
                    if (Low[i] <= ema[i] + proximity)
                        return true;
            }
            else
            {
                double proximity = ShortTouchTicks > 0 ? ShortTouchTicks * TickSize : 0.0;
                for (int i = 1; i <= look; i++)
                    if (High[i] >= ema[i] - proximity)
                        return true;
            }

            return false;
        }

        private bool TrendConfirm(int bars, bool longSide)
        {
            string _;
            int __;
            int barsAgo = 0;
            return TrendConfirm(bars, longSide, barsAgo, out _, out __);
        }

        private bool TrendConfirm(int bars, bool longSide, int barsAgo)
        {
            string _;
            int __;
            return TrendConfirm(bars, longSide, barsAgo, out _, out __);
        }

        private bool TrendConfirm(int bars, bool longSide, int barsAgo, out string failReason, out int failIndex)
        {
            failReason = "none";
            failIndex = -1;

            if (CurrentBar < barsAgo + 1)
            {
                failReason = "insufficient-bars";
                return false;
            }

            int refBar = Math.Max(0, barsAgo);
            int lookBack = Math.Min(bars, CurrentBar - refBar);

            if (longSide)
            {
                for (int i = 1; i <= lookBack; i++)
                {
                    int idx = refBar + i;

                    if (!(Close[idx] > emaSlow[idx]))
                    {
                        failReason = "close-not-above-slow-ema";
                        failIndex = idx;
                        return false;
                    }
                }

                if (!(emaFast[refBar] > emaSlow[refBar]))
                {
                    failReason = "ema-not-bullish";
                    return false;
                }

                if (!(Close[refBar] > emaFast[refBar]))
                {
                    failReason = "close-not-above-ema";
                    return false;
                }

                if (ConfirmAboveEmaTicks > 0)
                {
                    double minLow = emaFast[refBar] + ConfirmAboveEmaTicks * TickSize;
                    if (Low[refBar] <= minLow)
                    {
                        failReason = "wick-below-ema";
                        return false;
                    }
                }

                if (!(Close[refBar] >= Open[refBar]))
                {
                    failReason = "not-bullish-candle";
                    return false;
                }

                double bodyTicks = Math.Abs(Close[refBar] - Open[refBar]) / TickSize;
                if (StrongBodyTicks > 0 && bodyTicks < StrongBodyTicks)
                {
                    failReason = "body-too-small";
                    return false;
                }

                if (CurrentBar >= refBar + 1 && Close[refBar] < Close[refBar + 1])
                {
                    failReason = "bullish-momentum-broken";
                    return false;
                }
            }
            else
            {
                for (int i = 1; i <= lookBack; i++)
                {
                    int idx = refBar + i;

                    if (!(emaFast[idx] < emaSlow[idx]))
                    {
                        failReason = "ema-not-bearish";
                        failIndex = idx;
                        return false;
                    }

                    if (!(Close[idx] < emaFast[idx]))
                    {
                        failReason = "close-not-below-ema";
                        failIndex = idx;
                        return false;
                    }
                }

                if (!(emaFast[refBar] < emaSlow[refBar]))
                {
                    failReason = "ema-not-bearish";
                    return false;
                }

                if (!(Close[refBar] < emaFast[refBar]))
                {
                    failReason = "close-not-below-ema";
                    return false;
                }

                if (ConfirmAboveEmaTicks > 0)
                {
                    double maxHigh = emaFast[refBar] - ConfirmAboveEmaTicks * TickSize;
                    if (High[refBar] >= maxHigh)
                    {
                        failReason = "wick-above-ema";
                        return false;
                    }
                }

                if (!(Close[refBar] <= Open[refBar]))
                {
                    failReason = "not-bearish-candle";
                    return false;
                }

                double bodyTicks = Math.Abs(Close[refBar] - Open[refBar]) / TickSize;
                if (StrongBodyTicks > 0 && bodyTicks < StrongBodyTicks)
                {
                    failReason = "body-too-small";
                    return false;
                }

                if (CurrentBar >= refBar + 1 && Close[refBar] > Close[refBar + 1])
                {
                    failReason = "bearish-momentum-broken";
                    return false;
                }
            }

            failReason = "none";
            failIndex = -1;
            return true;
        }

        private void PrepareBracket(string tag, double atrNow)
        {
            double atrTicksRaw = (atrNow / TickSize) * StopMultATR;

            int stopTicks = Math.Min(
                MaxStopTicks,
                Math.Max(8, (int)Math.Round(atrTicksRaw)));

            int targetTicksBase = Math.Max(8, (int)Math.Round(stopTicks * R_Ratio));
            if (MaxProfitTicks > 0)
                targetTicksBase = Math.Min(targetTicksBase, MaxProfitTicks);

            SetStopLoss(tag, CalculationMode.Ticks, stopTicks, false);
            SetProfitTarget(tag, CalculationMode.Ticks, targetTicksBase);
        }

        public int Sig()
        {
            // bool isRealtime = (State == State.Realtime);
            // int sig = (isRealtime && Calculate == Calculate.OnEachTick) ? 1 : 0;
            return 0;
        }

        private void ManageBreakEven()
        {
            if (!UseBreakEven)
                return;

            if (Position.MarketPosition == MarketPosition.Flat)
                return;

            if (string.IsNullOrEmpty(lastEntryTag))
                return;

            if (CurrentBar == lastBEBar)
                return;

            double entryPrice = Position.AveragePrice;
            if (entryPrice <= 0)
                return;

            int upTicks;
            if (Position.MarketPosition == MarketPosition.Long)
                upTicks = (int)Math.Floor((Close[0] - entryPrice) / TickSize);
            else
                upTicks = (int)Math.Floor((entryPrice - Close[0]) / TickSize);
            
            var adxNow = adx[Sig()];
            var triggerTicks = BE_TriggerTicks;

            if (adxNow < 30)
                triggerTicks /= 2;

            if (upTicks < triggerTicks)
                return;
            
            var newStop = Position.MarketPosition == MarketPosition.Long
                ? entryPrice + BE_PlusTicks * TickSize
                : entryPrice - BE_PlusTicks * TickSize;

            newStop = Instrument.MasterInstrument.RoundToTickSize(newStop);

            if (Math.Abs(newStop - lastBEPrice) >= TickSize)
            {
                try
                {
                    if (DebugMode)
                    {
                        Print(string.Format(
                            "[BE MOVE] {0:yyyy-MM-dd HH:mm:ss} pos={1} entry={2} newStop={3} upTicks={4} (trigger={5})",
                            Time[0], Position.MarketPosition, entryPrice, newStop, upTicks, BE_TriggerTicks));
                    }

                    SetStopLoss(lastEntryTag, CalculationMode.Price, newStop, false);
                    lastBEBar = CurrentBar;
                    lastBEPrice = newStop;
                }
                catch (Exception ex)
                {
                    Print("[WARN] BE adjustment failed: " + ex.Message);
                }
            }
        }

        private double GetDailyKillLimitUsd()
        {
            if (MaxDailyLossPerContractUSD <= 0)
                return 0;
            
            int eff = GetEffectiveContractsToday(Sig());
            return Math.Abs(MaxDailyLossPerContractUSD) * eff;
        }

        private bool EnforceDailyKill()
        {
            double dailyKill = GetDailyKillLimitUsd();
            if (dailyKill <= 0)
                return false;

            double totalToday = GetTotalTodayPnlIncludingOpen();

            if (DayNotLocked() && totalToday <= -dailyKill)
            {
                dayLocked = DayLocked.MaxLossReached;

                if (DebugMode)
                    Print(string.Format("[DAILY KILL] {0:yyyy-MM-dd HH:mm:ss} totalToday={1:C2} <= -{2:C2} -> LOCK DAY{3}",
                        Time[0], totalToday, dailyKill,
                        Position.MarketPosition != MarketPosition.Flat ? " + FLATTEN" : ""));

                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    CancelWorkingOrders();
                    ForceFlatten("DAILY_KILL");
                }

                return true;
            }

            return dayLocked == DayLocked.MaxLossReached;
        }

        private bool EnforceDailyProfitLock()
        {
            double limit = GetDailyProfitLimitUsd();
            if (limit <= 0)
                return false;

            double realizedToday = GetRealizedToday();

            if (DayNotLocked() && realizedToday >= limit)
            {
                dayLocked = DayLocked.DailyProfitReached;

                if (DebugMode)
                    Print(string.Format("[DAY LOCK - PROFIT] {0:yyyy-MM-dd HH:mm:ss} realizedToday={1:C2} >= {2:C2} -> LOCK DAY{3}",
                        Time[0], realizedToday, limit,
                        Position.MarketPosition != MarketPosition.Flat ? " + FLATTEN" : ""));

                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    CancelWorkingOrders();
                    ForceFlatten("DAILY_PROFIT_LOCK");
                }

                return true;
            }

            return dayLocked == DayLocked.DailyProfitReached;
        }

        private double GetDailyProfitLimitUsd()
        {
            if (MaxDailyProfitPerContractUSD <= 0)
                return 0;
            
            var eff = GetEffectiveContractsToday(Sig());

            return Math.Abs(MaxDailyProfitPerContractUSD) * eff;
        }

        private double GetRealizedToday()
        {
            double cumProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
            return cumProfit - cumAtSessionOpen;
        }

        private double GetTotalTodayPnlIncludingOpen()
        {
            double realizedToday = GetRealizedToday();
            double unrealized = (Position.MarketPosition == MarketPosition.Flat)
                ? 0.0
                : Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency);

            return realizedToday + unrealized;
        }

        private double GetConsistencyPct()
        {
            if (ConsistencyRule == ConsistencyRuleMode.ThirtyPercent) return 0.30;
            if (ConsistencyRule == ConsistencyRuleMode.FiftyPercent) return 0.50;
            return 0.0;
        }

        private double GetProfitBeforeToday()
        {
            if (!strategyStartSet) return 0.0;
            return (cumAtSessionOpen - cumAtStrategyStart);
        }

        private bool EnforceConsistencyRule()
        {
            double pct = GetConsistencyPct();
            if (pct <= 0)
                return false;

            double profitBeforeToday = GetProfitBeforeToday();
            if (profitBeforeToday <= 0)
                return false;

            double realizedToday = GetRealizedToday();
            if (realizedToday <= 0)
                return false;

            double maxToday = (pct / (1.0 - pct)) * profitBeforeToday;

            if (DayNotLocked() && realizedToday > maxToday)
            {
                dayLocked = DayLocked.ConsistencyRule;

                if (DebugMode)
                {
                    Print(string.Format(
                        "[CONSISTENCY LOCK] {0:yyyy-MM-dd HH:mm:ss} realizedToday={1:C2} > maxAllowed={2:C2} (mode={3}, pct={4:P0}, profitBeforeToday={5:C2}) -> LOCK DAY{6}",
                        Time[0], realizedToday, maxToday, ConsistencyRule, pct, profitBeforeToday,
                        Position.MarketPosition != MarketPosition.Flat ? " + FLATTEN" : ""));
                }

                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    CancelWorkingOrders();
                    ForceFlatten("CONSISTENCY_LOCK");
                }

                return true;
            }

            return dayLocked == DayLocked.ConsistencyRule;
        }

        private bool DayNotLocked()
        {
            return dayLocked == DayLocked.NoLock;
        }

        private int GetContractsForDay(DayOfWeek d)
        {
            int baseQty = Math.Max(1, Contracts);

            if (!DynamicContractSizing)
                return baseQty;

            if (d == DayOfWeek.Monday || d == DayOfWeek.Friday)
                return Math.Max(1, baseQty - Math.Max(0, dynamicReduceMonFriBy));

            return baseQty;
        }

        private int GetEntryQty(int sig)
        {
            DayOfWeek d = Time[sig].DayOfWeek;
            return GetContractsForDay(d);
        }

        private int GetEffectiveContractsToday(int sig)
        {
            return GetContractsForDay(Time[sig].DayOfWeek);
        }

        private bool IsExcludedAccountForDupLock()
        {
            string acc = (Account != null && Account.Name != null) ? Account.Name : string.Empty;
            if (string.IsNullOrEmpty(acc))
                return false;

            return acc.IndexOf("Playback", StringComparison.OrdinalIgnoreCase) >= 0
                || acc.IndexOf("Sim101", StringComparison.OrdinalIgnoreCase) >= 0;
        }
		
		private bool IsWithinTradingWindow()
		{
		    if (IsInStrategyAnalyzer) return false;
		    if (sessionStart == DateTime.MinValue) return false;
            
		
		    int minFromOpen = (int)Math.Floor(Time[Sig()].Subtract(sessionStart).TotalMinutes);
		
		    // mid-break block
		    if (MidBreakEndMin > MidBreakStartMin)
		        if (minFromOpen >= MidBreakStartMin && minFromOpen < MidBreakEndMin)
		            return false;
		
		    return minFromOpen >= MinMinutesFromOpen && minFromOpen <= MaxMinutesFromOpen;
		}
		
		private bool HasRiskOn()
		{
		    if (Position.MarketPosition != MarketPosition.Flat)
		        return true;
		
		    try
		    {
		        if (Account == null || Instrument == null)
		            return false;
		
		        foreach (var o in Account.Orders)
		        {
		            if (o == null || o.Instrument == null) continue;
		            if (o.Instrument.FullName != Instrument.FullName) continue;
		
		            if (o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted || o.OrderState == OrderState.PartFilled)
		                return true;
		        }
		    }
		    catch { }
		
		    return false;
		}
		
		private void TryReportNewClosedTrades()
		{
		    try
		    {
		        if (SystemPerformance == null || SystemPerformance.AllTrades == null)
		            return;
		
		        int count = SystemPerformance.AllTrades.Count;
		        if (count <= _lastReportedTradeCount)
		            return;
		
		        string acc = (Account != null && Account.Name != null) ? Account.Name : string.Empty;
		
		        for (int i = _lastReportedTradeCount; i < count; i++)
		        {
		            Trade t = SystemPerformance.AllTrades[i];
		            if (t == null)
		                continue;
		
		            // Only send closed trades (Exit exists)
		            // Reporter also checks this, but we keep it tight here too.
		            object exitObj = t.GetType().GetProperty("Exit")?.GetValue(t, null);
		            if (exitObj == null)
		                continue;
		
		            _tradeApi.TryReportClosedTrade(this, t, acc);
		        }
		
		        _lastReportedTradeCount = count;
		    }
		    catch (Exception ex)
		    {
		        if (DebugMode)
		            Print("[API Reporter] TryReportNewClosedTrades error: " + ex.Message);
		    }
		}
		
		private string BuildStrategyParamsJson()
		{
		    try
		    {
		        var dict = new Dictionary<string, object>();
		
		        var props = GetType().GetProperties();
		
		        foreach (var p in props)
		        {
		            if (!Attribute.IsDefined(p, typeof(NinjaScriptPropertyAttribute)))
		                continue;
		
		            object value;
		
		            try
		            {
		                value = p.GetValue(this, null);
		            }
		            catch
		            {
		                continue;
		            }
		
		            dict[p.Name] = value;
		        }
		
		        var serializer = new JavaScriptSerializer();
		        return serializer.Serialize(dict);
		    }
		    catch (Exception ex)
		    {
		        if (DebugMode)
		            Print("[PARAM JSON ERROR] " + ex.Message);
		
		        return "{}";
		    }
		}


        private void PrintDiagnostics(int minFromOpen)
        {
            if (!DebugMode)
                return;
            
            var effContracts = GetEffectiveContractsToday(Sig());

            var diagTime = Time[Sig()];
            if ((diagTime - lastDiagTime).TotalSeconds < diagThrottleSeconds)
                return;

            lastDiagTime = diagTime;

            string accountName = Account != null ? Account.Name : "N/A";
				
//			var account = Account.All.FirstOrDefault(a => a.Name == "Playback101")
//			.GetAccountItem(AccountItem.TrailingMaxDrawdown, Currency.UsDollar);
//			Print("!!!! trailing drawdown" + account);

            var sig = Sig();
            double realizedToday = GetRealizedToday();
            double dailyKill = GetDailyKillLimitUsd();
            double dailyMaxProfit = GetDailyProfitLimitUsd();

            bool hasSessionStart = sessionStart != DateTime.MinValue;
            bool inMainWindow = minFromOpen >= MinMinutesFromOpen && minFromOpen <= MaxMinutesFromOpen;
            bool inMidBreak = (MidBreakEndMin > MidBreakStartMin) && (minFromOpen >= MidBreakStartMin && minFromOpen < MidBreakEndMin);
            bool timeWindowOk = hasSessionStart && inMainWindow && !inMidBreak;

            bool tradeCountLock = tradesToday >= MaxTradesPerDay;
            bool pnlOrDdLock = !DayNotLocked() || tradeCountLock;

            double atrNow = Math.Max(atr[sig], TickSize);
            double atrTicksNow = atrNow / TickSize;
            bool volBlocked = MaxAtrTicks > 0 && atrTicksNow > MaxAtrTicks;

            double adxNow = adx[sig];
            bool adxOk = adxNow >= ADXMin && adxNow <= ADXMax;

            bool trendUp = emaFast[sig] > emaSlow[sig];
            bool trendDown = emaFast[sig] < emaSlow[sig];

            bool baseLongFilter = EnableLongs && trendUp && adxOk && !volBlocked && !pnlOrDdLock && timeWindowOk;
            bool baseShortFilter = EnableShorts && trendDown && adxOk && !volBlocked && !pnlOrDdLock && timeWindowOk;

            bool longPulledBack = Touched(emaFast, LookbackBars, true);
            bool shortPulledBack = Touched(emaFast, LookbackBars, false);

            bool longReclaimed = Close[sig] > emaFast[sig];
            bool shortReclaimed = Close[sig] < emaFast[sig];

            string longConfirmReason, shortConfirmReason;
            int longConfirmIndex, shortConfirmIndex;

            bool longConfirm = TrendConfirm(ConfirmBars, true, sig, out longConfirmReason, out longConfirmIndex);
            bool shortConfirm = TrendConfirm(ConfirmBars, false, sig, out shortConfirmReason, out shortConfirmIndex);

            bool longTradesOk = baseLongFilter && longPulledBack && longReclaimed && longConfirm;
            bool shortTradesOk = baseShortFilter && shortPulledBack && shortReclaimed && shortConfirm;

            string notes = "";
            if (!hasSessionStart) notes += "no-session-start;";
            if (!inMainWindow) notes += "outside-main-window;";
            if (inMidBreak) notes += "mid-break;";
            if (volBlocked) notes += "atr-too-high;";
            if (!adxOk) notes += "adx-out-of-range;";
            if (!DayNotLocked()) notes += "dayLocked;";
            if (tradeCountLock) notes += "max-trades-reached;";
            if (Position.MarketPosition != MarketPosition.Flat) notes += "position-open;";
            if (string.IsNullOrEmpty(notes)) notes = "none";

            string pos = trendUp ? "Long" : (trendDown ? "Short" : "None");
            bool entry = trendUp ? longTradesOk : (trendDown ? shortTradesOk : false);
            string confirmFail = trendUp ? longConfirmReason : (trendDown ? shortConfirmReason : "n/a");
            string trend = trendUp ? "trendUp=True" : (trendDown ? "trendDown=True" : "trend=None");

            double unrealizedNow = (Position.MarketPosition == MarketPosition.Flat)
                ? 0.0
                : Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency);

            double totalToday = realizedToday + unrealizedNow;
            double bufferToKill = dailyKill + totalToday;
            double dailyProfitRemaining = dailyMaxProfit - totalToday;

            if (!timeWindowOk)
            {
                Print(string.Format(
                    "Out of time window: {0:yyyy-MM-dd HH:mm:ss} (minFromOpen={1}, inMainWindow={2}, midBreak={3})\n",
                    Time[sig], minFromOpen, inMainWindow, inMidBreak));
                return;
            }

            Print(string.Format(
                "[DIAG] {0:yyyy-MM-dd HH:mm:ss} - Acc: {1} (Default Contracts: {2} - Effective Contracts: {7})\n" +
                "  a) PnL/DD/Trades Lock: {8}  (realizedToday={9:C2}, unrealizedNow={10:C2}, totalToday={11:C2}, dayLocked={12}, tradesToday={14})\n" +
                "  b) Other blocks/filters: {15}\n" +
                "  c) adxOk={16}, adx={17:F2}, min={18}, max={19}, volBlocked={20}\n" +
                "  d) MaxDailyLoss={21:C0}, LossRemaining={22:C0}, MaxDailyProfit={30:C0}, ProfitRemaining={13:C0}\n" +
                "  e) {23} Entry {24} ({25}, pulledBack={26}, reclaimed={27}, confirm={28}, confirmFail={29})\n" +
                "-------------------------------------------------------------------------------------------------------\n",
                Time[sig],
                accountName,
                Contracts,
                timeWindowOk,
                minFromOpen,
                inMainWindow,
                inMidBreak,
                effContracts,
                pnlOrDdLock,
                realizedToday,
                unrealizedNow,
                totalToday,
                dayLocked.ToString(),
                dailyProfitRemaining,
                tradesToday,
                notes,
                adxOk,
                adxNow,
                ADXMin,
                ADXMax,
                volBlocked,
                dailyKill,
                bufferToKill,
                pos,
                OkIcon(entry),
                trend,
                trendUp ? longPulledBack : shortPulledBack,
                trendUp ? longReclaimed : shortReclaimed,
                trendUp ? longConfirm : shortConfirm,
                confirmFail,
                dailyMaxProfit
            ));
        }

        private string OkIcon(bool ok) { return ok ? "✔" : "✖"; }
    }
}
