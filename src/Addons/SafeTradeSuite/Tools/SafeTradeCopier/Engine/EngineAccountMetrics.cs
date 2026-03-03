#region Using declarations
using System;
using System.Collections.Generic;
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
                if (acc == null) return;
                acc.AccountItemUpdate -= OnAccountItemUpdate;
                acc.AccountItemUpdate += OnAccountItemUpdate;
            }

            private void UnsubscribePnl(Account acc)
            {
                if (acc == null) return;
                acc.AccountItemUpdate -= OnAccountItemUpdate;
            }

            private void OnAccountItemUpdate(object sender, AccountItemEventArgs e)
            {
                // cache only USD PnL
                Log("P1");
                if (e?.Account == null) return;
                if (e.Currency != Currency.UsDollar) return;

                Log("P2");
                
                var name = e.Account.Name ?? "";
                if (string.IsNullOrWhiteSpace(name)) return;

                Log($"P3 account name: {name}");

                lock (_gate)
                {
                    Log("P4");
                    
                    if (!_pnlByAccount.TryGetValue(name, out var snap))
                    {
                        snap = new PnlSnap();
                        _pnlByAccount[name] = snap;
                    }
                    
                    Log("P5");

                    if (e.AccountItem == AccountItem.RealizedProfitLoss)
                        snap.Realized = e.Value;
                    
                    Log("P6 Realized" + snap.Realized);

                    if (e.AccountItem == AccountItem.UnrealizedProfitLoss)
                        snap.Unrealized = e.Value;
                    
                    Log("O7 Unrealized" + snap.Unrealized);
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

                foreach (var p in acc.Positions)
                {
                    if (p?.Instrument == null) continue;
                    if (!SameInstrument(p.Instrument, instr)) continue;

                    var qty = (int)Math.Round((double)p.Quantity, MidpointRounding.AwayFromZero);
                    if (p.MarketPosition == MarketPosition.Short) return -Math.Abs(qty);
                    if (p.MarketPosition == MarketPosition.Long)  return  Math.Abs(qty);
                    return 0;
                }

                return 0;
            }
        }
    }
}