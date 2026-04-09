using System;
using Playnite;

namespace GsPlugin.Infrastructure {
    public static class GsLogger {
        private static readonly ILogger _logger = LogManager.GetLogger();

        /// <summary>
        /// Set to true to show interactive HTTP debug windows in Debug builds.
        /// Disabled by default to prevent modal dialog spam during normal development.
        /// Enable explicitly when diagnosing HTTP issues.
        /// </summary>
        internal static bool ShowHttpDebugWindows = false;

        public static void Info(string message) {
            _logger.Info(message);
        }

        public static void Warn(string message) {
            _logger.Warn(message);
        }

        public static void Error(string message) {
            _logger.Error(message);
        }

        public static void Error(string message, Exception ex) {
            _logger.Error(ex, message);
        }

        public static void ShowHTTPDebugBox(string requestData, string responseData, bool isError = false) {
#if DEBUG
            if (!ShowHttpDebugWindows) return;
            _logger.Info($"[HTTP {(isError ? "ERROR" : "DEBUG")}] Request: {requestData} | Response: {responseData}");
#endif
        }

        public static void ShowDebugInfoBox(string message, string title = "Debug Info") {
#if DEBUG
            _logger.Info($"[{title}] {message}");
#endif
        }
    }
}
