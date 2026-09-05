using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Threading;
using System.Threading.Tasks;
using GsPlugin.Api;
using GsPlugin.Models;
using GsPlugin.Services;
using Playnite.SDK;
using Xunit;

namespace GsPlugin.Tests {
    [Collection("StaticManagerTests")]
    public class AccountLinkingConcurrencyTests {
        private sealed class DelayedHttpHandler : HttpMessageHandler {
            private readonly TaskCompletionSource<HttpResponseMessage> _firstResponse =
                new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource<bool> FirstRequest { get; } =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public int CallCount;
            public string LaterResponse { get; set; } = "{\"success\":true,\"userId\":\"new-account\"}";

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
                if (Interlocked.Increment(ref CallCount) == 1) {
                    FirstRequest.TrySetResult(true);
                    return _firstResponse.Task;
                }
                return Task.FromResult(Response(LaterResponse));
            }

            public void CompleteFirst(string json) => _firstResponse.TrySetResult(Response(json));

            private static HttpResponseMessage Response(string json) => new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
        }

        // Only unlink confirmation is used. A remoting proxy avoids adding a mocking dependency
        // or implementing the entire Playnite API just to return Yes from its dialog service.
        private sealed class ConfirmingPlayniteProxy : RealProxy {
            public ConfirmingPlayniteProxy(Type interfaceType) : base(interfaceType) { }

            public override IMessage Invoke(IMessage message) {
                var call = (IMethodCallMessage)message;
                var method = (MethodInfo)call.MethodBase;
                object result;
                if (method.Name == "get_Dialogs") {
                    result = new ConfirmingPlayniteProxy(method.ReturnType).GetTransparentProxy();
                }
                else if (method.Name == "ShowMessage") {
                    result = Enum.Parse(method.ReturnType, "Yes");
                }
                else {
                    return new ReturnMessage(new InvalidOperationException("Unexpected Playnite call: " + method.Name), call);
                }
                return new ReturnMessage(result, null, 0, call.LogicalCallContext, call);
            }
        }

        private static GsAccountLinkingService CreateService(DelayedHttpHandler handler) =>
            new GsAccountLinkingService(new GsApiClient(new HttpClient(handler)),
                (IPlayniteAPI)new ConfirmingPlayniteProxy(typeof(IPlayniteAPI)).GetTransparentProxy());

