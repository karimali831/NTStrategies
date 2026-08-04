#region Using declarations
using System;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexPremarketRangeResearch
    {
        private void Diagnostic(DateTime eventTime, string format, params object[] args)
        {
            if (!EnableDiagnostics)
                return;

            string message;
            try { message = string.Format(format, args); }
            catch { message = format; }

            Print($"{eventTime:yyyy-MM-dd HH:mm:ss.fff} | {Name} | {message}");
        }
    }
}
