using System;
using System.Threading.Tasks;
using GsPlugin.Api;
using GsPlugin.Models;
using GsPlugin.Services;
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

        [Fact]
        public void MessageFor_MapsEachOutcome() {
            Assert.Contains("re-enabled", GsOptBackIn.MessageFor(GsOptBackIn.Outcome.Success), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Too many", GsOptBackIn.MessageFor(GsOptBackIn.Outcome.RateLimited), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("error", GsOptBackIn.MessageFor(GsOptBackIn.Outcome.Error), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Failed", GsOptBackIn.MessageFor(GsOptBackIn.Outcome.Failed), StringComparison.OrdinalIgnoreCase);
        }
    }
}
