using System;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Ninjex;

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// Execution strategy built from the neutral ES market-research study.
    ///
    /// Expected chart:
    ///     ES 5-minute
    ///     CME US Index Futures ETH
    ///     Chart and collector timestamps must both be US Eastern (ET).
    ///     No timezone conversion is performed.
    ///
    /// Added series:
    ///     BIP 1 = 1-minute signal series
    ///     BIP 2 = 1-tick causal execution series
    ///
    /// Baseline portfolio:
    ///     - LONG prior-day-close reclaim.
    ///     - PMH regime family: strict rejection SHORT or confirmed continuation LONG.
    ///     - SHORT RTH-open breakdown.
    ///     - SHORT premarket-low breakdown.
    ///
    /// Execution:
    ///     - First causal tick after the completed 1-minute signal.
    ///     - Fixed 20-tick stop / 40-tick target.
    ///     - Maximum 60-minute holding period by default.
    ///     - Protective orders are fill-relative.
    ///     - No break-even.
    ///
    /// Portfolio controls:
    ///     - Maximum 3 entries per RTH day.
    ///     - Maximum 2 winning trades per RTH day.
    ///     - Maximum 2 losing trades per RTH day (0 disables the limit).
    /// </summary>
    public partial class NinjexOvernightEdgePortfolio : Strategy
    {
        private const string StrategyVersion = "1.5.1-pmh-regime-baseline";

        private const int ContextSeriesIndex = 0;
        private const int SignalSeriesIndex = 1;
        private const int TickSeriesIndex = 2;

        private const string PriorCloseEntrySignal = "PDC-RECLAIM-L";
        private const string PremarketHighEntrySignal = "PMH-REJECT-S";
        private const string RthOpenEntrySignal = "RTHOPEN-BREAK-S";
        private const string PremarketLowEntrySignal = "PML-BREAK-S";

        private const string PriorCloseModelName =
            "Prior-day-close reclaim";

        private const string PremarketHighModelName =
            "Premarket-high sweep/rejection";

        private const string RthOpenModelName =
            "RTH-open breakdown";

        private const string PremarketLowModelName =
            "Premarket-low breakdown";

        private const string LongEodExitSignal = "EOD-L";
        private const string ShortEodExitSignal = "EOD-S";

        private const string LongTimeExitSignal = "TIME-L";
        private const string ShortTimeExitSignal = "TIME-S";


        private enum PendingDirection
        {
            None,
            Long,
            Short
        }


        #region Engines / indicators

        private NinjexPremarketRangeEngine overnightRangeEngine;
        private NinjexPremarketRangeEngine premarketRangeEngine;

        private ATR atr5m;
        private EMA emaFast5m;
        private EMA emaSlow5m;

        #endregion


        #region Overnight range state

        private DateTime overnightRangeDate =
            Core.Globals.MinDate;

        private double overnightHigh =
            double.NaN;

        private double overnightLow =
            double.NaN;

        private int overnightBars;

        private bool overnightRangeReady;

        private DateTime premarketRangeDate =
            Core.Globals.MinDate;

        private double premarketHigh =
            double.NaN;

        private double premarketLow =
            double.NaN;

        private int premarketBars;

        private bool premarketRangeReady;

        private DateTime currentRthReferenceDate =
            Core.Globals.MinDate;

        private double currentRthLastClose =
            double.NaN;

        private DateTime priorDayCloseDate =
            Core.Globals.MinDate;

        private double priorDayClose =
            double.NaN;

        private DateTime rthOpenDate =
            Core.Globals.MinDate;

        private double rthOpen =
            double.NaN;

        #endregion


        #region Completed 5-minute context

        private DateTime last5mTime =
            Core.Globals.MinDate;

        private double last5mAtrTicks =
            double.NaN;

        private double last5mEmaFast =
            double.NaN;

        private double last5mEmaSlow =
            double.NaN;

        #endregion


        #region Pending causal entry

        private PendingDirection pendingDirection =
            PendingDirection.None;

        private string pendingModelName =
            string.Empty;

        private string pendingEntrySignal =
            string.Empty;

        private DateTime pendingSignalTime =
            Core.Globals.MinDate;

        private DateTime pendingEarliestExecutionTime =
            Core.Globals.MinDate;

        private double pendingSignalClose =
            double.NaN;

        private double pendingSignalHigh =
            double.NaN;

        private double pendingSignalLow =
            double.NaN;

        private double pendingAtr5mTicks =
            double.NaN;

        private double pendingEmaSlow5m =
            double.NaN;

        private double pendingEmaFast5m =
            double.NaN;

        private double pendingOvernightWidthTicks =
            double.NaN;

        private double pendingPremarketWidthTicks =
            double.NaN;

        private int pendingMinutesFromOpen;

        #endregion


        #region Order / trade state

        private bool entryOrderPending;
        private bool manualExitPending;

        private Order activeEntryOrder;

        private bool activeTradeCounted;
        private PendingDirection activeTradeDirection =
            PendingDirection.None;

        private string activeModelName =
            string.Empty;

        private string activeEntrySignal =
            string.Empty;

        private DateTime submittedSignalTime =
            Core.Globals.MinDate;

        private DateTime activeMaxHoldExitTime =
            Core.Globals.MinDate;

        private int activeEntryFilledQuantity;
        private double activeEntryPriceQuantity;

        private double activeTradeGrossPnl;

        #endregion


        #region Daily portfolio state

        private DateTime activeTradingDate =
            Core.Globals.MinDate;

        private int tradesToday;
        private int winnersToday;
        private int lossesToday;

        private double grossPnlToday;

        #endregion


        #region NinjaScript lifecycle

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name =
                    "Ninjex Overnight Edge Portfolio";

                Description =
                    "ES overnight-edge baseline: PDC reclaim, PMH regime split, RTH-open breakdown and PML breakdown.";

                Calculate =
                    Calculate.OnEachTick;

                EntriesPerDirection = 1;

                EntryHandling =
                    EntryHandling.AllEntries;

                IsExitOnSessionCloseStrategy =
                    false;

                ExitOnSessionCloseSeconds = 30;

                StartBehavior =
                    StartBehavior.WaitUntilFlat;

                RealtimeErrorHandling =
                    RealtimeErrorHandling.StopCancelClose;

                StopTargetHandling =
                    StopTargetHandling.PerEntryExecution;

                BarsRequiredToTrade = 30;

                IsInstantiatedOnEachOptimizationIteration =
                    false;


                //
                // Session / range
                //
                OvernightStartTime = 180000;
                PremarketStartTime = 30000;
                MarketOpenTime = 93000;

                EntryStartTime = 93500;
                EntryEndTime = 160000;

                FlattenTime = 160000;

                RequireCompleteOvernightRange = true;
                ExpectedOvernightBars = 186;

                RequireCompletePremarketRange = true;
                ExpectedPremarketBars = 78;


                //
                // Indicators
                //
                AtrPeriod = 14;
                EmaFastPeriod = 9;
                EmaSlowPeriod = 21;


                //
                // Entry models
                //
                PriorCloseMaximumRangeTicks = 30.0;
                PriorCloseMinimumOvernightWidthTicks = 200.0;
                PdcEarlyMaximumMinutesFromOpen = 60;
                PdcEarlyMinimumPremarketWidthTicks = 140.0;

                PremarketHighMaximumMinutesFromOpen = 120;
                PremarketHighMinimumAtr5mTicks = 30.0;
                PmhShortMaximumAtrTicks = 45.0;
                PmhShortMaximumFastEmaDistanceTicks = 0.0;
                PmhContinuationReclaimMinutes = 10;
                PmhContinuationReclaimTicks = 2;
                PmhContinuationReclaimCloses = 2;
                PmhContinuationRetestMinutes = 10;
                PmhContinuationRetestTouchTicks = 6;
                PmhContinuationMaxBreachTicks = 6;
                PmhContinuationFollowThroughMinutes = 7;
                PmhContinuationFollowThroughTicks = 0;
                PmhContinuationMinEmaSpreadTicks = 0.0;
                PmhContinuationMinFastSlopeTicks = 0.0;
                PmhContinuationStructureBufferTicks = 2;
                PmhContinuationMaxStopTicks = 30;

                RthOpenMinimumMinutesFromOpen = 120;
                RthOpenMinimumPremarketWidthTicks = 140.0;
                PremarketLowMinimumAtr5mTicks = 20.0;

                //
                // Portfolio risk
                //
                OrderQuantity = 1;
                StopLossTicks = 20;
                ProfitTargetTicks = 40;
                MaxHoldMinutes = 60;
                MaxTradesPerDay = 3;
                MaxWinnersPerDay = 2;
                MaxLossesPerDay = 2;

                //
                // Operations / research
                //
                EnableRestartRecovery = true;
                EnableFileLogging = true;
                EnableDiagnostics = false;
                MirrorVerifiedLiveExecutions = false;
                EnableResearchTelemetry = false;
                EnableResearchSessionExport = false;
                EnableResearchPerturbationScenarios = false;
            }
            else if (State == State.Configure)
            {
                //
                // BIP 1: completed 1-minute signals.
                //
                AddDataSeries(
                    BarsPeriodType.Minute,
                    1);

                //
                // BIP 2: causal tick execution.
                //
                AddDataSeries(
                    BarsPeriodType.Tick,
                    1);
            }
            else if (State == State.DataLoaded)
            {
                overnightRangeEngine =
                    new NinjexPremarketRangeEngine();

                premarketRangeEngine =
                    new NinjexPremarketRangeEngine();

                atr5m =
                    ATR(
                        Closes[ContextSeriesIndex],
                        AtrPeriod);

                emaFast5m =
                    EMA(
                        Closes[ContextSeriesIndex],
                        EmaFastPeriod);

                emaSlow5m =
                    EMA(
                        Closes[ContextSeriesIndex],
                        EmaSlowPeriod);

                InitializeResearchSessionExport();
                InitializeResearchTelemetry();
                InitializeResearchPortfolioDrawdown();
                InitializePmhRegime();

                Diagnostic(
                    DateTime.Now,
                    "READY Version={0} Stop={1}t Target={2}t MaxHold={3}m " +
                    "Caps={4}/{5}/{6} PdcEarlyMaxMinutes={7} " +
                    "PdcEarlyMinPmWidth={8:0.0}t MirrorVerifiedLive={9} " +
                    "ResearchTelemetry={10} Perturbation={11} RestartRecovery={12}",
                    StrategyVersion,
                    StopLossTicks,
                    ProfitTargetTicks,
                    MaxHoldMinutes,
                    MaxTradesPerDay,
                    MaxWinnersPerDay,
                    MaxLossesPerDay,
                    PdcEarlyMaximumMinutesFromOpen,
                    PdcEarlyMinimumPremarketWidthTicks,
                    MirrorVerifiedLiveExecutions,
                    EnableResearchTelemetry,
                    EnableResearchPerturbationScenarios,
                    EnableRestartRecovery);
            }
            else if (State == State.Realtime)
            {
                ResetLiveDailyStateRecoveryOnRealtimeTransition();
            }
            else if (State == State.Terminated)
            {
                DisposeResearchPortfolioDrawdown();
                DisposeResearchTelemetry();
                DisposeResearchSessionExport();
                CloseDiagnosticFile();
            }
        }


        protected override void OnBarUpdate()
        {
            if (BarsInProgress == ContextSeriesIndex)
            {
                ProcessContextSeries();
                return;
            }

            if (BarsInProgress == SignalSeriesIndex)
            {
                ProcessSignalSeries();
                return;
            }

            if (BarsInProgress == TickSeriesIndex)
            {
                ProcessTickSeries();
            }
        }

        #endregion


    }
}
