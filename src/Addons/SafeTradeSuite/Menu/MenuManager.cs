using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Menu
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
        private readonly Window _controlCenterWindow;
        private MenuItem _toolsRoot;
        private bool _hooked;

        private const string SuiteSeparatorAutomationId = "SafeTradeSuite_ToolsSeparator";

        public MenuManager(Window controlCenterWindow)
        {
            _controlCenterWindow = controlCenterWindow;
        }

        public MenuItem FindToolsRootMenuItem()
        {
            if (_controlCenterWindow == null) return null;

            // In NT 8.1.x, top menu items are MenuItems in the visual tree.
            return FindMenuItemByHeader(_controlCenterWindow, "Tools");
        }

        public void HookToolsMenu(MenuItem toolsRoot, MenuNode[] suiteNodes)
        {
            if (toolsRoot == null) return;

            _toolsRoot = toolsRoot;

            if (!_hooked)
            {
                toolsRoot.SubmenuOpened -= ToolsRoot_SubmenuOpened;
                toolsRoot.SubmenuOpened += ToolsRoot_SubmenuOpened;
                _hooked = true;
            }

            InjectMenuTree(toolsRoot, suiteNodes);
        }

        private void ToolsRoot_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            // Re-inject on open (NT rebuilds menus sometimes, and recompiles can cause duplicates without guards)
            if (_toolsRoot == null) return;
        }

        private static void InjectMenuTree(MenuItem toolsRoot, MenuNode[] suiteNodes)
        {
            if (toolsRoot == null) return;
            if (suiteNodes == null || suiteNodes.Length == 0) return;

            RemoveExistingSuiteNodes(toolsRoot);

            var refMi = FindFirstNativeMenuItem(toolsRoot);

            EnsureSuiteSeparator(toolsRoot);

            foreach (var rootNode in suiteNodes)
                EnsureNode(toolsRoot, rootNode, refMi);
        }

        private static void EnsureSuiteSeparator(MenuItem toolsRoot)
        {
            var existing = FindChildSeparatorByAutomationId(toolsRoot, SuiteSeparatorAutomationId);
            if (existing != null) return;

            var sep = new Separator();
            AutomationProperties.SetAutomationId(sep, SuiteSeparatorAutomationId);
            toolsRoot.Items.Add(sep);
        }

        private static void EnsureNode(ItemsControl parent, MenuNode node, MenuItem refMi)
        {
            if (parent == null || node == null)
                return;

            var existing = FindChildMenuItemByAutomationId(parent, node.AutomationId)
                           ?? FindChildMenuItemByHeader(parent, node.Header);

            MenuItem mi;
            if (existing == null)
            {
                mi = new MenuItem
                {
                    Header = node.Header
                };

                if (!string.IsNullOrWhiteSpace(node.AutomationId))
                    AutomationProperties.SetAutomationId(mi, node.AutomationId);

                ApplyMenuStyleFromReference(mi, refMi);
                parent.Items.Add(mi);
            }
            else
            {
                mi = existing;
                mi.Header = node.Header;
                ApplyMenuStyleFromReference(mi, refMi);
            }

            if (node.IsLeaf)
            {
                mi.Tag = node.OnClick;

                mi.Click -= OnSuiteLeafMenuClick;
                mi.Click += OnSuiteLeafMenuClick;
            }

            if (!node.IsLeaf)
            {
                foreach (var child in node.Children)
                    EnsureNode(mi, child, refMi);
            }
        }
        
        private static void RemoveExistingSuiteNodes(MenuItem toolsRoot)
        {
            if (toolsRoot == null)
                return;

            for (var i = toolsRoot.Items.Count - 1; i >= 0; i--)
            {
                var obj = toolsRoot.Items[i];

                if (obj is MenuItem mi)
                {
                    var id = AutomationProperties.GetAutomationId(mi);
                    if (!string.IsNullOrWhiteSpace(id) &&
                        id.StartsWith("SafeTradeSuite_", StringComparison.Ordinal))
                    {
                        toolsRoot.Items.RemoveAt(i);
                    }
                }
                else if (obj is Separator sep)
                {
                    var id = AutomationProperties.GetAutomationId(sep);
                    if (string.Equals(id, SuiteSeparatorAutomationId, StringComparison.Ordinal))
                        toolsRoot.Items.RemoveAt(i);
                }
            }
        }
        
        private static void OnSuiteLeafMenuClick(object sender, RoutedEventArgs e)
        {
            if (!(sender is MenuItem mi))
                return;

            if (mi.Tag is Action action)
                action();
        }

        private static MenuItem FindFirstNativeMenuItem(MenuItem toolsRoot)
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

        private static void ApplyMenuStyleFromReference(MenuItem target, MenuItem reference)
        {
            if (target == null || reference == null) return;

            target.FontFamily = reference.FontFamily;
            target.FontSize = reference.FontSize;
            target.FontStyle = reference.FontStyle;
            target.FontWeight = reference.FontWeight;
        }

        private static Separator FindChildSeparatorByAutomationId(MenuItem parent, string automationId)
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

        private static MenuItem FindChildMenuItemByAutomationId(ItemsControl parent, string automationId)
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

        private static MenuItem FindChildMenuItemByHeader(ItemsControl parent, string headerText)
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
            if (_toolsRoot != null)
                _toolsRoot.SubmenuOpened -= ToolsRoot_SubmenuOpened;

            _toolsRoot = null;
            _hooked = false;
        }
    }
}