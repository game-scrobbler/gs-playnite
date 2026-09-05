using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Playnite.SDK;
using Sentry;
using GsPlugin.Models;

namespace GsPlugin.Infrastructure {
    /// <summary>
    /// Service responsible for initializing and managing Sentry error tracking.
    /// Handles configuration, opt-out behavior, and release information.
    /// </summary>
    public class GsSentry {
        private static readonly ILogger _logger = LogManager.GetLogger();
        private static bool _initialized;
        private static readonly object LifecycleLock = new object();
        private static GsTelemetryConsent _consent;
        private static IDisposable _sdkLifetime;
        private static UnhandledExceptionEventHandler _unhandledExceptionHandler;
        private static EventHandler<System.Threading.Tasks.UnobservedTaskExceptionEventArgs> _unobservedTaskExceptionHandler;

        /// <summary>
        /// True when <see cref="Initialize"/> completed successfully.
        /// </summary>
        public static bool IsInitialized => _initialized;

        /// <summary>
        /// Initializes Sentry with appropriate configuration settings.
        /// </summary>
        public static void Initialize() {
            InitializeWithHandler(null);
        }

        internal static void InitializeWithHandler(Func<System.Net.Http.HttpMessageHandler> createHandler, Action<Exception> onError = null) {
            lock (LifecycleLock) {
                if (_initialized) return;
                InitializeCore(createHandler, onError);
            }
        }

        /// <summary>Applies settings changes to the running SDK, including automatic capture.</summary>
        public static void ApplyPreferences() {
            if (GsTelemetryConsent.HasConsent("no-sentry")) Initialize();
            else Shutdown();
        }

