using System;
using System.Linq;
using System.Reflection;
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

                    // Set release version for proper release tracking
                    // Format: GsPlugin@version (e.g., GsPlugin@0.8.0)
                    try {
                        var version = Assembly.GetExecutingAssembly().GetName().Version;
                        options.Release = $"GsPlugin@{version.Major}.{version.Minor}.{version.Build}";
                    }
                    catch (Exception ex) {
                        _logger.Warn(ex, "Failed to get assembly version for Sentry release");
                        options.Release = "GsPlugin@unknown";
                    }

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

                // Set global scope context/tags so any auto-captured events include our identifiers
                try {
                    SentrySdk.ConfigureScope(scope => {
                        scope.SetTag("plugin", "GsPlugin");
                        scope.SetTag("installId", GsDataManager.Data.InstallID);
                        if (!string.IsNullOrEmpty(GsDataManager.Data.LinkedUserId)) {
                            scope.SetTag("LinkedUserId", GsDataManager.Data.LinkedUserId);
                        }
                    });
                }
                catch { /* best-effort */ }

                // Hook global exception handlers to prevent UnobservedTaskException crashes and capture in Sentry
                AppDomain.CurrentDomain.UnhandledException += (s, e) => {
                    try {
                        var ex = e.ExceptionObject as Exception;
                        if (ex != null) {
                            _logger.Error(ex, "Unhandled exception (AppDomain.CurrentDomain.UnhandledException)");
                            CaptureException(ex, "AppDomain.CurrentDomain.UnhandledException");
                        }
                        else {
                            _logger.Error("Unhandled exception (non-Exception type) captured in AppDomain.CurrentDomain.UnhandledException");
                        }
                    }
                    catch { /* no-throw */ }
                };

                System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) => {
                    try {
                        // Filter: only capture if stack trace points to our plugin to avoid reporting other extensions' errors
                        bool fromUs = false;
                        Exception ex = e.Exception;
                        while (ex != null && !fromUs) {
                            var stack = ex.StackTrace;
                            if (!string.IsNullOrEmpty(stack) && stack.Contains("GsPlugin")) {
                                fromUs = true;
                                break;
                            }
                            ex = ex.InnerException;
                        }

                        if (fromUs) {
                            _logger.Error(e.Exception, "UnobservedTaskException captured (from GsPlugin)");
                            CaptureException(e.Exception, "TaskScheduler.UnobservedTaskException");
                        }
                        else {
                            _logger.Warn("UnobservedTaskException not from our plugin; marking observed without capture.");
                        }

                        e.SetObserved();
                    }
                    catch { /* no-throw */ }
                };

                _logger.Info($"Sentry initialized. Tracking enabled: {!disableSentryFlag}");
            }
            catch (Exception ex) {
                _logger.Error(ex, "Failed to initialize Sentry");
            }
        }

        /// <summary>
        /// Retrieves the current version of the plugin from the Assembly.
        /// </summary>
        /// <returns>The version string (e.g., "0.8.0") or "unknown" if not found.</returns>
        public static string GetPluginVersion() {
            try {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                return $"{version.Major}.{version.Minor}.{version.Build}";
            }
            catch (Exception ex) {
                LogManager.GetLogger().Warn(ex, "Failed to get assembly version");
                return "unknown";
            }
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
                scope.SetTag("LinkedUserId", GsDataManager.Data.LinkedUserId);
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
                scope.SetTag("LinkedUserId", GsDataManager.Data.LinkedUserId);

                if (!string.IsNullOrEmpty(message)) {
                    scope.SetExtra("contextMessage", message);
                }
            });
        }
    }
}
