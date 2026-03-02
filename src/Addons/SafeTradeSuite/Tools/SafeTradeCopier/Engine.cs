#region Using declarations
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NinjaTrader.Cbi;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools
{
    public partial class SafeTradeCopierTool : IDisposable
    {
        internal partial class SafeCopierEngine : IDisposable
        {
            private Account master;
            private List<Account> followers = new List<Account>();
            private string instrumentName;
            private Instrument instrument;

            // Config snapshot used for rewiring
            private Account configuredMaster;
            private List<Account> configuredFollowers = new List<Account>();
            private string configuredInstrumentName;
            private Instrument configuredInstrument;

            // Shadow net position for master (avoids stale Account.Positions during ExecutionUpdate)
            private int masterNetShadow;
            private bool masterNetShadowInit;

            private volatile bool armed;
            private volatile bool copyEnabled;
            private readonly object gate = new object();

            private readonly ConcurrentDictionary<string, long> seen = new ConcurrentDictionary<string, long>();
            private readonly ConcurrentQueue<long> copiedTicks = new ConcurrentQueue<long>();

            // Safety defaults (not exposed to UI)
            private const int MaxAbsQtyPerFollower = 2;
            private const int MaxCopiesPer2Sec = 20;
            private const int StaggerMsPerFollower = 125;

            private readonly SemaphoreSlim submitLock = new SemaphoreSlim(1, 1);
            private CancellationTokenSource cts = new CancellationTokenSource();

            public event Action<string> OnStatus;
            public event Action<bool, bool> OnModeChanged;
            public event Action<bool, string> OnReadyChanged;

            public bool CopyEnabled
            {
                get { lock (gate) return copyEnabled; }
            }

            public void ApplyConfig(Account masterAccount, List<Account> followerAccounts, string instrName)
            {
                var followersClean = followerAccounts?
                    .Where(a => a != null && masterAccount != null && !ReferenceEquals(a, masterAccount))
                    .Distinct()
                    .ToList() ?? new List<Account>();

                var name = instrName ?? "";
                var instr = string.IsNullOrWhiteSpace(name) ? null : Instrument.GetInstrument(name);

                lock (gate)
                {
                    configuredMaster = masterAccount;
                    configuredFollowers = followersClean;
                    configuredInstrumentName = name;
                    configuredInstrument = instr;

                    // If COPY is ON and config becomes invalid -> fail safe to OFF
                    if (copyEnabled && !IsReady_NoLock(out var reason))
                    {
                        copyEnabled = false;
                        DisarmUnsafe_NoLock("Config no longer valid");
                        RaiseModeChanged_NoLock();
                        RaiseReady_NoLock(reasonOverride: reason);
                        Log($"COPY OFF (auto): {reason}");
                        return;
                    }

                    // If COPY is ON and config changed -> rewire immediately
                    if (copyEnabled)
                        RewireUnsafe_NoLock("Config changed");

                    RaiseReady_NoLock();
                }
            }

            public void SetCopyEnabled(bool enabled)
            {
                lock (gate)
                {
                    if (enabled)
                    {
                        if (!IsReady_NoLock(out var reason))
                        {
                            copyEnabled = false;
                            DisarmUnsafe_NoLock("COPY ON blocked");
                            RaiseModeChanged_NoLock();
                            RaiseReady_NoLock(reasonOverride: reason);
                            Log($"COPY ON blocked: {reason}");
                            return;
                        }

                        copyEnabled = true;
                        RewireUnsafe_NoLock("COPY ON");
                        RaiseModeChanged_NoLock();
                        RaiseReady_NoLock();
                        Log("COPY ON.");
                    }
                    else
                    {
                        copyEnabled = false;
                        DisarmUnsafe_NoLock("COPY OFF");
                        RaiseModeChanged_NoLock();
                        RaiseReady_NoLock();
                        Log("COPY OFF.");
                    }
                }
            }

            private void RaiseModeChanged_NoLock()
            {
                OnModeChanged?.Invoke(armed, copyEnabled);
            }

            private void RaiseReady_NoLock(string reasonOverride = null)
            {
                var ready = IsReady_NoLock(out var reason);
                if (!string.IsNullOrWhiteSpace(reasonOverride))
                    reason = reasonOverride;

                OnReadyChanged?.Invoke(ready, reason ?? "");
            }

            private bool IsReady_NoLock(out string reason)
            {
                if (configuredMaster == null)
                {
                    reason = "Select a master account";
                    return false;
                }

                if (configuredInstrument == null)
                {
                    reason = "Invalid instrument (must match NT instrument name)";
                    return false;
                }

                if (configuredFollowers == null || configuredFollowers.Count == 0)
                {
                    reason = "Select at least one follower";
                    return false;
                }

                if (!configuredFollowers.Any(a => a != null && a.ConnectionStatus == ConnectionStatus.Connected))
                {
                    reason = "No connected followers";
                    return false;
                }

                reason = "";
                return true;
            }

            private void RewireUnsafe_NoLock(string reason)
            {
                // Tear down old wiring (if any)
                if (armed)
                {
                    if (master != null)
                        master.ExecutionUpdate -= OnMasterExecution;

                    foreach (var f in followers)
                        f.OrderUpdate -= OnFollowerOrderUpdate;
                }

                // Apply current config into active fields used by copier
                master = configuredMaster;
                followers = (configuredFollowers ?? new List<Account>())
                    .Where(a => a != null && a.ConnectionStatus == ConnectionStatus.Connected && master != null && !ReferenceEquals(a, master))
                    .Distinct()
                    .ToList();

                instrumentName = configuredInstrumentName;
                instrument = configuredInstrument;

                // Reset shadow to current master position
                masterNetShadow = (instrument != null && master != null) ? GetNetPosition(master, instrument) : 0;
                masterNetShadowInit = (instrument != null && master != null);

                // Arm only if we’re copy-enabled and ready
                if (!copyEnabled || !IsReady_NoLock(out _))
                {
                    armed = false;
                    return;
                }

                armed = true;

                master.ExecutionUpdate += OnMasterExecution;

                foreach (var f in followers)
                    f.OrderUpdate += OnFollowerOrderUpdate;

                // reset circuit-breaker bookkeeping
                seen.Clear();
                while (copiedTicks.TryDequeue(out _)) { }

                // token swap
                var oldCts = cts;
                cts = new CancellationTokenSource();
                oldCts.Cancel();
                oldCts.Dispose();

                Log($"ARMED (auto). Reason={reason}. Master={master?.Name}, Followers={followers.Count}, Instr='{instrumentName}'");
            }

            private void DisarmUnsafe_NoLock(string reason)
            {
                if (armed)
                {
                    if (master != null)
                        master.ExecutionUpdate -= OnMasterExecution;

                    foreach (var f in followers)
                        f.OrderUpdate -= OnFollowerOrderUpdate;
                }

                armed = false;
                masterNetShadowInit = false;
                masterNetShadow = 0;

                // token swap
                var oldCts = cts;
                cts = new CancellationTokenSource();
                oldCts.Cancel();
                oldCts.Dispose();

                seen.Clear();
                while (copiedTicks.TryDequeue(out _)) { }

                if (!string.IsNullOrWhiteSpace(reason))
                    Log($"DISARMED: {reason}");
            }
            
            public void Log(string msg) => OnStatus?.Invoke(msg);

            public void Dispose()
            {
                lock (gate)
                {
                    copyEnabled = false;
                    DisarmUnsafe_NoLock("Dispose");
                    RaiseModeChanged_NoLock();
                    RaiseReady_NoLock(reasonOverride: "Disposed");
                }

                submitLock.Dispose();

                lock (gate)
                {
                    cts.Dispose();
                }
            }
        }
    }
}