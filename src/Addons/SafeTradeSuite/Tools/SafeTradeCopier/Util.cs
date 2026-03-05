using System;
using System.Windows;
using System.Windows.Controls;
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
        
        private static string FormatPnL(double realized, double unrealized, string prefix, bool shortened)
        {
            if (shortened)
                return $"R: {FmtUsd(realized)}  |  U: {FmtUsd(unrealized)}";

            return $"{prefix} PnL: Realized {FmtUsd(realized)} | Unrealized {FmtUsd(unrealized)}";
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

        private void RenderFlipBar(ProgressBar bar, double unrealized, int qty, int stopTicks, int targetTicks, Instrument instr)
        {
            if (bar == null) return;

            // show only if we have an actual bracket spec
            if (instr == null || qty <= 0 || (stopTicks <= 0 && targetTicks <= 0))
            {
                bar.Visibility = Visibility.Collapsed;
                bar.Value = 0;
                return;
            }

            var tickValue = GetTickValue(instr);
            if (tickValue <= 0)
            {
                bar.Visibility = Visibility.Collapsed;
                bar.Value = 0;
                return;
            }

            var stopRisk = stopTicks > 0 ? stopTicks * tickValue * qty : 0.0;
            var targetRisk = targetTicks > 0 ? targetTicks * tickValue * qty : 0.0;

            bar.Visibility = Visibility.Visible;

            if (unrealized >= 0)
            {
                // green 0..100 to target
                var denom = targetRisk > 0 ? targetRisk : 1.0;
                var p = Clamp01(unrealized / denom);
                bar.Foreground = Brushes.DarkGreen;
                bar.Value = p * 100.0;
            }
            else
            {
                // red flipped: 100..0 to stop
                var denom = stopRisk > 0 ? stopRisk : 1.0;
                var used = Clamp01(Math.Abs(unrealized) / denom);
                var remaining = 1.0 - used;
                bar.Foreground = Brushes.Maroon;
                bar.Value = remaining * 100.0;
            }
        }
    }
}