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
            ManageRunner();

            // If flat, look for primary entry
          	if (Position.MarketPosition == MarketPosition.Flat)
			{
			    if (!blockPrimary)
				{
				    if (reEntryWaitPullback)
				    {
				        if (PullbackSatisfied(reEntryDir))
				        {
				            reEntryWaitPullback = false;
				            LogDiag($"Re-entry pullback satisfied dir={(reEntryDir > 0 ? "LONG" : "SHORT")}: allow new primary");
				        }
				        else
				        {
				            // still waiting for pullback; do not take a new primary yet
				            return;
				        }
				    }
					
					if (TradeCooldownMinutes > 0 && lastTradeTime > Core.Globals.MinDate)
				    {
				        var minsSinceTrade = (GetEtTime() - lastTradeTime).TotalMinutes;
				        if (minsSinceTrade < TradeCooldownMinutes)
				        {
				            LogDiag($"BLOCK: cooldown active ({minsSinceTrade:F1}m < {TradeCooldownMinutes}m)");
				            return;
				        }
				    }
				
				    TryEnterPrimary();
				}
			
			    return;
			}

            // If in position, runner may still be allowed
            TryEnterRunner();
        }
        

        private void TryEnterPrimary()
        {
            if (tradesToday >= MaxTradesPerDay)
                return;

            if (primarySubmitted)
                return;

            var now = GetEtTime();
            if (!IsInTradeWindow(now))
                return;

            var sig = GetEntrySignal();
            if (sig == 0)
                return;

            // Risk in ticks derived from dollars
            var lossTicks   = DollarsToTicks(MaxLossPerTrade);
            var profitTicks = DollarsToTicks(MaxProfitPerTrade);

            if (lossTicks < 1 || profitTicks < 1)
            {
                LogDiag($"BLOCK: bad tick conversion lossTicks={lossTicks} profitTicks={profitTicks}");
                return;
            }

            if (sig > 0 && EnableLongs)
            {
                SetStopLoss(SigPrimaryLong, CalculationMode.Ticks, lossTicks, false);
                SetProfitTarget(SigPrimaryLong, CalculationMode.Ticks, profitTicks);

                EnterLong(Contracts, SigPrimaryLong);

                primarySubmitted  = true;
                primaryDir        = +1;

                LogDiag($"ENTER primary LONG @{Close[0]:F2} lossTicks={lossTicks} profitTicks={profitTicks}");
            }
            else if (sig < 0 && EnableShorts)
            {
                SetStopLoss(SigPrimaryShort, CalculationMode.Ticks, lossTicks, false);
                SetProfitTarget(SigPrimaryShort, CalculationMode.Ticks, profitTicks);

                EnterShort(Contracts, SigPrimaryShort);

                primarySubmitted  = true;
                primaryDir        = -1;

                LogDiag($"ENTER primary SHORT @{Close[0]:F2} lossTicks={lossTicks} profitTicks={profitTicks}");
            }
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
		
		    // Step 2: wait for a pullback (to emaFast)
		    if (!runnerPullbackSeen)
		    {
		        if (primaryDir > 0)
		        {
		            if (Low[0] <= emaFast[0] - (Math.Max(1, RunnerPullbackTicks) * TickSize))
		            {
		                runnerPullbackSeen = true;
		                LogDiag($"Runner pullback seen LONG: low={Low[0]:F2} emaFast={emaFast[0]:F2}");
		            }
		        }
		        else
		        {
		            if (High[0] >= emaFast[0] + (Math.Max(1, RunnerPullbackTicks) * TickSize))
		            {
		                runnerPullbackSeen = true;
		                LogDiag($"Runner pullback seen SHORT: high={High[0]:F2} emaFast={emaFast[0]:F2}");
		            }
		        }
		
		        return;
		    }
		
		    // Step 3: enter on first bullish/bearish candle after pullback
		    if (primaryDir > 0)
		    {
		        if (!(Close[0] > Open[0]))
		            return;
		    }
		    else
		    {
		        if (!(Close[0] < Open[0]))
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