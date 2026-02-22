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
			
			    return;
			}

            // If in position, runner may still be allowed
            TryEnterRunner();
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

            // Risk in ticks derived from dollars
            var lossTicks   = DollarsToTicks(MaxLossPerTrade);
            var profitTicks = DollarsToTicks(MaxProfitPerTrade);

            if (lossTicks < 1 || profitTicks < 1)
            {
	            failReason = $"bad-tick-conversion (lossTicks={lossTicks} profitTicks={profitTicks})";
	            return false;
            }

            if (sig > 0 && EnableLongs)
            {
                SetStopLoss(SigPrimaryLong, CalculationMode.Ticks, lossTicks, false);
                SetProfitTarget(SigPrimaryLong, CalculationMode.Ticks, profitTicks);

                EnterLong(Contracts, SigPrimaryLong);

                primarySubmitted  = true;
                primaryDir        = +1;

                LogDiag($"ENTER primary LONG @{Close[0]:F2} lossTicks={lossTicks} profitTicks={profitTicks}");
                return true;
            }
            else if (sig < 0 && EnableShorts)
            {
                SetStopLoss(SigPrimaryShort, CalculationMode.Ticks, lossTicks, false);
                SetProfitTarget(SigPrimaryShort, CalculationMode.Ticks, profitTicks);

                EnterShort(Contracts, SigPrimaryShort);

                primarySubmitted  = true;
                primaryDir        = -1;

                LogDiag($"ENTER primary SHORT @{Close[0]:F2} lossTicks={lossTicks} profitTicks={profitTicks}");
                return true;
            }
            
            failReason = sig > 0 ? "longs-disabled" : "shorts-disabled";
            return false;
        }

        private void TryEnterRunner()
		{
		    if (!EnableRunner)
		        return;
		
		    if (tradesToday >= MaxTradesPerDay)
		        return;
		
		    if (runnerSubmitted)
		        return;
		
		    if (primaryDir == 0)
		        return;
		
		    // Step 1: arm runner once primary is in favor by trigger ticks
		    var upTicks = GetPrimaryUnrealizedTicks();
		
		    if (!runnerArmed)
		    {
		        if (upTicks < RunnerTriggerTicks)
		            return;
		
		        runnerArmed = true;
		        runnerPullbackSeen = false;
		        runnerArmBar = CurrentBar;
		
		        LogDiag($"Runner armed: dir={(primaryDir > 0 ? "LONG" : "SHORT")} upTicks={upTicks:F1} waiting pullback->bull/bear");
		        return;
		    }
		
		    // Don't enter on the same bar we armed it
		    if (CurrentBar == runnerArmBar)
		        return;
		
		    // Step 2: wait for a pullback (to emaFast by RunnerPullbackTicks)
		    if (!runnerPullbackSeen)
		    {
			    if (RunnerPullbackTouched(primaryDir, out var pbFail))
			    {
				    runnerPullbackSeen = true;
				    LogDiag(primaryDir > 0
					    ? $"Runner pullback seen LONG: low={Low[0]:F2} emaFast={emaFast[0]:F2}"
					    : $"Runner pullback seen SHORT: high={High[0]:F2} emaFast={emaFast[0]:F2}");
			    }
			    else
			    {
				    LogBlockOnce($"runner-wait-pullback: {pbFail}");
			    }

			    return;
		    }
		
		    // Step 3: after pullback, require confirm-bars logic (bull/bear + close above/below both EMAs, min body, etc.)
		    if (!ConfirmBarsSatisfied(primaryDir, out var confirmFail))
		    {
			    LogBlockOnce($"runner-confirm: {confirmFail}");
			    return;
		    }
		
		    // Runner risk rules requested:
		    // Max profit = MaxProfitPerTrade * 2
		    // Max loss   = MaxLossPerTrade (converted)
		    var runnerLossTicks   = DollarsToTicks(MaxLossPerTrade);
		    var runnerProfitTicks = DollarsToTicks(MaxProfitPerTrade * 2.0);
		
		    if (runnerLossTicks < 1 || runnerProfitTicks < 1)
		    {
		        LogDiag($"BLOCK: runner bad tick conversion lossTicks={runnerLossTicks} profitTicks={runnerProfitTicks}");
		        return;
		    }
		
		    if (primaryDir > 0 && EnableLongs)
		    {
		        SetStopLoss(SigRunnerLong, CalculationMode.Ticks, runnerLossTicks, false);
		        SetProfitTarget(SigRunnerLong, CalculationMode.Ticks, runnerProfitTicks);
		
		        EnterLong(Contracts, SigRunnerLong);
		
		        runnerSubmitted = true;
		        runnerArmed = false;
		
		        LogDiag($"ENTER runner LONG (pullback+bull) runnerLossTicks={runnerLossTicks} runnerProfitTicks={runnerProfitTicks}");
		    }
		    else if (primaryDir < 0 && EnableShorts)
		    {
		        SetStopLoss(SigRunnerShort, CalculationMode.Ticks, runnerLossTicks, false);
		        SetProfitTarget(SigRunnerShort, CalculationMode.Ticks, runnerProfitTicks);
		
		        EnterShort(Contracts, SigRunnerShort);
		
		        runnerSubmitted = true;
		        runnerArmed = false;
		
		        LogDiag($"ENTER runner SHORT (pullback+bear) runnerLossTicks={runnerLossTicks} runnerProfitTicks={runnerProfitTicks}");
		    }
		}
    }
}