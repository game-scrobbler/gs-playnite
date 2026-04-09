using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Playnite;
using GsPlugin.Api;
using GsPlugin.Infrastructure;
using GsPlugin.Models;

namespace GsPlugin.Services {
    /// <summary>
    /// Fetches server-side notifications at startup and displays them
    /// in Playnite's native notification tray.
    /// </summary>
    internal class GsNotificationService {
        private static readonly ILogger _logger = LogManager.GetLogger();
        private readonly IPlayniteApi _playniteApi;
        private readonly IGsApiClient _apiClient;
        private readonly string _pluginId;
        private const string NotifIdPrefix = "gs-server-notif-";
        private const int MaxShownIds = 100;

        public GsNotificationService(IPlayniteApi playniteApi, IGsApiClient apiClient, string pluginId) {
            _playniteApi = playniteApi;
            _apiClient = apiClient;
            _pluginId = pluginId;
        }

        /// <summary>
        /// Fetches active notifications from the server and shows new ones in Playnite.
        /// Skips notifications that were already shown in a previous session.
        /// Called from a background thread — marshals UI work onto the dispatcher.
        /// </summary>
        public async Task FetchAndShowNotificationsAsync() {
            if (!GsDataManager.Data.ShowImportantNotifications) {
                return;
            }

            var installToken = GsDataManager.DataOrNull?.InstallToken;
            if (string.IsNullOrEmpty(installToken)) {
                return;
            }

            var result = await _apiClient.GetNotifications();
            if (result == null || result.notifications == null || result.notifications.Count == 0) {
                return;
            }

            // Take a snapshot of already-shown IDs under the lock to avoid racing with other threads.
            var alreadyShown = GsDataManager.GetShownNotificationIds();
            var toShow = new List<(string id, NotificationSeverity severity, string message, Func<Task>? action)>();

            foreach (var notif in result.notifications) {
                if (string.IsNullOrEmpty(notif.id) || alreadyShown.Contains(notif.id)) {
                    continue;
                }

                var playniteType = MapNotificationSeverity(notif.notification_type);
                var clickAction = CreateClickAction(notif.action_url);

                var message = !string.IsNullOrEmpty(notif.title)
                    ? $"{notif.title}: {notif.message}"
                    : notif.message;

                toShow.Add((notif.id, playniteType, message, clickAction));
            }

            if (toShow.Count == 0) {
                return;
            }

            // Marshal notification display onto the UI dispatcher to avoid cross-thread exceptions.
            // Wrapped in try/catch so a dispatcher fault cannot surface as an unhandled exception
            // that would be captured by Sentry as a false plugin error.
            try {
                UIDispatcher.Invoke(() => {
                    foreach (var (id, severity, message, action) in toShow) {
                        _playniteApi.Notifications.Add(new NotificationMessage(
                            NotifIdPrefix + id,
                            message,
                            severity,
                            action));
                    }
                });
            }
            catch (Exception ex) {
                GsLogger.Warn($"GsNotificationService: dispatcher invoke failed: {ex.Message}");
                return;
            }

            // Atomically record shown IDs and persist under the GsDataManager lock.
            var newIds = toShow.Select(t => t.id).ToList();
            GsDataManager.RecordShownNotifications(newIds, MaxShownIds);
            GsLogger.Info($"Displayed {toShow.Count} server notification(s)");
        }

        private static NotificationSeverity MapNotificationSeverity(string backendType) {
            switch (backendType) {
                case "error":
                    return NotificationSeverity.Error;
                default:
                    return NotificationSeverity.Info;
            }
        }

        private Func<Task>? CreateClickAction(string? actionUrl) {
            if (string.IsNullOrEmpty(actionUrl)) {
                return null;
            }

            if (actionUrl == "gs://settings") {
                return async () => {
                    try {
                        await _playniteApi.MainView.OpenPluginSettingsAsync(_pluginId);
                    }
                    catch (Exception ex) {
                        GsLogger.Warn($"Failed to open settings: {ex.Message}");
                    }
                };
            }

            if (actionUrl == "gs://addons") {
                return async () => {
                    await Task.CompletedTask;
                    try {
                        OpenAddonsDialog();
                    }
                    catch (Exception ex) {
                        GsLogger.Warn($"Failed to open add-ons dialog: {ex.Message}");
                    }
                };
            }

            if (actionUrl.StartsWith("https://")) {
                if (!IsTrustedUrl(actionUrl)) {
                    GsLogger.Warn($"Notification action_url rejected (untrusted host): {actionUrl}");
                    return null;
                }
                return async () => {
                    await Task.CompletedTask;
                    try {
                        Process.Start(new ProcessStartInfo(actionUrl) { UseShellExecute = true });
                    }
                    catch (Exception ex) {
                        GsLogger.Warn($"Failed to open URL {actionUrl}: {ex.Message}");
                    }
                };
            }

            return null;
        }

        private static bool IsTrustedUrl(string url) {
            return GsPlayniteHelper.IsTrustedUrl(url);
        }

        private static void OpenAddonsDialog() {
            GsPlayniteHelper.OpenAddonsDialog();
        }
    }
}
