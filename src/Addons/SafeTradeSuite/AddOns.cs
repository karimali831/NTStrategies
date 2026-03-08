using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Menu;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite
{
    public class AddOns : AddOnBase
    {
        private Window _controlCenterWindow;
        private MenuManager _menuManager;
        private readonly string _instanceId = Guid.NewGuid().ToString("N").Substring(0, 8);

        protected override void OnStateChange()
        {
            SafeTradeSuiteRuntime.PrintLog($"AddOns[{_instanceId}] OnStateChange -> {State}");

            if (State == State.SetDefaults)
            {
                Name = "SafeTradeSuite";
                Description = "SafeTrade Suite - tools for safer execution and account operations.";
            }
            else if (State == State.Terminated)
            {
                _menuManager?.Dispose();
                _menuManager = null;
                _controlCenterWindow = null;

                SafeTradeSuiteRuntime.DisposeCopierIfExists();
            }
        }
        
        protected override void OnWindowCreated(Window w)
        {
            SafeTradeSuiteRuntime.PrintLog($"AddOns[{_instanceId}] OnWindowCreated");

            try
            {
                if (w == null)
                    return;

                var typeName = w.GetType().FullName ?? w.GetType().Name;
                var title = w.Title ?? "";

                SafeTradeSuiteRuntime.PrintLog("Window type: " + typeName);
                SafeTradeSuiteRuntime.PrintLog("Window title: " + (string.IsNullOrWhiteSpace(title) ? "<null-title>" : title));
                
                if (typeName.IndexOf("ControlCenter", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    SafeTradeSuiteRuntime.PrintLog("Skipping non-ControlCenter window.");
                    return;
                }

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
            catch (Exception ex)
            {
                SafeTradeSuiteRuntime.PrintLog("OnWindowCreated error: " + ex);
            }
        }
        

        private bool TryInitMenu(Window cc)
        {
            if (cc == null)
                return false;

            if (_menuManager == null)
                _menuManager = new MenuManager(cc);

            var toolsRoot = _menuManager.FindToolsRootMenuItem();
            if (toolsRoot == null)
                return false;

            var nodes = SafeTradeSuiteMenuNodes.Build(() => SafeTradeSuiteRuntime.GetOrCreateCopier().Show());

            _menuManager.HookToolsMenu(toolsRoot, nodes);
            return true;
        }
    }
}