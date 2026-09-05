using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GsPlugin.Infrastructure;
using GsPlugin.Models;
using Sentry;
using Xunit;

namespace GsPlugin.Tests {
    [Collection("StaticManagerTests")]
    public class GsTelemetryTests {
        [Fact]
        public async Task SentryLifecycle_StopsOwnedSdkAndAutomaticTrafficWhenDisabled() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                var transport = new CountingHandler();
                try {
                    Exception initializationError = null;
                    GsSentry.InitializeWithHandler(() => transport, error => initializationError = error);
                    Assert.True(GsSentry.IsInitialized, "Plugin Sentry initialization failed: " + initializationError);
                    Assert.True(SentrySdk.IsEnabled, "The Sentry hub is disabled after initialization.");
                    GsSentry.CaptureMessage("synthetic startup test");
                    await SentrySdk.FlushAsync(TimeSpan.FromSeconds(2));
                    Assert.True(transport.Calls > 0, "No synthetic envelope reached the mock transport.");

                    GsDataManager.MutateAndSave(d => d.UpdateFlags(true, false, true));
                    var beforeShutdown = transport.Calls;
                    GsSentry.ApplyPreferences();
                    Assert.False(GsSentry.IsInitialized);
                    Assert.Equal(beforeShutdown, transport.Calls);
                    GsSentry.CaptureException(new InvalidOperationException("synthetic disabled error"));
                    Assert.Equal(beforeShutdown, transport.Calls);
                }
                finally {
                    GsSentry.Shutdown();
                }
            }
        }

        [Fact]
        public void Initialize_WithTelemetryDisabled_InitializesTypeWithoutStartingSdk() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                GsDataManager.MutateAndSave(d => d.UpdateFlags(true, false, true));
                // Exercises the same static initializer that previously threw on plugin startup.
                GsSentry.Initialize();
                GsSentry.ApplyPreferences();
                GsPostHog.ApplyPreferences();
                Assert.False(GsSentry.IsInitialized);
            }
        }

        [Theory]
        [InlineData(@"Could not read C:\Users\Alice\AppData\state.json", @"Could not read C:\Users\%USER%\AppData\state.json")]
        [InlineData(@"Could not read 'D:\Users\Alice Jones\state.json'", @"Could not read 'C:\Users\%USER%\state.json'")]
        [InlineData(@"C:\Users\کاربر\state.json", @"C:\Users\%USER%\state.json")]
        [InlineData("No user path here", "No user path here")]
        public void ScrubText_RemovesProfileNameWithoutBreakingThePath(string input, string expected) {
            Assert.Equal(expected, GsSentry.ScrubText(input));
        }

        [Theory]
        [InlineData("no-sentry")]
        [InlineData("no-posthog")]
        public async Task Transport_DiscardsBufferedWorkAfterPreferenceWithdrawal(string flag) {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                var transport = new CountingHandler();
                var consent = new GsTelemetryConsent(flag);
                using (var client = new HttpClient(new GsTelemetryConsentHandler(consent, transport))) {
                    using (await client.PostAsync("https://telemetry.invalid/batch", new StringContent("before"))) { }
                    Assert.Equal(1, transport.Calls);

                    GsDataManager.MutateAndSave(d => d.Flags.Add(flag));
                    using (await client.PostAsync("https://telemetry.invalid/batch", new StringContent("buffered"))) { }
                    Assert.Equal(1, transport.Calls);

                    consent.Revoke();
                    GsDataManager.MutateAndSave(d => d.Flags.Remove(flag));
                    using (await client.PostAsync("https://telemetry.invalid/batch", new StringContent("old-worker"))) { }
                    Assert.Equal(1, transport.Calls);
                    Assert.True(new GsTelemetryConsent(flag).IsAllowed);
                }
            }
        }

        [Theory]
        [InlineData("no-sentry")]
        [InlineData("no-posthog")]
        public async Task Transport_StopsAutomaticTrafficAfterDataDeletion(string flag) {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                var transport = new CountingHandler();
                using (var client = new HttpClient(new GsTelemetryConsentHandler(new GsTelemetryConsent(flag), transport))) {
                    GsDataManager.PerformOptOut();
                    using (await client.PostAsync("https://telemetry.invalid/session", new StringContent("automatic-session-end"))) { }
                    Assert.Equal(0, transport.Calls);
                }
            }
        }

        private sealed class CountingHandler : HttpMessageHandler {
            public int Calls { get; private set; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
                Calls++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
            }
        }
    }
}
