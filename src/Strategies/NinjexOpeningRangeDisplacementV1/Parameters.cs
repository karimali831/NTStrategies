using System.ComponentModel.DataAnnotations;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOpeningRangeDisplacementV1
    {
        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Contracts", GroupName = "01. Risk", Order = 1)]
        public int TradingQuantity { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 20)]
        [Display(Name = "Reward / Risk", GroupName = "01. Risk", Order = 2)]
        public double RiskRewardRatio { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Max Entries / Day (0 = unlimited)", GroupName = "01. Risk", Order = 3)]
        public int MaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(93500, 155500)]
        [Display(Name = "Entry Start ET (HHmmss)", GroupName = "02. Session", Order = 1)]
        public int EntryStartTime { get; set; }

        [NinjaScriptProperty]
        [Range(93500, 160000)]
        [Display(Name = "Entry End ET (HHmmss)", GroupName = "02. Session", Order = 2)]
        public int EntryEndTime { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "Min FVG Gap Ticks", GroupName = "03. FVG", Order = 1)]
        public int MinFvgTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable ATR Filter", GroupName = "04. Research Filters", Order = 1)]
        public bool EnableAtrFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2000)]
        [Display(Name = "Min ATR14 Ticks", GroupName = "04. Research Filters", Order = 2)]
        public double MinAtrTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable ADX Filter", GroupName = "04. Research Filters", Order = 3)]
        public bool EnableAdxFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Min ADX14", GroupName = "04. Research Filters", Order = 4)]
        public double MinAdx { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Body/ATR Filter", GroupName = "04. Research Filters", Order = 5)]
        public bool EnableBodyAtrFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10)]
        [Display(Name = "Min Breakout Body / ATR14", GroupName = "04. Research Filters", Order = 6)]
        public double MinBreakoutBodyAtr { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Daily CSV File Logs", GroupName = "05. Diagnostics", Order = 1)]
        public bool EnableFileLogging { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Print Important Events", GroupName = "05. Diagnostics", Order = 2)]
        public bool EnablePrintDiagnostics { get; set; }
    }
}