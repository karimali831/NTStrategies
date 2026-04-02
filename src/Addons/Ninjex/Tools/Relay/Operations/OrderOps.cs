using System;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        private void SubmitMasterMarket(RelayEngine eng, bool isBuy)
        {
            try
            {
                if (eng == null) return;

                if (!(GetMasterAccount() is Account master))
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
                        "Relay disarmed",
                        "You have follower accounts enabled, but the copier is currently disarmed.\n" +
                        $"Submitting this {sideText} market order will only place the order on the master account.",
                        okText: "Submit master only",
                        cancelText: "Cancel");

                    if (!confirmed)
                    {
                        eng.Log("Master market order cancelled because copier is disarmed.");
                        return;
                    }
                }

                if (!eng.CanEnterForRisk(master, out _, out var fullReason))
                {
                    eng.Log($"Master blocked by risk -> {master.Name}: {fullReason}");
                    return;
                }

                var currentNet = GetNetPosition(master, instr);
                if (currentNet != 0)
                {
                    eng.Log(
                        $"Master submit blocked -> {master.Name}: open position already exists on {instr.FullName} (net {currentNet}).");
                    return;
                }

                var qty = ParseQtyOrDefault(_masterQtyBox?.Text);
                
                if (qty < 1)
                {
                    eng.Log("Invalid order quantity. Must be >= 1.");
                    return;
                }
                
                var action = isBuy ? OrderAction.Buy : OrderAction.Sell;
                var atm = NormalizeAtm(_masterBracketBox?.SelectedItem as string);
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
            var isDisarmed = !_engine.IsRequested || !_engine.Armed;

            return hasFollowersEnabled && isDisarmed;
        }
    }
}