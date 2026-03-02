#region Using declarations
using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using NinjaTrader.Code;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite
{
    public sealed class MenuNode
    {
        public string Header { get; }
        public string AutomationId { get; }
        public Action OnClick { get; }
        public MenuNode[] Children { get; }

        public bool IsLeaf => Children == null || Children.Length == 0;

        public MenuNode(string header, string automationId, Action onClick = null, MenuNode[] children = null)
        {
            Header = header ?? "";
            AutomationId = automationId ?? "";
            OnClick = onClick;
            Children = children ?? Array.Empty<MenuNode>();
        }
    }

    public sealed class MenuManager : IDisposable
    {
        private readonly Window controlCenterWindow;

        private MenuItem toolsRoot;
        private bool hooked;

        private const string SuiteSeparatorAutomationId = "SafeTradeSuite_ToolsSeparator";

        public MenuManager(Window controlCenterWindow)
        {
            this.controlCenterWindow = controlCenterWindow;
        }

        public MenuItem FindToolsRootMenuItem()
        {
            if (controlCenterWindow == null) return null;

            // In NT 8.1.x, top menu items are MenuItems in the visual tree.
            return FindMenuItemByHeader(controlCenterWindow, "Tools");
        }

        public void HookToolsMenu(MenuItem toolsRoot, MenuNode[] suiteNodes)
        {
            if (toolsRoot == null) return;

            this.toolsRoot = toolsRoot;

            if (!hooked)
            {
                toolsRoot.SubmenuOpened -= ToolsRoot_SubmenuOpened;
                toolsRoot.SubmenuOpened += ToolsRoot_SubmenuOpened;
                hooked = true;
            }

            InjectMenuTree(toolsRoot, suiteNodes);
        }

        private void ToolsRoot_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            // Re-inject on open (NT rebuilds menus sometimes, and recompiles can cause duplicates without guards)
            if (toolsRoot == null) return;
        }

        private void InjectMenuTree(MenuItem toolsRoot, MenuNode[] suiteNodes)
        {
            if (toolsRoot == null) return;
            if (suiteNodes == null || suiteNodes.Length == 0) return;

            var refMi = FindFirstNativeMenuItem(toolsRoot);

            EnsureSuiteSeparator(toolsRoot);

            foreach (var rootNode in suiteNodes)
            {
                EnsureNode(toolsRoot, rootNode, refMi);
            }
        }

        private void EnsureSuiteSeparator(MenuItem toolsRoot)
        {
            var existing = FindChildSeparatorByAutomationId(toolsRoot, SuiteSeparatorAutomationId);
            if (existing != null) return;

            var sep = new Separator();
            AutomationProperties.SetAutomationId(sep, SuiteSeparatorAutomationId);
            toolsRoot.Items.Add(sep);
        }

        private void EnsureNode(ItemsControl parent, MenuNode node, MenuItem refMi)
        {
            if (parent == null || node == null) return;

            var existing = FindChildMenuItemByAutomationId(parent, node.AutomationId)
                           ?? FindChildMenuItemByHeader(parent, node.Header);

            MenuItem mi;
            if (existing == null)
            {
                mi = new MenuItem { Header = node.Header };
                if (!string.IsNullOrWhiteSpace(node.AutomationId))
                    AutomationProperties.SetAutomationId(mi, node.AutomationId);

                ApplyMenuStyleFromReference(mi, refMi);

                if (node.IsLeaf && node.OnClick != null)
                {
                    mi.Click += (s, e) => node.OnClick();
                }

                parent.Items.Add(mi);
            }
            else
            {
                mi = existing;

                // Keep style consistent even if it already existed
                ApplyMenuStyleFromReference(mi, refMi);

                // Ensure leaf click wired (without duplication)
                if (node.IsLeaf && node.OnClick != null)
                {
                    mi.Click -= LeafClickShim;
                    mi.Click += LeafClickShim;

                    void LeafClickShim(object s, RoutedEventArgs e)
                    {
                        node.OnClick();
                    }
                }
            }

            // Children
            if (!node.IsLeaf)
            {
                foreach (var child in node.Children)
                    EnsureNode(mi, child, refMi);
            }
        }

        private MenuItem FindFirstNativeMenuItem(MenuItem toolsRoot)
        {
            // Use first menu item that is not our suite root as a font/style reference
            foreach (var obj in toolsRoot.Items)
            {
                var mi = obj as MenuItem;
                if (mi == null) continue;

                var header = mi.Header as string;
                if (string.IsNullOrWhiteSpace(header)) continue;

                // Skip our own, if present
                var id = AutomationProperties.GetAutomationId(mi);
                if (!string.IsNullOrWhiteSpace(id) && id.StartsWith("SafeTradeSuite_", StringComparison.Ordinal))
                    continue;

                return mi;
            }

            return null;
        }

        private void ApplyMenuStyleFromReference(MenuItem target, MenuItem reference)
        {
            if (target == null || reference == null) return;

            target.FontFamily = reference.FontFamily;
            target.FontSize = reference.FontSize;
            target.FontStyle = reference.FontStyle;
            target.FontWeight = reference.FontWeight;
        }

        private Separator FindChildSeparatorByAutomationId(MenuItem parent, string automationId)
        {
            if (parent == null) return null;

            foreach (var obj in parent.Items)
            {
                if (!(obj is Separator sep)) continue;

                var id = AutomationProperties.GetAutomationId(sep);
                if (string.Equals(id, automationId, StringComparison.Ordinal))
                    return sep;
            }

            return null;
        }

        private MenuItem FindChildMenuItemByAutomationId(ItemsControl parent, string automationId)
        {
            if (parent == null || string.IsNullOrWhiteSpace(automationId)) return null;

            foreach (var obj in parent.Items)
            {
                var mi = obj as MenuItem;
                if (mi == null) continue;

                var id = AutomationProperties.GetAutomationId(mi);
                if (string.Equals(id, automationId, StringComparison.Ordinal))
                    return mi;
            }

            return null;
        }

        private MenuItem FindChildMenuItemByHeader(ItemsControl parent, string headerText)
        {
            if (parent == null || string.IsNullOrWhiteSpace(headerText)) return null;

            foreach (var obj in parent.Items)
            {
                var mi = obj as MenuItem;
                if (mi == null) continue;

                var h = mi.Header as string;
                if (!string.IsNullOrWhiteSpace(h) &&
                    string.Equals(h.Trim(), headerText.Trim(), StringComparison.OrdinalIgnoreCase))
                    return mi;
            }

            return null;
        }

        private MenuItem FindMenuItemByHeader(DependencyObject root, string headerText)
        {
            if (root == null) return null;

            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);

                if (child is MenuItem mi)
                {
                    var h = mi.Header as string;
                    if (!string.IsNullOrWhiteSpace(h) &&
                        string.Equals(h.Trim(), headerText, StringComparison.OrdinalIgnoreCase))
                        return mi;
                }

                var nested = FindMenuItemByHeader(child, headerText);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        public void Dispose()
        {
            if (toolsRoot != null)
                toolsRoot.SubmenuOpened -= ToolsRoot_SubmenuOpened;

            toolsRoot = null;
            hooked = false;
        }
    }
}