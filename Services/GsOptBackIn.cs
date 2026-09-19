using System;
using System.Threading.Tasks;
using GsPlugin.Api;
using GsPlugin.Infrastructure;
using GsPlugin.Models;

namespace GsPlugin.Services {
    /// <summary>
    /// Shared opt-back-in path used by Settings and the opted-out dashboard panel.
    /// Clears the local tombstone only after the server accepts the request.
    /// </summary>
    internal static class GsOptBackIn {
        public enum Outcome {
            Success,
            RateLimited,
            Failed,
            Error
        }

        public static async Task<Outcome> TryAsync(IGsApiClient apiClient) {
            if (apiClient == null) {
                return Outcome.Failed;
            }

            try {
                var result = await apiClient.RequestOptIn(new OptInReq());
                if (result == null || !result.success) {
                    return result != null && result.rateLimited
                        ? Outcome.RateLimited
                        : Outcome.Failed;
                }

                if (!GsDataManager.PerformOptIn()) {
                    return Outcome.Failed;
                }
                // Telemetry stays paused until Playnite restarts. ApplyPreferences would
                // otherwise see OptedOut=false and start Sentry/PostHog this session.
                return Outcome.Success;
            }
            catch (Exception ex) {
                GsLogger.Error("Error during opt-back-in", ex);
                GsSentry.CaptureException(ex, "Error during opt-back-in");
                return Outcome.Error;
            }
        }

        public static string MessageFor(Outcome outcome) {
            switch (outcome) {
                case Outcome.Success:
                    return GsLocalization.Get(
                        "LOCGsPluginOptBackInSuccess",
                        "Plugin re-enabled. Please restart Playnite to resume syncing.");
                case Outcome.RateLimited:
                    return GsLocalization.Get(
                        "LOCGsPluginOptBackInRateLimited",
                        "Too many attempts. Please wait and try again.");
                case Outcome.Error:
                    return GsLocalization.Get(
                        "LOCGsPluginOptBackInError",
                        "An error occurred. Please restart Playnite to try again.");
                default:
                    return GsLocalization.Get(
                        "LOCGsPluginOptBackInFailed",
                        "Failed to re-enable. Please restart Playnite to try again.");
            }
        }
    }
}
