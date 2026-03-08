using System;
using System.Windows.Media;
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
        
        // private static string FormatPnL(double realized, double unrealized, string prefix, bool shortened)
        // {
        //     if (shortened)
        //         return $"R: {FmtUsd(realized)}  |  U: {FmtUsd(unrealized)}";
        //
        //     return $"{prefix} PnL: Realized {FmtUsd(realized)} | Unrealized {FmtUsd(unrealized)}";
        // }
        
        private static string FormatPnL(double realized, double unrealized, string prefix, bool shortened)
        {
            if (shortened)
                return $"R {FmtUsd(realized)}   •   U {FmtUsd(unrealized)}";

            return $"{prefix}   R {FmtUsd(realized)}   •   U {FmtUsd(unrealized)}";
        }
        
        private static Brush GetPnlBrush(double realized, double unrealized)
        {
            var total = realized + unrealized;

            if (total > 0.009)
                return Brushes.DarkGreen;

            if (total < -0.009)
                return Brushes.Firebrick;

            return Brushes.DimGray;
        }

        // private static string FormatPnL(double realized, double unrealized, string prefix, bool shortened)
        // {
        //     var txt = "";
        //     if (realized != 0 || unrealized != 0)
        //     {
        //         if (!shortened)
        //             txt += $"{prefix} PnL: ";
        //         
        //         if (realized != 0)
        //         {
        //             txt += shortened ? $"R: {FmtUsd(realized)}" : $"Realized {FmtUsd(realized)}";
        //
        //             if (unrealized != 0)
        //                 txt += " | ";
        //         }
        //         
        //         if (unrealized != 0)
        //         {
        //             txt += shortened ? $"U: {FmtUsd(unrealized)}" : $"Unrealized {FmtUsd(unrealized)}";
        //         }
        //     }
        //
        //     return txt;
        // }
        
        private static double Clamp01(double x)
        {
            if (x < 0) return 0;
            if (x > 1) return 1;
            return x;
        }

        private static double GetTickValue(Instrument instr)
        {
            if (instr?.MasterInstrument == null) return 0.0;
            var tickSize = instr.MasterInstrument.TickSize;
            var pointValue = instr.MasterInstrument.PointValue;
            if (tickSize <= 0 || pointValue <= 0) return 0.0;
            return tickSize * pointValue;
        }

        private bool TryGetInstrumentUnrealized(Account acc, Instrument instr, out double unrealized, out int absQty)
        {
            unrealized = 0;
            absQty = 0;
            if (acc == null || instr == null) return false;

            foreach (var pos in acc.Positions)
            {
                if (pos?.Instrument == null) continue;
                if (!string.Equals(pos.Instrument.FullName, instr.FullName, StringComparison.Ordinal)) continue;

                absQty = Math.Abs((int)Math.Round((double)pos.Quantity, MidpointRounding.AwayFromZero));
                unrealized = pos.GetUnrealizedProfitLoss(PerformanceUnit.Currency);
                return absQty > 0;
            }

            return false;
        }
    }
}