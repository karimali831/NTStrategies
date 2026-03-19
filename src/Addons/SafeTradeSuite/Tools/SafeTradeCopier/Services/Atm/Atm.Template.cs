using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private const string InheritMasterBracketOption = "Inherit Master";
        private const string FollowMasterExitBracketOption = "Follow Master Exit";

        private static string NormalizeAtm(string s)
        {
            s = (s ?? "").Trim();
            if (string.IsNullOrWhiteSpace(s)) return "None";
            return s;
        }

        private static bool IsFollowerFollowMasterExitOption(string s)
        {
            return string.Equals(
                NormalizeAtm(s),
                FollowMasterExitBracketOption,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFollowerInheritMasterOption(string s)
        {
            return string.Equals(
                NormalizeAtm(s),
                InheritMasterBracketOption,
                StringComparison.OrdinalIgnoreCase);
        }

         private static void LoadAtmTemplatesInto(ComboBox combo, bool includeInherit)
        {
            if (combo == null)
                return;

            var existingSelection =
                NormalizeAtm(combo.SelectedItem as string) != "None"
                    ? NormalizeAtm(combo.SelectedItem as string)
                    : NormalizeAtm(combo.Text);

            var items = new List<string>();

            if (includeInherit)
            {
                items.Add(InheritMasterBracketOption);
                items.Add(FollowMasterExitBracketOption);
            }

            items.Add("None");

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

            var special = new List<string>();
            if (includeInherit)
            {
                special.Add(InheritMasterBracketOption);
                special.Add(FollowMasterExitBracketOption);
            }
            special.Add("None");

            var atmItems = items
                .Except(special, StringComparer.OrdinalIgnoreCase)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var finalItems = new List<string>();
            foreach (var s in special.Distinct(StringComparer.OrdinalIgnoreCase))
                finalItems.Add(s);

            finalItems.AddRange(atmItems);

            combo.ItemsSource = finalItems;

            if (!string.IsNullOrWhiteSpace(existingSelection) &&
                finalItems.Any(x => string.Equals(x, existingSelection, StringComparison.OrdinalIgnoreCase)))
            {
                combo.SelectedItem = finalItems.First(x =>
                    string.Equals(x, existingSelection, StringComparison.OrdinalIgnoreCase));
            }
            else if (includeInherit && finalItems.Contains(InheritMasterBracketOption))
            {
                combo.SelectedItem = InheritMasterBracketOption;
            }
            else if (finalItems.Contains("None"))
            {
                combo.SelectedItem = "None";
            }
            else
            {
                combo.SelectedItem = finalItems.FirstOrDefault();
            }
        }
    }
}