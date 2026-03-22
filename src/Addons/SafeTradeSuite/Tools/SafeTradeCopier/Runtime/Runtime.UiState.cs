using System;
using System.IO;
using System.Web.Script.Serialization;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite
{
    internal partial class SafeTradeSuiteRuntime
    {
        private static readonly string SafeTradeCopierUiStateFilePath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "NinjaTrader 8",
                "templates",
                "SafeTradeSuite",
                "safe-trade-copier-ui-state.json");

        public static void SaveCopierUiState(object state)
        {
            try
            {
                var dir = Path.GetDirectoryName(SafeTradeCopierUiStateFilePath);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = new JavaScriptSerializer().Serialize(state);
                File.WriteAllText(SafeTradeCopierUiStateFilePath, json);
            }
            catch (Exception ex)
            {
                PrintLog("SaveCopierUiState failed: " + ex);
            }
        }

        public static T LoadCopierUiState<T>() where T : class
        {
            try
            {
                if (!File.Exists(SafeTradeCopierUiStateFilePath))
                    return null;

                var json = File.ReadAllText(SafeTradeCopierUiStateFilePath);
                if (string.IsNullOrWhiteSpace(json))
                    return null;

                return new JavaScriptSerializer().Deserialize<T>(json);
            }
            catch (Exception ex)
            {
                PrintLog("LoadCopierUiState failed: " + ex);
                return null;
            }
        }
    }
}