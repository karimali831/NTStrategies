using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        public partial class SafeCopierEngine
        {
            private readonly Dictionary<string, FollowerGuardState> _guardStateByAccount =
                new Dictionary<string, FollowerGuardState>(StringComparer.Ordinal);

            private FollowerGuard _followerGuard = new FollowerGuard();

            private FollowerGuardState GetGuardState(Account follower)
            {
                if (follower == null)
                    return null;

                lock (_gate)
                {
                    if (!_guardStateByAccount.TryGetValue(follower.Name, out var state))
                    {
                        state = new FollowerGuardState();
                        _guardStateByAccount[follower.Name] = state;
                    }

                    return state;
                }
            }

            private void MarkFollowerEntrySubmitted(Account follower, string entryName)
            {
                var state = GetGuardState(follower);
                if (state == null)
                    return;

                state.PendingEntryTimeUtc = DateTime.UtcNow;
                state.EntryWorking = true;
                state.PendingEntryName = entryName ?? "";
            }

            private void MarkFollowerEntryResolved(Account follower)
            {
                var state = GetGuardState(follower);
                if (state == null)
                    return;

                state.PendingEntryTimeUtc = null;
                state.EntryWorking = false;
                state.PendingEntryName = null;
            }

            private void ResetFollowerDesync(Account follower)
            {
                var state = GetGuardState(follower);
                if (state == null)
                    return;

                state.DesyncDetectedAtUtc = null;
            }

            private void DisableFollower(Account follower, string reason)
            {
                if (follower == null)
                    return;

                var state = GetGuardState(follower);
                if (state != null)
                {
                    state.IsGuardDisabled = true;
                    state.LastGuardReason = reason ?? "";
                }

                Log($"[GUARD] follower disabled -> {follower.Name} reason={reason}");
            }

            private bool IsFollowerGuardDisabled(Account follower)
            {
                var state = GetGuardState(follower);
                return state != null && state.IsGuardDisabled;
            }

            private void ApplyGuardAction(Account follower, GuardAction action, string reason)
            {
                if (follower == null)
                    return;

                Log($"[GUARD] {follower.Name} action={action} reason={reason}");

                switch (action)
                {
                    case GuardAction.Ignore:
                    case GuardAction.LogOnly:
                        return;

                    case GuardAction.DisableFollower:
                        DisableFollower(follower, reason);
                        return;

                    case GuardAction.Flatten:
                        if (_instrument != null)
                            EnsureFlatInstrument(follower, _instrument);
                        return;

                    case GuardAction.FlattenAndDisable:
                        if (_instrument != null)
                            EnsureFlatInstrument(follower, _instrument);

                        DisableFollower(follower, reason);
                        return;

                    case GuardAction.RetryThenDisable:
                        DisableFollower(follower, reason);
                        return;

                    case GuardAction.RetryThenFlatten:
                        if (_instrument != null)
                            EnsureFlatInstrument(follower, _instrument);

                        DisableFollower(follower, reason);
                        return;
                }
            }

            private void CheckFollowerEntryTimeouts(FollowerGuard settings)
            {
                List<Account> followers;

                lock (_gate)
                    followers = _followers.ToList();

                foreach (var follower in followers)
                {
                    if (follower == null)
                        continue;

                    var state = GetGuardState(follower);
                    if (state == null || !state.EntryWorking || state.PendingEntryTimeUtc == null)
                        continue;

                    var elapsed = DateTime.UtcNow - state.PendingEntryTimeUtc.Value;
                    if (elapsed.TotalSeconds < settings.EntryFillTimeoutSeconds)
                        continue;

                    var pendingEntryName = state.PendingEntryName ?? "";
                    MarkFollowerEntryResolved(follower);
                    
                    try
                    {
                        if (_instrument != null && !string.IsNullOrWhiteSpace(pendingEntryName))
                        {
                            var workingOrders = GetWorkingOrdersForInstrument(follower, _instrument)
                                .Where(o => string.Equals((o.Name ?? "").Trim(), pendingEntryName, StringComparison.OrdinalIgnoreCase))
                                .ToArray();

                            if (workingOrders.Length > 0)
                                follower.Cancel(workingOrders);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[GUARD] entry timeout cancel failed -> {follower.Name} msg={ex.Message}");
                    }

                    ApplyGuardAction(
                        follower,
                        settings.OnEntryTimeout,
                        "Follower entry fill timeout exceeded.");
                }
            }

            private void CheckFollowerDesyncs(FollowerGuard settings)
            {
                if (_master == null || _instrument == null)
                    return;

                var masterNet = GetNetPosition(_master, _instrument);

                List<Account> followers;
                lock (_gate)
                    followers = _followers.ToList();

                foreach (var follower in followers)
                {
                    if (follower == null)
                        continue;

                    if (IsFollowerGuardDisabled(follower))
                        continue;
                    
                    var bracketMode = ResolveFollowerAtm(follower);
                    var followMasterExit = FollowerUsesMasterExit(follower);
                    var hasOwnBracket =
                        !string.IsNullOrWhiteSpace(bracketMode) &&
                        !string.Equals(bracketMode, "None", StringComparison.OrdinalIgnoreCase) &&
                        !followMasterExit;

                    if (hasOwnBracket)
                    {
                        var stateOwnBracket = GetGuardState(follower);
                        if (stateOwnBracket != null)
                            stateOwnBracket.DesyncDetectedAtUtc = null;

                        continue;
                    }

                    var expected = masterNet == 0 ? 0 : ResolveFollowerQty(follower, Math.Abs(masterNet));
                    if (masterNet < 0)
                        expected = -Math.Abs(expected);
                    else if (masterNet > 0)
                        expected = Math.Abs(expected);

                    var actual = GetNetPosition(follower, _instrument);
                    var desynced = actual != expected;

                    var state = GetGuardState(follower);
                    if (state == null)
                        continue;

                    if (!desynced)
                    {
                        state.DesyncDetectedAtUtc = null;
                        continue;
                    }

                    if (state.DesyncDetectedAtUtc == null)
                        state.DesyncDetectedAtUtc = DateTime.UtcNow;

                    var elapsed = DateTime.UtcNow - state.DesyncDetectedAtUtc.Value;
                    if (elapsed.TotalSeconds < settings.DesyncGraceSeconds)
                        continue;

                    state.DesyncDetectedAtUtc = null;
                    
                    ApplyGuardAction(
                        follower,
                        settings.OnDesync,
                        $"Follower desync detected. expected={expected}, actual={actual}");
                }
            }
            
            private void RunFollowerGuardWatchdog()
            {
                FollowerGuard settings;
                bool armed;

                lock (_gate)
                {
                    settings = _followerGuard ?? new FollowerGuard();
                    armed = Armed;
                }

                if (!settings.Enabled || !armed)
                    return;

                CheckFollowerEntryTimeouts(settings);
                CheckFollowerDesyncs(settings);
            }
            
            public void UpdateFollowerGuardSettings(FollowerGuard settings)
            {
                lock (_gate)
                {
                    _followerGuard = settings ?? new FollowerGuard();

                    _followerGuard.EntryFillTimeoutSeconds =
                        Math.Max(1, _followerGuard.EntryFillTimeoutSeconds);

                    _followerGuard.DesyncGraceSeconds =
                        Math.Max(1, _followerGuard.DesyncGraceSeconds);
                }

                Log(
                    "[GUARD] settings updated -> " +
                    $"enabled={_followerGuard.Enabled}, " +
                    $"entryTimeout={_followerGuard.EntryFillTimeoutSeconds}, " +
                    $"desyncGrace={_followerGuard.DesyncGraceSeconds}, " +
                    $"onReject={_followerGuard.OnEntryReject}, " +
                    $"onTimeout={_followerGuard.OnEntryTimeout}, " +
                    $"onDesync={_followerGuard.OnDesync}");
            }
            
            public void TryGetFollowerGuardState(Account follower, out FollowerGuardState snapshot)
            {
                snapshot = null;
                if (follower == null) return;

                lock (_gate)
                {
                    if (!_guardStateByAccount.TryGetValue(follower.Name, out var state) || state == null) return;

                    snapshot = new FollowerGuardState
                    {
                        PendingEntryTimeUtc = state.PendingEntryTimeUtc,
                        EntryWorking = state.EntryWorking,
                        PendingEntryName = state.PendingEntryName,
                        DesyncDetectedAtUtc = state.DesyncDetectedAtUtc,
                        IsGuardDisabled = state.IsGuardDisabled,
                        LastGuardReason = state.LastGuardReason
                    };
                }
            }
        }
    }
}