using System;
using System.Collections.Generic;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Analysis;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Contracts;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Models
{
    public sealed class CandidateModelContext
    {
        public RangeSessionSnapshot Session { get; }

        public CandleSnapshot Bar { get; }

        public CandleSnapshot PreviousBar { get; }

        public CandleMetrics Metrics { get; }

        public IReadOnlyList<CandleSnapshot> History { get; }

        public CandidateFeatureSnapshot Features { get; }

        public CandidateModelContext(
            RangeSessionSnapshot session,
            CandleSnapshot bar,
            CandleSnapshot previousBar,
            CandleMetrics metrics,
            IReadOnlyList<CandleSnapshot> history,
            CandidateFeatureSnapshot features)
        {
            Session = session
                      ?? throw new ArgumentNullException(nameof(session));

            Bar = bar
                  ?? throw new ArgumentNullException(nameof(bar));

            PreviousBar = previousBar;

            Metrics = metrics ?? new CandleMetrics();

            History = history
                      ?? new List<CandleSnapshot>().AsReadOnly();

            Features = features
                       ?? CandidateFeatureSnapshot.Empty;
        }
    }
}