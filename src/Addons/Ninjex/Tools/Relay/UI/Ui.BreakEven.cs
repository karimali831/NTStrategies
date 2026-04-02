using System.Windows.Controls;
using System.Windows.Threading;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        private void RenderBreakEvenEnablementUi()
        {
            var display = _uiDispatcher ?? _window?.Dispatcher;

            display?.InvokeAsync(() =>
            {
                var instr = GetInstrument();
                var beText = BreakEvenAutoMode ? "Auto" : CheckIcon;

                if (instr == null)
                {
                    var tip = BreakEvenAutoMode
                        ? "Break-even is managed automatically in Auto mode."
                        : BreakEvenDisabled
                            ? "Break-even is disabled in Settings."
                            : "Select an instrument first.";

                    if (_btnMasterFreeTrade != null)
                        RenderFreeTradeButtonState(
                            _btnMasterFreeTrade,
                            false,
                            false,
                            BreakEvenAutoMode ? "Auto BE" : "Break-even",
                            all: false,
                            toolTip: tip);

                    if (_btnFreeTradeAll != null)
                        RenderFreeTradeButtonState(
                            _btnFreeTradeAll,
                            false,
                            false,
                            BreakEvenAutoMode ? "Auto BE All" : "Break-even All",
                            all: true,
                            toolTip: tip);

                    foreach (var row in _followerRows)
                    {
                        if (row?.FreeTradeBtn != null)
                            RenderFreeTradeButtonState(
                                row.FreeTradeBtn,
                                false,
                                false,
                                BreakEvenAutoMode ? "Auto" : CheckIcon,
                                all: false,
                                toolTip: tip);
                    }

                    return;
                }

                if (BreakEvenDisabled)
                {
                    const string tip = "Break-even is disabled in Settings.";

                    if (_btnMasterFreeTrade != null)
                        RenderFreeTradeButtonState(_btnMasterFreeTrade, false, false, "Break-even", all: false, toolTip: tip);

                    if (_btnFreeTradeAll != null)
                        RenderFreeTradeButtonState(_btnFreeTradeAll, false, false, "Break-even All", all: true, toolTip: tip);

                    foreach (var row in _followerRows)
                    {
                        if (row?.FreeTradeBtn != null)
                            RenderFreeTradeButtonState(row.FreeTradeBtn, false, false, beText, all: false, toolTip: tip);
                    }

                    return;
                }

                var anyCanApplyOrUndo = false;
                var canUndoAll = false;

                if (_btnMasterFreeTrade != null && GetMasterAccount() is Account master)
                {
                    var undoReasonMaster = "";
                    var applyReasonMaster = "";

                    var canUndoMaster = _engine != null &&
                                        _engine.CanUndoFreeTrade(master, instr, out undoReasonMaster);

                    var canApplyMaster = _engine != null &&
                                         _engine.CanApplyFreeTrade(master, instr, _freeTradeMinProfitPoints, out applyReasonMaster);

                    var enabledMaster = BreakEvenAutoMode
                        ? canUndoMaster
                        : canUndoMaster || canApplyMaster;

                    var masterButtonText = BreakEvenAutoMode ? "Auto BE" : "Break-even";

                    var masterToolTip = canUndoMaster
                        ? "Undo break-even on the master account."
                        : BreakEvenAutoMode
                            ? "Auto break-even is waiting for its trigger."
                            : canApplyMaster
                                ? "Move master stop to break-even."
                                : !string.IsNullOrWhiteSpace(applyReasonMaster) ? applyReasonMaster : undoReasonMaster;

                    RenderFreeTradeButtonState(
                        _btnMasterFreeTrade,
                        enabledMaster,
                        canUndoMaster,
                        masterButtonText,
                        all: false,
                        toolTip: masterToolTip);

                    anyCanApplyOrUndo |= enabledMaster;
                    canUndoAll |= canUndoMaster;
                }
                
                foreach (var row in _followerRows)
                {
                    if (row?.Account == null || row.FreeTradeBtn == null)
                        continue;

                    var enabledFollower = row.EnabledCheck?.IsChecked == true;
                    if (!enabledFollower)
                    {
                        RenderFreeTradeButtonState(
                            row.FreeTradeBtn,
                            false,
                            false,
                            beText,
                            all: false,
                            toolTip: "Enable this follower first.");
                        continue;
                    }

                    var undoReason = "";
                    var applyReason = "";

                    var canUndo = _engine != null &&
                                  _engine.CanUndoFreeTrade(row.Account, instr, out undoReason);

                    var canApply = _engine != null &&
                                   _engine.CanApplyFreeTrade(row.Account, instr, _freeTradeMinProfitPoints, out applyReason);

                    var enabled = BreakEvenAutoMode
                        ? canUndo
                        : canUndo || canApply;

                    var buttonText = BreakEvenAutoMode ? "Auto" : CheckIcon;

                    var toolTip = canUndo
                        ? "Undo break-even for this follower."
                        : BreakEvenAutoMode
                            ? "Auto break-even is waiting for its trigger."
                            : canApply
                                ? "Move follower stop to break-even."
                                : (!string.IsNullOrWhiteSpace(applyReason) ? applyReason : undoReason);

                    RenderFreeTradeButtonState(
                        row.FreeTradeBtn,
                        enabled,
                        canUndo,
                        buttonText,
                        all: false,
                        toolTip: toolTip);

                    anyCanApplyOrUndo |= enabled;
                    canUndoAll |= canUndo;
                }

                if (_btnFreeTradeAll != null)
                {
                    var allEnabled = BreakEvenAutoMode ? canUndoAll : anyCanApplyOrUndo;
                    var allText = BreakEvenAutoMode ? "Auto BE All" : "Break-even All";

                    var allToolTip = canUndoAll
                        ? "Undo break-even for all eligible selected accounts."
                        : BreakEvenAutoMode
                            ? "Auto break-even is waiting for its trigger."
                            : anyCanApplyOrUndo
                                ? "Apply break-even to all eligible selected accounts."
                                : "No eligible accounts for break-even.";

                    RenderFreeTradeButtonState(
                        _btnFreeTradeAll,
                        allEnabled,
                        canUndoAll,
                        allText,
                        all: true,
                        toolTip: allToolTip);
                }
            }, DispatcherPriority.Background);
        }
        
        private void WireFollowerFreeTradeButtons(RelayEngine eng)
        {
            foreach (var r in _followerRows)
            {
                if (r?.FreeTradeBtn == null)
                    continue;

                r.FreeTradeBtn.Click += (s, e) =>
                {
                    if (eng == null || r.Account == null)
                        return;

                    var instr = GetInstrument();
                    if (instr == null)
                    {
                        eng.Log("Invalid instrument.");
                        return;
                    }

                    if (BreakEvenDisabled)
                    {
                        eng.Log("Break-even disabled in Settings.");
                        return;
                    }

                    var canUndo = eng.CanUndoFreeTrade(r.Account, instr, out _);

                    if (BreakEvenAutoMode)
                    {
                        if (!canUndo)
                        {
                            eng.Log("Break-even manual apply disabled in Auto mode.");
                            return;
                        }

                        if (eng.UndoFreeTrade(r.Account, instr))
                            eng.Log($"Break-even undone -> {r.Account.Name} ({instr.FullName})");

                        RenderBreakEvenEnablementUi();
                        return;
                    }

                    if (canUndo)
                    {
                        if (eng.UndoFreeTrade(r.Account, instr))
                            eng.Log($"Break-even undone -> {r.Account.Name} ({instr.FullName})");
                    }
                    else
                    {
                        if (eng.ApplyFreeTrade(r.Account, instr, _freeTradeMinProfitPoints, _freeTradePlusPoints))
                            eng.Log($"Break-even applied -> {r.Account.Name} ({instr.FullName})");
                    }

                    RenderBreakEvenEnablementUi();
                };
            }
        }
        
        private static void RenderFreeTradeButtonState(
            Button btn,
            bool enabled,
            bool undoMode,
            string btnText,
            bool all,
            string toolTip = null)
        {
            if (btn == null)
                return;

            var tone = enabled
                ? undoMode ? FormButtonTone.Warning : FormButtonTone.Primary
                : FormButtonTone.Neutral;

            btn.Content = undoMode ? $"Undo BE{(all ? " All" : "")}" : btnText;
            btn.ToolTip = toolTip ?? (enabled
                ? undoMode ? "Undo break-even." : "Apply break-even."
                : "Break-even unavailable.");

            ApplyButtonTheme(btn, tone, FormButtonStyle.Solid, enabled);
        }
    }
}