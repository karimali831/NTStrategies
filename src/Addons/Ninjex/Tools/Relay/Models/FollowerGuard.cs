using System;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public sealed class FollowerGuardState
    {
        public string PendingEntryName { get; set; }
        public DateTime? PendingEntryTimeUtc { get; set; }
        public bool EntryWorking { get; set; }
        public DateTime? DesyncDetectedAtUtc { get; set; }
        public bool IsGuardDisabled { get; set; }
        public string LastGuardReason { get; set; }
    }
    
    public sealed class FollowerGuard
    {
        public bool Enabled { get; set; } = true;

        public int EntryFillTimeoutSeconds { get; set; } = 5;
        public int DesyncGraceSeconds { get; set; } = 3;

        public RelayTool.GuardAction OnEntryReject { get; set; } = RelayTool.GuardAction.FlattenAndDisable;
        public RelayTool.GuardAction OnEntryTimeout { get; set; } = RelayTool.GuardAction.FlattenAndDisable;
        public RelayTool.GuardAction OnDesync { get; set; } = RelayTool.GuardAction.FlattenAndDisable;

        // Keep these hidden for now until engine paths exist.
        // public RelayTool.GuardAction OnChangeFailure { get; set; } = RelayTool.GuardAction.RetryThenFlatten;
        // public RelayTool.GuardAction OnCancelFailure { get; set; } = RelayTool.GuardAction.RetryThenFlatten;
    }
}