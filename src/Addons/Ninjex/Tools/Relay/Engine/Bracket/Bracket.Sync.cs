using System;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        public partial class RelayEngine
        {
            private void SyncBracketFromOrderUpdate(Order order)
            {
                if (order == null)
                    return;

                var acc = order.Account;
                var instr = order.Instrument;
                if (acc == null || instr == null)
                    return;

                if (!TryGetActiveBracketSpec(acc, instr, out var spec) || spec == null)
                    return;

                var name = (order.Name ?? "").Trim();
                if (string.IsNullOrWhiteSpace(name))
                    return;

                var isStop = name.StartsWith("STC:SL", StringComparison.OrdinalIgnoreCase);
                var isTarget = name.StartsWith("STC:TP", StringComparison.OrdinalIgnoreCase);

                if (!isStop && !isTarget)
                    return;

                var state = order.OrderState;

                if (isStop)
                {
                    if (state == OrderState.Working ||
                        state == OrderState.Accepted ||
                        state == OrderState.Submitted)
                    {
                        var stopPrice = order.StopPrice;

                        UpdateActiveBracketSpec(acc, instr, x =>
                        {
                            x.CurrentStopPrice = stopPrice;
                            x.StopOrderName = order.Name;
                            x.StopOco = order.Oco;
                        });

                        Log($"[SYNC] STOP acc={acc.Name} instr={instr.FullName} stop={stopPrice:0.00} state={state}");
                    }
                    else if (state == OrderState.Cancelled)
                    {
                        UpdateActiveBracketSpec(acc, instr, x =>
                        {
                            x.CurrentStopPrice = 0.0;
                            x.StopOrderName = null;
                        });

                        Log($"[SYNC] STOP CANCELLED acc={acc.Name} instr={instr.FullName}");
                    }
                }

                if (isTarget)
                {
                    if (state == OrderState.Working ||
                        state == OrderState.Accepted ||
                        state == OrderState.Submitted)
                    {
                        var targetPrice = order.LimitPrice;

                        UpdateActiveBracketSpec(acc, instr, x =>
                        {
                            x.TargetPrice = targetPrice;
                            x.TargetOrderName = order.Name;
                        });

                        Log($"[SYNC] TARGET acc={acc.Name} instr={instr.FullName} target={targetPrice:0.00} state={state}");
                    }
                    else if (state == OrderState.Cancelled)
                    {
                        UpdateActiveBracketSpec(acc, instr, x =>
                        {
                            x.TargetPrice = 0.0;
                            x.TargetOrderName = null;
                        });

                        Log($"[SYNC] TARGET CANCELLED acc={acc.Name} instr={instr.FullName}");
                    }
                }
            }
        }
    }
}