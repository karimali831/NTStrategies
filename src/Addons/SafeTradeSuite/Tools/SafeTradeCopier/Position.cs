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
        
        internal bool SameInstrument(Instrument a, Instrument b)
        {
            if (a == null || b == null) return false;
            if (ReferenceEquals(a, b)) return true;

            // strongest: FullName match (case-insensitive)
            var af = (a.FullName ?? "").Trim();
            var bf = (b.FullName ?? "").Trim();
            if (af.Length > 0 && bf.Length > 0 &&
                string.Equals(af, bf, StringComparison.OrdinalIgnoreCase))
                return true;

            // fallback: master instrument name match (NQ/ES etc)
            var am = a.MasterInstrument?.Name ?? "";
            var bm = b.MasterInstrument?.Name ?? "";
            if (!string.IsNullOrWhiteSpace(am) &&
                string.Equals(am, bm, StringComparison.OrdinalIgnoreCase))
            {
                // still require same expiry if both look like futures contracts
                // (this prevents matching NQ 03-26 to NQ 06-26)
                // If your FullName is like "NQ 03-26", keep it strict by month-year token.
                var atok = ExtractExpiryToken(af);
                var btok = ExtractExpiryToken(bf);
                if (atok.Length == 0 || btok.Length == 0) return true;
                return string.Equals(atok, btok, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }
        
        private static string ExtractExpiryToken(string fullName)
        {
            // expects "... 03-26" or "... 12-25"
            if (string.IsNullOrWhiteSpace(fullName)) return "";
            var parts = fullName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return "";
            return parts[parts.Length - 1].Trim();
        }

        private int GetNetPositionForUi(Account acc, Instrument instr)
        {
            if (acc == null || instr == null) return 0;

            foreach (var p in acc.Positions)
            {
                if (p?.Instrument == null) continue;
                if (!SameInstrument(p.Instrument, instr)) continue;

                var qty = (int)Math.Round((double)p.Quantity, MidpointRounding.AwayFromZero);
                if (p.MarketPosition == MarketPosition.Short) return -Math.Abs(qty);
                if (p.MarketPosition == MarketPosition.Long)  return  Math.Abs(qty);
                return 0;
            }

            return 0;
        }
    }
}