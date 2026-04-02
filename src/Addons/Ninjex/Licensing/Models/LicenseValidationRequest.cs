namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Licensing.Models
{
    public sealed class LicenseValidationRequest
    {
        public string Fingerprint { get; set; }
        public string MachineName { get; set; }
        public string AddonVersion { get; set; }
    }
}