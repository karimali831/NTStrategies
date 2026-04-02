using System.Windows.Controls;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
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
