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

    public partial class MicroPBV3 : Strategy
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
        public bool DebugMode { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "End-of-run DIAG report", Order = 95, GroupName = "01-General")]
        public bool EnableEndOfRunReport { get; set; } = true;

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

        // ---- Duplicate-instance guard ----
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
        public int MaxMinutesFromOpen { get; set; } = 200;

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
        [Display(Name = "EMA slope lookback (bars)", Order = 2, GroupName = "03-Trend & Filters")]
        public int EmaSlopeLookbackBars { get; set; } = 8;

        [NinjaScriptProperty]
        [Display(Name = "Min EMA slope (ticks)", Order = 3, GroupName = "03-Trend & Filters")]
        public double MinEmaSlopeTicks { get; set; } = 70;

        [NinjaScriptProperty]
        [Display(Name = "Min EMA separation (ticks)", Order = 4, GroupName = "03-Trend & Filters")]
        public double MinEmaSeparationTicks { get; set; } = 8;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "ADX period", Order = 5, GroupName = "03-Trend & Filters")]
        public int ADXPeriod { get; set; } = 14;

        [NinjaScriptProperty]
        [Display(Name = "ADX min", Order = 6, GroupName = "03-Trend & Filters")]
        public double ADXMin { get; set; } = 15.0;

        [NinjaScriptProperty]
        [Display(Name = "ADX max", Order = 7, GroupName = "03-Trend & Filters")]
        public double ADXMax { get; set; } = 45.0;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Confirm bars", Order = 8, GroupName = "03-Trend & Filters")]
        public int ConfirmBars { get; set; } = 1;

        [NinjaScriptProperty]
        [Display(Name = "Strong body ticks", Order = 9, GroupName = "03-Trend & Filters")]
        public int StrongBodyTicks { get; set; } = 16;

        [NinjaScriptProperty]
        [Display(Name = "Long pullback near-touch ticks", Order = 10, GroupName = "03-Trend & Filters")]
        public int LongTouchTicks { get; set; } = 20;

        [NinjaScriptProperty]
        [Display(Name = "Short pullback near-touch ticks", Order = 11, GroupName = "03-Trend & Filters")]
        public int ShortTouchTicks { get; set; } = 20;

        [NinjaScriptProperty]
        [Display(Name = "Max ATR (ticks, 0=disabled)", Order = 12, GroupName = "03-Trend & Filters")]
        public double MaxAtrTicks { get; set; } = 0.0;

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "Wick filter lookback bars (0=off)", Order = 13, GroupName = "03-Trend & Filters")]
        public int WickFilterLookback { get; set; } = 3;

        [NinjaScriptProperty]
        [Range(0, 200)]
        [Display(Name = "Max single wick (ticks, 0=off)", Order = 14, GroupName = "03-Trend & Filters")]
        public int MaxSingleWickTicks { get; set; } = 36;

        [NinjaScriptProperty]
        [Range(0, 200)]
        [Display(Name = "Max BOTH wicks on same bar (ticks, 0=off)", Order = 15, GroupName = "03-Trend & Filters")]
        public int MaxBothWicksTicks { get; set; } = 28;

        [NinjaScriptProperty]
        [Range(0.0, 1.0)]
        [Display(Name = "Min body % of range (0=off)", Order = 16, GroupName = "03-Trend & Filters")]
        public double MinBodyPctOfRange { get; set; } = 0.25;

        [NinjaScriptProperty]
        [Display(Name = "Wick filter only previous bar", Order = 17, GroupName = "03-Trend & Filters")]
        public bool WickOnlyPreviousBar { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Wick filter: block single long wick", Order = 18, GroupName = "03-Trend & Filters")]
        public bool WickBlockSingleWick { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Wick filter: block small body", Order = 19, GroupName = "03-Trend & Filters")]
        public bool WickBlockSmallBody { get; set; } = false;

        [NinjaScriptProperty]
        [Range(0, 2000)]
        [Display(Name = "Max entry distance from EMA fast (ticks, 0=off)", Order = 20, GroupName = "03-Trend & Filters")]
        public int MaxEntryDistFromEmaFastTicks { get; set; } = 200;
        
        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "Entry distance cooldown bars after rejection", Order = 21, GroupName = "03-Trend & Filters")]
        public int EntryDistCooldownBars { get; set; } = 1;
        
        [NinjaScriptProperty]
        [Display(Name = "Enable chop filter", Order = 22, GroupName = "03-Trend & Filters")]
        public bool EnableChopFilter { get; set; } = true;
        
        [NinjaScriptProperty]
        [Range(3, 50)]
        [Display(Name = "Chop lookback bars", GroupName = "03-Trend & Filters", Order = 23)]
        public int ChopLookbackBars { get; set; } = 8;

        [NinjaScriptProperty]
        [Range(0.01, 0.99)]
        [Display(Name = "Min chop efficiency (higher = less chop)", Order = 24, GroupName = "03-Trend & Filters")]
        public double MinChopEfficiency { get; set; } = 0.35;
        
        [NinjaScriptProperty]
        [Display(Name="Chop min range ticks (0=off)", Order=25, GroupName="03-Trend & Filters")]
        public int ChopMinRangeTicks { get; set; } = 80;   // NQ: 80 ticks = 20 points

        [NinjaScriptProperty]
        [Display(Name="Chop max flip pct (0=off)", Order=26, GroupName="03-Trend & Filters")]
        public double ChopMaxFlipPct { get; set; } = 0.65; // 65% flips = very choppy
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Chop bypass ADX (0=off)", Order = 27, GroupName = "03-Trend & Filters")]
        public double ChopBypassAdx { get; set; } = 30.0;

        [NinjaScriptProperty]
        [Range(0, 9999)]
        [Display(Name = "Chop bypass EMA slope strength ticks (0=off)", Order = 28, GroupName = "03-Trend & Filters")]
        public double ChopBypassEmaSlopeStrengthTicks { get; set; } = 25.0;
        
        [NinjaScriptProperty]
        [Display(Name = "Enable Momentum Filter", Order = 29, GroupName = "03-Trend & Filters")]
        public bool EnableMomentumFilter { get; set; } = true;

        // ========== 04 – Risk / Money Management ==========
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max stop (ticks)", Order = 1, GroupName = "04-Risk")]
        public int MaxStopTicks { get; set; } = 60;

        [NinjaScriptProperty]
        [Display(Name = "Max profit (ticks, 0=none)", Order = 2, GroupName = "04-Risk")]
        public int MaxProfitTicks { get; set; } = 80;

        [NinjaScriptProperty]
        [Display(Name = "Stop multiplier ATR (0=disabled)", Order = 3, GroupName = "04-Risk")]
        public double StopMultATR { get; set; } = 0.9;

        [NinjaScriptProperty]
        [Display(Name = "Reward/Risk ratio", Order = 4, GroupName = "04-Risk")]
        public double R_Ratio { get; set; } = 1.8;

        [NinjaScriptProperty]
        [Display(Name = "Min stop offset ticks", Order = 5, GroupName = "04-Risk")]
        public int MinStopOffsetTicks { get; set; } = 2;

        [NinjaScriptProperty]
        [Display(Name = "Max daily loss per contract (USD, 0=disabled)", Order = 6, GroupName = "04-Risk")]
        public double MaxDailyLossPerContractUSD { get; set; } = 600.0;

        [NinjaScriptProperty]
        [Display(Name = "Max trades per day", Order = 7, GroupName = "04-Risk")]
        public int MaxTradesPerDay { get; set; } = 8;

        [NinjaScriptProperty]
        [Display(Name = "Max daily profit per contrat (USD, 0=none)", Order = 9, GroupName = "04-Risk")]
        public double MaxDailyProfitPerContractUSD { get; set; } = 800.0;

        [NinjaScriptProperty]
        [Display(Name = "Consistency rule", Order = 10, GroupName = "04-Risk")]
        public ConsistencyRuleMode ConsistencyRule { get; set; } = ConsistencyRuleMode.None;

        [NinjaScriptProperty]
        [Display(Name = "Min minutes between trades (0=none)", Order = 11, GroupName = "04-Risk")]
        public int MinMinutesBetweenTrades { get; set; } = 10;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Emergency stop ticks (hard)", Order = 12, GroupName = "04-Risk")]
        public int EmergencyStopTicks { get; set; } = 65;

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

        [NinjaScriptProperty]
        [Display(Name = "Entry order timeout (minutes)", Order = 16, GroupName = "04-Risk")]
        public int EntryOrderTimeoutMinutes { get; set; } = 15;

        // ========== 05 – Break-even / Trade Management ==========
        [NinjaScriptProperty]
        [Display(Name = "Use break-even", Order = 1, GroupName = "05-BE & Management")]
        public bool UseBreakEven { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "BE trigger (ticks in favour)", Order = 2, GroupName = "05-BE & Management")]
        public int BE_TriggerTicks { get; set; } = 40;

        [NinjaScriptProperty]
        [Display(Name = "BE plus ticks", Order = 3, GroupName = "05-BE & Management")]
        public int BE_PlusTicks { get; set; } = 8;
        
        [NinjaScriptProperty]
        [Display(Name="Disable BE Above ADX", Order = 4, GroupName="05-BE & Management")]
        public double DisableBeAboveAdx { get; set; } = 30;

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
        [Display(Name = "TDD PlanContextId (PropFirmPlans.Id Default Apex 150k Funded)", Order = 103, GroupName = "99-API Reporter")]
        public int TddPlanContextId { get; set; } = 12;

        [NinjaScriptProperty]
        [Range(1, 60)]
        [Display(Name = "TDD Send interval seconds", Order = 104, GroupName = "99-API Reporter")]
        public int TddSendIntervalSeconds { get; set; } = 1;

        [NinjaScriptProperty]
        [Display(Name = "Override Trailing DD (USD, 0 = none)", Order = 105, GroupName = "99-API Reporter")]
        public double OverrideTrailingDrawdown { get; set; } = 0;

        // ===== Indicators =====
        private EMA emaFast;
        private EMA emaSlow;
        private ADX adx;
        private ATR atr;

        // ===== Session / state =====
        private SessionIterator _sessionIter;
        private DateTime sessionStart = DateTime.MinValue;
        private DateTime prevSessionDate = DateTime.MinValue;
        private DateTime _currentSessionBegin = DateTime.MinValue;
        private DateTime _currentSessionEnd = DateTime.MinValue;
        private int _sessionStartBarIdx = -1;

        private int tradesToday;
        private DayLocked dayLocked = DayLocked.NoLock;
        private double cumAtSessionOpen;
        
        // ---- Trade outcome logging (flat) ----
        private bool _wasInPosition = false;
        private MarketPosition _entrySide = MarketPosition.Flat;
        private int _entryQty = 0;

        // optional running PnL extremes during the trade (for MAE/MFE)
        private double _mfeTicks = 0.0;
        private double _maeTicks = 0.0;

        // Orders
        private readonly Dictionary<string, DateTime> _entryOrderBirth = new Dictionary<string, DateTime>();

        // Break-even throttle
        private int lastBEBar = -1;
        private double lastBEPrice = 0.0;
        private string lastEntryTag = string.Empty;

        // Cooldown
        private DateTime lastEntryExecutionTime = DateTime.MinValue;
        private DateTime lastFlatExecutionTime = DateTime.MinValue;

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
        
        private int _entryDistBlockLastBar = -1;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "MicroPBV3";
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
                _sessionIter = new SessionIterator(Bars);

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

                        Alert("DUPLICATE_" + _instanceGuid, Priority.High, msg, null, 0, null, null);
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

                // if (DebugMode)
                //     Print($"[INSTANCE] {_instanceGuid} terminated key={_instanceKey}");

                // Final diagnostics summary (useful for Market Replay / Strategy Analyzer / historical runs)
                // if (EnableEndOfRunReport)
                //     PrintEndOfRunReport();
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
            lastFlatExecutionTime = DateTime.MinValue;

            entryBarIdx = -1;
            entryPriceHard = 0.0;
            hardStopTriggered = false;
            entryFillTime = DateTime.MinValue;
            protectiveSeenSinceEntry = false;
            _entryDistBlockLastBar = -1;

            cumAtSessionOpen = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
        }

        private bool UpdateSessionTimes(DateTime t)
        {
            if (_sessionIter == null || Bars == null)
                return false;

            _sessionIter.GetNextSession(t, true);

            var begin = _sessionIter.ActualSessionBegin;
            var end = _sessionIter.ActualSessionEnd;

            var changed = begin != _currentSessionBegin;

            _currentSessionBegin = begin;
            _currentSessionEnd = end;

            return changed;
        }

        // sigSignal = last CLOSED bar (safe for filters, diagnostics, confirmations)
        private int SigSignal()
        {
            var isRealtime = State == State.Realtime;
            return isRealtime && Calculate == Calculate.OnEachTick ? 1 : 0;
        }

        // sigEntry = current forming bar (used for entry logic + DIAG alignment)
        private static int SigEntry()
        {
            return 0;
        }
        
        private int SigClosed()
        {
            // In intrabar modes, the last CLOSED bar is barsAgo=1 (if it exists)
            if (Calculate != Calculate.OnBarClose && CurrentBar > 0)
                return 1;

            // OnBarClose runs at bar close, so barsAgo=0 is closed
            return 0;
        }
        
        // Heuristic: suppress live API calls when running in Strategy Analyzer / optimizations.
        // (Market Replay typically has an Account, Strategy Analyzer typically does not.)
        private bool IsInStrategyAnalyzer
        {
            get
            {
                if (State == State.Realtime)
                    return false;

                // In Analyzer/Optimization, Account is usually null and no connection context exists.
                return Account == null;
            }
        }

        private bool IsExcludedAccountForDupLock()
        {
            string acc = (Account != null && Account.Name != null) ? Account.Name : string.Empty;
            if (string.IsNullOrEmpty(acc))
                return false;

            return acc.IndexOf("Playback", StringComparison.OrdinalIgnoreCase) >= 0
                || acc.IndexOf("Sim101", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

}
