using System;
using System.Windows.Threading;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        private void InvalidateUi(Action refreshAction, Action setPending, Action clearPending)
        {
            if (_isClosing)
                return;

            var display = _uiDispatcher ?? _window?.Dispatcher;
            if (display == null || display.HasShutdownStarted || display.HasShutdownFinished)
                return;

            setPending();

            display.InvokeAsync(() =>
            {
                clearPending();

                if (_isClosing || _window == null)
                    return;

                refreshAction();

            }, DispatcherPriority.Background);
        }
    }
}