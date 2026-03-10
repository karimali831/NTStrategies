using System; 
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool : IDisposable
    {
        public partial class SafeCopierEngine
        {
            private Account _master;
            private List<Account> _followers = new List<Account>();
            private string _instrumentName;
            private Instrument _instrument;

            // Config snapshot used for rewiring
            private Account _configuredMaster;
            private List<Account> _configuredFollowers = new List<Account>();
            private string _configuredInstrumentName;
            private Instrument _configuredInstrument;

            public volatile bool Armed;
            private volatile bool _copyEnabled;
            private readonly object _gate = new object();

            private readonly ConcurrentDictionary<string, long> _seen = new ConcurrentDictionary<string, long>();
            private readonly ConcurrentQueue<long> _copiedTicks = new ConcurrentQueue<long>();

            // Safety defaults (not exposed to UI)
            private const int MaxAbsQtyPerFollower = 2;
            private const int MaxCopiesPer2Sec = 20;
            private const int StaggerMsPerFollower = 125;

            private readonly SemaphoreSlim _submitLock = new SemaphoreSlim(1, 1);
            private CancellationTokenSource _cts = new CancellationTokenSource();

            public event Action<string> OnStatus;
            public event Action<bool, bool> OnModeChanged;
            public event Action<bool, string> OnReadyChanged;

            private string _configuredMasterAtm = "None";

            private Dictionary<string, int> _configuredFollowerQtyOverrides = new Dictionary<string, int>(StringComparer.Ordinal);
            private Dictionary<string, string> _configuredFollowerAtmOverrides = new Dictionary<string, string>(StringComparer.Ordinal);

            public bool CopyEnabled
            {
                get { lock (_gate) return _copyEnabled; }
            }

            public void ApplyConfig(
                Account masterAccount,
                List<Account> followerAccounts,
                string instrName,
                int masterQty,
                string masterAtm,
                Dictionary<string, int> followerQtyOverridesByAccountName,
                Dictionary<string, string> followerAtmOverridesByAccountName)
            {
                var followersClean = followerAccounts?
                    .Where(a => a != null && masterAccount != null && !ReferenceEquals(a, masterAccount))
                    .Distinct()
                    .ToList() ?? new List<Account>();
                

                var name = instrName ?? "";
                var instr = string.IsNullOrWhiteSpace(name) ? null : Instrument.GetInstrument(name);
                
                SafeTradeSuiteRuntime.PrintLog(
                    $"[APPLY CONFIG] master={masterAccount?.Name} followers={followersClean.Count} instr={name} atm={masterAtm}");

                Account oldMaster;
                List<Account> oldFollowers;

                lock (_gate)
                {
                    oldMaster = _configuredMaster;
                    oldFollowers = _configuredFollowers?.ToList() ?? new List<Account>();
                }

                UnsubscribePnl(oldMaster);
                foreach (var f in oldFollowers)
                    UnsubscribePnl(f);

                lock (_gate)
                {
                    _configuredMaster = masterAccount;
                    _configuredFollowers = followersClean;
                    _configuredInstrumentName = name;
                    _configuredInstrument = instr;
                    _configuredMasterAtm = string.IsNullOrWhiteSpace(masterAtm) ? "None" : masterAtm.Trim();
                    _configuredFollowerQtyOverrides =
                        followerQtyOverridesByAccountName ?? new Dictionary<string, int>(StringComparer.Ordinal);
                    _configuredFollowerAtmOverrides =
                        followerAtmOverridesByAccountName ?? new Dictionary<string, string>(StringComparer.Ordinal);
                }

                SubscribePnl(masterAccount);
                foreach (var f in followersClean)
                    SubscribePnl(f);

                lock (_gate)
                {
                    if (_copyEnabled && !IsReady_NoLock(out var reason))
                    {
                        _copyEnabled = false;
                        DisarmUnsafe_NoLock("Config no longer valid");
                        RaiseModeChanged_NoLock();
                        RaiseReady_NoLock(reasonOverride: reason);
                        Log($"COPY OFF (auto): {reason}");
                        return;
                    }

                    RewireUnsafe_NoLock("Config changed");
                    RaiseReady_NoLock();
                }
            }

            private void RaiseModeChanged_NoLock()
            {
                OnModeChanged?.Invoke(Armed, _copyEnabled);
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
                if (_configuredMaster == null)
                {
                    reason = "Select a master account";
                    return false;
                }

                if (_configuredInstrument == null)
                {
                    reason = "Invalid instrument (must match NT instrument name)";
                    return false;
                }

                if (_configuredFollowers == null || _configuredFollowers.Count == 0)
                {
                    reason = "Select at least one follower";
                    return false;
                }

                if (!_configuredFollowers.Any(a => a != null && a.ConnectionStatus == ConnectionStatus.Connected))
                {
                    reason = "No connected followers";
                    return false;
                }

                reason = "";
                return true;
            }

            private void RewireUnsafe_NoLock(string reason)
            {
                SafeTradeSuiteRuntime.PrintLog(
                    $"[REWIRE] reason={reason} copyEnabled={_copyEnabled} armed={Armed} master={_configuredMaster?.Name} followers={_configuredFollowers?.Count ?? 0} instr={_configuredInstrumentName}");
                
                // Tear down old wiring (if any)
                if (_master != null)
                    _master.ExecutionUpdate -= OnMasterExecution;

                foreach (var f in _followers)
                {
                    f.OrderUpdate -= OnFollowerOrderUpdate;
                    f.ExecutionUpdate -= OnFollowerExecution;
                }

                // Apply current config into active fields used by copier
                _master = _configuredMaster;
                _followers = (_configuredFollowers ?? new List<Account>())
                    .Where(a => a != null && a.ConnectionStatus == ConnectionStatus.Connected && _master != null && !ReferenceEquals(a, _master))
                    .Distinct()
                    .ToList();
                
                _instrumentName = _configuredInstrumentName;
                _instrument = _configuredInstrument;

                // Always wire master execution if we have a valid master + instrument.
                // This is required so master ATM/bracket orders work even with COPY OFF / no followers.
                if (_master != null && _instrument != null)
                    _master.ExecutionUpdate += OnMasterExecution;

                // Arm follower-copy logic only if copy is enabled and config is fully ready
                if (!_copyEnabled || !IsReady_NoLock(out _))
                {
                    Armed = false;
                    return;
                }

                Armed = true;

                foreach (var f in _followers)
                {
                    f.OrderUpdate += OnFollowerOrderUpdate;
                    f.ExecutionUpdate += OnFollowerExecution;
                }

                // reset circuit-breaker bookkeeping
                _seen.Clear();
                while (_copiedTicks.TryDequeue(out _)) { }

                // token swap
                var oldCts = _cts;
                _cts = new CancellationTokenSource();
                oldCts.Cancel();
                oldCts.Dispose();

                Log($"ARMED (auto). Reason={reason}. Master={_master?.Name}, Followers={_followers.Count}, Instr='{_instrumentName}'");
            }

            private void DisarmUnsafe_NoLock(string reason)
            {
                if (_master != null)
                    _master.ExecutionUpdate -= OnMasterExecution;

                foreach (var f in _followers)
                {
                    f.OrderUpdate -= OnFollowerOrderUpdate;
                    f.ExecutionUpdate -= OnFollowerExecution;
                }

                if (Armed)
                {
                    UnsubscribePnl(_master);
                    foreach (var f in _followers) UnsubscribePnl(f);
                }
                Armed = false;

                // token swap
                var oldCts = _cts;
                _cts = new CancellationTokenSource();
                oldCts.Cancel();
                oldCts.Dispose();

                _seen.Clear();
                while (_copiedTicks.TryDequeue(out _)) { }

                if (!string.IsNullOrWhiteSpace(reason))
                    Log($"DISARMED: {reason}");
            }
            
            
            public void Log(string msg) => OnStatus?.Invoke(msg);

            public void Dispose()
            {
                lock (_gate)
                {
                    _copyEnabled = false;
                    DisarmUnsafe_NoLock("Dispose");
                    RaiseModeChanged_NoLock();
                    RaiseReady_NoLock(reasonOverride: "Disposed");
                }

                _submitLock.Dispose();

                lock (_gate)
                {
                    _cts.Dispose();
                }
            }
        }
    }
}