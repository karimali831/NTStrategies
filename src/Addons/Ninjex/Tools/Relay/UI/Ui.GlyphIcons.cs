using System.Windows.Media;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        private const string CheckIcon = "✓";
        
        private const string CrossIcon = "❌";
        
        private static readonly Geometry CheckGlyph =
            Geometry.Parse("M 2,6 L 4.5,8.5 L 10,2");

        private static readonly Geometry CrossGlyph =
            Geometry.Parse("M 3,3 L 9,9 M 9,3 L 3,9");
    }
}