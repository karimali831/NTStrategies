using System;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
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

        public SafeTradeCopierTool.GuardAction OnEntryReject { get; set; } = SafeTradeCopierTool.GuardAction.FlattenAndDisable;
        public SafeTradeCopierTool.GuardAction OnEntryTimeout { get; set; } = SafeTradeCopierTool.GuardAction.FlattenAndDisable;
        public SafeTradeCopierTool.GuardAction OnDesync { get; set; } = SafeTradeCopierTool.GuardAction.FlattenAndDisable;

        // Keep these hidden for now until engine paths exist.
        public SafeTradeCopierTool.GuardAction OnChangeFailure { get; set; } = SafeTradeCopierTool.GuardAction.RetryThenFlatten;
        public SafeTradeCopierTool.GuardAction OnCancelFailure { get; set; } = SafeTradeCopierTool.GuardAction.RetryThenFlatten;
    }
}