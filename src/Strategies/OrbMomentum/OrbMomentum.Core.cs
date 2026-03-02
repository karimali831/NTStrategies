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
        private bool     primaryStopMoved;
        private bool     primaryFilled;
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
		private int    lastLoggedSigBar;
		private double lastLoggedOrHigh;
		private double lastLoggedOrLow;
		private int    lastLoggedOrbBar;
		private int    lastLoggedBlockBar;
		private string lastLoggedBlockReason;
		private int    lastLoggedEmaDistBar;
		private int    lastDiagBar;
		
		// Double top/bottom
		private int lastDtbMarkBar = -1;

        // Constants
        private const string SigPrimaryLong  = "ORB1L";
        private const string SigPrimaryShort = "ORB1S";
        private const string SigRunnerLong   = "ORB2L";
        private const string SigRunnerShort  = "ORB2S";

		//-- GENERAL --//
		[NinjaScriptProperty]
		[Display(Name="Enable diagnostics", Order=1, GroupName="01-General")]
		public bool EnableDiagnostics { get; set; } = true;
		
        [NinjaScriptProperty]
        [Display(Name="Contracts", Order=2, GroupName="01-General")]
        public int Contracts { get; set; } = 1;
        
        [NinjaScriptProperty]
        [Display(Name="Mins from start", Order = 3, GroupName="01-General")]
        public int MinsFromStart { get; set; } = 20;

        [NinjaScriptProperty]
        [Display(Name="Mins from end", Order = 4, GroupName="01-General")]
        public int MinsFromEnd { get; set; } = 75;

        [NinjaScriptProperty]
        [Display(Name="Enable longs", Order=5, GroupName="01-General")]
        public bool EnableLongs { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name="Enable shorts", Order=6, GroupName="01-General")]
        public bool EnableShorts { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name="Enable runner", Order=7, GroupName="01-General")]
        public bool EnableRunner { get; set; } = true;
        
        [NinjaScriptProperty]
        [Display(Name="Display Double Top/Bottom Dots", Order=8, GroupName="01-General")]
        public bool EnableDoubleTopBottomFilter { get; set; } = true;
        
        [NinjaScriptProperty]
        [Display(Name="Require pullback", Order=9, GroupName="01-General")]
        public bool RequirePullback { get; set; } = true;
        
        //-- FILTERS --//
        [NinjaScriptProperty]
        [Display(Name="EMA Fast", Order = 1, GroupName="02-Filters")]
        public int EMAFast { get; set; } = 14;
		
        [NinjaScriptProperty]
        [Display(Name="EMA Slow", Order = 2, GroupName="02-Filters")]
        public int EMASlow { get; set; } = 40;
        
        [NinjaScriptProperty]
        [Display(Name="Confirm bars", Order= 3, GroupName="02-Filters")]
        public int ConfirmBars { get; set; } = 1;

        [NinjaScriptProperty]
        [Display(Name="Confirm body ticks", Order= 4, GroupName="02-Filters")]
        public int ConfirmBodyTicks { get; set; } = 16;
        
        [NinjaScriptProperty]
        [Display(Name="Trade cooldown minutes", Order= 5, GroupName="02-Filters")]
        public int TradeCooldownMinutes { get; set; } = 10;
        
        [NinjaScriptProperty]
        [Display(Name="Min ticks outside ORB", Order = 6, GroupName="02-Filters")]
        public int MinTicksOutsideOrb { get; set; } = 8;
        
        [NinjaScriptProperty]
        [Display(Name="Entry EMA min proximity ticks", Order=7, GroupName="02-Filters")]
        public int EntryEmaMinProximityTicks { get; set; } = 8;
        
        [NinjaScriptProperty]
        [Display(Name="Entry EMA max proximity ticks", Order=8, GroupName="02-Filters")]
        public int EntryEmaMaxProximityTicks { get; set; } = 30;
        
        [NinjaScriptProperty]
        [Display(Name="Runner pullback ticks", Order=9, GroupName="02-Filters")]
        public int RunnerPullbackTicks { get; set; } = 8;
        
        [NinjaScriptProperty]
        [Display(Name="Indecision wick diff ticks", Order=0, GroupName="02-Filters")]
        public int IndecisionWickDiffTicks { get; set; } = 6;

        [NinjaScriptProperty]
        [Display(Name="Rejection wick min ticks", Order=11, GroupName="02-Filters")]
        public int RejectionWickMinTicks { get; set; } = 24;
        
        [NinjaScriptProperty]
        [Display(Name="Early entry range ticks", Order=12, GroupName="02-Filters")]
        public int EarlyEntryRangeTicks { get; set; } = 40;
        
        [NinjaScriptProperty]
        [Display(Name="ADX Min", Order=13, GroupName="02-Filters")]
        public int ADXMin { get; set; } = 15;
        
        [NinjaScriptProperty]
        [Display(Name="Double Top/Bottom Lookback Bars", Order=15, GroupName="02-Filters")]
        public int DoubleTopBottomLookbackBars { get; set; } = 4;

        [NinjaScriptProperty]
        [Display(Name="Double Top/Bottom Max Diff Ticks", Order=16, GroupName="02-Filters")]
        public int DoubleTopBottomMaxDiffTicks { get; set; } = 5;
        
        [NinjaScriptProperty]
        [Display(Name="EMA separation min ticks", Order=17, GroupName="02-Filters")]
        public int EmaSeparationMinTicks { get; set; } = 15;

        [NinjaScriptProperty]
        [Display(Name="EMA separation max ticks (0=off)", Order=18, GroupName="02-Filters")]
        public int EmaSeparationMaxTicks { get; set; } = 180;
        
        //-- RISK --//
        [NinjaScriptProperty]
        [Display(Name="Max profit per trade ($)", Order=1, GroupName="03-Risk")]
        public double MaxProfitPerTrade { get; set; } = 500;

        [NinjaScriptProperty]
        [Display(Name="Max loss per trade ($)", Order=2, GroupName="03-Risk")]
        public double MaxLossPerTrade { get; set; } = 250;

        [NinjaScriptProperty]
        [Display(Name="Max trades per day", Order=3, GroupName = "03-Risk")]
        public int MaxTradesPerDay { get; set; } = 8;
		
		[NinjaScriptProperty]
		[Display(Name="Max daily loss ($)", Order=4, GroupName = "03-Risk")]
		public double MaxDailyLoss { get; set; } = 500;
		
		[NinjaScriptProperty]
		[Display(Name="Max daily profit ($)", Order=5, GroupName= "03-Risk")]
		public double MaxDailyProfit { get; set; } = 1500;
		
		//-- BREAK-EVEN --//
		[NinjaScriptProperty]
		[Display(Name="Primary break-even enabled", Order=1, GroupName="04-Break-even")]
		public bool PrimaryBreakEvenEnabled { get; set; } = true;

		[NinjaScriptProperty]
		[Display(Name="Primary BE trigger ticks", Order=2, GroupName="04-Break-even")]
		public int PrimaryBeTriggerTicks { get; set; } = 25;

		[NinjaScriptProperty]
		[Display(Name="Primary BE plus ticks", Order=3, GroupName="04-Break-even")]
		public int PrimaryBePlusTicks { get; set; } = 6;
		
		[NinjaScriptProperty]
		[Display(Name="Runner break-even enabled", Order=4, GroupName="04-Break-even")]
		public bool RunnerBreakEvenEnabled { get; set; } = true;

		[NinjaScriptProperty]
		[Display(Name="Runner trigger ticks", Order=5, GroupName="04-Break-even")]
		public int RunnerTriggerTicks { get; set; } = 50;

		[NinjaScriptProperty]
		[Display(Name="Runner plus ticks", Order=6, GroupName="04-Break-even")]
		public int RunnerPlusTicks { get; set; } = 8;
		

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name  = "OrbMomentum";
                Calculate = Calculate.OnEachTick;
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

                ConfigureEmaVisuals();
                ResetDailyState();
            }
        }
    }
}