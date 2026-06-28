#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Strategies;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class BaselineTrendPullback : Strategy
    {
        private const string SIG_LONG  = "L-PB";
        private const string SIG_SHORT = "S-PB";

        private EMA emaFast;
        private EMA emaSlow;
        private ADX adx;
        private ATR atr;

        private BaselineLogger _log;
        private DateTime _lastSessionDate = NinjaTrader.Core.Globals.MinDate;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                    = "BaselineTrendPullback";
                Description             = "Baseline EMA-structure pullback strategy with strict risk mgmt and structured logging.";
                Calculate               = Calculate.OnBarClose;
                EntriesPerDirection     = 1;
                EntryHandling           = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds    = 30;
                IsInstantiatedOnEachOptimizationIteration = false;
                
                UseBreakoutTrigger = false;
                // ----- Parameter groups -----

                // Indicators
                EmaFastPeriod = 14;
                EmaSlowPeriod = 50;
                AdxPeriod     = 14;
                AtrPeriod     = 14;

                // Trend / structure
                TrendSlopeLookbackBars = 12;
                MinTrendSlopeTicks     = 5;     // slope in ticks over lookback
                MinEmaSepTicks         = 3;     // separation between EMAs in ticks

                // Regime (minimal)
                MinAdx = 15;
                MaxAdx = 40;
                AtrMedianLookbackBars = 0;     // "ATR above rolling median" check

                // Pullback / trigger
                TouchLookbackBars = 6;          // how far back we consider "touched"
                TouchTicks        = 4;          // proximity in ticks to fast EMA
                RequireCloseBackAcrossFastEma = true;
                RequireSignalCandleInTrendDir = true;

                // Candle quality
                MaxWickPct      =  0.90;         // (range - body) / range
                MinRangeTicks   = 8;            // ignore tiny bars

                // Risk
                RiskMode                = BaselineRiskMode.SwingWithCap;
                SwingLookbackBars       = 3;
                StopBufferTicks         = 2;
                MaxStopTicks            = 60;
                ProfitTargetR           = 2.0;   // target = R * stopTicks
                UseBreakEven            = true;
                BreakEvenAtR            = 1.0;   // move stop to BE after 1R
                BreakEvenPlusTicks      = 1;

                // Session & trade limits
                UseTimeWindow           = true;
                StartTimeHHmm           = 0930; 
                EndTimeHHmm             = 1600;
                MaxTradesPerSession     = 4;

                // Logging
                DiagEnabled             = true;
                LogOnlyOnSignalOrBlock  = true; // if false, logs every bar (huge)
                LogToFile               = true;
                LogFileNamePrefix       = "BaselineTrendPullback";
            }
            else if (State == State.Configure)
            {
                // Baseline expects a single primary series (apply to 5-minute chart).
                // If you add additional series later, keep the decision logic on BarsInProgress == 0.
            }
            else if (State == State.DataLoaded)
            {
                emaFast = EMA(EmaFastPeriod);
                emaSlow = EMA(EmaSlowPeriod);
                adx     = ADX(AdxPeriod);
                atr     = ATR(AtrPeriod);

                // Optional: show on chart
                AddChartIndicator(emaFast);
                AddChartIndicator(emaSlow);

                _log = new BaselineLogger(
                    this,
                    Name,
                    Instrument?.FullName ?? "",
                    LogToFile,
                    LogFileNamePrefix);

                _log.Info("INIT", new Dictionary<string, object>
                {
                    ["calculate"] = Calculate.ToString(),
                    ["barsPeriod"] = BarsPeriod?.ToString() ?? "",
                    ["tickSize"] = TickSize.ToString(CultureInfo.InvariantCulture)
                });
            }
            else if (State == State.Terminated)
            {
                _log?.Dispose();
                _log = null;
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            if (CurrentBar < Math.Max(TrendSlopeLookbackBars, Math.Max(EmaSlowPeriod, AtrMedianLookbackBars)) + 5)
                return;

            // Reset per-session counters
            ResetSessionIfNeeded();

            // Hard constraints
            if (UseTimeWindow && !IsWithinTimeWindow(Time[0], StartTimeHHmm, EndTimeHHmm))
            {
                MaybeLogBar("BLOCK", "outside-time-window");
                return;
            }

            if (TradesThisSession >= MaxTradesPerSession)
            {
                MaybeLogBar("BLOCK", "max-trades-per-session");
                return;
            }

            // Compute core features (single place to avoid duplication)
            var f = ComputeFeatures(barsAgo: 0);

            // Optional per-bar diagnostic logging (usually OFF)
            if (!LogOnlyOnSignalOrBlock && DiagEnabled)
            {
                _log.Bar("BAR", f.ToLogMap(extraReason: "none"));
            }

            // Do not open new position if already in one
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                ManageOpenPosition(f);
                return;
            }

            // Evaluate entries
            EvaluateEntries(f);
        }

        private void EvaluateEntries(BarFeatures f)
        {
            // Regime filter
            if (!f.RegimeOk)
            {
                MaybeLogSignalOrBlock("BLOCK", f.RegimeFailReason, f);
                return;
            }

            // Trend / structure
            if (!f.StructureOk)
            {
                MaybeLogSignalOrBlock("BLOCK", f.StructureFailReason, f);
                return;
            }

            // Candle quality
            if (!f.CandleOk)
            {
                MaybeLogSignalOrBlock("BLOCK", f.CandleFailReason, f);
                return;
            }

            // Pullback touch + trigger
            if (f.LongBias)
            {
                if (!TouchedFastEma(longSide: true))
                {
                    // no log (too chatty)
                    return;
                }

                if (!LongTriggerOk(f))
                {
                    MaybeLogSignalOrBlock("BLOCK", "long-trigger-fail", f);
                    return;
                }

                TryEnterLong(f);
            }
            else if (f.ShortBias)
            {
                if (!TouchedFastEma(longSide: false))
                {
                    return;
                }

                if (!ShortTriggerOk(f))
                {
                    MaybeLogSignalOrBlock("BLOCK", "short-trigger-fail", f);
                    return;
                }

                TryEnterShort(f);
            }
        }

        private void TryEnterLong(BarFeatures f)
        {
            if (!TryComputeStopAndTargetTicks(longSide: true, out int stopTicks, out int targetTicks, out string fail))
            {
                MaybeLogSignalOrBlock("BLOCK", fail, f);
                return;
            }

            PrepareOrders(SIG_LONG, stopTicks, targetTicks);

            MaybeLogSignalOrBlock("SIGNAL", "enter-long", f, extra: new Dictionary<string, object>
            {
                ["stopTicks"] = stopTicks,
                ["targetTicks"] = targetTicks,
                ["targetR"] = ProfitTargetR.ToString(CultureInfo.InvariantCulture)
            });

            EnterLong(1, SIG_LONG);
            TradesThisSession++;
        }

        private void TryEnterShort(BarFeatures f)
        {
            if (!TryComputeStopAndTargetTicks(longSide: false, out int stopTicks, out int targetTicks, out string fail))
            {
                MaybeLogSignalOrBlock("BLOCK", fail, f);
                return;
            }

            PrepareOrders(SIG_SHORT, stopTicks, targetTicks);

            MaybeLogSignalOrBlock("SIGNAL", "enter-short", f, extra: new Dictionary<string, object>
            {
                ["stopTicks"] = stopTicks,
                ["targetTicks"] = targetTicks,
                ["targetR"] = ProfitTargetR.ToString(CultureInfo.InvariantCulture)
            });

            EnterShort(1, SIG_SHORT);
            TradesThisSession++;
        }

        private void PrepareOrders(string signalName, int stopTicks, int targetTicks)
        {
            // Stop
            SetStopLoss(signalName, CalculationMode.Ticks, stopTicks, false);

            // Target
            SetProfitTarget(signalName, CalculationMode.Ticks, targetTicks);
        }

        private void ManageOpenPosition(BarFeatures f)
        {
            if (!UseBreakEven)
                return;

            // Move to breakeven after >= BreakEvenAtR * initialRisk
            // We approximate initial risk as current stop distance. For true exact tracking, capture stopTicks at entry via OnExecutionUpdate.
            // Baseline approach: use unrealized ticks vs "typical" max stop cap. Good enough to start clean.
            double unrealizedTicks = (Position.MarketPosition == MarketPosition.Long)
                ? (Close[0] - Position.AveragePrice) / TickSize
                : (Position.AveragePrice - Close[0]) / TickSize;

            // If you want strict BE based on actual stopTicks, we’ll extend with captured per-trade risk next iteration.
            double thresholdTicks = MaxStopTicks * BreakEvenAtR;

            if (unrealizedTicks < thresholdTicks)
                return;

            // ADX drops = more chop risk => encourage BE
            if (adx[0] >= MinAdx)
                return;

            // Move stop to BE(+)
            int plus = Math.Max(0, BreakEvenPlusTicks);
            double newStop = (Position.MarketPosition == MarketPosition.Long)
                ? Position.AveragePrice + plus * TickSize
                : Position.AveragePrice - plus * TickSize;

            // NT: must use SetStopLoss with a price mode for an active position adjustment.
            // We keep it simple: apply by price for the active signal.
            string sig = (Position.MarketPosition == MarketPosition.Long) ? SIG_LONG : SIG_SHORT;

            SetStopLoss(sig, CalculationMode.Price, newStop, false);

            if (DiagEnabled)
            {
                _log.Info("RISK", new Dictionary<string, object>
                {
                    ["t"] = Time[0].ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    ["action"] = "move-stop-breakeven",
                    ["sig"] = sig,
                    ["adx"] = adx[0].ToString("0.00", CultureInfo.InvariantCulture),
                    ["unrealTicks"] = unrealizedTicks.ToString("0.0", CultureInfo.InvariantCulture),
                    ["newStop"] = newStop.ToString("0.00", CultureInfo.InvariantCulture)
                });
            }
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (_log == null || execution == null || execution.Order == null)
                return;

            // Log fills only (ignore partial state chatter)
            if (execution.Order.OrderState != OrderState.Filled && execution.Order.OrderState != OrderState.PartFilled)
                return;

            _log.Info("FILL", new Dictionary<string, object>
            {
                ["t"] = time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                ["order"] = execution.Order.Name ?? "",
                ["sig"] = execution.Order.FromEntrySignal ?? "",
                ["state"] = execution.Order.OrderState.ToString(),
                ["side"] = execution.Order.OrderAction.ToString(),
                ["qty"] = quantity,
                ["price"] = price.ToString("0.00", CultureInfo.InvariantCulture),
                ["pos"] = marketPosition.ToString(),
                ["avg"] = Position.AveragePrice.ToString("0.00", CultureInfo.InvariantCulture),
                ["unreal"] = Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]).ToString("0.00", CultureInfo.InvariantCulture)
            });
        }
    }
}