using System;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private static int GetNetPosition(Account acc, Instrument instr)
        {
            if (acc == null || instr == null) return 0;

            foreach (var p in acc.Positions)
            {
                if (p?.Instrument == null) continue;
                if (p.Instrument.FullName != instr.FullName) continue;

                var qty = (int)Math.Round((double)p.Quantity, MidpointRounding.AwayFromZero);
                if (p.MarketPosition == MarketPosition.Short)
                    qty = -Math.Abs(qty);
                else if (p.MarketPosition == MarketPosition.Long)
                    qty = Math.Abs(qty);
                else
                    qty = 0;

                return qty;
            }

            return 0;
        }
        
        private static bool SameInstrument(Instrument a, Instrument b)
        {
            if (a == null || b == null) return false;

            // FullName is usually enough, but can differ by formatting sometimes.
            // MasterInstrument.Name is the safest "symbol" match.
            if (!string.IsNullOrWhiteSpace(a.FullName) && !string.IsNullOrWhiteSpace(b.FullName) &&
                string.Equals(a.FullName, b.FullName, StringComparison.OrdinalIgnoreCase))
                return true;

            var an = a.MasterInstrument?.Name ?? "";
            var bn = b.MasterInstrument?.Name ?? "";
            if (!string.IsNullOrWhiteSpace(an) && !string.IsNullOrWhiteSpace(bn) &&
                string.Equals(an, bn, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private static double GetUnrealizedFromPositions(Account acc, Instrument instr)
        {
            if (acc == null || instr == null) return 0.0;

            foreach (var pos in acc.Positions)
            {
                if (pos?.Instrument == null) continue;
                if (!SameInstrument(pos.Instrument, instr)) continue;

                return pos.GetUnrealizedProfitLoss(PerformanceUnit.Currency);
            }

            return 0.0;
        }

        private static int GetNetFromPositions(Account acc, Instrument instr)
        {
            if (acc == null || instr == null) return 0;

            var net = 0;

            foreach (var pos in acc.Positions)
            {
                if (pos?.Instrument == null) continue;
                if (!SameInstrument(pos.Instrument, instr)) continue;

                var q = (int)Math.Round((double)pos.Quantity, MidpointRounding.AwayFromZero);
                if (q == 0) continue;

                if (pos.MarketPosition == MarketPosition.Short)
                    net -= Math.Abs(q);
                else if (pos.MarketPosition == MarketPosition.Long)
                    net += Math.Abs(q);

                // instrument-only scope => break after first match
                break;
            }

            return net;
        }
    }
}