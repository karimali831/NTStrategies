using System.Windows.Controls;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private sealed class FollowerRow
        {
            public Account Account;

            public Border StatusDot;
            public CheckBox EnabledCheck;
            public TextBlock AccountText;

            public TextBox QtyOverrideBox;
            public ComboBox AtmOverrideBox;
            public TextBlock PnlText;
            public Button FlattenBtn;
            public ProgressBar PnlBar;
            public TextBlock PnlBarStatusText;

            public string AccountName => Account?.Name ?? "";
            public CheckBox IncludeCheck => EnabledCheck;
            public CheckBox OverrideCheck => null;
            public TextBox QtyBox => QtyOverrideBox;
            public ComboBox AtmBox => AtmOverrideBox;
        }
    }
}
