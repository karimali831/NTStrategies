using System;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private void SubmitMasterMarket(SafeCopierEngine eng, bool isBuy)
        {
            try
            {
                if (eng == null) return;

                if (!(_masterBox?.SelectedItem is Account master))
                {
                    eng.Log("Select a master account first.");
                    return;
                }

                var instr = GetInstrument();
                if (instr == null)
                {
                    eng.Log("Invalid instrument.");
                    return;
                }

                if (ShouldConfirmMasterSubmitBecauseDisarmed())
                {
                    var sideText = isBuy ? "buy" : "sell";

                    var confirmed = ShowConfirmDialog(
                        _window,
                        "Copier disarmed",
                        "You have follower accounts enabled, but the copier is currently disarmed.\n\n" +
                        $"Submitting this {sideText} market order will only place the order on the master account.\n\n" +
                        "Do you want to continue?",
                        okText: "Submit order",
                        cancelText: "Cancel");

                    if (!confirmed)
                    {
                        eng.Log("Master market order cancelled because copier is disarmed.");
                        return;
                    }
                }

                if (!eng.CanEnterForRisk(master, out var riskReason))
                {
                    eng.Log($"Master blocked by risk -> {master.Name}: {riskReason}");
                    return;
                }

                var currentNet = GetNetPosition(master, instr);
                if (currentNet != 0)
                {
                    var confirmed = ShowConfirmDialog(
                        _window,
                        "Confirm additional entry",
                        $"There is already an open position on {master.Name} for {instr.FullName} (net {currentNet}).\n\nSubmitting another market order may increase exposure.\n\nDo you want to continue?",
                        okText: "Submit order",
                        cancelText: "Cancel");

                    if (!confirmed)
                    {
                        eng.Log("Master submit cancelled by user.");
                        return;
                    }
                }

                var qty = ParseQtyOrDefault(_masterQtyBox?.Text);
                
                if (qty < 1)
                {
                    eng.Log("Invalid order quantity. Must be >= 1.");
                    return;
                }
                
                var action = isBuy ? OrderAction.Buy : OrderAction.Sell;
                var atm = NormalizeAtm(_masterAtmBox?.SelectedItem as string);
                var entryName = "STC:ENTRY:" + Guid.NewGuid().ToString("N");

                if (!eng.TryBeginMasterManualSubmit(master, instr, action, qty, atm, entryName,
                        out var submitGuardReason))
                {
                    eng.Log(submitGuardReason);
                    return;
                }

                try
                {
                    if (!string.Equals(atm, "None", StringComparison.OrdinalIgnoreCase))
                    {
                        eng.SubmitMasterMarketWithBracket(master, instr, action, qty, atm, entryName);
                        return;
                    }

                    var ord = master.CreateOrder(
                        instr,
                        action,
                        OrderType.Market,
                        OrderEntry.Manual,
                        TimeInForce.Day,
                        qty,
                        0,
                        0,
                        string.Empty,
                        entryName,
                        DateTime.MaxValue,
                        null
                    );

                    eng.Log($"Master submit -> {master.Name}: {action} MKT qty={qty} instr={instr.FullName}");
                    master.Submit(new[] { ord });
                }
                catch (Exception ex)
                {
                    eng.ResetMasterManualSubmit(entryName);
                    eng.Log($"Master submit failed -> {master.Name}: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                LogUnhandled("SubmitMasterMarket", ex);
                throw;
            }
        }
        
        private bool ShouldConfirmMasterSubmitBecauseDisarmed()
        {
            if (_engine == null)
                return false;

            var hasFollowersEnabled = HasAnyCheckedFollowers();
            var isDisarmed = !_engine.CopyEnabled || !_engine.Armed;

            return hasFollowersEnabled && isDisarmed;
        }
    }
}