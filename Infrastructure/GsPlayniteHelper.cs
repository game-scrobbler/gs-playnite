using System;
using System.Reflection;

namespace GsPlugin.Infrastructure {
    /// <summary>
    /// Shared helpers for Playnite interaction that are used by multiple services.
    /// </summary>
    internal static class GsPlayniteHelper {
        private static readonly string[] TrustedHosts = {
            "gamescrobbler.com",
            "www.gamescrobbler.com",
            "playnite.link",
        };

        /// <summary>
        /// Returns true if the URL belongs to a trusted host (gamescrobbler.com, playnite.link).
        /// </summary>
        public static bool IsTrustedUrl(string url) {
            try {
                var uri = new Uri(url);
                var host = uri.Host.ToLowerInvariant();
                foreach (var trusted in TrustedHosts) {
                    if (host == trusted || host.EndsWith("." + trusted)) {
                        return true;
                    }
                }

                return false;
            }
            catch {
                return false;
            }
        }

        /// <summary>
        /// Opens the Playnite Add-ons dialog via reflection.
        /// Playnite does not expose a public API for this, so we reach into
        /// PlayniteApplication.Current.MainModelBase.OpenAddonsCommand.
        /// </summary>
        public static void OpenAddonsDialog() {
            try {
                var appType = Type.GetType("Playnite.PlayniteApplication, Playnite");
                if (appType == null) return;

                var currentProp = appType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
                var playniteApp = currentProp?.GetValue(null);
                if (playniteApp == null) return;

                var mainModelProp = appType.GetProperty("MainModelBase", BindingFlags.Public | BindingFlags.Instance);
                var mainModel = mainModelProp?.GetValue(playniteApp);
                if (mainModel == null) return;

                var openAddonsCommandProp = mainModel
                    .GetType()
                    .GetProperty("OpenAddonsCommand", BindingFlags.Public | BindingFlags.Instance);
                var command = openAddonsCommandProp?.GetValue(mainModel);
                if (command == null) return;

                var executeMethod = command
                    .GetType()
                    .GetMethod("Execute", new[] { typeof(object) });
                executeMethod?.Invoke(command, new object[] { null });
            }
            catch (Exception ex) {
                GsLogger.Warn($"Failed to open Add-ons dialog via reflection: {ex.Message}");
            }
        }
    }
}
