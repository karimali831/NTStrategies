#region Using declarations 
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Menu;
using NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools;
using NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier;

#endregion

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite
{
    public class AddOns : AddOnBase
    {
        private Window _controlCenterWindow;
        private MenuManager _menuManager;
        private ToolRegistry _tools;
        
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "SafeTradeSuite";
                Description = "SafeTrade Suite - tools for safer execution and account operations.";
                
                _tools = new ToolRegistry();
                _tools.RegisterSingleton(SafeTradeSuiteMenuNodes.ToolKeys.SafeTradeCopier, () => new SafeTradeCopierTool());
            }
            else if (State == State.Terminated)
            {
                _menuManager?.Dispose();
                _menuManager = null;

                _tools?.Dispose();
                _tools = null;

                _controlCenterWindow = null;
            }
        }

        protected override void OnWindowCreated(Window w)
        {
            if (w == null) return;

            var typeName = w.GetType().FullName ?? w.GetType().Name;
            if (typeName.IndexOf("ControlCenter", StringComparison.OrdinalIgnoreCase) < 0)
                return;

            _controlCenterWindow = w;

            _controlCenterWindow.Dispatcher.InvokeAsync(async () =>
            {
                for (var i = 0; i < 50; i++)
                {
                    if (TryInitMenu(_controlCenterWindow))
                        break;

                    await Task.Delay(100);
                }
            }, DispatcherPriority.Loaded);
        }

        protected override void OnWindowDestroyed(Window w)
        {
            if (w == null) return;

            if (_controlCenterWindow != null && ReferenceEquals(w, _controlCenterWindow))
            {
                _menuManager?.Dispose();
                _menuManager = null;

                // Keep tools alive across CC rebuilds, or dispose here if you prefer.
                // I’m leaving them alive so tools don’t get torn down when CC recreates visuals.
                _controlCenterWindow = null;
            }
        }

        private bool TryInitMenu(Window cc)
        {
            if (cc == null) return false;

            if (_menuManager == null)
                _menuManager = new MenuManager(cc);
            

            var toolsRoot = _menuManager.FindToolsRootMenuItem();
            if (toolsRoot == null) return false;

            var nodes = SafeTradeSuiteMenuNodes.Build(_tools);

            _menuManager.HookToolsMenu(toolsRoot, nodes);
            return true;
        }
    }
}