        private static void InitializeCore(Func<System.Net.Http.HttpMessageHandler> createHandler, Action<Exception> onError) {
            try {
                _logger.Info("Initializing Sentry error tracking");

                // Set sampling rates based on user preference or opt-out
                bool disableSentryFlag = !GsTelemetryConsent.HasConsent("no-sentry");

                if (disableSentryFlag) {
                    // Previously Init still ran with SampleRate 0. That leaves AutoSessionTracking
                    // posting session envelopes and SendClientReports (default true) periodically
                    // POSTing discarded-event counts -- network traffic from an install that asked
                    // for none. Not initializing is the only way to be silent; the Capture* wrappers
                    // below already no-op, so nothing else needs to change.
                    _logger.Info("Sentry disabled by user preference or opt-out — not initializing");
                    return;
                }

                var consent = new GsTelemetryConsent("no-sentry");
                _consent = consent;
                _sdkLifetime = SentrySdk.Init(options => {
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
                    // Note: null means "use default", 0.0f means "disable completely"
                    options.SendDefaultPii = false;
                    options.SampleRate = disableSentryFlag ? 0.0f : 1.0f;
                    options.TracesSampleRate = disableSentryFlag ? 0.0f : 0.1f;
                    // Never enable continuous profiling in-process — it keeps background
                    // workers alive and has been implicated in Playnite refusing to quit.
                    options.ProfilesSampleRate = 0.0f;
                    options.AutoSessionTracking = !disableSentryFlag;
                    // CaptureFailedRequests + FailedRequestStatusCodes.Add((400,499)) constructs
                    // Sentry.HttpStatusCodeRange. Playnite loads all plugins into one AppDomain;
                    // if another extension already loaded an older Sentry.dll, that Add() throws
                    // MissingMethodException and aborts init. Keep HTTP capture on the SDK default
                    // (5xx only) so we never touch HttpStatusCodeRange.
                    options.CaptureFailedRequests = false;

                    options.StackTraceMode = StackTraceMode.Enhanced;
                    options.IsGlobalModeEnabled = false;
                    options.DiagnosticLevel = SentryLevel.Warning;
                    options.AttachStacktrace = true;
                    // Cap breadcrumb buffer to reduce per-session memory overhead.
                    options.MaxBreadcrumbs = 50;

                    // Filter out events that don't originate from our plugin.
                    // Without this, Sentry's global hooks capture unhandled exceptions
                    // from other Playnite plugins and Playnite core, polluting our dashboard.
                    options.SendClientReports = false;
                    options.ShutdownTimeout = TimeSpan.FromSeconds(2);
                    options.CreateHttpMessageHandler = () =>
                        new GsTelemetryConsentHandler(consent, createHandler?.Invoke() ?? new System.Net.Http.HttpClientHandler());
                    options.SetBeforeSendTransaction(transaction => consent.IsAllowed ? transaction : null);

                    options.SetBeforeSend((sentryEvent, hint) => {
                        if (!consent.IsAllowed) return null;
                        // Always allow explicitly captured messages (our own CaptureMessage calls)
                        if (sentryEvent.Exception == null) {
                            return Scrub(sentryEvent);
                        }

                        // Check if any exception in the chain originates from our assembly
                        if (IsExceptionFromOurPlugin(sentryEvent.Exception)) {
                            return Scrub(sentryEvent);
                        }

                        // Also check SentryExceptions (populated from the event's exception list)
                        if (sentryEvent.SentryExceptions != null) {
                            var ourNamespace = "GsPlugin";
                            foreach (var se in sentryEvent.SentryExceptions) {
                                if (se.Module != null && se.Module.StartsWith(ourNamespace)) {
                                    return Scrub(sentryEvent);
                                }
                                if (se.Stacktrace?.Frames != null) {
                                    foreach (var frame in se.Stacktrace.Frames) {
                                        if (frame.Module != null && frame.Module.StartsWith(ourNamespace)) {
                                            return Scrub(sentryEvent);
                                        }
                                    }
                                }
                            }
                        }

                        // Not from our plugin — drop the event
                        return null;
                    });
                });

                // Set global scope context/tags so any auto-captured events include our identifiers
                // Skip when opted out — no user identifiers should be sent
                try {
                    if (!disableSentryFlag) {
                        SentrySdk.ConfigureScope(scope => {
                            scope.SetTag("plugin", "GsPlugin");
                            scope.SetTag("installId", GsDataManager.Data.InstallID);
                            if (!string.IsNullOrEmpty(GsDataManager.Data.LinkedUserId)) {
                                scope.SetTag("LinkedUserId", GsDataManager.Data.LinkedUserId);
                            }
                        });
                    }
                }
                catch (Exception ex) {
                    _logger.Debug(ex, "Failed to configure Sentry scope (non-critical)");
                }

                // Hook global exception handlers to prevent UnobservedTaskException crashes and capture in Sentry
                _unhandledExceptionHandler = (s, e) => {
                    try {
                        var ex = e.ExceptionObject as Exception;
                        if (ex != null) {
                            // Filter: only capture if exception originates from our plugin to avoid reporting other extensions' errors
                            bool fromUs = IsExceptionFromOurPlugin(ex);
                            if (fromUs) {
                                _logger.Error(ex, "Unhandled exception (AppDomain.CurrentDomain.UnhandledException)");
                                CaptureException(ex, "AppDomain.CurrentDomain.UnhandledException");
                            }
                            else {
                                _logger.Debug("Unhandled exception not from our plugin; skipping capture.");
                            }
                        }
                        else {
                            _logger.Error("Unhandled exception (non-Exception type) captured in AppDomain.CurrentDomain.UnhandledException");
                        }
                    }
                    catch (Exception handlerEx) {
                        // Last resort: log to Playnite but don't throw from exception handler
                        try { _logger.Debug(handlerEx, "Exception in UnhandledException handler"); } catch { }
                    }
                };
                AppDomain.CurrentDomain.UnhandledException += _unhandledExceptionHandler;

                _unobservedTaskExceptionHandler = (s, e) => {
                    try {
                        // Filter: only capture if exception originates from our plugin assembly to avoid reporting other extensions' errors
                        bool fromUs = IsExceptionFromOurPlugin(e.Exception);

                        if (fromUs) {
                            _logger.Error(e.Exception, "UnobservedTaskException captured (from GsPlugin)");
                            CaptureException(e.Exception, "TaskScheduler.UnobservedTaskException");
                        }
                        else {
                            _logger.Debug("UnobservedTaskException not from our plugin; marking observed without capture.");
                        }

                        e.SetObserved();
                    }
                    catch (Exception handlerEx) {
                        // Last resort: log to Playnite but don't throw from exception handler
                        try { _logger.Debug(handlerEx, "Exception in UnobservedTaskException handler"); } catch { }
                    }
                };
                System.Threading.Tasks.TaskScheduler.UnobservedTaskException += _unobservedTaskExceptionHandler;

                _initialized = true;
                _logger.Info($"Sentry initialized. Tracking enabled: {!disableSentryFlag}");
            }
            catch (Exception ex) {
                _consent?.Revoke();
                ShutdownCore();
                _logger.Error(ex, "Failed to initialize Sentry");
                onError?.Invoke(ex);
            }
        }

