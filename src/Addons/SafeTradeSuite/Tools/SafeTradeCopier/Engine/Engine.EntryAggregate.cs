using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        public partial class SafeCopierEngine
        {
            private readonly Dictionary<string, MasterEntryAggregate> _masterEntryAgg =
                new Dictionary<string, MasterEntryAggregate>(StringComparer.Ordinal);

            private readonly Dictionary<string, FollowerEntryProgress> _followerEntryProgress =
                new Dictionary<string, FollowerEntryProgress>(StringComparer.Ordinal);
            
            private async Task ProcessMasterEntryAggregate(Execution execution)
            {
                var ord = execution?.Order;
                if (ord == null)
                    return;

                var entryName = (ord.Name ?? "").Trim();
                if (string.IsNullOrWhiteSpace(entryName))
                    return;

                var fillQty = Math.Abs((int)Math.Round((double)execution.Quantity, MidpointRounding.AwayFromZero));
                if (fillQty <= 0)
                    return;

                MasterEntryAggregate snapshot;
                int totalFilledQty;
                double entryValueSum;

                lock (_gate)
                {
                    if (!_masterEntryAgg.TryGetValue(entryName, out var agg) || agg == null)
                    {
                        agg = new MasterEntryAggregate
                        {
                            EntryName = entryName,
                            IsBuy =
                                ord.OrderAction == OrderAction.Buy ||
                                ord.OrderAction == OrderAction.BuyToCover,
                            TotalFilledQty = 0,
                            EntryValueSum = 0.0
                        };

                        _masterEntryAgg[entryName] = agg;
                    }

                    agg.TotalFilledQty += fillQty;
                    agg.EntryValueSum += execution.Price * fillQty;

                    totalFilledQty = agg.TotalFilledQty;
                    entryValueSum = agg.EntryValueSum;

                    snapshot = new MasterEntryAggregate
                    {
                        EntryName = agg.EntryName,
                        IsBuy = agg.IsBuy,
                        TotalFilledQty = agg.TotalFilledQty,
                        EntryValueSum = agg.EntryValueSum
                    };
                }

                var avgEntry = totalFilledQty > 0
                    ? entryValueSum / totalFilledQty
                    : 0.0;

                Log(
                    $"[MASTER AGG] entry={entryName} fillQty={fillQty} totalFilled={totalFilledQty} avgEntry={avgEntry:0.00}");

                await TryCopyAggregateToFollowers(entryName, snapshot);
            }
            
            private async Task TryCopyAggregateToFollowers(string entryName, MasterEntryAggregate agg)
            {
                if (agg == null || string.IsNullOrWhiteSpace(entryName))
                    return;

                List<Account> followerSnap;
                Account masterSnap;
                Instrument instrSnap;
                FollowerGuard guard;

                lock (_gate)
                {
                    followerSnap = _followers?.ToList() ?? new List<Account>();
                    masterSnap = _master;
                    instrSnap = _instrument;
                    guard = _followerGuard ?? new FollowerGuard();
                }

                if (instrSnap == null)
                    return;

                foreach (var f in followerSnap)
                {
                    if (f == null)
                        continue;

                    if (masterSnap != null && ReferenceEquals(f, masterSnap))
                        continue;

                    if (f.ConnectionStatus != ConnectionStatus.Connected)
                    {
                        lock (_gate)
                        {
                            _isRequested = false;
                            DisarmUnsafe_NoLock($"Follower {f.Name} not Connected");
                            RaiseModeChanged_NoLock();
                            RaiseReady_NoLock(reasonOverride: $"Follower {f.Name} disconnected");
                        }
                        return;
                    }

                    if (IsFollowerGuardDisabled(f))
                    {
                        Log($"Copy skipped -> {f.Name}: follower disabled by guard.");
                        continue;
                    }

                    if (!CanEnterForRisk(f, out _, out var longReason))
                    {
                        Log($"Copy skipped -> {f.Name}: {longReason}");
                        continue;
                    }

                    var desiredQty = ResolveFollowerQty(f, agg.TotalFilledQty);
                    if (desiredQty < 1)
                    {
                        Log($"Copy skipped -> {f.Name}: invalid follower qty ({desiredQty}). Must be >= 1.");
                        continue;
                    }

                    if (desiredQty > _maxAbsQtyPerFollower)
                    {
                        Log($"Copy skipped -> {f.Name}: follower qty ({desiredQty}) exceeds max allowed ({_maxAbsQtyPerFollower}).");
                        continue;
                    }

                    var progressKey = BuildFollowerProgressKey(entryName, f.Name);
                    var followerOrderName = BuildFollowerOrderName(entryName, f.Name);

                    FollowerEntryProgress progress;
                    int requestedBefore;
                    int deltaQty;

                    lock (_gate)
                    {
                        if (!_followerEntryProgress.TryGetValue(progressKey, out progress) || progress == null)
                        {
                            progress = new FollowerEntryProgress
                            {
                                MasterEntryName = entryName,
                                FollowerAccountName = f.Name,
                                FollowerOrderName = followerOrderName,
                                RequestedQty = 0,
                                FilledQty = 0,
                                LastUpdateUtc = DateTime.UtcNow
                            };

                            _followerEntryProgress[progressKey] = progress;
                        }

                        requestedBefore = progress.RequestedQty;
                        deltaQty = desiredQty - requestedBefore;

                        if (deltaQty > 0)
                        {
                            progress.RequestedQty += deltaQty;   // reserve BEFORE submit
                            progress.LastUpdateUtc = DateTime.UtcNow;
                        }
                    }

                    if (deltaQty <= 0)
                    {
                        Log(
                            $"[FOLLOWER DELTA SKIP] acc={f.Name} instr={instrSnap.FullName} entry={entryName} " +
                            $"desired={desiredQty} requested={requestedBefore} delta={deltaQty}");
                        continue;
                    }

                    var bracketMode = ResolveFollowerBracket(f);
                    var followMasterExit = FollowerUsesMasterExit(f);
                    var hasOwnBracket =
                        !string.IsNullOrWhiteSpace(bracketMode) &&
                        !string.Equals(bracketMode, "None", StringComparison.OrdinalIgnoreCase) &&
                        !followMasterExit;

                    Log(
                        $"[FOLLOWER DELTA COPY] acc={f.Name} instr={instrSnap.FullName} entry={entryName} " +
                        $"desired={desiredQty} requested={requestedBefore} delta={deltaQty} " +
                        $"mode={(followMasterExit ? "FOLLOW_MASTER_EXIT" : hasOwnBracket ? $"OWN_BRACKET:{bracketMode}" : "ENTRY_ONLY")}");

                    try
                    {
                        var action = agg.IsBuy ? OrderAction.Buy : OrderAction.Sell;

                        MarkFollowerEntrySubmitted(f, followerOrderName);

                        if (hasOwnBracket)
                        {
                            SubmitFollowerMarketWithBracket(
                                f,
                                instrSnap,
                                action,
                                deltaQty,
                                bracketMode,
                                followerOrderName);
                        }
                        else
                        {
                            var ord = f.CreateOrder(
                                instrSnap,
                                action,
                                OrderType.Market,
                                OrderEntry.Manual,
                                TimeInForce.Day,
                                deltaQty,
                                0,
                                0,
                                string.Empty,
                                followerOrderName,
                                DateTime.MaxValue,
                                null
                            );

                            f.Submit(new[] { ord });
                        }
                        
                        RecordCopy();
                        if (StaggerMsPerFollower > 0)
                            await Task.Delay(StaggerMsPerFollower).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        lock (_gate)
                        {
                            if (_followerEntryProgress.TryGetValue(progressKey, out var rollbackProgress) &&
                                rollbackProgress != null)
                            {
                                rollbackProgress.RequestedQty = Math.Max(0, rollbackProgress.RequestedQty - deltaQty);
                                rollbackProgress.LastUpdateUtc = DateTime.UtcNow;
                            }
                        }

                        MarkFollowerEntryResolved(f);
                        ApplyGuardAction(
                            f,
                            guard.OnEntryReject,
                            $"Follower aggregate submit failed: {ex.Message}");
                    }
                }
            }
            
            private static string BuildFollowerProgressKey(string masterEntryName, string followerAccountName)
            {
                return $"{masterEntryName}|{followerAccountName}";
            }

            private static string BuildFollowerOrderName(string masterEntryName, string followerAccountName)
            {
                var trimmedMasterEntry = (masterEntryName ?? "").Trim();
                var trimmedFollower = (followerAccountName ?? "").Trim();

                return $"{trimmedMasterEntry}:{trimmedFollower}";
            }

            private bool TryGetFollowerEntryProgressByOrderName(string followerOrderName, out FollowerEntryProgress progress)
            {
                progress = null;

                if (string.IsNullOrWhiteSpace(followerOrderName))
                    return false;

                lock (_gate)
                {
                    foreach (var kvp in _followerEntryProgress)
                    {
                        var candidate = kvp.Value;
                        if (candidate == null)
                            continue;

                        if (string.Equals(candidate.FollowerOrderName ?? "", followerOrderName, StringComparison.Ordinal))
                        {
                            progress = candidate;
                            return true;
                        }
                    }
                }

                return false;
            }
        }
    }
}
