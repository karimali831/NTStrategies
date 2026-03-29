using System.Linq;
using System.Windows.Controls;
using System.Windows.Threading;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private void RenderFlattenEnablementUi()
        {
            var display = _uiDispatcher ?? _window?.Dispatcher;

            display?.InvokeAsync(() =>
            {
                var instr = GetInstrument();

                foreach (var row in _followerRows)
                {
                    if (row?.Account == null || row.FlattenBtn == null)
                        continue;

                    if (instr == null)
                    {
                        RenderFlattenFollowerButtonState(row.FlattenBtn, false);
                        continue;
                    }

                    var canFlatten = CanFlatten(row.Account, instr) && row.EnabledCheck?.IsChecked == true;
                    RenderFlattenFollowerButtonState(row.FlattenBtn, canFlatten);
                }

                RenderFlattenMasterButtonState();
                RenderFlattenAllButtonState();
                RenderMasterSubmitButtonsState();
            }, DispatcherPriority.Background);
        }
        
        private static void RenderFlattenFollowerButtonState(Button btn, bool enabled)
        {
            if (btn == null)
                return;

            var tone = enabled ? FormButtonTone.Flatten : FormButtonTone.Neutral;
            
            btn.ToolTip = enabled
                 ? "Flatten this follower position"
                 : "No open position for this instrument";
            
            ApplyButtonTheme(btn, tone, FormButtonStyle.Solid, enabled);
        }

        private void RenderFlattenMasterButtonState()
        {
            if (_btnFlattenMaster == null) return;

            var master = GetMasterAccount();
            var instr = GetInstrument();
            var canFlattenMaster = CanFlatten(master, instr);
            var tone = canFlattenMaster ? FormButtonTone.Flatten : FormButtonTone.Neutral;
            
            ApplyButtonTheme(_btnFlattenMaster, tone, FormButtonStyle.Solid, canFlattenMaster);
        }
        
        private void RenderFlattenAllButtonState()
        {
            if (_btnFlattenAll == null) return;

            var master = GetMasterAccount();
            var instr = GetInstrument();

            var canFlattenMaster = CanFlatten(master, instr);

            var canFlattenFollowers = _followerRows.Any(r =>
                r?.Account != null &&
                r.EnabledCheck?.IsChecked == true &&
                CanFlatten(r.Account, instr));

            // var canFlattenAny = canFlattenMaster || canFlattenFollowers;
            var tone = canFlattenFollowers ? FormButtonTone.Flatten : FormButtonTone.Neutral;
            
            ApplyButtonTheme(_btnFlattenAll, tone, FormButtonStyle.Solid, canFlattenFollowers);
        }
    }
}