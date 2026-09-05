using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GsPlugin.Models;

namespace GsPlugin.Infrastructure {
    /// <summary>
    /// A telemetry client's permission ends permanently when it is stopped. An old SDK
    /// worker must not send buffered events after the user disables and re-enables telemetry.
    /// </summary>
    internal sealed class GsTelemetryConsent {
        private readonly string _disabledFlag;
        private int _revoked;

        public GsTelemetryConsent(string disabledFlag) {
            _disabledFlag = disabledFlag;
        }

        internal static bool HasConsent(string disabledFlag) {
            var data = GsDataManager.DataOrNull;
            return data != null && !data.OptedOut && data.Flags != null
                && !data.Flags.Contains(disabledFlag);
        }

        public bool IsAllowed => Volatile.Read(ref _revoked) == 0 && HasConsent(_disabledFlag);
        public void Revoke() => Interlocked.Exchange(ref _revoked, 1);
    }

    /// <summary>Checks consent at dispatch, including automatic sessions and buffered batches.</summary>
    internal sealed class GsTelemetryConsentHandler : DelegatingHandler {
        private readonly GsTelemetryConsent _consent;

        public GsTelemetryConsentHandler(GsTelemetryConsent consent, HttpMessageHandler innerHandler) : base(innerHandler) {
            _consent = consent;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            if (!_consent.IsAllowed) {
                // Acknowledge the discarded batch locally so the SDK doesn't retain/retry it.
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                    Content = new StringContent("{}"),
                    RequestMessage = request
                });
            }
            return base.SendAsync(request, cancellationToken);
        }
    }

    internal sealed class GsTelemetryHttpClientFactory : IHttpClientFactory, IDisposable {
        private readonly HttpMessageHandler _handler;

        public GsTelemetryHttpClientFactory(GsTelemetryConsent consent) {
            _handler = new GsTelemetryConsentHandler(consent, new HttpClientHandler());
        }

        public HttpClient CreateClient(string name) => new HttpClient(_handler, disposeHandler: false);
        public void Dispose() => _handler.Dispose();
    }
}
