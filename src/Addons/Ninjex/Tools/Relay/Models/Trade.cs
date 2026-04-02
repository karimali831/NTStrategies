using System;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        private sealed class ActiveTradeRuntime
        {
            public int TradeNumber { get; set; }
            public string Key { get; set; }
            public string InstrumentName { get; set; }
            public string MarketPosition { get; set; }
            public string AccountName { get; set; }
            public DateTime EntryTimeUtc { get; set; }
            public string BracketUsed { get; set; }
            public bool IsMaster { get; set; }
            public bool BreakEvenApplied { get; set; }
            public bool WasFlattenedManually { get; set; }
            public BreakEvenTriggerKind BreakEvenKind { get; set; }
            public FlattenTriggerReason PendingFlattenReason { get; set; }
            public string PendingFlattenDetail { get; set; }
            public string EntryOrderName { get; set; }

            public int EntryFilledQty;
            public double EntryValueSum;

            public int ClosedQty;
            public double ExitValueSum;
            public DateTime? LastExitTimeUtc;
            public string LastExitOrderName;
        }

        private static string TradeKey(Account acc, Instrument instr)
        {
            return (acc?.Name ?? "") + "|" + (instr?.FullName ?? "");
        }
    }
}