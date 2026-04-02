namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Licensing.Models
{
    public sealed class LicenseValidationResult
    {
        public bool CanUseSimulation { get; set; }
        public bool CanUseLive { get; set; }
        public string Tier { get; set; }
        public int MaxMachines { get; set; }
    }
}