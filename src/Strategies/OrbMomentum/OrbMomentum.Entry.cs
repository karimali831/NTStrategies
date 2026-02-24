#region Using declarations
using System;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class OrbMomentum : Strategy
    {

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            if (CurrentBar < 50)
                return;

            if (Bars.IsFirstBarOfSession)
            {
                ResetDailyState();
                LogDiag("New session: reset state");
            }

            if (!IsRth())
                return;
			
			if (State != State.Realtime)
				return;

			UpdateOpeningRange();
			LogEmaDistanceEveryBar();

            if (!orBuilt)
                return;
			
			var lossHit   = DailyLossHit(out var dailyPnL);
			var profitHit = DailyProfitHit(out var dailyPnL2);
			var blockPrimary = lossHit || profitHit;
			
			if (lossHit)
			    LogDiag($"BLOCK: max daily loss hit dailyPnL={dailyPnL:F2} <= -{Math.Abs(MaxDailyLoss):F2}");
			
			if (profitHit)
			    LogDiag($"BLOCK: max daily profit hit dailyPnL={dailyPnL2:F2} >= {Math.Abs(MaxDailyProfit):F2}");

            // Manage runner logic (stop move / trigger)
            ManagePrimaryBreakEven();
            ManageRunner();

            // If flat, look for primary entry
          	if (Position.MarketPosition == MarketPosition.Flat)
			{
			    if (!blockPrimary)
				{
					if (reEntryWaitPullback)
					{
						if (RunnerPullbackTouched(reEntryDir, out var pbFail))
						{
							reEntryWaitPullback = false;
							LogDiag($"Re-entry satisfied dir={(reEntryDir > 0 ? "LONG" : "SHORT")}: allow new primary");
						}
						else
						{
							LogBlockOnce($"re-entry-wait ({(reEntryDir > 0 ? "LONG" : "SHORT")}): {pbFail}");
							return;
						}
					}
					
					if (TradeCooldownMinutes > 0 && lastTradeTime > Core.Globals.MinDate)
					{
						var minsSinceTrade = (GetEtTime() - lastTradeTime).TotalMinutes;
						if (minsSinceTrade < TradeCooldownMinutes)
						{
							LogBlockOnce($"cooldown ({minsSinceTrade:F1}m < {TradeCooldownMinutes}m)");
							return;
						}
					}
				
					if (!TryEnterPrimary(out var primaryFail) && primaryFail != "none")
						LogBlockOnce(primaryFail);
				}
			}

			// Runner now submits with primary (no separate runner entry sequencing)
        }
        

        private bool TryEnterPrimary(out string failReason)
        {
            failReason = "none";

            if (tradesToday >= MaxTradesPerDay)
            {
                failReason = $"max-trades ({tradesToday} >= {MaxTradesPerDay})";
                return false;
            }

            if (primarySubmitted)
            {
                failReason = "primary-already-submitted";
                return false;
            }

            var now = GetEtTime();
            if (!IsInTradeWindow(now))
            {
                failReason = "outside-trade-window";
                return false;
            }

            var sig = GetEntrySignal(out var sigFail);
            if (sig == 0)
            {
                failReason = sigFail;
                return false;
            }

            var longSide = sig > 0;
            
            // Reclaimed
            if (!Reclaimed(longSide, sig))
            {
                DrawWaitingConfirmMarker(sig, "reclaimed-false", "RECLAIMED_FALSE");
                failReason = "confirm-fail: long-reclaimed-false";
                return false;
            }
            
            // Confirm bars (your current behavior)
            if (!ConfirmBarsSatisfied(sig, out var confirmFail))
            {
                DrawWaitingConfirmMarker(sig, confirmFail, "PRIMARY_WAIT_CONFIRM");
                failReason = $"confirm-fail: {confirmFail}";
                return false;
            }

            // Risk in ticks derived from dollars
            var lossTicks = DollarsToTicks(MaxLossPerTrade);
            var profitTicks = DollarsToTicks(MaxProfitPerTrade);

            if (lossTicks < 1 || profitTicks < 1)
            {
                failReason = $"bad-tick-conversion (lossTicks={lossTicks} profitTicks={profitTicks})";
                return false;
            }

            // Early-entry gating (intrabar): use "power" instead of just range.
            var rangeTicks = (High[0] - Low[0]) / TickSize;
            var moveTicks  = Math.Abs(Close[0] - Open[0]) / TickSize;
            var powerTicks = Math.Max(rangeTicks, moveTicks);

            if (powerTicks < Math.Max(1, EarlyEntryRangeTicks))
            {
                failReason = $"early-entry-wait (power={powerTicks:F1}t range={rangeTicks:F1}t move={moveTicks:F1}t < {EarlyEntryRangeTicks}t)";
                return false;
            }

            // Directional check WITHOUT waiting for close:
            if (longSide)
            {
                if (Close[0] <= Open[0])
                {
                    failReason = "early-entry-wait (current-bar-not-green-yet)";
                    return false;
                }
            }
            else
            {
                if (Close[0] >= Open[0])
                {
                    failReason = "early-entry-wait (current-bar-not-red-yet)";
                    return false;
                }
            }

            if (longSide && EnableLongs)
            {
                SetStopLoss(SigPrimaryLong, CalculationMode.Ticks, lossTicks, false);
                SetProfitTarget(SigPrimaryLong, CalculationMode.Ticks, profitTicks);

                LogDiag("PRIMARY PASS: orb-break + adxOk + ema-structure + ema-touch + confirm-bars OK", oncePerBar: false);

                EnterLong(Contracts, SigPrimaryLong);

                primarySubmitted = true;
                primaryDir = +1;

                LogDiag($"ENTER primary LONG @{Close[0]:F2} lossTicks={lossTicks} profitTicks={profitTicks}", oncePerBar: false);

                // Submit runner immediately (same bar) with its own stop/target
                if (EnableRunner && !runnerSubmitted && tradesToday < MaxTradesPerDay)
                {
                    var runnerLossTicks = DollarsToTicks(MaxLossPerTrade);
                    var runnerProfitTicks = DollarsToTicks(MaxProfitPerTrade * 2.0);

                    if (runnerLossTicks >= 1 && runnerProfitTicks >= 1)
                    {
                        SetStopLoss(SigRunnerLong, CalculationMode.Ticks, runnerLossTicks, false);
                        SetProfitTarget(SigRunnerLong, CalculationMode.Ticks, runnerProfitTicks);

                        EnterLong(Contracts, SigRunnerLong);
                        runnerSubmitted = true;

                        LogDiag($"ENTER runner LONG (with primary) runnerLossTicks={runnerLossTicks} runnerProfitTicks={runnerProfitTicks}");
                    }
                    else
                    {
                        LogDiag($"BLOCK: runner bad tick conversion lossTicks={runnerLossTicks} profitTicks={runnerProfitTicks}");
                    }
                }

                return true;
            }

            if (!longSide && EnableShorts)
            {
                SetStopLoss(SigPrimaryShort, CalculationMode.Ticks, lossTicks, false);
                SetProfitTarget(SigPrimaryShort, CalculationMode.Ticks, profitTicks);

                LogDiag("PRIMARY PASS: orb-break + adxOk + ema-structure + ema-touch + confirm-bars OK", oncePerBar: false);

                EnterShort(Contracts, SigPrimaryShort);

                primarySubmitted = true;
                primaryDir = -1;

                LogDiag($"ENTER primary SHORT @{Close[0]:F2} lossTicks={lossTicks} profitTicks={profitTicks}", oncePerBar: false);

                // Submit runner immediately (same bar) with its own stop/target
                if (EnableRunner && !runnerSubmitted && tradesToday < MaxTradesPerDay)
                {
                    var runnerLossTicks = DollarsToTicks(MaxLossPerTrade);
                    var runnerProfitTicks = DollarsToTicks(MaxProfitPerTrade * 2.0);

                    if (runnerLossTicks >= 1 && runnerProfitTicks >= 1)
                    {
                        SetStopLoss(SigRunnerShort, CalculationMode.Ticks, runnerLossTicks, false);
                        SetProfitTarget(SigRunnerShort, CalculationMode.Ticks, runnerProfitTicks);

                        EnterShort(Contracts, SigRunnerShort);
                        runnerSubmitted = true;

                        LogDiag($"ENTER runner SHORT (with primary) runnerLossTicks={runnerLossTicks} runnerProfitTicks={runnerProfitTicks}");
                    }
                    else
                    {
                        LogDiag($"BLOCK: runner bad tick conversion lossTicks={runnerLossTicks} profitTicks={runnerProfitTicks}");
                    }
                }

                return true;
            }

            failReason = sig > 0 ? "longs-disabled" : "shorts-disabled";
            return false;
        }
    }
}