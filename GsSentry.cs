using System;
using Playnite.SDK;
using Sentry;

namespace GsPlugin {
    /// <summary>
    /// Service responsible for initializing and managing Sentry error tracking.
    /// Handles configuration, opt-out behavior, and release information.
    /// </summary>
    public class GsSentry {
        private static readonly ILogger _logger = LogManager.GetLogger();

        /// <summary>
        /// Initializes Sentry with appropriate configuration settings.
        /// </summary>
        public static void Initialize() {
            try {
                _logger.Info("Initializing Sentry error tracking");

                // Set sampling rates based on user preference
                bool disableSentryFlag = GsDataManager.Data.Flags.Contains("no-sentry");

                SentrySdk.Init(options => {
                    // A Sentry Data Source Name (DSN) is required.
                    // See https://docs.sentry.io/product/sentry-basics/dsn-explainer/
                    options.Dsn = "https://af79b5bda2a052b04b3f490b79d0470a@o4508777256124416.ingest.de.sentry.io/4508777265627216";

                    // Use method to get the plugin version
                    options.Release = GetPluginVersion();

                    // Environment configuration based on build type
#if DEBUG
                    options.Environment = "development";
                    options.Debug = true;
#else
                    options.Environment = "production";
                    options.Debug = false;
#endif

                    // Set sample rates to 0 if user opted out of Sentry
                    options.SendDefaultPii = false;
                    options.SampleRate = disableSentryFlag ? (float?)null : 1.0f;
                    options.TracesSampleRate = disableSentryFlag ? (float?)null : 1.0f;
                    options.ProfilesSampleRate = disableSentryFlag ? (float?)null : 1.0f;
                    options.AutoSessionTracking = !disableSentryFlag;
                    options.CaptureFailedRequests = !disableSentryFlag;
                    options.FailedRequestStatusCodes.Add((400, 499));

                    options.StackTraceMode = StackTraceMode.Enhanced;
                    options.IsGlobalModeEnabled = false;
                    options.DiagnosticLevel = SentryLevel.Warning;
                    options.AttachStacktrace = true;
                });

                _logger.Info($"Sentry initialized. Tracking enabled: {!disableSentryFlag}");
            }
            catch (Exception ex) {
                _logger.Error(ex, "Failed to initialize Sentry");
            }
        }

        /// <summary>
        /// Retrieves the current version of the plugin from the extension.yaml file.
        /// </summary>
        /// <returns>The version string or "Unknown Version" if not found.</returns>
        public static string GetPluginVersion() {
            string pluginFolder = System.IO.Path.GetDirectoryName(typeof(GsPlugin).Assembly.Location);
            string yamlPath = System.IO.Path.Combine(pluginFolder, "extension.yaml");

            if (System.IO.File.Exists(yamlPath)) {
                foreach (var line in System.IO.File.ReadAllLines(yamlPath)) {
                    if (line.StartsWith("Version:")) {
                        return line.Split(':')[1].Trim();
                    }
                }
            }

            return "Unknown Version";
        }

        /// <summary>
        /// Captures a custom message in Sentry with additional context.
        /// </summary>
        /// <param name="message">The message to capture.</param>
        /// <param name="level">The severity level of the message.</param>
        public static void CaptureMessage(string message, SentryLevel level = SentryLevel.Info) {
            // Skip if Sentry is disabled
            if (GsDataManager.Data.Flags.Contains("no-sentry")) {
                return;
            }

            SentrySdk.CaptureMessage(message, scope => {
                scope.Level = level;
                scope.SetTag("installId", GsDataManager.Data.InstallID);
                scope.SetTag("isLinked", GsDataManager.Data.IsLinked.ToString());
            });
        }

        /// <summary>
        /// Captures an exception in Sentry with additional context.
        /// </summary>
        /// <param name="exception">The exception to capture.</param>
        /// <param name="message">An optional message describing the context of the exception.</param>
        public static void CaptureException(Exception exception, string message = null) {
            // Skip if Sentry is disabled
            if (GsDataManager.Data.Flags.Contains("no-sentry")) {
                return;
            }

            SentrySdk.CaptureException(exception, scope => {
                scope.SetTag("installId", GsDataManager.Data.InstallID);
                scope.SetTag("isLinked", GsDataManager.Data.IsLinked.ToString());

                if (!string.IsNullOrEmpty(message)) {
                    scope.SetExtra("contextMessage", message);
                }
            });
        }
    }
}
