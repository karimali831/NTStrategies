using System;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Licensing.Models
{
    public sealed class LicenseState
    {
        public bool CanUseSimulation { get; set; } = true;
        public bool CanUseLive { get; set; }
        public string Tier { get; set; } = "None";
        public int MaxMachines { get; set; }
        public string Fingerprint { get; set; }
        public string MachineName { get; set; }
        public string AddonVersion { get; set; }
        public DateTime? LastValidatedUtc { get; set; }
        public string StatusText { get; set; } = "Not validated";
    }
}