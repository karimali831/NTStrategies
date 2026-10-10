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
    /// Run 8 four-model portfolio:
    ///     - LONG prior-day-close reclaim, 1-minute range <= 30 ticks,
    ///       Overnight width >= 200 ticks. During the first 60 minutes,
    ///       Premarket width must be >= 140 ticks by default.
    ///     - SHORT premarket-high sweep/rejection in the first 120 minutes,
    ///       completed 5-minute ATR >= 30 ticks.
    ///     - SHORT RTH-open breakdown from 120 minutes after the open,
    ///       Premarket width >= 140 ticks.
    ///     - SHORT premarket-low breakdown, completed 5-minute ATR >= 20 ticks.
    ///       Requires close > completed 5-minute EMA(9) when the EMA filter is enabled.
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
        private const string StrategyVersion = "1.4.0-run8-production";

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
                    "Run 8 ES overnight-edge portfolio: PDC reclaim, PMH rejection, RTH-open breakdown and PML breakdown.";

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
                EnableEMAFilter = true;
                EmaFastPeriod = 9;
                EmaSlowPeriod = 21;


                //
                // Four-model portfolio
                //
                PriorCloseMaximumRangeTicks = 30.0;
                PriorCloseMinimumOvernightWidthTicks = 200.0;
                EnablePdcEarlyPremarketWidthFilter = true;
                PdcEarlyMaximumMinutesFromOpen = 60;
                PdcEarlyMinimumPremarketWidthTicks = 140.0;

                PremarketHighMaximumMinutesFromOpen = 120;
                PremarketHighMinimumAtr5mTicks = 30.0;

                RthOpenMinimumMinutesFromOpen = 120;
                RthOpenMinimumPremarketWidthTicks = 140.0;

                PremarketLowMinimumAtr5mTicks = 20.0;

                //
                // Risk
                //
                OrderQuantity = 1;

                StopLossTicks = 20;
                ProfitTargetTicks = 40;

                MaxHoldMinutes = 60;

                MaxTradesPerDay = 3;
                MaxWinnersPerDay = 2;

                MaxLossesPerDay = 2;


                //
                // Diagnostics
                //
                EnableDiagnostics = true;
                EnableResearchTelemetry = false;
                EnableResearchPerturbationScenarios = false;

                // Safety function. Persist and restore the authoritative
                // daily trade counters across strategy/NT/VPS restarts. Applies
                // to genuine live trading and Playback live-restart simulation.
                EnableRestartRecovery = true;

                // Disabled by default. Forensic Replay/Historical mirror
                // for independently verified live discrepancies only.
                MirrorVerifiedLiveExecutions = false;
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

                InitializeResearchTelemetry();

                Diagnostic(
                    DateTime.Now,
                    "READY Version={0} Stop={1}t Target={2}t MaxHold={3}m " +
                    "Caps={4}/{5}/{6} PdcEarlyFilter={7} PdcEarlyMaxMinutes={8} " +
                    "PdcEarlyMinPmWidth={9:0.0}t MirrorVerifiedLive={10} " +
                    "ResearchTelemetry={11} Perturbation={12} RestartRecovery={13}",
                    StrategyVersion,
                    StopLossTicks,
                    ProfitTargetTicks,
                    MaxHoldMinutes,
                    MaxTradesPerDay,
                    MaxWinnersPerDay,
                    MaxLossesPerDay,
                    EnablePdcEarlyPremarketWidthFilter,
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
                DisposeResearchTelemetry();
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
