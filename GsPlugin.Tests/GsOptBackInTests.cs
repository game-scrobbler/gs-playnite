using System;
using System.IO;
using System.Threading.Tasks;
using GsPlugin.Api;
using GsPlugin.Infrastructure;
using GsPlugin.Models;
using GsPlugin.Services;
using Playnite;
using Xunit;

namespace GsPlugin.Tests {
    [Collection("StaticManagerTests")]
    public class GsOptBackInTests : IDisposable {
        private readonly TempPluginDir _temp;

        public GsOptBackInTests() {
            _temp = TempPluginDir.CreateWithDataManager();
        }

        public void Dispose() {
            _temp.Dispose();
        }

        [Fact]
        public async Task TryAsync_NullClient_ReturnsFailedWithoutClearingOptOut() {
            GsDataManager.PerformOptOut();

            var outcome = await GsOptBackIn.TryAsync(null);

            Assert.Equal(GsOptBackIn.Outcome.Failed, outcome);
            Assert.True(GsDataManager.IsOptedOut);
        }

        [Fact]
        public async Task TryAsync_Success_ClearsLocalOptOut() {
            GsDataManager.PerformOptOut();
            var client = new MockGsApiClient {
                OptInResponse = new OptInRes { success = true }
            };

            var outcome = await GsOptBackIn.TryAsync(client);

            Assert.Equal(GsOptBackIn.Outcome.Success, outcome);
            Assert.False(GsDataManager.IsOptedOut);
            Assert.True(GsDataManager.PendingRestartAfterOptIn);
            Assert.True(GsDataManager.IsTrackingPaused);
            Assert.False(GsTelemetryConsent.HasConsent("no-sentry"));
            Assert.False(GsTelemetryConsent.HasConsent("no-posthog"));
        }

        [Fact]
        public async Task TryAsync_SaveFailure_ReturnsFailedAndLeavesOptOut() {
            GsDataManager.PerformOptOut();
            var client = new MockGsApiClient {
                OptInResponse = new OptInRes { success = true }
            };
            var path = Path.Combine(_temp.Path, "gs_data.json");

            GsOptBackIn.Outcome outcome;
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                outcome = await GsOptBackIn.TryAsync(client);
            }

            Assert.Equal(GsOptBackIn.Outcome.Failed, outcome);
            Assert.True(GsDataManager.IsOptedOut);
            Assert.False(GsDataManager.PendingRestartAfterOptIn);
        }

        [Fact]
        public async Task TryAsync_RateLimited_LeavesOptOutInPlace() {
            GsDataManager.PerformOptOut();
            var client = new MockGsApiClient {
                OptInResponse = new OptInRes { success = false, rateLimited = true }
            };

            var outcome = await GsOptBackIn.TryAsync(client);

            Assert.Equal(GsOptBackIn.Outcome.RateLimited, outcome);
            Assert.True(GsDataManager.IsOptedOut);
        }

        [Fact]
        public async Task TryAsync_FailedResponse_LeavesOptOutInPlace() {
            GsDataManager.PerformOptOut();
            var client = new MockGsApiClient {
                OptInResponse = new OptInRes { success = false }
            };

            var outcome = await GsOptBackIn.TryAsync(client);

            Assert.Equal(GsOptBackIn.Outcome.Failed, outcome);
            Assert.True(GsDataManager.IsOptedOut);
        }

        [Fact]
        public async Task TryAsync_Exception_ReturnsErrorWithoutClearingOptOut() {
            GsDataManager.PerformOptOut();
            var client = new MockGsApiClient {
                OptInException = new InvalidOperationException("network down")
            };

            var outcome = await GsOptBackIn.TryAsync(client);

            Assert.Equal(GsOptBackIn.Outcome.Error, outcome);
            Assert.True(GsDataManager.IsOptedOut);
        }

        // No test sets Loc.Api, so Loc returns the Fluent message id. That still pins what
        // matters here, which is that each outcome maps to its own message.
        [Fact]
        public void MessageFor_MapsEachOutcome() {
            Assert.Equal(LocId.opt_back_in_success, GsOptBackIn.MessageFor(GsOptBackIn.Outcome.Success));
            Assert.Equal(LocId.opt_back_in_rate_limited, GsOptBackIn.MessageFor(GsOptBackIn.Outcome.RateLimited));
            Assert.Equal(LocId.opt_back_in_error, GsOptBackIn.MessageFor(GsOptBackIn.Outcome.Error));
            Assert.Equal(LocId.opt_back_in_failed, GsOptBackIn.MessageFor(GsOptBackIn.Outcome.Failed));
        }
    }
}
