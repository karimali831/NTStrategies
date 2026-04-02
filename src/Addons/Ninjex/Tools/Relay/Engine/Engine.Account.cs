using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        public partial class RelayEngine
        {
            public bool TryGetAccountLockReason(Account acc, out string lockShortReason, out string lockLongReason)
            {
                lockShortReason = "";
                lockLongReason = "";
                if (_engine == null)
                    return false;

                if (!_engine.CanEnterForRisk(acc, out var shortReason, out var fullReason))
                {
                    lockShortReason = shortReason;
                    lockLongReason = fullReason;
                    return true;
                }

                return false;
            }

            public bool TryGetRealizedPnl(Account acc, out double realized)
            {
                realized = 0;

                if (acc == null)
                    return false;

                try
                {
                    realized = acc.Get(AccountItem.RealizedProfitLoss, Currency.UsDollar);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            
            private bool IsProtectionEnabledForAccount(Account acc, AutoFlattenProtectionScope scope)
            {
                if (acc == null || scope == AutoFlattenProtectionScope.Disabled)
                    return false;

                if (_master != null && ReferenceEquals(acc, _master))
                    return scope == AutoFlattenProtectionScope.MasterOnly ||
                           scope == AutoFlattenProtectionScope.MasterAndFollowers;

                return scope == AutoFlattenProtectionScope.MasterAndFollowers;
            }
            
            private void OnAccountItemUpdate(object sender, AccountItemEventArgs e)
            {
                if (e?.Account == null) 
                    return;

                if (e.Currency != Currency.UsDollar) 
                    return;

                if (e.AccountItem != AccountItem.RealizedProfitLoss &&
                    e.AccountItem != AccountItem.UnrealizedProfitLoss)
                    return;

                var name = e.Account.Name ?? "";
                if (string.IsNullOrWhiteSpace(name)) 
                    return;

                lock (_pnlByAccount)
                {
                    if (!_pnlByAccount.TryGetValue(name, out var snap))
                    {
                        snap = new PnlSnap();
                        _pnlByAccount[name] = snap;
                    }

                    if (e.AccountItem == AccountItem.RealizedProfitLoss)
                        snap.Realized = e.Value;
                    else
                        snap.Unrealized = e.Value;
                }
            }
        }
    }
}