using System;
using System.Collections.Generic;
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

        // TODO: Replace with your actual PostHog project API key
        private const string ApiKey = "phc_la6sOuOYr4cEb9Rpq27MMi6Mv8EhCLsVi6ovp6azdSi";
        private const string HostUrl = "https://eu.i.posthog.com";

        /// <summary>
        /// Initializes the PostHog analytics client.
        /// Must be called after GsDataManager.Initialize().
        /// </summary>
        public static void Initialize() {
            try {
                if (GsDataManager.DataOrNull == null) {
                    _logger.Warn("PostHog init skipped: GsDataManager not initialized");
                    return;
                }

                if (GsDataManager.Data.Flags.Contains("no-posthog")) {
                    _logger.Info("PostHog disabled by user preference");
                    return;
                }

                var options = Options.Create(new PostHogOptions {
                    ProjectApiKey = ApiKey,
                    HostUrl = new Uri(HostUrl)
                });

                _client = new PostHogClient(options);

                _logger.Info("PostHog analytics initialized");
            }
            catch (Exception ex) {
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
            if (data.Flags.Contains("no-posthog")) return;
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
            try {
                _client?.Dispose();
                _client = null;
            }
            catch (Exception ex) {
                try { _logger.Debug(ex, "PostHog shutdown failed (non-critical)"); } catch { }
            }
        }
    }
}
