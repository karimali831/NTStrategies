using System;
using System.Collections.Generic;
using System.Windows.Threading;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private readonly Dictionary<string, (double r, double u)> _uiPnl = new Dictionary<string, (double r, double u)>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _uiNet = new Dictionary<string, int>(StringComparer.Ordinal);
        
        private void RenderPnlUi()
        {
            if (_isClosing)
                return;

            var display = _uiDispatcher ?? _window?.Dispatcher;
            if (display == null || display.HasShutdownStarted || display.HasShutdownFinished)
                return;

            display.InvokeAsync(() =>
            {
                if (_isClosing || _window == null)
                    return;

                var totalR = 0.0;
                var totalU = 0.0;

                // master
                if (_masterBox?.SelectedItem is Account master)
                {
                    var mr = 0.0;
                    var mu = 0.0;
                    lock (_uiPnl)
                    {
                        if (_uiPnl.TryGetValue(master.Name, out var snap))
                        {
                            mr = snap.r;
                            mu = snap.u;
                        }
                    }

                    totalR += mr;
                    totalU += mu;

                    if (_masterPnlText != null)
                        SetPnlText(_masterPnlText, "", mr, mu, shortened: false);

                    RenderProgressBar(_masterPnlBar, _masterPnlBarStatusText, master);
                }

                // followers
                foreach (var row in _followerRows)
                {
                    var acc = row?.Account;
                    if (acc == null) continue;

                    var r = 0.0;
                    var u = 0.0;
                    lock (_uiPnl)
                    {
                        if (_uiPnl.TryGetValue(acc.Name, out var snap))
                        {
                            r = snap.r;
                            u = snap.u;
                        }
                    }

                    totalR += r;
                    totalU += u;

                    if (row.PnlText != null)
                        SetPnlText(row.PnlText, "", r, u, shortened: true);

                    RenderProgressBar(row.PnlBar, row.PnlBarStatusText, acc);
                }

                if (_totalPnlText != null)
                    SetPnlText(_totalPnlText, "Total", totalR, totalU, shortened: false);

                RenderFlattenEnablementUi();
                RenderBreakEvenEnablementUi();
                InvalidatePositionsPanel();
            }, DispatcherPriority.Background);
        }
    }
}