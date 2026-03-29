using System; 
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool : IDisposable
    {
        public partial class SafeCopierEngine
        {
            private Account _master;
            private readonly SafeTradeCopierTool _owner;
            private List<Account> _followers = new List<Account>();
            private string _instrumentName;
            private Instrument _instrument;

            // Config snapshot used for rewiring
            private Account _configuredMaster;
            private List<Account> _configuredFollowers = new List<Account>();
            private string _configuredInstrumentName;
            private Instrument _configuredInstrument;

            public volatile bool Armed;
            private volatile bool _isRequested;
            private readonly object _gate = new object();

            private readonly ConcurrentDictionary<string, long> _seen = new ConcurrentDictionary<string, long>();
            private readonly ConcurrentQueue<long> _copiedTicks = new ConcurrentQueue<long>();

            // Safety defaults (not exposed to UI)
            private int _maxAbsQtyPerFollower = 99; // Number is hard-coded in Ui.Followers WARNING !!
            private const int MaxCopiesPer2Sec = 20;
            private const int StaggerMsPerFollower = 125;

            private readonly SemaphoreSlim _submitLock = new SemaphoreSlim(1, 1);
            private CancellationTokenSource _cts = new CancellationTokenSource();

            public event Action<string> OnStatus;
            public event Action<bool, bool> OnModeChanged;
            public event Action<bool, string> OnReadyChanged;

            private string _configuredMasterBracket = "None";

            private Dictionary<string, int> _configuredFollowerQtyOverrides = new Dictionary<string, int>(StringComparer.Ordinal);
            private Dictionary<string, string> _configuredFollowerAtmOverrides = new Dictionary<string, string>(StringComparer.Ordinal);
            
            private double _configuredMasterMaxDailyProfit;
            private double _configuredMasterMaxDailyLoss;

            private Dictionary<string, bool> _configuredFollowerUseMasterRisk =
                new Dictionary<string, bool>(StringComparer.Ordinal);

            private Dictionary<string, double> _configuredFollowerMaxDailyProfit =
                new Dictionary<string, double>(StringComparer.Ordinal);

            private Dictionary<string, double> _configuredFollowerMaxDailyLoss =
                new Dictionary<string, double>(StringComparer.Ordinal);
            
            // Watchdog
            private int _guardWatchdogRunning;
            private const int GuardWatchdogIntervalMs = 500;
            
            public SafeCopierEngine(SafeTradeCopierTool owner)
            {
                _owner = owner;
            }

            public bool IsRequested
            {
                get { lock (_gate) return _isRequested; }
            }

            public void ApplyConfig(
                Account masterAccount,
                List<Account> followerAccounts,
                string instrName,
                string masterBracket,
                Dictionary<string, int> followerQtyOverridesByAccountName,
                Dictionary<string, string> followerAtmOverridesByAccountName,
                double masterMaxDailyProfit,
                double masterMaxDailyLoss,
                Dictionary<string, bool> followerUseMasterRiskByAccountName,
                Dictionary<string, double> followerMaxDailyProfitByAccountName,
                Dictionary<string, double> followerMaxDailyLossByAccountName,
                BreakEvenMode breakEvenMode,
                double freeTradeMinProfitPoints,
                double freeTradePlusPoints)
            {
                var followersClean = followerAccounts?
                    .Where(a => a != null && masterAccount != null && !ReferenceEquals(a, masterAccount))
                    .Distinct()
                    .ToList() ?? new List<Account>();
                
                var name = instrName ?? "";
                var instr = string.IsNullOrWhiteSpace(name) ? null : Instrument.GetInstrument(name);
                
                SafeTradeSuiteRuntime.PrintLog(
                    $"[APPLY CONFIG] master={masterAccount?.Name} followers={followersClean.Count} instr={name} atm={masterBracket}");

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
                    _configuredMasterBracket = string.IsNullOrWhiteSpace(masterBracket) ? "None" : masterBracket.Trim();
                    _breakEvenMode = breakEvenMode;
                    _freeTradeMinProfitPoints = Math.Max(0, freeTradeMinProfitPoints);
                    _freeTradePlusPoints = Math.Max(0, freeTradePlusPoints);
                    _configuredFollowerQtyOverrides =
                        followerQtyOverridesByAccountName ?? new Dictionary<string, int>(StringComparer.Ordinal);
                    _configuredFollowerAtmOverrides =
                        followerAtmOverridesByAccountName ?? new Dictionary<string, string>(StringComparer.Ordinal);
                    _configuredMasterMaxDailyProfit = Math.Max(0, masterMaxDailyProfit);
                    _configuredMasterMaxDailyLoss = Math.Max(0, masterMaxDailyLoss);

                    _configuredFollowerUseMasterRisk =
                        followerUseMasterRiskByAccountName ?? new Dictionary<string, bool>(StringComparer.Ordinal);

                    _configuredFollowerMaxDailyProfit =
                        followerMaxDailyProfitByAccountName ?? new Dictionary<string, double>(StringComparer.Ordinal);

                    _configuredFollowerMaxDailyLoss =
                        followerMaxDailyLossByAccountName ?? new Dictionary<string, double>(StringComparer.Ordinal);
                }

                SubscribePnl(masterAccount);
                foreach (var f in followersClean)
                    SubscribePnl(f);

                lock (_gate)
                {
                    if (_isRequested && !IsReady_NoLock(out var reason))
                    {
                        DisarmUnsafe_NoLock("Config no longer valid");
                        RaiseModeChanged_NoLock();
                        RaiseReady_NoLock(reasonOverride: reason);
                        Log($"ARM pending: {reason}");
                        return;
                    }

                    RewireUnsafe_NoLock("Config changed");
                    RaiseReady_NoLock();
                }
            }

            private void RaiseModeChanged_NoLock()
            {
                OnModeChanged?.Invoke(Armed, _isRequested);
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
                    $"[REWIRE] reason={reason} isRequested={_isRequested} armed={Armed} master={_configuredMaster?.Name} followers={_configuredFollowers?.Count ?? 0} instr={_configuredInstrumentName}");
                
                _guardStateByAccount.Clear();
                
                // Tear down old wiring (if any)
                if (_master != null)
                {
                    _master.ExecutionUpdate -= OnMasterExecution;
                    _master.OrderUpdate -= OnMasterOrderUpdate;
                }

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

                // Always wire master execution + order updates if we have a valid master + instrument.
                // This is required so master ATM/bracket orders and manual chart edits stay synced
                // even with COPY OFF / no followers.
                if (_master != null && _instrument != null)
                {
                    _master.ExecutionUpdate += OnMasterExecution;
                    _master.OrderUpdate += OnMasterOrderUpdate;
                }

                // Arm follower-copy logic only if copy is enabled and config is fully ready
                if (!_isRequested || !IsReady_NoLock(out _))
                {
                    Armed = false;

                    // Keep watchdog alive for master-only protection features
                    // such as auto break-even and missing bracket checks.
                    if (_master != null && _instrument != null)
                        StartProtectiveWatchdog_NoLock();

                    return;
                }

                Armed = true;
                foreach (var f in _followers)
                {
                    f.OrderUpdate += OnFollowerOrderUpdate;
                    f.ExecutionUpdate += OnFollowerExecution;
                    GetGuardState(f);
                }

                // reset circuit-breaker bookkeeping
                _seen.Clear();
                while (_copiedTicks.TryDequeue(out _)) { }

                // token swap
                var oldCts = _cts;
                _cts = new CancellationTokenSource();
                oldCts.Cancel();
                oldCts.Dispose();
                
                StartProtectiveWatchdog_NoLock();
                Log($"ARMED (auto). Reason={reason}. Master={_master?.Name}, Followers={_followers.Count}, Instr='{_instrumentName}'");
            }

            private void DisarmUnsafe_NoLock(string reason)
            {
                if (_master != null)
                {
                    _master.ExecutionUpdate -= OnMasterExecution;
                    _master.OrderUpdate -= OnMasterOrderUpdate;
                }

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
                
                StopFollowerGuardWatchdog_NoLock();

                _seen.Clear();
                while (_copiedTicks.TryDequeue(out _)) { }

                if (!string.IsNullOrWhiteSpace(reason))
                    Log($"DISARMED: {reason}");
            }
            
            private bool AllowCopyNow()
            {
                var cutoff = DateTime.UtcNow.AddSeconds(-2).Ticks;
                while (_copiedTicks.TryPeek(out var t) && t < cutoff)
                    _copiedTicks.TryDequeue(out _);

                return _copiedTicks.Count <= MaxCopiesPer2Sec;
            }
            
            private void RecordCopy()
            {
                _copiedTicks.Enqueue(DateTime.UtcNow.Ticks);
            }
            
            public void SetCopyEnabled(bool enabled)
            {
                lock (_gate)
                {
                    if (enabled)
                    {
                        _isRequested = true;

                        if (!IsReady_NoLock(out var reason))
                        {
                            DisarmUnsafe_NoLock("COPY ON blocked");
                            RaiseModeChanged_NoLock();
                            RaiseReady_NoLock(reasonOverride: reason);
                            Log($"ARM pending: {reason}");
                            return;
                        }

                        RewireUnsafe_NoLock("COPY ON");
                        RaiseModeChanged_NoLock();
                        RaiseReady_NoLock();
                        Log("COPY ON.");
                    }
                    else
                    {
                        _isRequested = false;
                        DisarmUnsafe_NoLock("COPY OFF");
                        RaiseModeChanged_NoLock();
                        RaiseReady_NoLock();
                        Log("COPY OFF.");
                    }
                }
            }

            public void Log(string msg)
            {
                SafeTradeSuiteRuntime.PrintLog(msg);
                // OnStatus?.Invoke(msg);
            }
            
            private void StartProtectiveWatchdog_NoLock()
            {
                if (Interlocked.Exchange(ref _guardWatchdogRunning, 1) == 1)
                    return;

                var token = _cts.Token;
                Task.Run(async () =>
                {
                    try
                    {
                        while (!token.IsCancellationRequested)
                        {
                            try
                            {
                                RunProtectiveWatchdog();
                            }
                            catch (Exception ex)
                            {
                                Log($"[GUARD] watchdog iteration failed -> {ex.Message}");
                            }

                            await Task.Delay(GuardWatchdogIntervalMs, token).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception ex)
                    {
                        Log($"[GUARD] watchdog fatal -> {ex.Message}");
                    }
                    finally
                    {
                        StopFollowerGuardWatchdog_NoLock();
                    }
                }, token);
            }

            private void StopFollowerGuardWatchdog_NoLock()
            {
                Interlocked.Exchange(ref _guardWatchdogRunning, 0);
            }

            public void Dispose()
            {
                lock (_gate)
                {
                    _isRequested = false;
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