        /// <summary>
        /// Flushes buffered events with a hard timeout, then closes the SDK.
        /// Safe to call when init failed — no-ops so Playnite can exit promptly.
        /// </summary>
        public static void Shutdown() {
            lock (LifecycleLock) {
                ShutdownCore();
            }
        }

        private static void ShutdownCore() {
            // On consent withdrawal, revoke before disposing: SDK disposal can emit a final
            // automatic-session envelope or flush work queued before the preference changed.
            if (!GsTelemetryConsent.HasConsent("no-sentry")) _consent?.Revoke();
            if (_unhandledExceptionHandler != null) {
                AppDomain.CurrentDomain.UnhandledException -= _unhandledExceptionHandler;
                _unhandledExceptionHandler = null;
            }
            if (_unobservedTaskExceptionHandler != null) {
                System.Threading.Tasks.TaskScheduler.UnobservedTaskException -= _unobservedTaskExceptionHandler;
                _unobservedTaskExceptionHandler = null;
            }
            if (_sdkLifetime == null) {
                _consent?.Revoke();
                _initialized = false;
                return;
            }

            try {
                // Dispose the hub we initialized, with the configured two-second bound.
                // Global Flush/Close could act on a hub installed later by another addon.
                _sdkLifetime?.Dispose();
            }
            catch (Exception ex) {
                try { _logger.Debug(ex, "Sentry Close failed during shutdown (non-critical)"); } catch { }
            }
            finally {
                _consent?.Revoke();
                _sdkLifetime = null;
                _initialized = false;
            }
        }

