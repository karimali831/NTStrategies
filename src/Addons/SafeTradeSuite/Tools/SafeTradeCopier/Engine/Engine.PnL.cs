using System;
using System.Collections.Generic;
using NinjaTrader.Cbi;

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
                if (acc == null)
                    return;

                acc.AccountItemUpdate -= OnAccountItemUpdate;
                acc.AccountItemUpdate += OnAccountItemUpdate;

                var name = acc.Name ?? "";
                if (string.IsNullOrWhiteSpace(name))
                    return;

                var r = 0.0;
                var u = 0.0;

                try
                {
                    r = acc.Get(AccountItem.RealizedProfitLoss, Currency.UsDollar);
                    u = acc.Get(AccountItem.UnrealizedProfitLoss, Currency.UsDollar);
                }
                catch
                {
                    // account metrics may not be available yet during startup/reconnect
                }

                lock (_pnlByAccount)
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

                lock (_pnlByAccount)
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
        }
    }
}