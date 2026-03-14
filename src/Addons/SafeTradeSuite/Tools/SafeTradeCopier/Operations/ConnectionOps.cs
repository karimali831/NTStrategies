using System.Linq;
using System.Windows.Threading;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
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
        
        private CopierStatusState GetStatusState()
        {
            var masterConnected = IsMasterConnected();
            var followersEnabled = HasAnyCheckedFollowers();
            var followersHealthy = AllCheckedFollowersHealthy();

            var armed = _engine?.Armed == true;
            var copyEnabled = _engine?.CopyEnabled == true;

            var globalLock = false; // future

            if (!masterConnected || globalLock)
                return CopierStatusState.Red;

            if (!_simOnlyMode && copyEnabled && armed && followersEnabled && followersHealthy)
                return CopierStatusState.Green;

            return CopierStatusState.Yellow;
        }

        private void OnGlobalConnectionStatusUpdate(object sender, ConnectionStatusEventArgs e)
        {
            var display = _uiDispatcher ?? _window?.Dispatcher;

            display?.InvokeAsync(() =>
            {
                RefreshAccountsUi();
                HandleFollowerConnectionSafety();
                TryAutoRearmAfterReconnect();

                _engine?.Log(
                    $"Connection update: " +
                    $"Conn={(e.Connection?.Options?.Name ?? "Unknown")} " +
                    $"Order={e.Status} Price={e.PriceStatus}"
                );
            }, DispatcherPriority.Background);
        }
        
        private void TryAutoRearmAfterReconnect()
        {
            if (_engine == null) return;
            if (_engine.CopyEnabled) return;
            if (_userManuallyDisarmed) return;
            if (!_autoRearmPending) return;
            if (!HasAnyCheckedFollowers()) return;
            if (!AllCheckedFollowersHealthy()) return;

            RequestCopyEnabled("Copy auto-rearmed after connection recovered.");
            _autoRearmPending = false;
        }
        
        private void RequestCopyEnabled(string reason = null)
        {
            if (_engine == null)
                return;

            ApplyConfigFromUi();
            _engine.SetCopyEnabled(true);

            if (!string.IsNullOrWhiteSpace(reason))
                RefreshStatusBar();
        }
        
        private void RequestCopyDisabled(bool manual, bool allowAutoRearm, string reason)
        {
            if (_engine == null)
                return;

            _userManuallyDisarmed = manual;
            _autoRearmPending = allowAutoRearm;

            _engine.SetCopyEnabled(false);

            if (!string.IsNullOrWhiteSpace(reason))
                _engine.Log(reason);

            RefreshStatusBar();
        }
        
        private void HandleFollowerConnectionSafety()
        {
            if (_engine == null) return;

            var anySelectedBad = _followerRows.Any(r =>
            {
                if (r?.Account == null) return false;
                if (r.EnabledCheck?.IsChecked != true) return false;

                var state = GetUiConnectionState(r.Account);
                return state != UiConnectionState.Connected;
            });

            if (!anySelectedBad)
                return;

            if (_engine.CopyEnabled)
            {
                RequestCopyDisabled(
                    manual: false,
                    allowAutoRearm: true,
                    reason: "Copy disarmed: one or more selected followers lost connection.");
            }

            RenderFollowerRowsState();
            ApplyConfigFromUi();
        }
    }
}