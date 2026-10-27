using System;
using System.Threading.Tasks;
using GsPlugin.Api;
using GsPlugin.Infrastructure;
using GsPlugin.Models;
using Playnite;

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
                    return Loc.opt_back_in_success();
                case Outcome.RateLimited:
                    return Loc.opt_back_in_rate_limited();
                case Outcome.Error:
                    return Loc.opt_back_in_error();
                default:
                    return Loc.opt_back_in_failed();
            }
        }
    }
}
