using System.ComponentModel.DataAnnotations;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOvernightEdgePortfolio
    {
        #region 1. Session & Data

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Overnight Start Time", GroupName = "1. Session & Data", Order = 0)]
        public int OvernightStartTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Premarket Start Time", GroupName = "1. Session & Data", Order = 1)]
        public int PremarketStartTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Market Open Time", GroupName = "1. Session & Data", Order = 2)]
        public int MarketOpenTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Entry Start Time", GroupName = "1. Session & Data", Order = 3)]
        public int EntryStartTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Entry End Time", GroupName = "1. Session & Data", Order = 4)]
        public int EntryEndTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Flatten Time", GroupName = "1. Session & Data", Order = 5)]
        public int FlattenTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require Complete Overnight Range", GroupName = "1. Session & Data", Order = 6)]
        public bool RequireCompleteOvernightRange { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(
            Name = "Expected Overnight Bars",
            Description = "Expected completed 5-minute bars in a normal 18:00-09:30 overnight range.",
            GroupName = "1. Session & Data",
            Order = 7)]
        public int ExpectedOvernightBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require Complete Premarket Range", GroupName = "1. Session & Data", Order = 8)]
        public bool RequireCompletePremarketRange { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(
            Name = "Expected Premarket Bars",
            Description = "Expected completed 5-minute bars in the normal 03:00-09:30 premarket range.",
            GroupName = "1. Session & Data",
            Order = 9)]
        public int ExpectedPremarketBars { get; set; }

        #endregion

        #region 2. Indicators

        [NinjaScriptProperty]
        [Range(1, 240)]
        [Display(Name = "ATR Period", GroupName = "2. Indicators", Order = 0)]
        public int AtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, 240)]
        [Display(Name = "EMA Fast Period", GroupName = "2. Indicators", Order = 1)]
        public int EmaFastPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, 240)]
        [Display(Name = "EMA Slow Period", GroupName = "2. Indicators", Order = 2)]
        public int EmaSlowPeriod { get; set; }

        #endregion

        #region 3. PDC Reclaim

        [NinjaScriptProperty]
        [Range(1.0, 1000.0)]
        [Display(Name = "Maximum 1-Minute Range Ticks", GroupName = "3. PDC Reclaim", Order = 0)]
        public double PriorCloseMaximumRangeTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 5000.0)]
        [Display(Name = "Minimum Overnight Width Ticks", GroupName = "3. PDC Reclaim", Order = 1)]
        public double PriorCloseMinimumOvernightWidthTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 390)]
        [Display(
            Name = "Early Maximum Minutes From Open",
            Description = "PDC signals at or before this many minutes from the RTH open require the configured minimum premarket width.",
            GroupName = "3. PDC Reclaim",
            Order = 2)]
        public int PdcEarlyMaximumMinutesFromOpen { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 5000.0)]
        [Display(
            Name = "Early Minimum Premarket Width Ticks",
            Description = "Minimum premarket width required for PDC entries inside the configured early-RTH window.",
            GroupName = "3. PDC Reclaim",
            Order = 3)]
        public double PdcEarlyMinimumPremarketWidthTicks { get; set; }

        #endregion

        #region 4. PMH Regime

        [NinjaScriptProperty]
        [Range(0, 390)]
        [Display(Name = "Maximum Minutes From Open", GroupName = "4. PMH Regime", Order = 0)]
        public int PremarketHighMaximumMinutesFromOpen { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 1000.0)]
        [Display(Name = "Minimum ATR5 Ticks", GroupName = "4. PMH Regime", Order = 1)]
        public double PremarketHighMinimumAtr5mTicks { get; set; }

        [NinjaScriptProperty]
        [Range(30.0, 200.0)]
        [Display(
            Name = "Short Maximum ATR5 Ticks",
            Description = "Strict PMH rejection short requires completed 5-minute ATR below this value.",
            GroupName = "4. PMH Regime",
            Order = 2)]
        public double PmhShortMaximumAtrTicks { get; set; }

        [NinjaScriptProperty]
        [Range(-100.0, 100.0)]
        [Display(
            Name = "Short Maximum Fast EMA Distance Ticks",
            Description = "Maximum signal-close distance above completed EMA(9). Default 0 requires close at or below EMA(9).",
            GroupName = "4. PMH Regime",
            Order = 3)]
        public double PmhShortMaximumFastEmaDistanceTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 30)]
        [Display(Name = "Continuation Reclaim Window Minutes", GroupName = "4. PMH Regime", Order = 4)]
        public int PmhContinuationReclaimMinutes { get; set; }

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "Continuation Reclaim Buffer Ticks", GroupName = "4. PMH Regime", Order = 5)]
        public int PmhContinuationReclaimTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 5)]
        [Display(Name = "Continuation Consecutive Reclaim Closes", GroupName = "4. PMH Regime", Order = 6)]
        public int PmhContinuationReclaimCloses { get; set; }

        [NinjaScriptProperty]
        [Range(1, 30)]
        [Display(Name = "Continuation Retest Window Minutes", GroupName = "4. PMH Regime", Order = 7)]
        public int PmhContinuationRetestMinutes { get; set; }

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "Continuation Retest Touch Tolerance Ticks", GroupName = "4. PMH Regime", Order = 8)]
        public int PmhContinuationRetestTouchTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "Continuation Maximum Wick Breach Ticks", GroupName = "4. PMH Regime", Order = 9)]
        public int PmhContinuationMaxBreachTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 30)]
        [Display(Name = "Continuation Follow-Through Window Minutes", GroupName = "4. PMH Regime", Order = 10)]
        public int PmhContinuationFollowThroughMinutes { get; set; }

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "Continuation Follow-Through Break Ticks", GroupName = "4. PMH Regime", Order = 11)]
        public int PmhContinuationFollowThroughTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 1000.0)]
        [Display(Name = "Continuation Minimum EMA Spread Ticks", GroupName = "4. PMH Regime", Order = 12)]
        public double PmhContinuationMinEmaSpreadTicks { get; set; }

        [NinjaScriptProperty]
        [Range(-100.0, 100.0)]
        [Display(Name = "Continuation Minimum Fast EMA Slope Ticks", GroupName = "4. PMH Regime", Order = 13)]
        public double PmhContinuationMinFastSlopeTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "Continuation Structure Stop Buffer Ticks", GroupName = "4. PMH Regime", Order = 14)]
        public int PmhContinuationStructureBufferTicks { get; set; }

        [NinjaScriptProperty]
        [Range(8, 100)]
        [Display(Name = "Continuation Maximum Stop Ticks", GroupName = "4. PMH Regime", Order = 15)]
        public int PmhContinuationMaxStopTicks { get; set; }

        #endregion

        #region 5. RTH & PML

        [NinjaScriptProperty]
        [Range(0, 390)]
        [Display(Name = "RTH Open Minimum Minutes From Open", GroupName = "5. RTH & PML", Order = 0)]
        public int RthOpenMinimumMinutesFromOpen { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 5000.0)]
        [Display(Name = "RTH Open Minimum Premarket Width Ticks", GroupName = "5. RTH & PML", Order = 1)]
        public double RthOpenMinimumPremarketWidthTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 1000.0)]
        [Display(
            Name = "PML Minimum ATR5 Ticks",
            Description = "PML requires raw 1-minute close above the completed 5-minute EMA(9).",
            GroupName = "5. RTH & PML",
            Order = 2)]
        public double PremarketLowMinimumAtr5mTicks { get; set; }

        #endregion

        #region 6. Portfolio Risk

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Order Quantity", GroupName = "6. Portfolio Risk", Order = 0)]
        public int OrderQuantity { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "Standard Stop Loss Ticks", GroupName = "6. Portfolio Risk", Order = 1)]
        public int StopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 5000)]
        [Display(Name = "Standard Profit Target Ticks", GroupName = "6. Portfolio Risk", Order = 2)]
        public int ProfitTargetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 390)]
        [Display(
            Name = "Maximum Hold Minutes",
            Description = "Maximum minutes from completed signal to market exit. 0 disables.",
            GroupName = "6. Portfolio Risk",
            Order = 3)]
        public int MaxHoldMinutes { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Maximum Trades Per Day", Description = "0 disables.", GroupName = "6. Portfolio Risk", Order = 4)]
        public int MaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Maximum Winners Per Day", Description = "0 disables.", GroupName = "6. Portfolio Risk", Order = 5)]
        public int MaxWinnersPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Maximum Losing Trades Per Day", Description = "0 disables.", GroupName = "6. Portfolio Risk", Order = 6)]
        public int MaxLossesPerDay { get; set; }

        #endregion

        #region 7. Operations

        [NinjaScriptProperty]
        [Display(
            Name = "Enable Restart Recovery",
            Description = "Persist/restore daily trade counters across NinjaTrader/VPS restarts. Applies to live trading and Playback restart simulation.",
            GroupName = "7. Operations",
            Order = 0)]
        public bool EnableRestartRecovery { get; set; }

        [NinjaScriptProperty]
        [Display(
            Name = "Enable Daily File Logging",
            Description = "Append diagnostics to daily UTF-8 files in NinjaTrader user data\\NinjexLogs\\OvernightEdgePortfolio.",
            GroupName = "7. Operations",
            Order = 1)]
        public bool EnableFileLogging { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable NinjaScript Output Diagnostics", GroupName = "7. Operations", Order = 2)]
        public bool EnableDiagnostics { get; set; }

        #endregion

        #region 8. Research & Replay

        [NinjaScriptProperty]
        [Display(
            Name = "Mirror Verified Live Executions",
            Description = "Forensic Market Replay only. Applies independently verified live-data/fill corrections. Leave false for live trading and clean research/backtests.",
            GroupName = "8. Research & Replay",
            Order = 0)]
        public bool MirrorVerifiedLiveExecutions { get; set; }

        [NinjaScriptProperty]
        [Display(
            Name = "Enable Research Telemetry",
            Description = "Write candidate/trade research rows. Observational only.",
            GroupName = "8. Research & Replay",
            Order = 1)]
        public bool EnableResearchTelemetry { get; set; }

        [NinjaScriptProperty]
        [Display(
            Name = "Enable Research Session Export",
            Description = "Export 1-minute bars, 5-minute context and daily coverage including zero-trade days.",
            GroupName = "8. Research & Replay",
            Order = 2)]
        public bool EnableResearchSessionExport { get; set; }

        [NinjaScriptProperty]
        [Display(
            Name = "Enable Research Perturbation Scenarios",
            Description = "Tick-level research scenarios for entry/target/break-even sensitivity. Requires Research Telemetry and never changes real orders.",
            GroupName = "8. Research & Replay",
            Order = 3)]
        public bool EnableResearchPerturbationScenarios { get; set; }

        #endregion
    }
}
