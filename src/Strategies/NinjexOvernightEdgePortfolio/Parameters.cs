using System.ComponentModel.DataAnnotations;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOvernightEdgePortfolio
    {
        #region Properties

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(
            Name = "Overnight Start Time",
            GroupName = "1. Session",
            Order = 0)]
        public int OvernightStartTime
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(
            Name = "Premarket Start Time",
            GroupName = "1. Session",
            Order = 1)]
        public int PremarketStartTime
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(
            Name = "Market Open Time",
            GroupName = "1. Session",
            Order = 2)]
        public int MarketOpenTime
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(
            Name = "Entry Start Time",
            GroupName = "1. Session",
            Order = 3)]
        public int EntryStartTime
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(
            Name = "Entry End Time",
            GroupName = "1. Session",
            Order = 4)]
        public int EntryEndTime
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(
            Name = "Flatten Time",
            GroupName = "1. Session",
            Order = 5)]
        public int FlattenTime
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Display(
            Name = "Require Complete Overnight Range",
            GroupName = "1. Session",
            Order = 6)]
        public bool RequireCompleteOvernightRange
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(
            Name = "Expected Overnight Bars",
            Description = "Expected completed 5-minute bars in a normal 18:00-09:30 overnight range.",
            GroupName = "1. Session",
            Order = 7)]
        public int ExpectedOvernightBars
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Display(
            Name = "Require Complete Premarket Range",
            GroupName = "1. Session",
            Order = 8)]
        public bool RequireCompletePremarketRange
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(
            Name = "Expected Premarket Bars",
            Description = "Expected completed 5-minute bars in the normal 03:00-09:30 premarket range.",
            GroupName = "1. Session",
            Order = 9)]
        public int ExpectedPremarketBars
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(1, 240)]
        [Display(
            Name = "ATR Period",
            GroupName = "2. Indicators",
            Order = 0)]
        public int AtrPeriod
        {
            get;
            set;
        } = 14;

        [NinjaScriptProperty]
        [Display(
            Name = "Enable EMA Filter",
            GroupName = "2. Indicators",
            Order = 1)]
        public bool EnableEMAFilter
        {
            get;
            set;
        }

        [NinjaScriptProperty]
        [Range(1, 240)]
        [Display(
            Name = "EMA Fast Period",
            GroupName = "2. Indicators",
            Order = 2)]
        public int EmaFastPeriod
        {
            get;
            set;
        } = 9;


        [NinjaScriptProperty]
        [Range(1, 240)]
        [Display(
            Name = "EMA Slow Period",
            GroupName = "2. Indicators",
            Order = 3)]
        public int EmaSlowPeriod
        {
            get;
            set;
        } = 21;


        [NinjaScriptProperty]
        [Range(1.0, 1000.0)]
        [Display(
            Name = "PDC Maximum 1-Minute Range Ticks",
            GroupName = "3. Entry Models",
            Order = 0)]
        public double PriorCloseMaximumRangeTicks
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0.0, 5000.0)]
        [Display(
            Name = "PDC Minimum Overnight Width Ticks",
            GroupName = "3. Entry Models",
            Order = 1)]
        public double PriorCloseMinimumOvernightWidthTicks
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Display(
            Name = "Enable PDC Early Premarket Width Filter",
            Description = "When enabled, PDC signals during the configured early-RTH window require the premarket range width to meet the configured minimum.",
            GroupName = "3. Entry Models",
            Order = 2)]
        public bool EnablePdcEarlyPremarketWidthFilter
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0, 390)]
        [Display(
            Name = "PDC Early Maximum Minutes From Open",
            Description = "PDC signals at or before this many minutes from the RTH open are subject to the early premarket-width filter.",
            GroupName = "3. Entry Models",
            Order = 3)]
        public int PdcEarlyMaximumMinutesFromOpen
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0.0, 5000.0)]
        [Display(
            Name = "PDC Early Minimum Premarket Width Ticks",
            Description = "Minimum premarket range width required for PDC entries inside the configured early-RTH window.",
            GroupName = "3. Entry Models",
            Order = 4)]
        public double PdcEarlyMinimumPremarketWidthTicks
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0, 390)]
        [Display(
            Name = "PMH Maximum Minutes From Open",
            GroupName = "3. Entry Models",
            Order = 5)]
        public int PremarketHighMaximumMinutesFromOpen
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0.0, 1000.0)]
        [Display(
            Name = "PMH Minimum ATR5 Ticks",
            GroupName = "3. Entry Models",
            Order = 6)]
        public double PremarketHighMinimumAtr5mTicks
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0, 390)]
        [Display(
            Name = "RTH Open Minimum Minutes From Open",
            GroupName = "3. Entry Models",
            Order = 7)]
        public int RthOpenMinimumMinutesFromOpen
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0.0, 5000.0)]
        [Display(
            Name = "RTH Open Minimum Premarket Width Ticks",
            GroupName = "3. Entry Models",
            Order = 8)]
        public double RthOpenMinimumPremarketWidthTicks
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0.0, 1000.0)]
        [Display(
            Name = "PML Minimum ATR5 Ticks",
            Description = "When EMA filtering is enabled, PML requires raw 1-minute close above the completed 5-minute EMA Fast.",
            GroupName = "3. Entry Models",
            Order = 9)]
        public double PremarketLowMinimumAtr5mTicks
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(
            Name = "Order Quantity",
            GroupName = "4. Risk",
            Order = 0)]
        public int OrderQuantity
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(
            Name = "Stop Loss Ticks",
            GroupName = "4. Risk",
            Order = 1)]
        public int StopLossTicks
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(1, 5000)]
        [Display(
            Name = "Profit Target Ticks",
            GroupName = "4. Risk",
            Order = 2)]
        public int ProfitTargetTicks
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0, 390)]
        [Display(
            Name = "Max Hold Minutes",
            Description = "Maximum minutes from the completed signal to a market exit. 0 disables this limit; 60 mirrors the research horizon.",
            GroupName = "4. Risk",
            Order = 3)]
        public int MaxHoldMinutes
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(
            Name = "Max Trades Per Day",
            Description = "0 disables this limit.",
            GroupName = "4. Risk",
            Order = 4)]
        public int MaxTradesPerDay
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(
            Name = "Max Winners Per Day",
            Description = "0 disables this limit.",
            GroupName = "4. Risk",
            Order = 5)]
        public int MaxWinnersPerDay
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(
            Name = "Max Losing Trades Per Day",
            Description = "Maximum completed losing trades per ET trading date. 0 disables this limit; 1 stops after the first loss; 2 stops after the second loss. Losses use gross trade PnL, matching the winner counter.",
            GroupName = "4. Risk",
            Order = 6)]
        public int MaxLossesPerDay
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Display(
            Name = "Enable Diagnostics",
            GroupName = "5. Diagnostics",
            Order = 0)]
        public bool EnableDiagnostics
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Display(
            Name = "Mirror Verified Live Executions",
            Description = "Replay/Historical forensic mode. Applies only date/time corrections independently verified from live evidence (currently 2026-09-24 RTH open and 2026-09-30 PMH bracket). Disabled by default.",
            GroupName = "5. Diagnostics",
            Order = 1)]
        public bool MirrorVerifiedLiveExecutions
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Display(
            Name = "Enable Restart Recovery",
            Description = "Safety function. Persists and restores the authoritative daily trade counters across strategy, NinjaTrader, or VPS restarts. Applies to live trading and Playback restart simulation.",
            GroupName = "4. Risk",
            Order = 7)]
        public bool EnableRestartRecovery
        {
            get;
            set;
        }

        [NinjaScriptProperty]
        [Display(
            Name = "Enable Research Telemetry",
            Description = "Writes candidate-signal and trade research rows to the NinjaTrader user-data NinjexResearch\\OvernightEdgePortfolio folder. Observational only; does not change entry/exit logic.",
            GroupName = "5. Diagnostics",
            Order = 2)]
        public bool EnableResearchTelemetry
        {
            get;
            set;
        }

        [NinjaScriptProperty]
        [Display(
            Name = "Enable Research Perturbation Scenarios",
            Description = "Research-only tick-level scenarios for entry fill sensitivity, target distance and break-even policies. Requires Research Telemetry. Never submits or changes real orders.",
            GroupName = "5. Diagnostics",
            Order = 3)]
        public bool EnableResearchPerturbationScenarios
        {
            get;
            set;
        }


        #endregion

    }
}
