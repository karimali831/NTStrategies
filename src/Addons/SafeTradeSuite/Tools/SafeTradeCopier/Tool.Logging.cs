using System;
using System.IO;
using System.Text;
using NinjaTrader.Core;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private static string CrashLogPath =>
            Path.Combine(Globals.UserDataDir, "log", "SafeTradeCopier.crash.log");

        private void LogUnhandled(string context, Exception ex)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("==================================================");
                sb.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                sb.AppendLine($"Context: {context}");
                sb.AppendLine();

                if (ex == null)
                {
                    sb.AppendLine("Exception: <null>");
                }
                else
                {
                    sb.AppendLine(ex.ToString());

                    var inner = ex.InnerException;
                    var depth = 1;

                    while (inner != null)
                    {
                        sb.AppendLine();
                        sb.AppendLine($"---- Inner Exception Level {depth} ----");
                        sb.AppendLine(inner.ToString());
                        inner = inner.InnerException;
                        depth++;
                    }
                }

                File.AppendAllText(CrashLogPath, sb.ToString());
            }
            catch
            {
                // never throw from logger
            }

            try
            {
                _engine?.Log($"[CRASH] {context}");
                if (ex != null)
                    _engine?.Log(ex.ToString());
            }
            catch
            {
                // ignore logging errors
            }
        }
    }
}