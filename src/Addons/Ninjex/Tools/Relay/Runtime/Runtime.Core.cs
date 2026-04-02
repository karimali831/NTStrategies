using System;
using NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex
{
    internal partial class NinjexRuntime
    {
        private static RelayTool _relay;

        public static RelayTool GetOrCreateCopier()
        {
            if (_relay == null)
                _relay = new RelayTool();
            
            return _relay;
        }

        public static void DisposeRelayIfExists()
        {
            if (_relay == null)
                return;

            try
            {
                _relay.Dispose();
            }
            catch (Exception ex)
            {
                PrintLog("DisposeRelayIfExists error: " + ex);
            }
            finally
            {
                _relay = null;
            }
        }
        
        public static void PrintLog(string msg)
        {
            Code.Output.Process(
                $"[Ninjex DEBUG] {DateTime.Now:HH:mm:ss.fff} {msg}",
                PrintTo.OutputTab2);
        }
    }
}