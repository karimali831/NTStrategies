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
        private DateTime GetEtTime()
        {
            // Assumes Time[0] is already ET due to chart/session settings
            return Time[0];
        }

        private bool IsRth()
        {
            // CME US Index Futures RTH typically 09:30–16:00 ET
            var tt = ToTime(Time[0]);
            return tt >= 093000 && tt < 160000;
        }

        private bool IsInTradeWindow(DateTime now)
        {
            var t = now.TimeOfDay;

            var open = new TimeSpan(9, 30, 0);
            var start = open.Add(TimeSpan.FromMinutes(Math.Max(1, MinsFromStart)));
            var end   = open.Add(TimeSpan.FromMinutes(Math.Max(MinsFromStart + 1, MinsFromEnd)));

            return t >= start && t < end;
        }
        
        private void ResetDailyState()
        {
            orHigh = 0;
            orLow  = 0;
            orBuilt = false;

            tradesToday = 0;

            primaryDir = 0;
            primaryEntryPrice = 0;
            primarySubmitted = false;
            primaryStopMoved = false;
            primaryFilled = false;
            reEntryWaitPullback = false;
            reEntryDir = 0;
            lastTradeTime = Core.Globals.MinDate;

            runnerSubmitted = false;
            runnerStopMoved = false;
            runnerFilled = false;
            runnerEntryPrice = 0;
            runnerArmed = false;
            runnerPullbackSeen = false;
            runnerArmBar = -1;
			
            lastLoggedSigBar = -1;
            lastLoggedOrHigh = 0;
            lastLoggedOrLow = 0;
            lastLoggedOrbBar = -1;
			
            startOfDayCumProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
        }
    }
}