using System;
using System.Threading;
using System.Threading.Tasks;
using NinjaTrader.NinjaScript.AddOns.Ninjex.Licensing.Api;
using NinjaTrader.NinjaScript.AddOns.Ninjex.Licensing.Helper;
using NinjaTrader.NinjaScript.AddOns.Ninjex.Licensing.Models;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Licensing.Services
{
    public sealed class LicenseManager : IDisposable
    {
        private readonly LicenseApiClient _client;
        private readonly Timer _timer;
        private int _isChecking;

        public LicenseState State { get; } = new LicenseState();

        public event Action<LicenseState> LicenseStateChanged;

        public LicenseManager(string apiBaseUrl)
        {
            _client = new LicenseApiClient(apiBaseUrl);

            State.Fingerprint = MachineFingerprintHelper.Build();
            State.MachineName = Environment.MachineName ?? string.Empty;
            State.AddonVersion = VersionInfo.CurrentVersion;

            _timer = new Timer(async _ => await CheckAsync().ConfigureAwait(false), null, Timeout.Infinite, Timeout.Infinite);
        }

        public async Task InitializeAsync()
        {
            await CheckAsync().ConfigureAwait(false);
            _timer.Change(TimeSpan.FromDays(1), TimeSpan.FromDays(1));
        }

        public async Task CheckAsync()
        {
            if (Interlocked.Exchange(ref _isChecking, 1) == 1)
                return;

            try
            {
                var result = await _client.ValidateAsync(
                    new LicenseValidationRequest
                    {
                        Fingerprint = State.Fingerprint,
                        MachineName = State.MachineName,
                        AddonVersion = State.AddonVersion
                    },
                    CancellationToken.None).ConfigureAwait(false);

                State.CanUseSimulation = result.CanUseSimulation;
                State.CanUseLive = result.CanUseLive;
                State.Tier = string.IsNullOrWhiteSpace(result.Tier) ? "None" : result.Tier;
                State.MaxMachines = result.MaxMachines;
                State.LastValidatedUtc = DateTime.UtcNow;
                State.StatusText = result.CanUseLive ? "Live enabled" : "Simulation only";

                var handler = LicenseStateChanged;
                if (handler != null)
                    handler(State);
            }
            catch (Exception ex)
            {
                State.LastValidatedUtc = DateTime.UtcNow;
                State.StatusText = "Validation failed";

                var handler = LicenseStateChanged;
                if (handler != null)
                    handler(State);

                NinjexRuntime.PrintLog("License validation error: " + ex);
            }
            finally
            {
                Interlocked.Exchange(ref _isChecking, 0);
            }
        }

        public void Dispose()
        {
            _timer.Dispose();
        }
    }
}