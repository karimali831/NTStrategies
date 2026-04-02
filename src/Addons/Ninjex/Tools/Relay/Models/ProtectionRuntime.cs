using System;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        internal sealed class ProtectionRuntime
        {
            public string AccountName { get; set; }
            public string InstrumentName { get; set; }
            public ProtectionState State { get; set; } = ProtectionState.Flat;
            public DateTime? LastStateChangeUtc { get; set; }
            public bool HasLivePosition { get; set; }
            public int NetQuantity { get; set; }
            public bool HasWorkingEntry { get; set; }
            public bool HasWorkingBracket { get; set; }
            public bool HasPendingBracket { get; set; }
            public bool FlattenInFlight { get; set; }
            public bool ExitExecutionSeenRecently { get; set; }
            public DateTime? LastExitExecutionUtc { get; set; }
            public string LastExitOrderName { get; set; }
            public string LastReason { get; set; }
            public bool BreachPending { get; set; }
            public DateTime? BreachFirstDetectedUtc { get; set; }
            public DateTime? LastEntryExecutionUtc { get; set; }
        }
    }
}