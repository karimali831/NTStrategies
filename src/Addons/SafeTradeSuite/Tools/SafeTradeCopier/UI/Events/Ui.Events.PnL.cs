using System;
using System.Collections.Generic;
using System.Windows;
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
            var display = _uiDispatcher ?? _window?.Dispatcher;

            display?.InvokeAsync(() =>
            {
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
                        _masterPnlText.Text = FormatPnL(mr, mu, "Master", shortened: false);

                    var instr = GetInstrument();

                    if (_masterPnlBar != null && instr != null)
                    {
                        if (_engine.TryGetActiveBracketSpecForUi(master, instr, out var st, out var tk))
                        {
                            if (TryGetInstrumentUnrealized(master, instr, out var uTmp, out var qTmp))
                            {
                                ClearBarOutcome(_masterPnlBarStatusText);
                                RenderFlipBar(_masterPnlBar, uTmp, Math.Max(1, qTmp), st, tk, instr);
                            }
                            else
                            {
                                // Bracket still cached, but position is already flat -> finalize outcome now
                                FinalizeBarOutcomeFromTag(_masterPnlBar);
                                ShowBarOutcome(_masterPnlBar, _masterPnlBarStatusText);
                            }
                        }
                        else
                        {
                            if (_masterPnlBar != null && _masterPnlBar.Tag is string)
                            {
                                FinalizeBarOutcomeFromTag(_masterPnlBar);
                                ShowBarOutcome(_masterPnlBar, _masterPnlBarStatusText);
                            }
                            else
                            {
                                _masterPnlBar.Visibility = Visibility.Collapsed;
                                _masterPnlBar.Value = 0;
                                ClearBarOutcome(_masterPnlBarStatusText);
                            }
                        }
                    }
                }

                // followers
                foreach (var row in _followerRows)
                {
                    var acc = row?.Account;
                    if (acc == null) continue;

                    var r = 0.0; var u = 0.0;
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
                        row.PnlText.Text = FormatPnL(r, u, "Total", shortened: true);

                    var instr = GetInstrument();

                    if (row.PnlBar != null && instr != null)
                    {
                        if (_engine.TryGetActiveBracketSpecForUi(acc, instr, out var st, out var tk))
                        {
                            if (TryGetInstrumentUnrealized(acc, instr, out var uTmp, out var qTmp))
                            {
                                ClearBarOutcome(row.PnlBarStatusText);
                                RenderFlipBar(row.PnlBar, uTmp, Math.Max(1, qTmp), st, tk, instr);
                            }
                            else
                            {
                                // Bracket still cached, but position is already flat -> finalize outcome now
                                FinalizeBarOutcomeFromTag(row.PnlBar);
                                ShowBarOutcome(row.PnlBar, row.PnlBarStatusText);
                            }
                        }
                        else
                        {
                            if (row.PnlBar != null && row.PnlBar.Tag is string)
                            {
                                FinalizeBarOutcomeFromTag(row.PnlBar);
                                ShowBarOutcome(row.PnlBar, row.PnlBarStatusText);
                            }
                            else
                            {
                                row.PnlBar.Visibility = Visibility.Collapsed;
                                row.PnlBar.Value = 0;
                                ClearBarOutcome(row.PnlBarStatusText);
                            }
                        }
                    }
                }

                if (_totalPnlText != null)
                    _totalPnlText.Text = FormatPnL(totalR, totalU, "Total", shortened: false);
                
            }, DispatcherPriority.Background);
        }
    }
}