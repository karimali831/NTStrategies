#region Using declarations
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexPremarketRangeResearch
    {
        private void ApplyPropertyDefaults()
        {
            EnableDiagnostics = true;
            EnableDataAnalysis = false;
            EnableTradeExecution = false;
            EnablePrecisionTickAnalysis = true;
            EnableRiskScenarioAnalysis = true;
            DataSourceLabel = "Historical";

            EnableRetestModel = false;
            EnableBreakoutModel = true;
            EnableAcceptanceModel = false;

            Atr1MinutePeriod = 14;
            Atr5MinutePeriod = 14;
            Adx5MinutePeriod = 14;

            MinimumAcceptanceClosesOutside = 2;
            MaximumAcceptanceBars = 5;
            MinimumAcceptanceExcursionTicks = 40;
            MinimumAcceptanceCloseDistanceTicks = 1;
            AllowAcceptanceLaterAttempts = true;
            MinimumAcceptancePriorFailedAttempts = 0;

            RangeStartTime = 30000;
            MarketOpenTime = 93000;
            EntryStartTime = 93500;
            EntryEndTime = 165500;
            FlattenTime = 165500;

            MinimumBreakoutDistanceTicks = 1;
            EntryMinimumDistanceTicksFromRange = 30;
            EntryMaximumDistanceTicksFromRange = 50;

            MaximumRetestBars = 5;
            RetestOutsideDistanceTicks = 40;
            RetestInsideDistanceTicks = 40;

            MinimumStrongBodyPercent = 60;
            MinimumCloseLocationPercent = 75;
            RelativeBodyLookback = 10;
            MinimumRelativeBodyMultiple = 1.25;
            MinimumRetestConfirmationBodyPercent = 50;

            Quantity = 1;
            RiskRewardRatio = 2.0;
            MaximumDailyProfit = 0;
            MaximumDailyLoss = 0;
            MaximumInitialStopTicks = 100;

            BEProfitTriggerTicks = 0;
            BEPlusTicks = 0;

            Step1ProfitTriggerTicks = 0;
            Step1StopLossTicks = 0;
            Step1FrequencyTicks = 0;
            Step2ProfitTriggerTicks = 0;
            Step2StopLossTicks = 0;
            Step2FrequencyTicks = 0;
            Step3ProfitTriggerTicks = 0;
            Step3StopLossTicks = 0;
            Step3FrequencyTicks = 0;

            OutputFolderName = "NinjexData";
            OutputFilePrefix = "premarket_range_research";
        }

        #region Analysis

        [NinjaScriptProperty]
        [Display(Name = "Enable Diagnostics", Order = 1, GroupName = "Analysis")]
        public bool EnableDiagnostics { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Data Analysis", Order = 2, GroupName = "Analysis")]
        public bool EnableDataAnalysis { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Precision Tick Analysis", Order = 3, GroupName = "Analysis")]
        public bool EnablePrecisionTickAnalysis { get; set; }
        
        [NinjaScriptProperty]
        [Display(
            Name = "Enable Risk Scenario Analysis",
            Order = 4,
            GroupName = "Analysis")]
        
        public bool EnableRiskScenarioAnalysis
        {
            get;
            set;
        }

        [NinjaScriptProperty]
        [Display(Name = "Data Source Label", Order = 4, GroupName = "Analysis")]
        public string DataSourceLabel { get; set; }

        #endregion

        #region Entries

        [NinjaScriptProperty]
        [Display(
            Name = "Enable Trade Execution",
            Order = 1,
            GroupName = "Execution")]
        
        public bool EnableTradeExecution { get; set; }
        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Range Start Time", Order = 8, GroupName = "Entries")]
        public int RangeStartTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Market Open Time", Order = 9, GroupName = "Entries")]
        public int MarketOpenTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Re-test Model", Order = 10, GroupName = "Entries")]
        public bool EnableRetestModel { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Breakout Model", Order = 11, GroupName = "Entries")]
        public bool EnableBreakoutModel { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Acceptance Model", Order = 12, GroupName = "Entries")]
        public bool EnableAcceptanceModel { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Entry Start Time", Order = 12, GroupName = "Entries")]
        public int EntryStartTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Entry End Time", Order = 13, GroupName = "Entries")]
        public int EntryEndTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Flatten Time", Order = 14, GroupName = "Entries")]
        public int FlattenTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "Minimum Breakout Distance Ticks", Order = 15, GroupName = "Entries")]
        public int MinimumBreakoutDistanceTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Entry Min Distance Ticks From Range", Order = 16, GroupName = "Entries")]
        public int EntryMinimumDistanceTicksFromRange { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Entry Max Distance Ticks From Range", Order = 17, GroupName = "Entries")]
        public int EntryMaximumDistanceTicksFromRange { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Maximum Retest Bars", Order = 20, GroupName = "Entries")]
        public int MaximumRetestBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Retest Outside Distance Ticks", Order = 21, GroupName = "Entries")]
        public int RetestOutsideDistanceTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Retest Inside Distance Ticks", Order = 22, GroupName = "Entries")]
        public int RetestInsideDistanceTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Minimum Strong Body %", Order = 30, GroupName = "Entries")]
        public double MinimumStrongBodyPercent { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Minimum Close Location %", Order = 31, GroupName = "Entries")]
        public double MinimumCloseLocationPercent { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Relative Body Lookback", Order = 32, GroupName = "Entries")]
        public int RelativeBodyLookback { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 10)]
        [Display(Name = "Minimum Relative Body Multiple", Order = 33, GroupName = "Entries")]
        public double MinimumRelativeBodyMultiple { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Minimum Retest Confirmation Body %", Order = 34, GroupName = "Entries")]
        public double MinimumRetestConfirmationBodyPercent { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "ATR 1 Minute Period", Order = 35, GroupName = "Research Features")]
        public int Atr1MinutePeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "ATR 5 Minute Period", Order = 36, GroupName = "Research Features")]
        public int Atr5MinutePeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "ADX 5 Minute Period", Order = 37, GroupName = "Research Features")]
        public int Adx5MinutePeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Acceptance Minimum Closes Outside", Order = 38, GroupName = "Acceptance Model")]
        public int MinimumAcceptanceClosesOutside { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Acceptance Maximum Bars", Order = 39, GroupName = "Acceptance Model")]
        public int MaximumAcceptanceBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Acceptance Minimum Excursion Ticks", Order = 40, GroupName = "Acceptance Model")]
        public double MinimumAcceptanceExcursionTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Acceptance Minimum Close Distance Ticks", Order = 41, GroupName = "Acceptance Model")]
        public double MinimumAcceptanceCloseDistanceTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Acceptance Allow Later Attempts", Order = 42, GroupName = "Acceptance Model")]
        public bool AllowAcceptanceLaterAttempts { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Acceptance Minimum Prior Failed Attempts", Order = 43, GroupName = "Acceptance Model")]
        public int MinimumAcceptancePriorFailedAttempts { get; set; }

        #endregion

        #region Risk

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "Quantity", Order = 40, GroupName = "Risk")]
        public int Quantity { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 20)]
        [Display(Name = "Risk To Reward Ratio", Order = 41, GroupName = "Risk")]
        public double RiskRewardRatio { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000000)]
        [Display(Name = "Max Daily Profit", Order = 42, GroupName = "Risk")]
        public double MaximumDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000000)]
        [Display(Name = "Max Daily Loss", Order = 43, GroupName = "Risk")]
        public double MaximumDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Maximum Initial Stop Ticks", Order = 44, GroupName = "Risk")]
        public int MaximumInitialStopTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "BE Profit Trigger Ticks", Order = 45, GroupName = "Risk")]
        public int BEProfitTriggerTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "BE Plus Ticks", Order = 46, GroupName = "Risk")]
        public int BEPlusTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Step 1 Profit Trigger Ticks", Order = 50, GroupName = "Risk")]
        public int Step1ProfitTriggerTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Step 1 Stop Loss Ticks", Order = 51, GroupName = "Risk")]
        public int Step1StopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Step 1 Frequency Ticks", Order = 52, GroupName = "Risk")]
        public int Step1FrequencyTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Step 2 Profit Trigger Ticks", Order = 53, GroupName = "Risk")]
        public int Step2ProfitTriggerTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Step 2 Stop Loss Ticks", Order = 54, GroupName = "Risk")]
        public int Step2StopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Step 2 Frequency Ticks", Order = 55, GroupName = "Risk")]
        public int Step2FrequencyTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Step 3 Profit Trigger Ticks", Order = 56, GroupName = "Risk")]
        public int Step3ProfitTriggerTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Step 3 Stop Loss Ticks", Order = 57, GroupName = "Risk")]
        public int Step3StopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Step 3 Frequency Ticks", Order = 58, GroupName = "Risk")]
        public int Step3FrequencyTicks { get; set; }

        #endregion

        #region Export

        [NinjaScriptProperty]
        [Display(Name = "Output Folder Name", Order = 70, GroupName = "Export")]
        public string OutputFolderName { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Output File Prefix", Order = 71, GroupName = "Export")]
        public string OutputFilePrefix { get; set; }

        #endregion
    }
}
