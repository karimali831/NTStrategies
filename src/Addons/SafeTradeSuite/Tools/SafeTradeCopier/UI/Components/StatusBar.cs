using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private Border _statusBar;
        private TextBlock _statusBarText;

        private UIElement BuildStatusBar()
        {
            _statusBarText = new TextBlock
            {
                Text = "NOT READY",
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10,0,10,0)
            };

            _statusBar = new Border
            {
                Height = 28,
                Background = Brushes.DarkRed,
                Child = _statusBarText
            };

            return _statusBar;
        }

        private void RenderStatusBar(
            bool masterConnected,
            bool copyEnabled,
            bool armed,
            bool simMode,
            bool anyFollowerEnabled,
            bool anyFollowerConnected,
            bool globalLock,
            string reason)
        {
            if (_statusBar == null || _statusBarText == null)
                return;

            CopierStatusState state;

            if (!masterConnected || globalLock)
            {
                state = CopierStatusState.Red;
            }
            else if (!simMode && copyEnabled && armed && anyFollowerEnabled && anyFollowerConnected)
            {
                state = CopierStatusState.Green;
            }
            else
            {
                state = CopierStatusState.Yellow;
            }

            switch (state)
            {
                case CopierStatusState.Red:

                    _statusBar.Background = Brushes.DarkRed;
                    _statusBarText.Text = $"NOT READY • {reason}";
                    break;

                case CopierStatusState.Green:

                    _statusBar.Background = Brushes.DarkGreen;
                    _statusBarText.Text = "READY • LIVE • COPIER ARMED";
                    break;

                default:

                    _statusBar.Background = Brushes.Goldenrod;
                    _statusBarText.Text = $"READY • {reason}";
                    break;
            }
        }
        
        private string GetReadyReason()
        {
            if (_engine is null)
                return "Engine uninitialised";

            var disarmed = !_engine.CopyEnabled || !_engine.Armed;
            
            if (!IsMasterConnected())
                return "Master disconnected";

            if (_simOnlyMode)
            {
                return $"SIMULATION MODE • COPIER {(disarmed ? "DISARMED" : "ARMED")}";
            }

            if (disarmed)
                return "COPIER DISARMED";
            
            if (!HasAnyCheckedFollowers())
                return "No followers selected";

            if (!AllCheckedFollowersHealthy())
                return "Followers not connected";

            return "";
        }
        

        
        private void RefreshStatusBar()
        {
            var state = GetStatusState();
            var reason = GetReadyReason();

            switch (state)
            {
                case CopierStatusState.Red:

                    _statusBar.Background = Brushes.DarkRed;
                    _statusBarText.Text = $"NOT READY • {reason}";
                    break;

                case CopierStatusState.Green:

                    _statusBar.Background = Brushes.DarkGreen;
                    _statusBarText.Text = "READY • LIVE • ARMED";
                    break;

                default:

                    _statusBar.Background = Brushes.Goldenrod;
                    _statusBarText.Text = $"READY • {reason}";
                    break;
            }
        }
    }
}