using System.Windows.Controls;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private sealed class FollowerRow
        {
            public Account Account;
            public CheckBox EnabledCheck;
            public TextBox QtyOverrideBox;
            public ComboBox BracketOverrideBox;
            public TextBlock Position;
            public TextBlock PnlText;
            public Button FlattenBtn;
            public ProgressBar PnlBar;
            public TextBlock PnlBarStatusText;
            public Button FreeTradeBtn { get; set; }
            public string AccountName => Account?.Name ?? "";
            public TextBlock GuardText { get; set; }
        }
    }
}
