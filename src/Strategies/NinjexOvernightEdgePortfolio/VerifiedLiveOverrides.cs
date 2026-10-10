using System;
using System.Collections.Generic;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOvernightEdgePortfolio
    {
        private sealed class RthOpenOverrideEntry
        {
            public RthOpenOverrideEntry(double price)
            {
                Price = price;
            }

            public readonly double Price;
        }

        // Only independently verified live-vs-stored-data discrepancies belong
        // here. Never add a value because it improves a historical outcome.
        private static readonly Dictionary<DateTime, RthOpenOverrideEntry>
            VerifiedRthOpenOverrides =
                new Dictionary<DateTime, RthOpenOverrideEntry>
                {
                    {
                        new DateTime(2026, 9, 24),
                        new RthOpenOverrideEntry(7734.25)
                    }
                };

        private sealed class VerifiedLiveBracketOverrideEntry
        {
            public VerifiedLiveBracketOverrideEntry(string signal, double verifiedEntryPrice, double stopPrice, double targetPrice, string source)
            {
                Signal = signal ?? string.Empty;
                VerifiedEntryPrice = verifiedEntryPrice;
                StopPrice = stopPrice;
                TargetPrice = targetPrice;
                Source = source ?? string.Empty;
            }

            public readonly string Signal;
            public readonly double VerifiedEntryPrice;
            public readonly double StopPrice;
            public readonly double TargetPrice;
            public readonly string Source;
        }

        private static readonly Dictionary<DateTime, VerifiedLiveBracketOverrideEntry> VerifiedLiveBracketOverrides =
            new Dictionary<DateTime, VerifiedLiveBracketOverrideEntry>
            {
                {
                    new DateTime(2026, 9, 30, 10, 26, 0),
                    new VerifiedLiveBracketOverrideEntry(
                        PremarketHighEntrySignal,
                        7770.00,
                        7775.00,
                        7760.00,
                        "Verified live ES 12-26 PMH: entry 7770.00, stop 7775.00, target 7760.00; Replay filled 7770.25 and targeted 7760.25")
                }
            };


        private bool TryGetVerifiedLiveBracketOverride(
            DateTime signalTime,
            string signalName,
            out VerifiedLiveBracketOverrideEntry entry)
        {
            entry = null;
            if (!MirrorVerifiedLiveExecutions)
                return false;

            VerifiedLiveBracketOverrideEntry candidate;
            if (!VerifiedLiveBracketOverrides.TryGetValue(signalTime, out candidate))
                return false;
            if (!string.Equals(candidate.Signal, signalName, StringComparison.Ordinal))
                return false;

            entry = candidate;
            return true;
        }


        private bool TryGetVerifiedRthOpenOverride(
            DateTime tradingDate,
            out double overridePrice)
        {
            overridePrice = double.NaN;

            if (!MirrorVerifiedLiveExecutions)
                return false;

            RthOpenOverrideEntry entry;
            if (!VerifiedRthOpenOverrides.TryGetValue(tradingDate.Date, out entry)
                || entry == null
                || !IsFinite(entry.Price)
                || entry.Price <= 0)
            {
                return false;
            }

            overridePrice =
                TickSize > 0
                    ? Math.Round(entry.Price / TickSize) * TickSize
                    : entry.Price;

            return true;
        }
    }
}
