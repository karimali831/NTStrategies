#region Using declarations
using System;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class OrbMomentum : Strategy
    {
        // Indicators
        private EMA emaFast;
        private EMA emaSlow;
        private ADX adx;

        // ORB state
        private double orHigh;
        private double orLow;
        private bool   orBuilt;

        // Trade state
        private int      tradesToday;
		private DateTime lastTradeTime;
        private int      primaryDir;                 // +1 long, -1 short, 0 none
        private double   primaryEntryPrice;
        private bool     primarySubmitted;
		private bool     reEntryWaitPullback;
		private int      reEntryDir;
		
		// Risk
		private double startOfDayCumProfit;
		
		// Runner
		private bool   runnerSubmitted;
        private bool   runnerStopMoved;
		private bool   runnerArmed;
		private bool   runnerPullbackSeen;
		private int    runnerArmBar;
		private bool   runnerFilled;
		private double runnerEntryPrice;
		
		// Logging 
		private int lastLoggedSigBar;
		private double lastLoggedOrHigh;
		private double lastLoggedOrLow;
		private int    lastLoggedOrbBar;

        // Constants
        private const string SigPrimaryLong  = "ORB1L";
        private const string SigPrimaryShort = "ORB1S";
        private const string SigRunnerLong   = "ORB2L";
        private const string SigRunnerShort  = "ORB2S";

        #region Params (as requested)
        [NinjaScriptProperty]
        [Display(Name="Contracts", Order=1, GroupName="General")]
        public int Contracts { get; set; } = 1;

        [NinjaScriptProperty]
        [Display(Name="Enable longs", Order=2, GroupName="General")]
        public bool EnableLongs { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name="Enable shorts", Order=3, GroupName="General")]
        public bool EnableShorts { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name="Enable runner", Order=4, GroupName="General")]
        public bool EnableRunner { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name="Max profit per trade ($)", Order=5, GroupName="Risk")]
        public double MaxProfitPerTrade { get; set; } = 500;

        [NinjaScriptProperty]
        [Display(Name="Max loss per trade ($)", Order=6, GroupName="Risk")]
        public double MaxLossPerTrade { get; set; } = 250;

        [NinjaScriptProperty]
        [Display(Name="Max trades per day", Order=7, GroupName="Risk")]
        public int MaxTradesPerDay { get; set; } = 8;
		
		[NinjaScriptProperty]
		[Display(Name="Max daily loss ($)", Order=8, GroupName="Risk")]
		public double MaxDailyLoss { get; set; } = 500;
		
		[NinjaScriptProperty]
		[Display(Name="Max daily profit ($)", Order=9, GroupName="Risk")]
		public double MaxDailyProfit { get; set; } = 1500;
		
		[NinjaScriptProperty]
		[Display(Name="Trade cooldown minutes", Order=10, GroupName="Risk")]
		public int TradeCooldownMinutes { get; set; } = 10;

        [NinjaScriptProperty]
        [Display(Name="Mins from start", Order = 1, GroupName="ORB")]
        public int MinsFromStart { get; set; } = 20;

        [NinjaScriptProperty]
        [Display(Name="Mins from end", Order = 2, GroupName="ORB")]
        public int MinsFromEnd { get; set; } = 75;
		
		[NinjaScriptProperty]
		[Display(Name="EMA Fast", Order = 3, GroupName="ORB")]
		public int EMAFast { get; set; } = 14;
		
		[NinjaScriptProperty]
		[Display(Name="EMA Slow", Order = 4, GroupName="ORB")]
		public int EMASlow { get; set; } = 40;
		
		[NinjaScriptProperty]
		[Display(Name="Min ticks outside ORB", Order = 5, GroupName="ORB")]
		public int MinTicksOutsideOrb { get; set; } = 8;

        [NinjaScriptProperty]
        [Display(Name="Runner break-even enabled", Order=10, GroupName="Runner")]
        public bool RunnerBreakEvenEnabled { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name="Runner trigger ticks", Order=11, GroupName="Runner")]
        public int RunnerTriggerTicks { get; set; } = 50;

        [NinjaScriptProperty]
        [Display(Name="Runner plus ticks", Order=12, GroupName="Runner")]
        public int RunnerPlusTicks { get; set; } = 8;
		
		[NinjaScriptProperty]
		[Display(Name="Runner pullback ticks", Order=13, GroupName="Runner")]
		public int RunnerPullbackTicks { get; set; } = 8;

        // Extra (suggested) param to control output noise
        [NinjaScriptProperty]
        [Display(Name="Enable diagnostics", Order=13, GroupName="Diagnostics")]
        public bool EnableDiagnostics { get; set; } = true;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name  = "OrbMomentum";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 2;
				EntryHandling = EntryHandling.UniqueEntries;
				StopTargetHandling = StopTargetHandling.PerEntryExecution;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds    = 30;
                IsInstantiatedOnEachOptimizationIteration = false;
            }
            else if (State == State.DataLoaded)
            {
                emaFast = EMA(EMAFast);
                emaSlow = EMA(EMASlow);
                adx     = ADX(14);

                ResetDailyState();
            }
        }
    }
}