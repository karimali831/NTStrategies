using System;
using System.IO;
using NinjaTrader.Core;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private static string CrashLogPath =>
            Path.Combine(Globals.UserDataDir, "log", "SafeTradeCopier.crash.log");
        
        

        private static void LogUnhandled(string scope, Exception ex)
        {
            try
            {
                if (ex == null)
                {
                    SafeTradeSuiteRuntime.PrintLog($"[UNHANDLED] scope={scope} ex=<null>");
                    return;
                }

                SafeTradeSuiteRuntime.PrintLog($"[UNHANDLED] scope={scope}");
                SafeTradeSuiteRuntime.PrintLog(ex.ToString());

                var inner = ex.InnerException;
                var depth = 1;

                while (inner != null)
                {
                    SafeTradeSuiteRuntime.PrintLog(
                        $"[UNHANDLED INNER {depth}] {inner.GetType().FullName}: {inner.Message}");
                    SafeTradeSuiteRuntime.PrintLog(inner.ToString());

                    inner = inner.InnerException;
                    depth++;
                }
            }
            catch
            {
                // never let logging crash the UI
            }
        }
    }
}