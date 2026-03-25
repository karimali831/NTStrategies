using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private static int GetNetPosition(Account acc, Instrument instr)
        {
            if (acc == null || instr == null) return 0;

            foreach (var p in acc.Positions)
            {
                if (p?.Instrument == null) continue;
                if (p.Instrument.FullName != instr.FullName) continue;

                var qty = (int)Math.Round((double)p.Quantity, MidpointRounding.AwayFromZero);
                if (p.MarketPosition == MarketPosition.Short)
                    qty = -Math.Abs(qty);
                else if (p.MarketPosition == MarketPosition.Long)
                    qty = Math.Abs(qty);
                else
                    qty = 0;

                return qty;
            }

            return 0;
        }
        
        private void FreeTradeMasterSelected(SafeCopierEngine eng)
        {
            if (!(_masterBox?.SelectedItem is Account masterAcc))
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

            if (eng.CanUndoFreeTrade(masterAcc, instr, out _))
            {
                if (eng.UndoFreeTrade(masterAcc, instr))
                    eng.Log($"Break-even undone -> {masterAcc.Name} ({instr.FullName})");
            }
            else
            {
                if (eng.ApplyFreeTrade(masterAcc, instr, _freeTradeMinProfitPoints, _freeTradePlusPoints))
                    eng.Log($"Break-even applied -> {masterAcc.Name} ({instr.FullName})");
            }

            RenderBreakEvenEnablementUi();
        }

        private void FreeTradeAllSelected(SafeCopierEngine eng)
        {
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

            var accounts = new List<Account>();
     
            if (_masterBox?.SelectedItem is Account masterAcc)
                accounts.Add(masterAcc);
            
            var checkedFollowerAccounts =  GetCheckedFollowers()
                .Select(x => x.Account)
                .ToList();

            if (checkedFollowerAccounts.Any())
            {
                accounts.AddRange(checkedFollowerAccounts);
            }

            var canUndoAll = eng.CanUndoFreeTradeAll(checkedFollowerAccounts, instr);

            if (canUndoAll)
            {
                eng.UndoFreeTradeAll(accounts, instr);
            }
            else
            {
                eng.ApplyFreeTradeAll(accounts, instr, _freeTradeMinProfitPoints, _freeTradePlusPoints);
            }
        }
        
        private void FlattenAllSelected(SafeCopierEngine eng)
        {
            try
            {
                if (eng == null)
                    return;

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

                eng.Log($"Flatten All clicked. Instr={instr.FullName}");

                var anySubmitted = false;

                var masterHadOpenPosition = HasOpenInstrumentPosition(master, instr);
                if (masterHadOpenPosition)
                {
                    if (_masterPnlBar != null)
                        _masterPnlBar.Tag = "ORDER_FILLED";

                    eng.EnsureFlatInstrument(master, instr);
                    anySubmitted = true;
                }
                else
                {
                    ClearBarOutcome(_masterPnlBarStatusText, _masterPnlBar);
                }

                foreach (var r in _followerRows)
                {
                    if (r?.Account == null)
                        continue;

                    var followerHadOpenPosition = HasOpenInstrumentPosition(r.Account, instr);
                    if (!followerHadOpenPosition)
                    {
                        ClearBarOutcome(r.PnlBarStatusText, r.PnlBar);
                        continue;
                    }

                    if (r.PnlBar != null)
                        r.PnlBar.Tag = "ORDER_FILLED";

                    eng.EnsureFlatInstrument(r.Account, instr);
                    anySubmitted = true;
                }

                eng.Log(anySubmitted
                    ? $"Flatten All submitted for {instr.FullName} only."
                    : "Flatten All skipped. No open positions found for selected accounts on this instrument.");
            }
            catch (Exception ex)
            {
                LogUnhandled("FlattenAllSelected", ex);
                throw;
            }
        }
        
        private static bool HasOpenInstrumentPosition(Account acc, Instrument instr)
        {
            return TryGetLivePosition(acc, instr, out _, out _);
        }

        private bool HasAnyOpenPositionOnActiveInstrument()
        {
            var instr = GetInstrument();
            if (instr == null)
                return false;

            if (_masterBox?.SelectedItem is Account master && HasOpenInstrumentPosition(master, instr))
                return true;

            return _followerRows.Any(r =>
                r?.Account != null &&
                HasOpenInstrumentPosition(r.Account, instr));
        }
        
        private string GetLivePosition(Account acc, bool masterPnl)
        {
            var instr = GetInstrument();
            var posTxt = $"{(masterPnl ? "Position " : "")}Flat";
            
            if (acc?.Positions == null || instr == null)
                return posTxt;

            var longCount = 0;
            var shortCount = 0;

            foreach (var p in acc.Positions)
            {
                if (p?.Instrument == null)
                    continue;

                if (!string.Equals(p.Instrument.FullName, instr.FullName, StringComparison.Ordinal))
                    continue;

                var qty = Math.Abs((int)Math.Round((double)p.Quantity, MidpointRounding.AwayFromZero));
                if (qty <= 0)
                    continue;

                if (p.MarketPosition == MarketPosition.Long)
                    longCount += qty;
                else if (p.MarketPosition == MarketPosition.Short)
                    shortCount += qty;
            }

            if (longCount > 0 && shortCount == 0)
                return $"Long ({longCount}x)";

            if (shortCount > 0 && longCount == 0)
                return $"Short ({shortCount}x)";

            var total = longCount + shortCount;
            return total > 0 ? $"{total}x" : posTxt;
        }
        
        
        private static bool TryGetLivePosition(Account acc, Instrument instr, out MarketPosition marketPosition, out int absQty)
        {
            marketPosition = MarketPosition.Flat;
            absQty = 0;

            if (acc == null || instr == null)
                return false;

            foreach (var pos in acc.Positions)
            {
                if (pos?.Instrument == null)
                    continue;

                if (!string.Equals(pos.Instrument.FullName, instr.FullName, StringComparison.Ordinal))
                    continue;

                marketPosition = pos.MarketPosition;
                absQty = Math.Abs((int)Math.Round((double)pos.Quantity, MidpointRounding.AwayFromZero));

                return marketPosition != MarketPosition.Flat && absQty > 0;
            }

            return false;
        }

        private void EnsureEnabledFollowersAndAutoRearmForOpenPositions()
        {
            ForceEnableFollowersWithOpenPositions();

            ApplyConfigFromUi();
            RenderFollowerRowsState();
            RefreshFollowerBulkActionButtons();
            RefreshCopierStatusPanel();
            // RenderFlattenMasterButtonState();
            // RenderFlattenAllButtonState();
            SavePersistentUiState();

            if (!HasAnyOpenPositionOnActiveInstrument())
                return;

            if (_engine == null)
                return;

            if (_engine.CopyEnabled && _engine.Armed)
                return;

            _userManuallyDisarmed = false;
            _autoRearmPending = false;
            
            RequestCopyEnabled("Auto re-armed because open positions exist on the active instrument.");
            SavePersistentUiState();
        }
        
        private void ForceEnableFollowersWithOpenPositions()
        {
            var instr = GetInstrument();
            if (instr == null)
                return;

            foreach (var row in _followerRows)
            {
                if (row?.Account == null || row.EnabledCheck == null)
                    continue;

                if (HasOpenInstrumentPosition(row.Account, instr))
                    row.EnabledCheck.IsChecked = true;
            }
        }
        
        private static bool CanFlatten(Account account, Instrument instr)
        {
            return HasOpenInstrumentPosition(account, instr);
        }
        
        private static bool TryGetInstrumentUnrealized(Account acc, Instrument instr, out double unrealized, out int absQty)
        {
            unrealized = 0;
            absQty = 0;
            if (acc == null || instr == null) return false;

            foreach (var pos in acc.Positions)
            {
                if (pos?.Instrument == null) continue;
                if (!string.Equals(pos.Instrument.FullName, instr.FullName, StringComparison.Ordinal)) continue;

                absQty = Math.Abs((int)Math.Round((double)pos.Quantity, MidpointRounding.AwayFromZero));
                unrealized = pos.GetUnrealizedProfitLoss(PerformanceUnit.Currency);
                return absQty > 0;
            }

            return false;
        }
    }
}