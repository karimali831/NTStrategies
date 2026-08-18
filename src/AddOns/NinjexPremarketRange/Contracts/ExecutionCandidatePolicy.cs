namespace NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Contracts
{
    public sealed class ExecutionCandidatePolicy
    {
        public string ModelId { get; set; }

        public bool AllowLongs { get; set; }

        public bool AllowShorts { get; set; }

        public int? AttemptMin { get; set; }

        public int? AttemptMax { get; set; }

        public bool RequireQualifiedSignal { get; set; }

        public bool EnableEntryDistanceFilter { get; set; }

        public double? EntryDistanceMinTicks { get; set; }

        public double? EntryDistanceMaxTicks { get; set; }
    }
}