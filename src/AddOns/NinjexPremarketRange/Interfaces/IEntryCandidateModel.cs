#region Using declarations
using System.Collections.Generic;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Contracts;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Models;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Interfaces
{
    public interface IEntryCandidateModel
    {
        string ModelId { get; }
        string ModelVersion { get; }
        bool IsEnabled { get; }

        void Reset(RangeSessionSnapshot session);
        void OnBreakout(BreakoutSignalSnapshot breakout);
        IReadOnlyList<CandidateSignal> Evaluate(
            CandidateModelContext context);
        void OnBreakoutResolved(string breakoutEventId);
    }
}
