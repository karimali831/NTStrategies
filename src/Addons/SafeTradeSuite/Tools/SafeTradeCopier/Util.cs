using System;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private static bool IsSimAccount(Account acc)
        {
            var n = acc?.Name ?? "";
            return n.StartsWith("Sim", StringComparison.OrdinalIgnoreCase)
                   || n.StartsWith("Playback", StringComparison.OrdinalIgnoreCase);
        }
        
        private static string FmtUsd(double v)
        {
            return v.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("en-US"));
        }

        private static string FormatPnL(double realized, double unrealized, string prefix, bool followerTbl)
        {
            var txt = "";
            if (unrealized != 0)
                txt  = $"{prefix} PnL: {(realized != 0 ? $"{(followerTbl ? "R" : "Realized")} {FmtUsd(realized)} |" : "")} {(followerTbl ? "U" : "Unrealized")}: {FmtUsd(unrealized)}";
            
            return txt;
        }
    }
}