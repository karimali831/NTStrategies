using System.ComponentModel.DataAnnotations;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOpeningRangeFvgBreakout : Strategy
    {
        [NinjaScriptProperty]
        [Display(Name = "Enable Diagnostics", GroupName = "Diagnostics", Order = 100)]
        public bool EnableDiagnostics { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Entry Start Time", GroupName = "Entry Window", Order = 1)]
        public int EntryStartTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Entry End Time", GroupName = "Entry Window", Order = 2)]
        public int EntryEndTime { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Quantity", GroupName = "Risk", Order = 1)]
        public int Quantity { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100000)]
        [Display(Name = "Max Daily Profit", GroupName = "Risk", Order = 2)]
        public double MaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100000)]
        [Display(Name = "Max Daily Loss", GroupName = "Risk", Order = 3)]
        public double MaxDailyLoss { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Range Start Time", GroupName = "Opening Range", Order = 10)]
        public int RangeStartTime { get; set; }

        [NinjaScriptProperty]
        [Range(1, 240)]
        [Display(Name = "Range Minutes", GroupName = "Opening Range", Order = 11)]
        public int RangeMinutes { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Min Opening Range Ticks", GroupName = "Opening Range", Order = 12)]
        public int MinOpeningRangeTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Min FVG Gap Ticks", GroupName = "FVG", Order = 20)]
        public int MinFvgGapTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "FVG Distance From Range Ticks", GroupName = "FVG", Order = 22)]
        public int FvgDistanceFromRangeTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "Max FVG Distance From Range Ticks", GroupName = "FVG", Order = 23)]
        public int MaxFvgDistanceFromRangeTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Convert Chart Time To Eastern", GroupName = "Time", Order = 30)]
        public bool ConvertChartTimeToEastern { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "Max Stop Ticks", GroupName = "Risk", Order = 4)]
        public int MaxStopTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "Auto BE Profit Trigger Ticks", GroupName = "Auto Breakeven", Order = 1)]
        public int AutoBreakevenProfitTriggerTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "Auto BE Plus Ticks", GroupName = "Auto Breakeven", Order = 2)]
        public int AutoBreakevenPlusTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "Step 1 Profit Trigger Ticks", GroupName = "3 Step Trail", Order = 1)]
        public int Trail1ProfitTriggerTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "Step 1 Stop Loss Ticks", GroupName = "3 Step Trail", Order = 2)]
        public int Trail1StopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "Step 1 Frequency Ticks", GroupName = "3 Step Trail", Order = 3)]
        public int Trail1FrequencyTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "Step 2 Profit Trigger Ticks", GroupName = "3 Step Trail", Order = 4)]
        public int Trail2ProfitTriggerTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "Step 2 Stop Loss Ticks", GroupName = "3 Step Trail", Order = 5)]
        public int Trail2StopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "Step 2 Frequency Ticks", GroupName = "3 Step Trail", Order = 6)]
        public int Trail2FrequencyTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "Step 3 Profit Trigger Ticks", GroupName = "3 Step Trail", Order = 7)]
        public int Trail3ProfitTriggerTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "Step 3 Stop Loss Ticks", GroupName = "3 Step Trail", Order = 8)]
        public int Trail3StopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "Step 3 Frequency Ticks", GroupName = "3 Step Trail", Order = 9)]
        public int Trail3FrequencyTicks { get; set; }
    }
}
