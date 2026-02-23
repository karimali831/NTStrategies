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
        private bool IsIndecisionCandle(int barsAgo, out double topWickTicks, out double botWickTicks)
        {
            topWickTicks = 0;
            botWickTicks = 0;

            var hi = High[barsAgo];
            var lo = Low[barsAgo];
            var op = Open[barsAgo];
            var cl = Close[barsAgo];

            var top = hi - Math.Max(op, cl);
            var bot = Math.Min(op, cl) - lo;

            topWickTicks = top / TickSize;
            botWickTicks = bot / TickSize;

            var diff = Math.Abs(topWickTicks - botWickTicks);
            return diff <= Math.Max(0, IndecisionWickDiffTicks);
        }

        private bool IsRejectionCandle(int dir, int barsAgo, out double rejWickTicks)
        {
            rejWickTicks = 0;

            var hi = High[barsAgo];
            var lo = Low[barsAgo];
            var op = Open[barsAgo];
            var cl = Close[barsAgo];

            var top = hi - Math.Max(op, cl);
            var bot = Math.Min(op, cl) - lo;

            var topTicks = top / TickSize;
            var botTicks = bot / TickSize;

            if (dir > 0)
            {
                rejWickTicks = botTicks;
                return botTicks >= Math.Max(0, RejectionWickMinTicks) && botTicks > topTicks;
            }

            if (dir < 0)
            {
                rejWickTicks = topTicks;
                return topTicks >= Math.Max(0, RejectionWickMinTicks) && topTicks > botTicks;
            }

            return false;
        }
    }
}