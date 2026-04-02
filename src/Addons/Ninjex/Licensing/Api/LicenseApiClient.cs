using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using NinjaTrader.NinjaScript.AddOns.Ninjex.Licensing.Models;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Licensing.Api
{
    internal sealed class LicenseApiClient
    {
        private static readonly LicenseValidationResult SimOnlyFallback = new LicenseValidationResult
        {
            CanUseSimulation = true,
            CanUseLive = false,
            Tier = "None",
            MaxMachines = 0
        };

        private readonly string _baseUrl;
        private readonly JavaScriptSerializer _serializer;

        public LicenseApiClient(string baseUrl)
        {
            _baseUrl = (baseUrl ?? string.Empty).TrimEnd('/');
            _serializer = new JavaScriptSerializer();
        }

        public async Task<LicenseValidationResult> ValidateAsync(
            LicenseValidationRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var url = _baseUrl + "/api/licenses/validate";
            var body = _serializer.Serialize(request);

            var httpRequest = (HttpWebRequest)WebRequest.Create(url);
            httpRequest.Method = "POST";
            httpRequest.ContentType = "application/json";
            httpRequest.Accept = "application/json";
            httpRequest.Timeout = 15000;
            httpRequest.ReadWriteTimeout = 15000;

            using (cancellationToken.Register(() => httpRequest.Abort()))
            {
                using (var requestStream = await httpRequest.GetRequestStreamAsync().ConfigureAwait(false))
                using (var writer = new StreamWriter(requestStream, Encoding.UTF8))
                {
                    await writer.WriteAsync(body).ConfigureAwait(false);
                    await writer.FlushAsync().ConfigureAwait(false);
                }

                using (var response = (HttpWebResponse)await httpRequest.GetResponseAsync().ConfigureAwait(false))
                using (var responseStream = response.GetResponseStream())
                using (var reader = new StreamReader(responseStream ?? Stream.Null))
                {
                    var json = await reader.ReadToEndAsync().ConfigureAwait(false);
                    var result = _serializer.Deserialize<LicenseValidationResult>(json);
                    return result ?? SimOnlyFallback;
                }
            }
        }
    }
}