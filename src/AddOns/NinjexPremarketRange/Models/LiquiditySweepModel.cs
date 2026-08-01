#region Using declarations
using System.Collections.Generic;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Analysis;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Models
{
    // Deliberately not registered by the research strategy yet.
    public sealed class LiquiditySweepModel : IEntryModel
    {
        public string Name
        {
            get { return "LiquiditySweep"; }
        }

        public bool IsEnabled { get; set; }

        public void Reset(RangeSessionContext session)
        {
        }

        public void OnBreakout(BreakoutEvent breakoutEvent)
        {
        }

        public IEnumerable<EntryCandidate> Evaluate(ModelBarContext context)
        {
            yield break;
        }
    }
}
