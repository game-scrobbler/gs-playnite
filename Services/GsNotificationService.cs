using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using Playnite.SDK;
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
        private readonly IPlayniteAPI _playniteApi;
        private readonly IGsApiClient _apiClient;
        private readonly Guid _pluginId;
        private const string NotifIdPrefix = "gs-server-notif-";
        private const int MaxShownIds = 100;

        public GsNotificationService(IPlayniteAPI playniteApi, IGsApiClient apiClient, Guid pluginId) {
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
            var toShow = new List<(string id, NotificationType type, string message, Action action)>();

            foreach (var notif in result.notifications) {
                if (string.IsNullOrEmpty(notif.id) || alreadyShown.Contains(notif.id)) {
                    continue;
                }

                var playniteType = MapNotificationType(notif.notification_type);
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
            Application.Current.Dispatcher.Invoke(() => {
                foreach (var (id, type, message, action) in toShow) {
                    _playniteApi.Notifications.Add(new NotificationMessage(
                        NotifIdPrefix + id,
                        message,
                        type,
                        action));
                }
            });

            // Atomically record shown IDs and persist under the GsDataManager lock.
            var newIds = toShow.Select(t => t.id).ToList();
            GsDataManager.RecordShownNotifications(newIds, MaxShownIds);
            GsLogger.Info($"Displayed {toShow.Count} server notification(s)");
        }

        private static NotificationType MapNotificationType(string backendType) {
            switch (backendType) {
                case "error":
                    return NotificationType.Error;
                default:
                    return NotificationType.Info;
            }
        }

        private Action CreateClickAction(string actionUrl) {
            if (string.IsNullOrEmpty(actionUrl)) {
                return null;
            }

            if (actionUrl == "gs://settings") {
                return () => {
                    try {
                        _playniteApi.MainView.OpenPluginSettings(_pluginId);
                    }
                    catch (Exception ex) {
                        GsLogger.Warn($"Failed to open settings: {ex.Message}");
                    }
                };
            }

            if (actionUrl == "gs://addons") {
                return () => {
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
                return () => {
                    try {
                        Process.Start(actionUrl);
                    }
                    catch (Exception ex) {
                        GsLogger.Warn($"Failed to open URL {actionUrl}: {ex.Message}");
                    }
                };
            }

            return null;
        }

        private static readonly string[] TrustedHosts = {
            "gamescrobbler.com",
            "www.gamescrobbler.com",
            "playnite.link",
        };

        private static bool IsTrustedUrl(string url) {
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
        /// Same approach as GsUpdateChecker.
        /// </summary>
        private static void OpenAddonsDialog() {
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
    }
}
