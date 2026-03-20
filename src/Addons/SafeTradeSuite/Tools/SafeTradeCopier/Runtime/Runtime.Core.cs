using System;
using System.Threading.Tasks;
using NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite
{
    internal static partial class SafeTradeSuiteRuntime
    {
        private static SafeTradeCopierTool _copier;

        public static SafeTradeCopierTool GetOrCreateCopier()
        {
            if (_copier == null)
                _copier = new SafeTradeCopierTool();
            
            return _copier;
        }

        public static void DisposeCopierIfExists()
        {
            if (_copier == null)
                return;

            try
            {
                _copier.Dispose();
            }
            catch (Exception ex)
            {
                PrintLog("DisposeCopierIfExists error: " + ex);
            }
            finally
            {
                _copier = null;
            }
        }
        
        public static void PrintLog(string msg)
        {
            Code.Output.Process(
                $"[SafeTradeSuite DEBUG] {DateTime.Now:HH:mm:ss.fff} {msg}",
                PrintTo.OutputTab2);
        }
    }
}