using System;
using NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite
{
    internal static partial class SafeTradeSuiteRuntime
    {
        private static readonly object CopierGate = new object();
        private static SafeTradeCopierTool _copier;

        public static SafeTradeCopierTool GetOrCreateCopier()
        {
            lock (CopierGate)
            {
                if (_copier == null)
                {
                    _copier = new SafeTradeCopierTool();
                    PrintLog("Created SafeTradeCopierTool singleton.");
                }

                return _copier;
            }
        }

        public static void DisposeCopierIfExists()
        {
            lock (CopierGate)
            {
                if (_copier == null)
                    return;

                try
                {
                    _copier.Dispose();
                }
                catch (Exception ex)
                {
                    PrintLog("DisposeCopierIfExists failed: " + ex);
                }
                finally
                {
                    _copier = null;
                    PrintLog("Disposed SafeTradeCopierTool singleton.");
                }
            }
        }
        
        public static void PrintLog(string msg)
        {
            Code.Output.Process(
                $"[SafeTradeSuite DEBUG] {DateTime.Now:HH:mm:ss.fff} {msg}",
                PrintTo.OutputTab1);
        }
    }
}