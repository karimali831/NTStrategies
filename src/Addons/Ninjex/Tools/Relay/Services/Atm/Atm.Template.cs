using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        private const string InheritMasterBracketOption = "Inherit Master";
        private const string FollowMasterExitBracketOption = "Follow Master Exit";

        private static string NormalizeAtm(string s)
        {
            s = (s ?? "").Trim();
            if (string.IsNullOrWhiteSpace(s)) return "None";
            return s;
        }

        private void LoadFollowerAtmTemplatesIntoSuppress(ComboBox box, string accName)
        {
            if (box == null)
                return;

            using (BeginSessionUiSuppression())
            {
                LoadAtmTemplatesInto(box, includeInherit: true);

                var atm = _activeInstrumentSession != null &&
                          _activeInstrumentSession.FollowerAtmOverrides.TryGetValue(accName, out var av)
                    ? NormalizeAtm(av)
                    : InheritMasterBracketOption;

                if (box.Items.Contains(atm))
                    box.SelectedItem = atm;
                else if (box.Items.Contains(InheritMasterBracketOption))
                    box.SelectedItem = InheritMasterBracketOption;
                else
                    box.SelectedItem = box.Items.Count > 0 ? box.Items[0] : null;
            }
        }
        
        private void LoadMasterAtmTemplatesIntoSuppress(ComboBox box)
        {
            if (box == null)
                return;

            using (BeginSessionUiSuppression())
            {
                LoadAtmTemplatesInto(box, includeInherit: false);

                var atm = NormalizeAtm(_activeInstrumentSession?.MasterAtm);

                if (box.Items.Contains(atm))
                    box.SelectedItem = atm;
                else if (box.Items.Contains("None"))
                    box.SelectedItem = "None";
                else
                    box.SelectedItem = box.Items.Count > 0 ? box.Items[0] : null;
            }
        }

        private static void LoadAtmTemplatesInto(ComboBox combo, bool includeInherit)
        {
            if (combo == null)
                return;

            var current = combo.SelectedItem as string;

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

            if (!string.IsNullOrWhiteSpace(current) && finalItems.Contains(current))
                combo.SelectedItem = current;
        }
    }
}