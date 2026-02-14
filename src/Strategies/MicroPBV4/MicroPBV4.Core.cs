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

    public partial class MicroPBV4 : Strategy
    {
        public enum DayLocked
        {
            NoLock = 0,
            DailyProfitReached = 1,
            MaxLossReached = 2
        }

        // ========== 01 – General / Mode ==========
        [NinjaScriptProperty]
        [Display(Name = "Debug mode", Order = 1, GroupName = "01-General")]
        public bool DebugMode { get; set; } = true;
        
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contracts", Order = 2, GroupName = "01-General")]
        public int Contracts { get; set; } = 2;

        [NinjaScriptProperty]
        [Display(Name = "Enable longs", Order = 3, GroupName = "01-General")]
        public bool EnableLongs { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Enable shorts", Order = 4, GroupName = "01-General")]
        public bool EnableShorts { get; set; } = true;
        
        // ========== 02 – Session / Time Filters ==========
        [NinjaScriptProperty]
        [Display(Name = "Min minutes from open", Order = 1, GroupName = "02-Session")]
        public int MinMinutesFromOpen { get; set; } = 20;

        [NinjaScriptProperty]
        [Display(Name = "Max minutes from open", Order = 2, GroupName = "02-Session")]
        public int MaxMinutesFromOpen { get; set; } = 200;

        [NinjaScriptProperty]
        [Display(Name = "Mid-break start (min from open)", Order = 3, GroupName = "02-Session")]
        public int MidBreakStartMin { get; set; } = 0;

        [NinjaScriptProperty]
        [Display(Name = "Mid-break end (min from open)", Order = 4, GroupName = "02-Session")]
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
        [Display(Name = "EMA slope lookback (bars)", Order = 3, GroupName = "03-Trend & Filters")]
        public int EmaSlopeLookbackBars { get; set; } = 8;
        
        [NinjaScriptProperty]
        [Display(Name = "Trend slope min ticks", Order = 4, GroupName = "03-Trend & Filters")]
        public int TrendSlopeMinTicks { get; set; } = 25;
        
        [NinjaScriptProperty]
        [Display(Name = "Min EMA slope (ticks)", Order = 5, GroupName = "03-Trend & Filters")]
        public double MinEmaSlopeTicks { get; set; } = 70;

        [NinjaScriptProperty]
        [Display(Name = "Min EMA separation (ticks)", Order = 6, GroupName = "03-Trend & Filters")]
        public double MinEmaSeparationTicks { get; set; } = 8;
        
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "ADX period", Order = 7, GroupName = "03-Trend & Filters")]
        public int ADXPeriod { get; set; } = 14;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Confirm bars", Order = 10, GroupName = "03-Trend & Filters")]
        public int ConfirmBars { get; set; } = 1;

        [NinjaScriptProperty]
        [Display(Name = "Strong body ticks", Order = 11, GroupName = "03-Trend & Filters")]
        public int StrongBodyTicks { get; set; } = 16;

        [NinjaScriptProperty]
        [Display(Name = "Long pullback near-touch ticks", Order = 12, GroupName = "03-Trend & Filters")]
        public int LongTouchTicks { get; set; } = 20;

        [NinjaScriptProperty]
        [Display(Name = "Short pullback near-touch ticks", Order = 13, GroupName = "03-Trend & Filters")]
        public int ShortTouchTicks { get; set; } = 20;

        [NinjaScriptProperty]
        [Display(Name = "Max ATR (ticks, 0=disabled)", Order = 14, GroupName = "03-Trend & Filters")]
        public double MaxAtrTicks { get; set; } = 0.0;

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "Wick filter lookback bars (0=off)", Order = 15, GroupName = "03-Trend & Filters")]
        public int WickFilterLookback { get; set; } = 3;

        [NinjaScriptProperty]
        [Range(0, 200)]
        [Display(Name = "Max single wick (ticks, 0=off)", Order = 16, GroupName = "03-Trend & Filters")]
        public int MaxSingleWickTicks { get; set; } = 36;

        [NinjaScriptProperty]
        [Range(0, 200)]
        [Display(Name = "Max BOTH wicks on same bar (ticks, 0=off)", Order = 17, GroupName = "03-Trend & Filters")]
        public int MaxBothWicksTicks { get; set; } = 28;
        
        [NinjaScriptProperty]
        [Display(Name = "Wick filter only previous bar", Order = 18, GroupName = "03-Trend & Filters")]
        public bool WickOnlyPreviousBar { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Wick filter: block single long wick", Order = 19, GroupName = "03-Trend & Filters")]
        public bool WickBlockSingleWick { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Wick filter: block small body", Order = 20, GroupName = "03-Trend & Filters")]
        public bool WickBlockSmallBody { get; set; } = false;
        
        [NinjaScriptProperty]
        [Range(0, 2000)]
        [Display(
            Name = "Max prior bar range (ticks, 0=off)",
            Description = "Blocks entry if the previous bar range exceeds this value",
            Order = 21,
            GroupName = "03-Trend & Filters")]
        public int MaxPriorBarRangeTicks { get; set; } = 200;
        
        [NinjaScriptProperty]
        [Range(0, 21)]
        [Display(Name = "Entry distance cooldown bars after rejection", Order = 22, GroupName = "03-Trend & Filters")]
        public int EntryDistCooldownBars { get; set; } = 1;
        
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
        public int MaxTradesPerDay { get; set; } = 5;

        [NinjaScriptProperty]
        [Display(Name = "Max daily profit per contrat (USD, 0=none)", Order = 8, GroupName = "04-Risk")]
        public double MaxDailyProfitPerContractUSD { get; set; } = 800.0;
        
        [NinjaScriptProperty]
        [Display(Name = "Min minutes between trades (0=none)", Order = 9, GroupName = "04-Risk")]
        public int MinMinutesBetweenTrades { get; set; } = 10;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Emergency stop ticks (hard)", Order = 10, GroupName = "04-Risk")]
        public int EmergencyStopTicks { get; set; } = 65;

        [NinjaScriptProperty]
        [Display(Name = "Auto emergency = MaxStop+5", Order = 11, GroupName = "04-Risk")]
        public bool AutoEmergencyStop { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Entry order timeout (minutes)", Order = 12, GroupName = "04-Risk")]
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
        
        // ========== 03 – Trend / Pullback / Confirmation ==========
        [NinjaScriptProperty]
        [Display(Name="RegimeErLookbackBars", Order = 1, GroupName = "06-Regime")]
        public int RegimeErLookbackBars { get; set; } = 10;
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name="RegimeScoreMin", GroupName="06-Regime", Order = 2)]
        public int RegimeScoreMin { get; set; } = 20;   // used later as filter if you want

        [NinjaScriptProperty]
        [Range(30, 500)]
        [Display(Name="ATR Percentile Lookback Bars", Order = 3, GroupName = "06-Regime")]
        public int RegimeAtrPctLookbackBars { get; set; } = 120;

        [NinjaScriptProperty]
        [Range(0.0, 1.0)]
        [Display(Name="ATR Percentile Min", Order = 4, GroupName = "06-Regime")]
        public double RegimeAtrPctMin { get; set; } = 0.25;   // below this = too quiet vs recent history

        [NinjaScriptProperty]
        [Range(0.0, 1.0)]
        [Display(Name="ATR Percentile Max", Order = 5, GroupName = "06-Regime")]
        public double RegimeAtrPctMax { get; set; } = 0.90;   // above this = very hot/volatile
        
        [NinjaScriptProperty]
        [Range(20, 500)]
        [Display(Name="ATR Median Lookback Bars", Order=6, GroupName = "06-Regime")]
        public int RegimeAtrMedianLookbackBars { get; set; } = 120;
        
        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name="RegimeAdxSweetLow", GroupName="06-Regime", Order = 7)]
        public int RegimeAdxSweetLow { get; set; } = 18;

        [NinjaScriptProperty]
        [Range(1, 80)]
        [Display(Name="RegimeAdxSweetHigh", GroupName="06-Regime", Order = 8)]
        public int RegimeAdxSweetHigh { get; set; } = 35;

        [NinjaScriptProperty]
        [Range(0.0, 1.0)]
        [Display(Name="RegimeErMin", GroupName="06-Regime", Order = 9)]
        public double RegimeErMin { get; set; } = 0.30;
        
        [NinjaScriptProperty]
        [Display(Name="RegimeErDecay", Order = 10, GroupName = "06-Regime")]
        public double RegimeErDecay { get; set; } = 0.90;   // 0.85..0.95 typical

        
        // Displacement / momentum detector
        [NinjaScriptProperty]
        [Range(1.0, 5.0)]
        [Display(Name="Disp Range ATR Mult", Order=1, GroupName="07-Momentum")]
        public double DispRangeAtrMult { get; set; } = 1.25;

        [NinjaScriptProperty]
        [Range(0.10, 0.95)]
        [Display(Name="Disp Body % Min", Order=2, GroupName="07-Momentum")]
        public double DispBodyPctMin { get; set; } = 0.55;

        [NinjaScriptProperty]
        [Range(0.50, 1.00)]
        [Display(Name="Disp CLV Min (bull)", Order=3, GroupName="07-Momentum")]
        public double DispClvMinBull { get; set; } = 0.80;

        [NinjaScriptProperty]
        [Range(0.00, 0.50)]
        [Display(Name="Disp CLV Max (bear)", Order=4, GroupName="07-Momentum")]
        public double DispClvMaxBear { get; set; } = 0.20;

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name="Disp Breakout Ticks", Order=5, GroupName="07-Momentum")]
        public int DispBreakoutTicks { get; set; } = 2;

        // How displacement overrides regime
        [NinjaScriptProperty]
        [Range(0.0, 1.0)]
        [Display(Name="Disp Override ER Min", Order=6, GroupName="07-Momentum")]
        public double DispOverrideErMin { get; set; } = 0.45;

        [NinjaScriptProperty]
        [Range(0, 30)]
        [Display(Name="Disp Override Cross Bars", Order=7, GroupName="07-Momentum")]
        public int DispOverrideCrossBars { get; set; } = 6;
        
        private double _lastRegimeScore = 0;
        private string _lastRegimeJson = "";
        private string _lastRegimeLabel = "UNKNOWN";

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

        // Orders, entries
        private double entryPriceHard = 0.0;
        private bool hardStopTriggered = false;
        private DateTime entryFillTime = DateTime.MinValue;
        private bool protectiveSeenSinceEntry = false;
        private int entrySeq = 0;
        private int entryBarIdx = -1;
        private readonly Dictionary<string, DateTime> _entryOrderBirth = new Dictionary<string, DateTime>();

        // --- one-shot deferral when entry-distance fails due to huge prior-bar range ---
        private int _entryDistDeferLongBar  = -1;
        private int _entryDistDeferShortBar = -1;
        
        // Break-even throttle
        private int lastBEBar = -1;
        private double lastBEPrice = 0.0;
        private string lastEntryTag = string.Empty;

        // Cooldown
        private DateTime lastFlatExecutionTime = DateTime.MinValue;

        // ===== Duplicate instance lock state =====
        private string _instanceKey = null;
        private bool _duplicateBlocked = false;
        private bool _duplicateCleanupDone = false;
        private string _instanceGuid = null;
        private static readonly object _dupLock = new object();
        private static readonly HashSet<string> _activeInstanceKeys = new HashSet<string>();
        
        private int _entryDistBlockLastBar = -1;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "MicroPBV4";
                Calculate = Calculate.OnBarClose;
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
            }
            else if (State == State.Realtime)
            {
                // Duplicate guard only on real accounts
                if (Account != null && Instrument != null && !IsExcludedAccountForDupLock())
                {
                    var strat = GetType().FullName ?? Name;
                    var acc = Account.Name ?? "N/A";
                    var inst = Instrument.FullName ?? "N/A";
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
                else if (DebugMode && Account != null && Instrument != null && IsExcludedAccountForDupLock())
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
                
                DumpFlatTradesSummary();
            }
        }

        private void ResetDay(DateTime time)
        {
            tradesToday = 0;
            dayLocked = DayLocked.NoLock;
            
            entryBarIdx = -1;
            entryPriceHard = 0.0;
            hardStopTriggered = false;
            entryFillTime = DateTime.MinValue;
            protectiveSeenSinceEntry = false;
            _entryDistBlockLastBar = -1;
            
            lastBEBar = -1;
            lastBEPrice = 0.0;
            lastEntryTag = string.Empty;

            sessionStart = time;
            prevSessionDate = time.Date;
            lastFlatExecutionTime = DateTime.MinValue;
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
