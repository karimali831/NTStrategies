using System;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        public partial class RelayEngine
        {
            private readonly object _masterSubmitGate = new object();
            private bool _masterSubmitInFlight;
            private string _masterSubmitPendingEntryName;
            private long _masterSubmitStartedUtcTicks;
            private string _lastMasterSubmitFingerprint;
            private long _lastMasterSubmitUtcTicks;

            private const int MasterSubmitInFlightTimeoutMs = 4000;
            private const int MasterDuplicateSuppressMs = 1000;

            internal bool TryBeginMasterManualSubmit(
                Account master,
                Instrument instr,
                OrderAction action,
                int qty,
                string atm,
                string entryName,
                out string reason)
            {
                reason = "";

                if (master == null || instr == null)
                {
                    reason = "Invalid master/instrument.";
                    return false;
                }

                var nowTicks = DateTime.UtcNow.Ticks;
                var fingerprint =
                    $"{master.Name}|{instr.FullName}|{action}|{qty}|{(atm ?? "None").Trim()}";

                lock (_masterSubmitGate)
                {
                    if (_masterSubmitInFlight)
                    {
                        var elapsedMs = TimeSpan.FromTicks(nowTicks - _masterSubmitStartedUtcTicks).TotalMilliseconds;
                        if (elapsedMs < MasterSubmitInFlightTimeoutMs)
                        {
                            reason = "Master order submission already in progress.";
                            return false;
                        }

                        // stale lock fallback
                        _masterSubmitInFlight = false;
                        _masterSubmitPendingEntryName = null;
                    }

                    if (!string.IsNullOrWhiteSpace(_lastMasterSubmitFingerprint) &&
                        string.Equals(_lastMasterSubmitFingerprint, fingerprint, StringComparison.Ordinal))
                    {
                        var sinceLastMs = TimeSpan.FromTicks(nowTicks - _lastMasterSubmitUtcTicks).TotalMilliseconds;
                        if (sinceLastMs < MasterDuplicateSuppressMs)
                        {
                            reason = "Duplicate master order suppressed.";
                            return false;
                        }
                    }

                    _masterSubmitInFlight = true;
                    _masterSubmitPendingEntryName = entryName;
                    _masterSubmitStartedUtcTicks = nowTicks;
                    _lastMasterSubmitFingerprint = fingerprint;
                    _lastMasterSubmitUtcTicks = nowTicks;
                    return true;
                }
            }

            private void CompleteMasterManualSubmit(Order order)
            {
                if (order == null)
                    return;

                var name = (order.Name ?? "").Trim();
                if (!name.StartsWith("STC:ENTRY:", StringComparison.OrdinalIgnoreCase))
                    return;

                lock (_masterSubmitGate)
                {
                    if (!string.Equals(_masterSubmitPendingEntryName, name, StringComparison.Ordinal))
                        return;

                    var state = order.OrderState;
                    if (state == OrderState.Accepted ||
                        state == OrderState.Working ||
                        state == OrderState.Filled ||
                        state == OrderState.Cancelled ||
                        state == OrderState.Rejected)
                    {
                        _masterSubmitInFlight = false;
                        _masterSubmitPendingEntryName = null;
                    }
                }
            }

            internal void ResetMasterManualSubmit(string entryName = null)
            {
                lock (_masterSubmitGate)
                {
                    if (!string.IsNullOrWhiteSpace(entryName) &&
                        !string.Equals(_masterSubmitPendingEntryName, entryName, StringComparison.Ordinal))
                        return;

                    _masterSubmitInFlight = false;
                    _masterSubmitPendingEntryName = null;
                }
            }

            internal bool IsMasterSubmitInFlight()
            {
                lock (_masterSubmitGate)
                {
                    if (!_masterSubmitInFlight)
                        return false;

                    var elapsedMs = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - _masterSubmitStartedUtcTicks).TotalMilliseconds;
                    if (elapsedMs >= MasterSubmitInFlightTimeoutMs)
                    {
                        _masterSubmitInFlight = false;
                        _masterSubmitPendingEntryName = null;
                        return false;
                    }

                    return true;
                }
            }
        }
    }
}