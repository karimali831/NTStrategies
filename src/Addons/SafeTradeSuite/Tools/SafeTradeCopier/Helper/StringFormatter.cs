namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private static string FmtUsd(double v)
        {
            return v.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("en-US"));
        }
        
        private static int ParseQtyOrDefault(string s)
        {
            if (int.TryParse((s ?? "").Trim(), out var v) && v > 0) return v;
            return 0;
        }
    }
}