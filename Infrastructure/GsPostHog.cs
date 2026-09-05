using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Playnite.SDK;
using PostHog;
using GsPlugin.Models;

namespace GsPlugin.Infrastructure {
    /// <summary>
    /// PostHog analytics wrapper using the official SDK.
    /// Mirrors the GsSentry pattern: static methods, DataOrNull guards, try/catch wrappers.
    /// </summary>
    public static class GsPostHog {
        private static readonly ILogger _logger = LogManager.GetLogger();
        private static PostHogClient _client;
        private static readonly object LifecycleLock = new object();
        private static GsTelemetryConsent _consent;
        private static GsTelemetryHttpClientFactory _httpClientFactory;

        public static void ApplyPreferences() {
            if (GsTelemetryConsent.HasConsent("no-posthog")) Initialize();
            else Shutdown();
        }

        private const string ApiKey = "phc_la6sOuOYr4cEb9Rpq27MMi6Mv8EhCLsVi6ovp6azdSi";
        private const string HostUrl = "https://eu.i.posthog.com";

        /// <summary>
        /// Initializes the PostHog analytics client.
        /// Must be called after GsDataManager.Initialize().
        /// </summary>
        public static void Initialize() {
            lock (LifecycleLock) {
                if (_client != null) return;
                InitializeCore();
            }
        }

        private static void InitializeCore() {
            try {
                if (GsDataManager.DataOrNull == null) {
                    _logger.Warn("PostHog init skipped: GsDataManager not initialized");
                    return;
                }

                if (GsDataManager.Data.Flags.Contains("no-posthog") || GsDataManager.IsOptedOut) {
                    _logger.Info("PostHog disabled by user preference or opt-out");
                    return;
                }

                var options = Options.Create(new PostHogOptions {
                    ProjectApiKey = ApiKey,
                    HostUrl = new Uri(HostUrl)
                });

                _consent = new GsTelemetryConsent("no-posthog");
                _httpClientFactory = new GsTelemetryHttpClientFactory(_consent);
                _client = new PostHogClient(options, httpClientFactory: _httpClientFactory);

                _logger.Info("PostHog analytics initialized");
            }
            catch (Exception ex) {
                _consent?.Revoke();
                _httpClientFactory?.Dispose();
                _httpClientFactory = null;
                _logger.Error(ex, "Failed to initialize PostHog (non-critical)");
            }
        }

        /// <summary>
        /// Captures an analytics event. Non-blocking.
        /// </summary>
        /// <param name="eventName">The event name (e.g., "plugin_started").</param>
        /// <param name="properties">Optional properties to attach to the event.</param>
        public static void Capture(string eventName, Dictionary<string, object> properties = null) {
            var data = GsDataManager.DataOrNull;
            if (data == null) return;
            if (data.Flags.Contains("no-posthog") || data.OptedOut) return;
            if (_client == null) return;

            try {
                var props = new Dictionary<string, object> {
                    { "app", "gs-playnite" },
                    { "plugin_version", GsSentry.GetPluginVersion() }
                };

                if (!string.IsNullOrEmpty(data.LinkedUserId)) {
                    props["linked_user_id"] = data.LinkedUserId;
                }

                if (properties != null) {
                    foreach (var kvp in properties) {
                        props[kvp.Key] = kvp.Value;
                    }
                }

                _client.Capture(
                    distinctId: data.InstallID,
                    eventName: eventName,
                    properties: props,
                    groups: null,
                    sendFeatureFlags: false
                );
            }
            catch (Exception ex) {
                try { _logger.Debug(ex, $"PostHog capture failed for '{eventName}' (non-critical)"); } catch { }
            }
        }

        /// <summary>
        /// Shuts down the PostHog client and releases resources.
        /// </summary>
        public static void Shutdown() {
            lock (LifecycleLock) {
                ShutdownCore();
            }
        }

        private static void ShutdownCore() {
            var client = _client;
            var factory = _httpClientFactory;
            var consent = _consent;
            if (!GsTelemetryConsent.HasConsent("no-posthog")) consent?.Revoke();
            _client = null;
            _httpClientFactory = null;
            if (client == null) {
                return;
            }

            try {
                // Bounded flush: PostHogClient.Dispose() blocks synchronously on an unbounded
                // async flush (DisposeAsync().GetAwaiter().GetResult()), which can hang Playnite
                // on exit when the ingest endpoint is slow/unreachable. Race it against a timeout
                // instead of waiting on it directly.
                var disposeTask = Task.Run(() => {
                    try { client.Dispose(); }
                    finally {
                        consent?.Revoke();
                        factory?.Dispose();
                    }
                });
                if (!disposeTask.Wait(TimeSpan.FromSeconds(2))) {
                    _logger.Warn("PostHog dispose timed out during shutdown; abandoning");
                    // Dispose keeps running in the background after we give up waiting on it —
                    // observe any fault it eventually throws so it doesn't surface later as an
                    // unobserved task exception.
                    disposeTask.ContinueWith(t => {
                        try { _logger.Warn(t.Exception?.GetBaseException(), "PostHog dispose failed after shutdown timeout (non-critical)"); } catch { }
                    }, TaskContinuationOptions.OnlyOnFaulted);
                }
            }
            catch (Exception ex) {
                try { _logger.Debug(ex, "PostHog shutdown failed (non-critical)"); } catch { }
            }
        }
    }
}
