using System;
using System.IO;
using System.Web.Script.Serialization;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex
{
    public partial class NinjexRuntime
    {
        private static readonly string RelayToolUiStateFilePath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "NinjaTrader 8",
                "templates",
                "Ninjex",
                "relay-ui-state.json");

        public static void SaveRelayUiState(object state)
        {
            try
            {
                var dir = Path.GetDirectoryName(RelayToolUiStateFilePath);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = new JavaScriptSerializer().Serialize(state);
                File.WriteAllText(RelayToolUiStateFilePath, json);
            }
            catch (Exception ex)
            {
                PrintLog("SaveRelayUiState failed: " + ex);
            }
        }

        public static T LoadRelayUiState<T>() where T : class
        {
            try
            {
                if (!File.Exists(RelayToolUiStateFilePath))
                    return null;

                var json = File.ReadAllText(RelayToolUiStateFilePath);
                if (string.IsNullOrWhiteSpace(json))
                    return null;

                return new JavaScriptSerializer().Deserialize<T>(json);
            }
            catch (Exception ex)
            {
                PrintLog("LoadRelayUiState failed: " + ex);
                return null;
            }
        }
    }
}