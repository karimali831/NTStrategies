using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        public partial class SafeCopierEngine : IDisposable
        {
            private readonly ConcurrentDictionary<string, long> _followMasterExitSeen =
                new ConcurrentDictionary<string, long>(StringComparer.Ordinal);
            
            private void OnMasterExecution(object sender, ExecutionEventArgs e)
            {
                SafeTradeSuiteRuntime.PrintLog(
                    $"[MASTER EXEC] acc={e?.Execution?.Account?.Name} name={e?.Execution?.Order?.Name} instr={e?.Execution?.Order?.Instrument?.FullName} state={e?.Execution?.Order?.OrderState} fillPrice={e?.Execution?.Price} qty={e?.Execution?.Quantity}");

                if (e?.Execution == null) return;
                if (_master == null || e.Execution.Account != _master) return;
                if (_instrument == null) return;
                if (e.Execution.Instrument == null || e.Execution.Instrument.FullName != _instrument.FullName)
                    return;

                var ord = e.Execution.Order;
                var orderName = (ord?.Name ?? "").Trim();

                var isMasterExitExecution =
                    orderName.StartsWith("STC:TP", StringComparison.OrdinalIgnoreCase) ||
                    orderName.StartsWith("STC:SL", StringComparison.OrdinalIgnoreCase) ||
                    orderName.StartsWith("STC:FLATTEN", StringComparison.OrdinalIgnoreCase);

                HandleBracketExitOutcome(_master, e.Execution);
                _owner?.TryTrackTradeExitFromExecution(_master, e.Execution);

                if (IsStcEntryExecution(ord))
                {
                    _owner?.TrackEntryExecution(
                        _master,
                        e.Execution,
                        isMaster: true,
                        bracketUsed: _configuredMasterBracket);

                    if (ord?.OrderState == OrderState.PartFilled || ord?.OrderState == OrderState.Filled)
                    {
                        SafeTradeSuiteRuntime.PrintLog(
                            $"[MASTER EXEC -> TRY BRACKET OnMasterExecution] name={e.Execution?.Order?.Name}");

                        TrySubmitBracketOnFill(_master, e.Execution);
                    }
                }

                if (isMasterExitExecution)
                {
                    TryTriggerFollowMasterExitFromMasterExecution(e.Execution);
                    return;
                }

                if (!IsStcEntryExecution(ord))
                    return;

                Log($"[MASTER COPY TRIGGER] acc={_master?.Name} instr={_instrument?.FullName} execId={e.Execution.ExecutionId} orderName={orderName} armed={Armed} requested={_isRequested}");

                if (!Armed || !_isRequested)
                    return;

                var execId = e.Execution.ExecutionId ?? "";
                if (string.IsNullOrWhiteSpace(execId))
                    execId = $"{e.Execution.Time.Ticks}_{e.Execution.Price}_{e.Execution.Quantity}_{e.Execution.MarketPosition}";

                if (!AllowCopyNow())
                {
                    lock (_gate)
                    {
                        _isRequested = false;
                        DisarmUnsafe_NoLock("Circuit breaker: too many copies in short window");
                        RaiseModeChanged_NoLock();
                        RaiseReady_NoLock(reasonOverride: "Circuit breaker tripped");
                    }
                    return;
                }

                var masterExecQty = (int)Math.Round((double)e.Execution.Quantity, MidpointRounding.AwayFromZero);
                masterExecQty = Math.Abs(masterExecQty);
                if (masterExecQty <= 0) return;

                var followerAction = e.Execution.Order?.OrderAction ?? OrderAction.Buy;

                CancellationToken token;
                lock (_gate)
                    token = _cts.Token;

                Task.Run(async () =>
                {
                    await _submitLock.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        await CopyToFollowers(execId, followerAction, masterExecQty, token).ConfigureAwait(false);
                    }
                    finally
                    {
                        _submitLock.Release();
                    }
                }, token);
            }

            private static bool IsStcEntryExecution(Order ord)
            {
                if (ord == null) return false;

                var name = (ord.Name ?? "").Trim();
                var fromSignal = (ord.FromEntrySignal ?? "").Trim();

                // ✅ Only treat STC entries as copy-eligible
                if (name.StartsWith("STC:ENTRY:", StringComparison.OrdinalIgnoreCase)) return true;
                if (fromSignal.StartsWith("STC:ENTRY:", StringComparison.OrdinalIgnoreCase)) return true;

                return false;
            }
            
            private void TryTriggerFollowMasterExitFromMasterExecution(Execution execution)
            {
                if (execution == null || _master == null || _instrument == null)
                    return;

                var execId = execution.ExecutionId ?? "";
                if (string.IsNullOrWhiteSpace(execId))
                    execId = $"{execution.Time.Ticks}_{execution.Price}_{execution.Quantity}_{execution.MarketPosition}";

                var key = $"{_master.Name}|{_instrument.FullName}|{execId}|FOLLOW_EXIT";
                if (!TryMarkFollowMasterExitSeen(key))
                {
                    Log($"Follow-master-exit skipped duplicate -> {_master.Name} ({_instrument.FullName}) execId={execId}");
                    return;
                }

                FlattenFollowersThatUseMasterExit(_instrument);
            }
            
            private bool TryMarkFollowMasterExitSeen(string key)
            {
                return _followMasterExitSeen.TryAdd(key, DateTime.UtcNow.Ticks);
            }
            
            private void OnFollowerExecution(object sender, ExecutionEventArgs e)
            {
                if (e?.Execution == null) return;

                // Only manage brackets for the instrument we’re operating on
                if (_instrument == null) return;
                if (e.Execution.Instrument == null || e.Execution.Instrument.FullName != _instrument.FullName)
                    return;

                var acc = e.Execution.Account;
                if (acc == null) return;

                HandleBracketExitOutcome(acc, e.Execution);
                _owner?.TryTrackTradeExitFromExecution(acc, e.Execution);

                // Brackets only submit if there's a pending entry name for this fill.
                SafeTradeSuiteRuntime.PrintLog(
                    $"[FOLLOWER EXEC -> TRY BRACKET] name={e.Execution?.Order?.Name}");
                
                var orderName = (e.Execution?.Order?.Name ?? "").Trim();
                if (orderName.StartsWith("STC:ENTRY:", StringComparison.OrdinalIgnoreCase) &&
                    e.Execution?.Order != null)
                {
                    _owner?.TrackEntryExecution(
                        acc,
                        e.Execution,
                        isMaster: false,
                        bracketUsed: ResolveFollowerBracket(acc));

                    if (e.Execution.Order.OrderState == OrderState.PartFilled ||
                        e.Execution.Order.OrderState == OrderState.Filled)
                    {
                        Log($"[FOLLOWER ENTRY FILL] acc={acc.Name} instr={_instrument?.FullName} orderName={orderName} qty={e.Execution.Quantity} price={e.Execution.Price}");

                        TrySubmitBracketOnFill(acc, e.Execution);
                        MarkFollowerEntryResolved(acc);
                        ResetFollowerDesync(acc);
                    }
                }
            }
        }
    }
}