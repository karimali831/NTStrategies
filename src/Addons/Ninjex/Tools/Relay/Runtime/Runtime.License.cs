using NinjaTrader.NinjaScript.AddOns.Ninjex.Licensing.Services;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex
{
    public static partial class NinjexRuntime
    {
        private static readonly object _licenseGate = new object();
        private static LicenseManager _licenseManager;

        public static LicenseManager GetOrCreateLicenseManager()
        {
            lock (_licenseGate)
            {
                if (_licenseManager == null)
                    _licenseManager = new LicenseManager("https://api.getninjex.com");

                return _licenseManager;
            }
        }

        public static void DisposeLicenseManagerIfExists()
        {
            lock (_licenseGate)
            {
                if (_licenseManager == null)
                    return;

                _licenseManager.Dispose();
                _licenseManager = null;
            }
        }
    }
}