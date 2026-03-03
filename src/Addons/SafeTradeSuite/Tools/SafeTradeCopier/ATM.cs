using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private static string NormalizeAtm(string s)
        {
            s = (s ?? "").Trim();
            if (string.IsNullOrWhiteSpace(s)) return "None";
            return s;
        }
        
        private static void LoadAtmTemplatesInto(ComboBox combo, bool includeInherit)
        {
            if (combo == null) return;

            var items = new List<string>();
            if (includeInherit)
                items.Add("(inherit master)");

            items.Add("None");

            try
            {
                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var folder = System.IO.Path.Combine(docs, "NinjaTrader 8", "templates", "AtmStrategy");

                if (System.IO.Directory.Exists(folder))
                {
                    foreach (var f in System.IO.Directory.GetFiles(folder, "*.xml"))
                    {
                        var name = System.IO.Path.GetFileNameWithoutExtension(f);
                        if (!string.IsNullOrWhiteSpace(name))
                            items.Add(name);
                    }
                }
            }
            catch
            {
                // non-critical
            }

            items = items
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();

            combo.ItemsSource = items;

            // default selection
            if (includeInherit && items.Contains("(inherit master)"))
                combo.SelectedItem = "(inherit master)";
            else if (items.Contains("None"))
                combo.SelectedItem = "None";
            else
                combo.SelectedItem = items.FirstOrDefault();
        }
    }
}