        [Theory]
        [InlineData("old-account")]
        [InlineData("not_linked")]
        public async Task LinkResponseAfterIdentityRotation_CannotSetOrClearReplacementAccount(string returnedUser) {
            using (var temp = TempPluginDir.CreateWithDataManagerAndHashIndex()) {
                var handler = new DelayedHttpHandler();
                var service = CreateService(handler);
                var pending = service.LinkAccountAsync("valid-token", LinkingContext.AutomaticUri);
                await handler.FirstRequest.Task;

                var replacementId = GsDataManager.RotateInstallId();
                GsDataManager.MutateAndSave(d => d.LinkedUserId = "replacement-account");
                handler.CompleteFirst("{\"success\":true,\"userId\":\"" + returnedUser + "\"}");
                var result = await pending;

                Assert.False(result.Success);
                Assert.Equal(replacementId, GsDataManager.Data.InstallID);
                Assert.Equal("replacement-account", GsDataManager.Data.LinkedUserId);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task LinkResponseAfterOptOut_CannotRestoreAccountEvenAfterQuickOptIn(bool optBackIn) {
            using (var temp = TempPluginDir.CreateWithDataManagerAndHashIndex()) {
                var handler = new DelayedHttpHandler();
                var pending = CreateService(handler).LinkAccountAsync("valid-token", LinkingContext.AutomaticUri);
                await handler.FirstRequest.Task;

                GsDataManager.PerformOptOut();
                if (optBackIn) {
                    GsDataManager.PerformOptIn();
                }
                handler.CompleteFirst("{\"success\":true,\"userId\":\"old-account\"}");
                var result = await pending;

                Assert.False(result.Success);
                Assert.Null(GsDataManager.Data.LinkedUserId);
                Assert.Equal(!optBackIn, GsDataManager.IsOptedOut);
            }
        }

        [Fact]
        public async Task UnlinkResponseAfterRotation_CannotClearReplacementIdentityState() {
            using (var temp = TempPluginDir.CreateWithDataManagerAndHashIndex()) {
                GsDataManager.SetInstallTokenIfActive("valid-token");
                GsDataManager.MutateAndSave(d => d.LinkedUserId = "old-account");
                var handler = new DelayedHttpHandler();
                var pending = CreateService(handler).UnlinkAccountAsync();
                await handler.FirstRequest.Task;

                GsDataManager.RotateInstallId();
                GsDataManager.MutateAndSave(d => {
                    d.LinkedUserId = "replacement-account";
                    d.LastLibraryHash = "replacement-baseline";
                    d.ActiveSessionsByGameId["new-game"] = "new-session";
                });
                handler.CompleteFirst("{\"success\":true}");
                var result = await pending;

                Assert.False(result.Success);
                Assert.Equal("replacement-account", GsDataManager.Data.LinkedUserId);
                Assert.Equal("replacement-baseline", GsDataManager.Data.LastLibraryHash);
                Assert.Equal("new-session", GsDataManager.Data.ActiveSessionsByGameId["new-game"]);
            }
        }

        [Fact]
        public async Task ConcurrentLinks_AreSentAndCommittedInOrderAcrossServiceInstances() {
            using (var temp = TempPluginDir.CreateWithDataManagerAndHashIndex()) {
                var handler = new DelayedHttpHandler();
                var first = CreateService(handler).LinkAccountAsync("first-token", LinkingContext.AutomaticUri);
                await handler.FirstRequest.Task;
                var second = CreateService(handler).LinkAccountAsync("second-token", LinkingContext.ManualSettings);
                int sentBeforeFirstCompleted = handler.CallCount;

                handler.CompleteFirst("{\"success\":true,\"userId\":\"first-account\"}");
                var results = await Task.WhenAll(first, second);

                Assert.Equal(1, sentBeforeFirstCompleted);
                Assert.All(results, result => Assert.True(result.Success));
                Assert.Equal(2, handler.CallCount);
                Assert.Equal("new-account", GsDataManager.Data.LinkedUserId);
            }
        }

        [Fact]
        public async Task LinkWhileUnlinkPending_WaitsUntilUnlinkStateIsCommitted() {
            using (var temp = TempPluginDir.CreateWithDataManagerAndHashIndex()) {
                GsDataManager.SetInstallTokenIfActive("valid-token");
                GsDataManager.MutateAndSave(d => d.LinkedUserId = "old-account");
                var handler = new DelayedHttpHandler();
                var service = CreateService(handler);
                var unlink = service.UnlinkAccountAsync();
                await handler.FirstRequest.Task;
                var link = service.LinkAccountAsync("new-token", LinkingContext.AutomaticUri);
                int sentBeforeUnlinkCompleted = handler.CallCount;

                handler.CompleteFirst("{\"success\":true}");
                var results = await Task.WhenAll(unlink, link);

                Assert.Equal(1, sentBeforeUnlinkCompleted);
                Assert.All(results, result => Assert.True(result.Success));
                Assert.Equal("new-account", GsDataManager.Data.LinkedUserId);
            }
        }

        [Fact]
        public async Task WaitingLinkAfterIdentityRotation_IsRejectedBeforeSending() {
            using (var temp = TempPluginDir.CreateWithDataManagerAndHashIndex()) {
                var handler = new DelayedHttpHandler();
                var service = CreateService(handler);
                var first = service.LinkAccountAsync("first-token", LinkingContext.AutomaticUri);
                await handler.FirstRequest.Task;
                var second = service.LinkAccountAsync("second-token", LinkingContext.ManualSettings);

                GsDataManager.RotateInstallId();
                handler.CompleteFirst("{\"success\":true,\"userId\":\"old-account\"}");
                var results = await Task.WhenAll(first, second);

                Assert.All(results, result => Assert.False(result.Success));
                Assert.Equal(1, handler.CallCount);
                Assert.Null(GsDataManager.Data.LinkedUserId);
            }
        }
    }
}
