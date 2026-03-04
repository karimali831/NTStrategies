#region Using declarations
using System;
using System.Collections.Generic;
using System.Windows.Threading;
using NinjaTrader.Cbi;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        public partial class SafeCopierEngine
        {
            private sealed class PnlSnap
            {
                public double Realized;
                public double Unrealized;
            }

            private readonly Dictionary<string, PnlSnap> _pnlByAccount =
                new Dictionary<string, PnlSnap>(StringComparer.Ordinal);

            private void SubscribePnl(Account acc)
            {
                acc.AccountItemUpdate -= OnAccountItemUpdate;
                acc.AccountItemUpdate += OnAccountItemUpdate;
                
                var name = acc.Name ?? "";
                if (!string.IsNullOrWhiteSpace(name))
                {
                    var r = acc.Get(AccountItem.RealizedProfitLoss, Currency.UsDollar);
                    var u = acc.Get(AccountItem.UnrealizedProfitLoss, Currency.UsDollar);

                    lock (_gate)
                    {
                        if (!_pnlByAccount.TryGetValue(name, out var snap))
                        {
                            snap = new PnlSnap();
                            _pnlByAccount[name] = snap;
                        }

                        snap.Realized = r;
                        snap.Unrealized = u;
                    }
                }
            }

            private void UnsubscribePnl(Account acc)
            {
                if (acc == null) return;
                acc.AccountItemUpdate -= OnAccountItemUpdate;
            }

            private void OnAccountItemUpdate(object sender, AccountItemEventArgs e)
            {
                if (e?.Account == null) return;
                if (e.Currency != Currency.UsDollar) return;

                var name = e.Account.Name ?? "";
                if (string.IsNullOrWhiteSpace(name)) return;
                
                lock (_gate)
                { 
                    if (!_pnlByAccount.TryGetValue(name, out var snap))
                    {
                        snap = new PnlSnap();
                        _pnlByAccount[name] = snap;
                    }
        
                    if (e.AccountItem == AccountItem.RealizedProfitLoss)
                        snap.Realized = e.Value;
                    
                    if (e.AccountItem == AccountItem.UnrealizedProfitLoss)
                        snap.Unrealized = e.Value;
                }
            }

            internal bool TryGetPnlForUi(Account acc, out double realized, out double unrealized)
            {
                realized = 0;
                unrealized = 0;
                if (acc == null) return false;

                lock (_gate)
                {
                    if (!_pnlByAccount.TryGetValue(acc.Name, out var snap))
                        return false;

                    realized = snap.Realized;
                    unrealized = snap.Unrealized;
                    return true;
                }
            }
            
            public int GetNetPositionForUi(Account acc, Instrument instr)
            {
                if (acc == null || instr == null) return 0;

                var want = instr.FullName ?? "";
                if (string.IsNullOrWhiteSpace(want)) return 0;

                foreach (var p in acc.Positions)
                {
                    if (p?.Instrument == null) continue;
                    if (!string.Equals(p.Instrument.FullName, want, StringComparison.Ordinal)) continue;

                    return p.Quantity;
                }

                return 0;
            }
        }
    }
}