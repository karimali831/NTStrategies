using System;
using System.Globalization;
using System.IO;
using System.Xml;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        // Phase 1: read only StopLoss + Target ticks (single target)
        public static bool TryReadAtmTemplateBasic(string templateName, out int stopTicks, out int targetTicks)
        {
            stopTicks = 0;
            targetTicks = 0;

            templateName = (templateName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(templateName) || templateName.Equals("None", StringComparison.OrdinalIgnoreCase))
                return false;

            try
            {
                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var folder = Path.Combine(docs, "NinjaTrader 8", "templates", "AtmStrategy");
                var path = Path.Combine(folder, templateName + ".xml");

                if (!File.Exists(path))
                    return false;

                var xml = new XmlDocument();
                xml.Load(path);

                // Your sample file contains: <StopLoss>80</StopLoss> and <Target>120</Target>
                // We’ll read the first occurrence of each.
                var stopNode = xml.SelectSingleNode("//*[local-name()='StopLoss']");
                var tgtNode  = xml.SelectSingleNode("//*[local-name()='Target']");

                if (stopNode != null)
                    stopTicks = ParseIntSafe(stopNode.InnerText);

                if (tgtNode != null)
                    targetTicks = ParseIntSafe(tgtNode.InnerText);

                return stopTicks > 0 || targetTicks > 0;
            }
            catch
            {
                return false;
            }
        }

        private static int ParseIntSafe(string s)
        {
            if (int.TryParse((s ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                return v;
            return 0;
        }
    }
}