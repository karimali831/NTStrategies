using System;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Contracts
{
    public sealed class CandidateResearchRecord
    {
        public CandidateSignal Signal { get; }

        public string FinalStatus { get; set; }

        public DateTime PlannedEntryTime { get; set; }
        public double PlannedEntryPrice { get; set; }
        public double EntryDistanceTicks { get; set; }

        public double StructuralRiskTicks { get; set; }
        public double ActualRiskTicks { get; set; }
        public double PlannedStopPrice { get; set; }
        public bool StopWasCapped { get; set; }

        public string ProcessingReason { get; set; }

        public CandidateResearchRecord(CandidateSignal signal)
        {
            Signal = signal ?? throw new ArgumentNullException(nameof(signal));
            FinalStatus = signal.IsQualified
                ? "SignalQualified"
                : "SignalRejected";
            ProcessingReason = signal.QualificationReason;
        }
    }
}