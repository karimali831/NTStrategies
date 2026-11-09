using System.ComponentModel.DataAnnotations;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOvernightEdgePortfolio
    {
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
        [Range(1, 240)]
        [Display(
            Name = "EMA Fast Period",
            GroupName = "2. Indicators",
            Order = 1)]
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
            Order = 2)]
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
        [Range(0, 390)]
        [Display(
            Name = "PDC Early Maximum Minutes From Open",
            Description = "PDC signals at or before this many minutes from the RTH open require the configured minimum premarket width.",
            GroupName = "3. Entry Models",
            Order = 2)]
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
            Order = 3)]
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
            Order = 4)]
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
            Order = 5)]
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
            Order = 6)]
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
            Order = 7)]
        public double RthOpenMinimumPremarketWidthTicks
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0.0, 1000.0)]
        [Display(
            Name = "PML Minimum ATR5 Ticks",
            Description = "PML requires raw 1-minute close above the completed 5-minute EMA Fast.",
            GroupName = "3. Entry Models",
            Order = 8)]
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
            Name = "Enable Daily File Logging",
            Description = "Append diagnostics to daily UTF-8 log files in NinjaTrader user data\\NinjexLogs\\OvernightEdgePortfolio. Independent of NinjaScript Output; creates separate files per strategy run/contract/account.",
            GroupName = "5. Diagnostics",
            Order = 4)]
        public bool EnableFileLogging
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

        [NinjaScriptProperty]
        [Display(
            Name = "Enable Research Session Export",
            Description = "Observational 1-minute candles, 5-minute context and daily coverage, including zero-trade days. Does not alter entries or orders.",
            GroupName = "5. Diagnostics",
            Order = 5)]
        public bool EnableResearchSessionExport
        {
            get;
            set;
        }
        [NinjaScriptProperty]
        [Display(
            Name = "Enable Failed PMH Continuation Research",
            Description = "Shadow-only PMH continuation longs. Requires Research Telemetry; never submits orders or modifies Run 8 signals.",
            GroupName = "5. Diagnostics",
            Order = 6)]
        public bool EnableFailedPmhContinuationResearch { get; set; }

        [NinjaScriptProperty]
        [Range(1, 30)]
        [Display(
            Name = "Failed PMH Confirmation Minutes",
            Description = "Maximum number of minutes after a qualified PMH rejection to confirm a bullish reclaim.",
            GroupName = "6. Failed PMH Research",
            Order = 0)]
        public int FailedPmhConfirmationMinutes { get; set; }

        [NinjaScriptProperty]
        [Range(1, 5)]
        [Display(
            Name = "Failed PMH Consecutive Closes",
            Description = "How many consecutive completed 1-minute closes must hold above PMH plus reclaim ticks.",
            GroupName = "6. Failed PMH Research",
            Order = 1)]
        public int FailedPmhConfirmCloses { get; set; }

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(
            Name = "Failed PMH Reclaim Ticks",
            Description = "Minimum ticks above premarket high on each confirming one-minute close.",
            GroupName = "6. Failed PMH Research",
            Order = 2)]
        public int FailedPmhReclaimTicks { get; set; }

        [NinjaScriptProperty]
        [Display(
            Name = "Failed PMH Require Bullish EMA Context",
            Description = "Requires EMA(9) > EMA(21) and PMH rejection close >= EMA(9), both from completed five-minute context.",
            GroupName = "6. Failed PMH Research",
            Order = 3)]
        public bool FailedPmhRequireBullishEma { get; set; }

        [NinjaScriptProperty]
        [Range(1, 240)]
        [Display(
            Name = "Failed PMH Max Shadow Hold Minutes",
            Description = "Maximum hypothetical holding time from the confirmed long's first causal tick.",
            GroupName = "6. Failed PMH Research",
            Order = 4)]
        public int FailedPmhMaxHoldMinutes { get; set; }

        // Independent PMH V2 retest/hold research. Existing Run 8 + PMH V1 unchanged.
        [NinjaScriptProperty]
        [Display(
            Name = "Enable Failed PMH V2 Retest Research",
            Description = "Opt-in separate shadow research; requires Research Telemetry. Does not submit orders.",
            GroupName = "7. Failed PMH V2 Research",
            Order = 0)]
        public bool EnableFailedPmhV2Research
        {
            get;
            set;
        }

        [NinjaScriptProperty]
        [Range(1, 30)]
        [Display(
            Name = "V2 Reclaim Window Minutes",
            Description = "Maximum minutes after original PMH rejection to close back above PMH.",
            GroupName = "7. Failed PMH V2 Research",
            Order = 1)]
        public int FailedPmhV2ReclaimMinutes
        {
            get;
            set;
        }

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(
            Name = "V2 Reclaim Buffer Ticks",
            Description = "Required ticks above PMH for every reclaim close.",
            GroupName = "7. Failed PMH V2 Research",
            Order = 2)]
        public int FailedPmhV2ReclaimTicks
        {
            get;
            set;
        }

        [NinjaScriptProperty]
        [Range(1, 5)]
        [Display(
            Name = "V2 Consecutive Reclaim Closes",
            Description = "Consecutive completed one-minute closes needed before the retest stage.",
            GroupName = "7. Failed PMH V2 Research",
            Order = 3)]
        public int FailedPmhV2ReclaimCloses
        {
            get;
            set;
        }

        [NinjaScriptProperty]
        [Range(1, 30)]
        [Display(
            Name = "V2 Retest Window Minutes",
            Description = "Maximum minutes after reclaim in which PMH must be retested and held.",
            GroupName = "7. Failed PMH V2 Research",
            Order = 4)]
        public int FailedPmhV2RetestMinutes
        {
            get;
            set;
        }

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(
            Name = "V2 Retest Touch Tolerance Ticks",
            Description = "Low must reach PMH plus this tolerance; retest candle must close >= PMH.",
            GroupName = "7. Failed PMH V2 Research",
            Order = 5)]
        public int FailedPmhV2RetestTouchTicks
        {
            get;
            set;
        }

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(
            Name = "V2 Maximum Wick Breach Ticks",
            Description = "Retest/follow-through wick below PMH by more than this invalidates the candidate.",
            GroupName = "7. Failed PMH V2 Research",
            Order = 6)]
        public int FailedPmhV2MaxBreachTicks
        {
            get;
            set;
        }

        [NinjaScriptProperty]
        [Range(1, 30)]
        [Display(
            Name = "V2 Follow-Through Window Minutes",
            Description = "Maximum minutes after retest for a separate bullish confirmation candle.",
            GroupName = "7. Failed PMH V2 Research",
            Order = 7)]
        public int FailedPmhV2FollowThroughMinutes
        {
            get;
            set;
        }

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(
            Name = "V2 Follow-Through Break Ticks",
            Description = "Bullish confirming close must clear retest high by this many ticks.",
            GroupName = "7. Failed PMH V2 Research",
            Order = 8)]
        public int FailedPmhV2FollowThroughTicks
        {
            get;
            set;
        }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(
            Name = "V2 Minimum EMA Spread Ticks",
            Description = "Parent completed five-minute EMA(9) must exceed EMA(21) by this spread.",
            GroupName = "7. Failed PMH V2 Research",
            Order = 9)]
        public double FailedPmhV2MinEmaSpreadTicks
        {
            get;
            set;
        }

        [NinjaScriptProperty]
        [Range(-100, 100)]
        [Display(
            Name = "V2 Minimum EMA Fast Slope Ticks",
            Description = "Parent completed five-minute EMA(9) slope, in ticks over one context update.",
            GroupName = "7. Failed PMH V2 Research",
            Order = 10)]
        public double FailedPmhV2MinFastSlopeTicks
        {
            get;
            set;
        }

        [NinjaScriptProperty]
        [Range(0.1, 2)]
        [Display(
            Name = "V2 ATR Stop Multiplier",
            Description = "Shadow ATR stop in ticks = ceiling(confirmation ATR ticks times multiplier), bounded 12 to max stop.",
            GroupName = "7. Failed PMH V2 Research",
            Order = 11)]
        public double FailedPmhV2AtrStopFactor
        {
            get;
            set;
        }

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(
            Name = "V2 Retest Structure Stop Buffer Ticks",
            Description = "Extra ticks below retest low for structural stop, bounded by max stop.",
            GroupName = "7. Failed PMH V2 Research",
            Order = 12)]
        public int FailedPmhV2StructureBufferTicks
        {
            get;
            set;
        }

        [NinjaScriptProperty]
        [Range(12, 100)]
        [Display(
            Name = "V2 Maximum Shadow Stop Ticks",
            Description = "Maximum ATR/structure stop in ticks (not a live-order risk rule).",
            GroupName = "7. Failed PMH V2 Research",
            Order = 13)]
        public int FailedPmhV2MaxStopTicks
        {
            get;
            set;
        }

        [NinjaScriptProperty]
        [Range(1, 240)]
        [Display(
            Name = "V2 Max Shadow Hold Minutes",
            Description = "Hypothetical maximum hold after causal entry, also subject to 16:00 flatten.",
            GroupName = "7. Failed PMH V2 Research",
            Order = 14)]
        public int FailedPmhV2MaxHoldMinutes
        {
            get;
            set;
        }


    }
}
