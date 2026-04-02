using System;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        public sealed class AccountDailyRuntime
        {
            public string AccountName { get; set; } = "";
            public double Realized { get; set; }
            public double Unrealized { get; set; }
            public int TradesToday { get; set; }
            public bool IsLocked { get; set; }
            public string LockReason { get; set; } = "";
            public DateTime SessionDate { get; set; }
        }
        
        public sealed class GlobalDailyRuntime
        {
            public double Realized { get; set; }
            public double Unrealized { get; set; }
            public bool IsLocked { get; set; }
            public string LockReason { get; set; } = "";
            public DateTime SessionDate { get; set; }
        }
    }
}