using System;
using System.Threading;
using System.Threading.Tasks;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        public partial class SafeCopierEngine : IDisposable
        {
            private void OnMasterExecution(object sender, ExecutionEventArgs e)
            {
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
                // Always try to submit bracket for STC entry fills
                TrySubmitBracketOnFill(_master, e.Execution);
                TryFlattenFollowersOnMasterFlat();

                // If master exit filled and master is now flat, flatten followers that follow master exit
                if (isMasterExitExecution && GetNetPosition(_master, _instrument) == 0)
                {
                    FlattenFollowersThatUseMasterExit(_instrument);
                    return;
                }

                // Only COPY on entry executions created by STC
                if (!IsStcEntryExecution(e.Execution.Order))
                    return;

                if (!Armed || !_copyEnabled) return;

                var execId = e.Execution.ExecutionId ?? "";
                if (string.IsNullOrWhiteSpace(execId))
                    execId = $"{e.Execution.Time.Ticks}_{e.Execution.Price}_{e.Execution.Quantity}_{e.Execution.MarketPosition}";

                if (!AllowCopyNow())
                {
                    lock (_gate)
                    {
                        _copyEnabled = false;
                        DisarmUnsafe_NoLock("Circuit breaker: too many copies in short window");
                        RaiseModeChanged_NoLock();
                        RaiseReady_NoLock(reasonOverride: "Circuit breaker tripped");
                    }
                    return;
                }

                var masterExecQty = (int)Math.Round((double)e.Execution.Quantity, MidpointRounding.AwayFromZero);
                masterExecQty = Math.Abs(masterExecQty);
                if (masterExecQty <= 0) return;

                var masterAction = e.Execution.Order?.OrderAction ?? OrderAction.Buy;
                var followerAction = masterAction;

                CancellationToken token;
                lock (_gate)
                {
                    token = _cts.Token;
                }

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

                // Brackets only submit if there's a pending entry name for this fill.
                TrySubmitBracketOnFill(acc, e.Execution);
            }
        }
    }
}