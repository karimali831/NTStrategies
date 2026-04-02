using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using NinjaTrader.NinjaScript.AddOns.Ninjex.Menu;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex
{
    public class AddOns : AddOnBase
    {
        private MenuManager _menuManager;
        private readonly string _instanceId = Guid.NewGuid().ToString("N").Substring(0, 8);

        protected override void OnStateChange()
        {
            NinjexRuntime.PrintLog($"AddOns[{_instanceId}] OnStateChange -> {State}");

            if (State == State.SetDefaults)
            {
                Name = "Ninjex";
                Description = "Ninjex - tools for safer execution and account operations.";
                return;
            }

            if (State == State.Terminated)
            {
                try
                {
                    _menuManager?.Dispose();
                    _menuManager = null;

                    NinjexRuntime.DisposeRelayIfExists();
                }
                catch (Exception ex)
                {
                    NinjexRuntime.PrintLog("AddOns terminate error: " + ex);
                }
            }
        }

        protected override void OnWindowCreated(Window w)
        {
            if (w == null)
                return;

            try
            {
                var typeName = w.GetType().FullName ?? w.GetType().Name;
                if (typeName.IndexOf("ControlCenter", StringComparison.OrdinalIgnoreCase) < 0)
                    return;

                w.Dispatcher.InvokeAsync(async () =>
                {
                    for (var i = 0; i < 30; i++)
                    {
                        if (TryInitMenu(w))
                            return;

                        await Task.Delay(100);
                    }
                    

                    NinjexRuntime.PrintLog($"AddOns[{_instanceId}] Failed to initialize menu.");
                }, DispatcherPriority.Loaded);
            }
            catch (Exception ex)
            {
                NinjexRuntime.PrintLog("OnWindowCreated error: " + ex);
            }
        }

        private bool TryInitMenu(Window cc)
        {
            if (cc == null)
                return false;

            _menuManager?.Dispose();
            _menuManager = new MenuManager(cc);

            var toolsRoot = _menuManager.FindToolsRootMenuItem();
            if (toolsRoot == null)
                return false;

            var nodes = NinjexMenuNodes.Build(() =>
            {
                NinjexRuntime.DisposeRelayIfExists();
                NinjexRuntime.GetOrCreateCopier().Show();
            });

            _menuManager.HookToolsMenu(toolsRoot, nodes);
            
            return true;
        }
    }
}