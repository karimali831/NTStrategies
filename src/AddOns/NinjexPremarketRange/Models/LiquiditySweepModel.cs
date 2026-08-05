using System.Collections.Generic;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Contracts;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Models
{
    // Deliberately not registered by the research strategy yet.
    public sealed class LiquiditySweepModel
        : IEntryCandidateModel
    {
        public string ModelId => "LiquiditySweep";

        public string ModelVersion => "0.1.0";

        public bool IsEnabled { get; set; }

        public void Reset(
            RangeSessionSnapshot session)
        {
        }

        public void OnBreakout(
            BreakoutSignalSnapshot breakout)
        {
        }

        public IReadOnlyList<CandidateSignal> Evaluate(
            CandidateModelContext context)
        {
            return new List<CandidateSignal>()
                .AsReadOnly();
        }

        public void OnBreakoutResolved(
            string breakoutEventId)
        {
        }
    }
}