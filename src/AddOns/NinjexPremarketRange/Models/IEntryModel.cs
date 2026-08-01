#region Using declarations
using System.Collections.Generic;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Analysis;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Models
{
    public interface IEntryModel
    {
        string Name { get; }
        bool IsEnabled { get; set; }

        void Reset(RangeSessionContext session);
        void OnBreakout(BreakoutEvent breakoutEvent);
        IEnumerable<EntryCandidate> Evaluate(ModelBarContext context);
    }
}