        /// <summary>
        /// Determines if an exception originated from this plugin by checking the assembly of stack frames.
        /// </summary>
        /// <param name="exception">The exception to check.</param>
        /// <returns>True if the exception originated from GsPlugin, false otherwise.</returns>
        /// <summary>
        /// Matches a Windows user-profile path so the account name can be replaced. The plugin's
        /// own data lives under %APPDATA%, so almost every IO exception it reports carries the
        /// user's real Windows account name inside the path.
        /// </summary>
        private static readonly Regex UserProfilePathRegex = new Regex(
            @"[A-Za-z]:\\Users\\[^\\""'\r\n]+",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        internal static string ScrubText(string value) =>
            string.IsNullOrEmpty(value)
                ? value
                : UserProfilePathRegex.Replace(value, @"C:\Users\%USER%");

        /// <summary>
        /// Removes personal data from an event immediately before it leaves the device.
        ///
        /// SetBeforeSend was only ever an origin filter — it decided whether to send and redacted
        /// nothing. With StackTraceMode.Enhanced and PDBs uploaded in CI, every IO exception carried
        /// an absolute path containing the user's Windows account name (the plugin's data file lives
        /// under %APPDATA%\Playnite\ExtensionsData\...), which is real PII the plugin never intended
        /// to collect.
        ///
        /// Wrapped in try/catch: a scrubbing failure must not take the event down with it, because
        /// losing crash reports is the worse outcome. On failure the event is returned unchanged,
        /// matching the previous behaviour.
        /// </summary>
        private static SentryEvent Scrub(SentryEvent sentryEvent) {
            try {
                if (sentryEvent?.Message != null) {
                    var scrubbed = ScrubText(sentryEvent.Message.Formatted ?? sentryEvent.Message.Message);
                    if (!string.IsNullOrEmpty(scrubbed)) {
                        sentryEvent.Message = scrubbed;
                    }
                }

                if (sentryEvent?.SentryExceptions != null) {
                    foreach (var se in sentryEvent.SentryExceptions) {
                        se.Value = ScrubText(se.Value);
                        if (se.Stacktrace?.Frames == null) {
                            continue;
                        }
                        foreach (var frame in se.Stacktrace.Frames) {
                            frame.AbsolutePath = ScrubText(frame.AbsolutePath);
                            frame.FileName = ScrubText(frame.FileName);
                        }
                    }
                }
            }
            catch (Exception ex) {
                _logger.Debug(ex, "Sentry PII scrub failed; sending event unscrubbed");
            }
            return sentryEvent;
        }

        private static bool IsExceptionFromOurPlugin(Exception exception) {
            if (exception == null) {
                return false;
            }

            try {
                // For AggregateException, check each inner exception independently
                if (exception is System.AggregateException aggEx) {
                    foreach (var inner in aggEx.Flatten().InnerExceptions) {
                        if (IsExceptionFromOurPlugin(inner)) {
                            return true;
                        }
                    }
                    return false;
                }

                var ourAssemblyName = Assembly.GetExecutingAssembly().GetName().Name;

                // Check the exception's Source property (set to the assembly that threw it)
                if (!string.IsNullOrEmpty(exception.Source) && exception.Source == ourAssemblyName) {
                    return true;
                }

                // Check only the throw-site frame (deepest frame) to avoid matching
                // our global handler's own call stack which appears in every exception.
                var stackTrace = new System.Diagnostics.StackTrace(exception, true);
                if (stackTrace.FrameCount > 0) {
                    var throwFrame = stackTrace.GetFrame(0);
                    if (throwFrame != null) {
                        var method = throwFrame.GetMethod();
                        var declaringType = method?.DeclaringType;
                        if (declaringType != null && declaringType.Assembly.GetName().Name == ourAssemblyName) {
                            return true;
                        }
                    }
                }

                // Check direct inner exception only (not recursive — AggregateException handled above)
                if (exception.InnerException != null) {
                    return IsExceptionFromOurPlugin(exception.InnerException);
                }

                return false;
            }
            catch {
                // If we can't determine, err on the side of not capturing to avoid false positives
                return false;
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
            if (!_initialized || _consent?.IsAllowed != true) return;
            // Skip if Sentry is disabled, data not yet initialized, or user opted out
            var data = GsDataManager.DataOrNull;
            if (data == null) return;
            if (data.Flags.Contains("no-sentry") || data.OptedOut) return;

            try {
                SentrySdk.CaptureMessage(message, scope => {
                    scope.Level = level;
                    scope.SetTag("installId", data.InstallID);
                    scope.SetTag("LinkedUserId", data.LinkedUserId);
                });
            }
            catch (Exception ex) { try { _logger.Debug(ex, "Sentry CaptureMessage failed (non-critical)"); } catch { } }
        }

        /// <summary>
        /// Captures an exception in Sentry with additional context.
        /// </summary>
        /// <param name="exception">The exception to capture.</param>
        /// <param name="message">An optional message describing the context of the exception.</param>
        public static void CaptureException(Exception exception, string message = null) {
            if (!_initialized || _consent?.IsAllowed != true) return;
            // Skip if Sentry is disabled or data not yet initialized
            var data = GsDataManager.DataOrNull;
            if (data == null) return;
            if (data.Flags.Contains("no-sentry") || data.OptedOut) return;

            try {
                SentrySdk.CaptureException(exception, scope => {
                    scope.SetTag("installId", data.InstallID);
                    scope.SetTag("LinkedUserId", data.LinkedUserId);

                    if (!string.IsNullOrEmpty(message)) {
                        scope.SetExtra("contextMessage", message);
                    }
                });
            }
            catch (Exception ex) { try { _logger.Debug(ex, "Sentry CaptureException failed (non-critical)"); } catch { } }
        }

        /// <summary>
        /// Adds a breadcrumb to the Sentry session for debugging context.
        /// Breadcrumbs are only sent if an error occurs later.
        /// </summary>
        /// <param name="message">The breadcrumb message.</param>
        /// <param name="category">The category of the breadcrumb.</param>
        /// <param name="data">Optional key-value data to attach.</param>
        /// <param name="level">The severity level of the breadcrumb.</param>
        public static void AddBreadcrumb(string message, string category = null, Dictionary<string, string> data = null, BreadcrumbLevel level = BreadcrumbLevel.Info) {
            if (!_initialized || _consent?.IsAllowed != true) return;
            // Skip if Sentry is disabled or data not yet initialized
            var gsData = GsDataManager.DataOrNull;
            if (gsData == null) return;
            if (gsData.Flags.Contains("no-sentry") || gsData.OptedOut) return;

            try {
                SentrySdk.AddBreadcrumb(message, category, null, data, level);
            }
            catch (Exception ex) { try { _logger.Debug(ex, "Sentry AddBreadcrumb failed (non-critical)"); } catch { } }
        }
    }
}
