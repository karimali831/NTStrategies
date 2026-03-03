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

        private static string FormatPnL(double realized, double unrealized, string prefix, bool shortened)
        {
            var txt = "";
            if (realized != 0 || unrealized != 0)
            {
                txt += $"{prefix} PnL: ";
                
                if (realized != 0)
                {
                    txt += shortened ? $"R: {FmtUsd(realized)}" : $"Realized {FmtUsd(realized)}";

                    if (unrealized != 0)
                        txt += " | ";
                }
                
                if (unrealized != 0)
                {
                    txt += shortened ? $"U: {FmtUsd(unrealized)}" : $"Unrealized {FmtUsd(unrealized)}";
                }
            }

            return txt;
        }
    }
}