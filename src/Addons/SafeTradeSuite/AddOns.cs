using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Menu;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite
{
    public class AddOns : AddOnBase
    {
        private Window _controlCenterWindow;
        private MenuManager _menuManager;
        private readonly string _instanceId = Guid.NewGuid().ToString("N").Substring(0, 8);
        private bool _bootstrappedOpenCharts;

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
                _bootstrappedOpenCharts = false;

                SafeTradeSuiteRuntime.DisposeCopierIfExists();
            }
        }

        public static System.Collections.Generic.List<string> GetChartInstruments()
        {
            return SafeTradeSuiteRuntime.GetChartInstrumentsSnapshot();
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

                RegisterChartWindowIfApplicable(w);

                if (typeName.IndexOf("ControlCenter", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    SafeTradeSuiteRuntime.PrintLog("Skipping non-ControlCenter window.");
                    return;
                }

                _controlCenterWindow = w;

                if (!_bootstrappedOpenCharts)
                {
                    _bootstrappedOpenCharts = true;
                    BootstrapExistingChartWindows();
                }

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

        protected override void OnWindowDestroyed(Window w)
        {
            SafeTradeSuiteRuntime.PrintLog($"AddOns[{_instanceId}] OnWindowDestroyed");

            try
            {
                if (w == null)
                    return;

                var typeName = w.GetType().FullName ?? w.GetType().Name;
                if (typeName.IndexOf("Chart", StringComparison.OrdinalIgnoreCase) < 0)
                    return;

                var title = w.Title ?? "";
                if (!title.StartsWith("Chart - ", StringComparison.OrdinalIgnoreCase))
                    return;

                var instrument = title.Substring("Chart - ".Length).Trim();
                SafeTradeSuiteRuntime.RemoveChartInstrument(instrument);
            }
            catch (Exception ex)
            {
                SafeTradeSuiteRuntime.PrintLog("OnWindowDestroyed error: " + ex);
            }
        }

        private void BootstrapExistingChartWindows()
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null)
                {
                    SafeTradeSuiteRuntime.PrintLog("BootstrapExistingChartWindows: no app dispatcher.");
                    return;
                }

                dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        var windows = Application.Current?.Windows?.Cast<Window>().ToList();
                        if (windows == null)
                        {
                            SafeTradeSuiteRuntime.PrintLog("BootstrapExistingChartWindows: windows collection is null.");
                            return;
                        }

                        SafeTradeSuiteRuntime.PrintLog($"BootstrapExistingChartWindows: scanning {windows.Count} windows.");

                        foreach (var win in windows)
                        {
                            RegisterChartWindowIfApplicable(win);
                        }

                        var snapshot = SafeTradeSuiteRuntime.GetChartInstrumentsSnapshot();
                        SafeTradeSuiteRuntime.PrintLog(
                            snapshot.Count == 0
                                ? "BootstrapExistingChartWindows: no chart instruments found."
                                : "BootstrapExistingChartWindows: " + string.Join(", ", snapshot));
                    }
                    catch (Exception ex)
                    {
                        SafeTradeSuiteRuntime.PrintLog("BootstrapExistingChartWindows failed: " + ex);
                    }
                }, DispatcherPriority.Loaded);
            }
            catch (Exception ex)
            {
                SafeTradeSuiteRuntime.PrintLog("BootstrapExistingChartWindows outer failure: " + ex);
            }
        }

        private static void RegisterChartWindowIfApplicable(Window w)
        {
            if (w == null)
                return;

            var typeName = w.GetType().FullName ?? w.GetType().Name;
            if (typeName.IndexOf("Chart", StringComparison.OrdinalIgnoreCase) < 0)
                return;

            var title = w.Title ?? "";
            if (!title.StartsWith("Chart - ", StringComparison.OrdinalIgnoreCase))
                return;

            var instrument = title.Substring("Chart - ".Length).Trim();
            if (string.IsNullOrWhiteSpace(instrument))
                return;

            SafeTradeSuiteRuntime.RegisterChartInstrument(instrument);
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