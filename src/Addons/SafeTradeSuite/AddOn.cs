#region Using declarations
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite
{
    public class AddOn : AddOnBase
    {
        private Window controlCenterWindow;

        private MenuManager menuManager;
        private SafeTradeCopierTool safeTradeCopier;

        private const string SuiteHeader = "SafeTrade Suite";

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "SafeTradeSuite";
                Description = "SafeTrade Suite - tools for safer execution and account operations.";
            }
            else if (State == State.Terminated)
            {
                menuManager?.Dispose();
                menuManager = null;

                safeTradeCopier?.Dispose(); 
                safeTradeCopier = null;

                controlCenterWindow = null;
            }
        }

        protected override void OnWindowCreated(Window w)
        {
            if (w == null) return;

            var typeName = w.GetType().FullName ?? w.GetType().Name;
            if (typeName.IndexOf("ControlCenter", StringComparison.OrdinalIgnoreCase) < 0)
                return;

            controlCenterWindow = w;

            controlCenterWindow.Dispatcher.InvokeAsync(async () =>
            {
                // Wait for CC visuals/menu to exist
                for (var i = 0; i < 50; i++)
                {
                    if (TryInitMenu(controlCenterWindow))
                        break;

                    await Task.Delay(100);
                }
            }, DispatcherPriority.Loaded);
        }

        protected override void OnWindowDestroyed(Window w)
        {
            if (w == null) return;
            if (controlCenterWindow != null && ReferenceEquals(w, controlCenterWindow))
            {
                menuManager?.Dispose();
                menuManager = null;

                controlCenterWindow = null;
            }
        }

        private bool TryInitMenu(Window cc)
        {
            if (cc == null) return false;

            if (menuManager == null)
                menuManager = new MenuManager(cc);

            if (safeTradeCopier == null)
                safeTradeCopier = new SafeTradeCopierTool();

            var toolsRoot = menuManager.FindToolsRootMenuItem();
            if (toolsRoot == null) return false;

            // Define suite menu tree (array of nodes)
            var nodes = new[]
            {
                new MenuNode(
                    header: SuiteHeader,
                    automationId: "SafeTradeSuite_Root",
                    children: new[]
                    {
                        new MenuNode(
                            header: "SafeTradeCopier",
                            automationId: "SafeTradeSuite_SafeTradeCopier",
                            onClick: () => safeTradeCopier.Show()
                        )
                    }
                )
            };

            menuManager.HookToolsMenu(toolsRoot, nodes);
            return true;
        }
    }
}