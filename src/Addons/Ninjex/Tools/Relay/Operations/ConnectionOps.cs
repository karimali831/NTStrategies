using System;
using System.Linq;
using System.Windows.Threading;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        private enum UiConnectionState
        {
            Connected,
            Warning,
            Disconnected
        }
        
        private bool _connectionStatusHooked;
        
        private static UiConnectionState GetUiConnectionState(Account acc)
        {
            if (acc?.Connection == null)
                return UiConnectionState.Disconnected;

            var status = acc.ConnectionStatus;

            if (status == ConnectionStatus.Connected)
                return UiConnectionState.Connected;

            if (status == ConnectionStatus.ConnectionLost
                || status == ConnectionStatus.Connecting)
                return UiConnectionState.Warning;

            return UiConnectionState.Disconnected;
        }
        
        private static string GetAccountConnectionLabel(Account acc)
        {
            if (acc == null)
                return "";

            var name = (acc.Connection?.Options?.Name ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(name))
                return name;

            var text = acc.ConnectionStatus.ToString();
            return !string.IsNullOrWhiteSpace(text) ? text : "";
        }

        private void HookConnectionStatusUpdates()
        {
            if (_connectionStatusHooked) return;

            Connection.ConnectionStatusUpdate += OnGlobalConnectionStatusUpdate;
            _connectionStatusHooked = true;
        }

        private void UnhookConnectionStatusUpdates()
        {
            if (!_connectionStatusHooked) return;

            Connection.ConnectionStatusUpdate -= OnGlobalConnectionStatusUpdate;
            _connectionStatusHooked = false;
        }

        private void OnGlobalConnectionStatusUpdate(object sender, ConnectionStatusEventArgs e)
        {
            var display = _uiDispatcher ?? _window?.Dispatcher;

            display?.InvokeAsync(() =>
            {
                RefreshAccountsUi();
                HandleFollowerConnectionSafety();
                TryResumeAfterReconnect();
                RefreshRelayStatusPanel();
                RenderMasterSubmitButtonsState();

                _engine?.Log(
                    "Connection update: " +
                    $"Conn={e.Connection?.Options?.Name ?? "Unknown"} " +
                    $"Order={e.Status} Price={e.PriceStatus}"
                );
            }, DispatcherPriority.Background);
        }
        
        private void TryResumeAfterReconnect()
        {
            if (_engine == null || _activeInstrumentSession == null)
                return;

            if (!_activeInstrumentSession.IsArmedRequested)
                return;

            if (!_activeInstrumentSession.IsConnectionSuspended)
                return;

            if (!ActiveSessionHasHealthyEnabledFollowers())
                return;
            
            if (!ActiveSessionHasHealthyMaster())
                return;

            ApplyConfigFromUi(force: true);

            if (!_engine.CanResumeAfterReconnect(out var reason))
            {
                if (!string.IsNullOrWhiteSpace(reason))
                    _engine.Log(reason);

                RefreshRelayStatusPanel();
                return;
            }

            _activeInstrumentSession.IsConnectionSuspended = false;
            _activeInstrumentSession.ConnectionSuspendReason = "";

            _engine.SetCopyEnabled(true);

            _activeInstrumentSession.IsConnectionSuspended = false;
            _activeInstrumentSession.ConnectionSuspendReason = "";

            _engine.Log("Copy resumed after connection recovered and recovery checks passed.");
        }
        
        private void RequestArmed(string reason = null)
        {
            if (_engine == null || _activeInstrumentSession == null)
                return;

            if (!CanRequestArmedForActiveSession(out var blockReason))
            {
                _activeInstrumentSession.IsArmedRequested = true;
                _activeInstrumentSession.IsConnectionSuspended = false;
                _activeInstrumentSession.ConnectionSuspendReason = "";

                RenderArmButton();
                RefreshRelayStatusPanel();

                if (!string.IsNullOrWhiteSpace(blockReason))
                    _engine.Log(blockReason);

                return;
            }

            _activeInstrumentSession.IsArmedRequested = true;
            _activeInstrumentSession.IsConnectionSuspended = false;
            _activeInstrumentSession.ConnectionSuspendReason = "";

            ApplyConfigFromUi();
            _engine.SetCopyEnabled(true);

            RenderArmButton();
            RefreshRelayStatusPanel();

            if (!string.IsNullOrWhiteSpace(reason))
                _engine.Log(reason);
        }
        
        private void RequestDisarmed(string reason)
        {
            if (_engine == null)
                return;

            if (_activeInstrumentSession != null)
            {
                _activeInstrumentSession.IsArmedRequested = false;
                _activeInstrumentSession.IsConnectionSuspended = false;
                _activeInstrumentSession.ConnectionSuspendReason = "";
            }

            _engine.SetCopyEnabled(false);

            if (!string.IsNullOrWhiteSpace(reason))
                _engine.Log(reason);

            RenderArmButton();
            RefreshRelayStatusPanel();
        }
        
        private void RequestConnectionSuspend(string reason)
        {
            if (_engine == null)
                return;

            if (_activeInstrumentSession != null)
            {
                _activeInstrumentSession.IsConnectionSuspended = true;
                _activeInstrumentSession.ConnectionSuspendReason = reason ?? "";
            }

            _engine.SetCopyEnabled(false);

            if (!string.IsNullOrWhiteSpace(reason))
                _engine.Log(reason);

            RenderArmButton();
            RefreshRelayStatusPanel();
        }
        
        private bool ActiveSessionHasHealthyMaster()
        {
            return _activeInstrumentSession?.MasterAccount != null &&
                   GetUiConnectionState(_activeInstrumentSession.MasterAccount) == UiConnectionState.Connected;
        }
        
        private void HandleFollowerConnectionSafety()
        {
            if (_engine == null || _activeInstrumentSession?.FollowersEnabled == null)
                return;

            var anySelectedBad = false;

            foreach (var kvp in _activeInstrumentSession.FollowersEnabled)
            {
                if (!kvp.Value)
                    continue;

                var account = Account.All.FirstOrDefault(a =>
                    a != null &&
                    string.Equals(a.Name, kvp.Key, StringComparison.Ordinal));

                var unhealthy = account == null || GetUiConnectionState(account) != UiConnectionState.Connected;
                if (unhealthy)
                {
                    anySelectedBad = true;
                    break;
                }
            }

            if (anySelectedBad && _engine.IsRequested)
            {
                if (_activeInstrumentSession == null || !_activeInstrumentSession.IsConnectionSuspended)
                    RequestConnectionSuspend("Copy suspended: one or more selected followers lost connection.");
            }

            RenderFollowerRowsState();
            RefreshFollowerBulkActionButtons();
            RefreshRelayStatusPanel();
            ApplyConfigFromUi();
        }
    }
}