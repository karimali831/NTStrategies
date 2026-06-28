#region Using declarations
using System.ComponentModel.DataAnnotations;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOpeningVolumeProfileBreakout : Strategy
    {
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
        [Display(Name = "Enable Longs", Order = 10, GroupName = "Entry")]
        public bool EnableLongs { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Shorts", Order = 11, GroupName = "Entry")]
        public bool EnableShorts { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Entry Start Time", Order = 12, GroupName = "Entry")]
        public int EntryStartTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Entry End Time", Order = 13, GroupName = "Entry")]
        public int EntryEndTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Entry Offset Ticks", Order = 14, GroupName = "Entry")]
        public int EntryOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 200)]
        [Display(Name = "Min Retracement Ticks", Order = 15, GroupName = "Entry")]
        public int MinRetracementTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Max Distance Ticks From Breakout Level", Order = 16, GroupName = "Entry")]
        public int MaxDistanceTicksFromBreakoutLevel { get; set; }

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
        [Display(Name = "Max Trades Per Day", Order = 24, GroupName = "Risk Management")]
        public int MaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Quantity", Order = 30, GroupName = "Order")]
        public int Quantity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Convert Chart Time To Eastern", Order = 40, GroupName = "Time")]
        public bool ConvertChartTimeToEastern { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Source Time Zone Id", Order = 41, GroupName = "Time")]
        public string SourceTimeZoneId { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Show Profile Horizontal Lines", Order = 52, GroupName = "Visual")]
        public bool ShowProfileHorizontalLines { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Debug", Order = 100, GroupName = "Debug")]
        public bool EnableDebug { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Enable Data Collection", Order = 200, GroupName = "Data Collection")]
        public bool EnableDataCollection { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Data Directory Name", Order = 201, GroupName = "Data Collection")]
        public string DataDirectoryName { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Data File Prefix", Order = 202, GroupName = "Data Collection")]
        public string DataFilePrefix { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Log Daily Profile Rows", Order = 203, GroupName = "Data Collection")]
        public bool LogDailyProfileRows { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Log Rejected Setups", Order = 204, GroupName = "Data Collection")]
        public bool LogRejectedSetups { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Track Forward Outcome", Order = 205, GroupName = "Data Collection")]
        public bool TrackForwardOutcome { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "Forward Bars To Track", Order = 206, GroupName = "Data Collection")]
        public int ForwardBarsToTrack { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Same Bar Stop First", Order = 207, GroupName = "Data Collection")]
        public bool SameBarStopFirst { get; set; }
    }
}