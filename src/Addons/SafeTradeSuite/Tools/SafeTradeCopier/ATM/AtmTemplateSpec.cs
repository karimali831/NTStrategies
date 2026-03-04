using System;
using System.Globalization;
using System.IO;
using System.Xml;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        internal sealed class AtmTemplateSpec
        {
            public string TemplateName;
            public string TimeInForce;      // e.g. "Gtc"
            public string CalculationMode;  // e.g. "Ticks"
            public int Quantity;            // first bracket
            public double StopLoss;         // first bracket (ticks if CalculationMode==Ticks)
            public double Target;           // first bracket
        }

        private static bool TryLoadAtmTemplateSpec(string templateName, out AtmTemplateSpec spec)
        {
            spec = null;

            templateName = NormalizeAtm(templateName);
            if (string.IsNullOrWhiteSpace(templateName) || templateName.Equals("None", StringComparison.OrdinalIgnoreCase))
                return false;

            try
            {
                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var folder = Path.Combine(docs, "NinjaTrader 8", "templates", "AtmStrategy");
                var path = Path.Combine(folder, templateName + ".xml");

                if (!File.Exists(path))
                    return false;

                var doc = new XmlDocument();
                doc.Load(path);

                var atm = doc.SelectSingleNode("//AtmStrategy");
                if (atm == null) return false;

                string Read(string name)
                {
                    var n = atm.SelectSingleNode(name);
                    return n?.InnerText?.Trim();
                }

                var tif = Read("TimeInForce") ?? "";
                var calc = Read("CalculationMode") ?? "";

                var firstBracket = atm.SelectSingleNode(".//Bracket");

                if (firstBracket == null) return false;

                string B(string name)
                {
                    var n = firstBracket.SelectSingleNode(name);
                    return n?.InnerText?.Trim();
                }

                var qtyText = B("Quantity");
                var stopText = B("StopLoss");
                var tgtText = B("Target");

                if (!int.TryParse(qtyText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var qty))
                    qty = 1;

                if (!double.TryParse(stopText, NumberStyles.Any, CultureInfo.InvariantCulture, out var stop))
                    stop = 0;

                if (!double.TryParse(tgtText, NumberStyles.Any, CultureInfo.InvariantCulture, out var tgt))
                    tgt = 0;

                spec = new AtmTemplateSpec
                {
                    TemplateName = templateName,
                    TimeInForce = tif,
                    CalculationMode = calc,
                    Quantity = qty,
                    StopLoss = stop,
                    Target = tgt
                };

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}