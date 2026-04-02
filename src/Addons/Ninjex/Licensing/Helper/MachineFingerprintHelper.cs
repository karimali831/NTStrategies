using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Licensing.Helper
{
    internal static class MachineFingerprintHelper
    {
        public static string Build()
        {
            var raw = string.Join("|", new[]
            {
                Environment.MachineName ?? "",
                Environment.UserName ?? "",
                Environment.OSVersion.VersionString ?? ""
            });

            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
                return string.Concat(bytes.Select(b => b.ToString("x2")));
            }
        }
    }
}