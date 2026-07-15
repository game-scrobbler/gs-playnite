using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using Playnite.SDK;
using Sentry;
using GsPlugin.Api;
using GsPlugin.Infrastructure;
using GsPlugin.Models;

namespace GsPlugin.Services {
    /// <summary>
    /// Represents the context in which account linking is being performed.
    /// </summary>
    public enum LinkingContext {
        ManualSettings,    // From settings UI
        AutomaticUri      // From URI handler
    }

    /// <summary>
    /// Represents the result of an account linking operation.
    /// </summary>
    public class LinkingResult {
        public bool Success { get; set; }
        public string UserId { get; set; }
        public string ErrorMessage { get; set; }
        public Exception Exception { get; set; }
        public LinkingContext Context { get; set; }
        /// <summary>
        /// True when the failure was caused by a network/connectivity problem rather than
        /// a server-side token rejection. Callers can use this to offer a retry action.
        /// </summary>
        public bool IsNetworkError { get; set; }
        /// <summary>
        /// True when the failure was caused by an expired or invalid link token.
        /// Callers can use this to offer contextual recovery (e.g. open linking page).
        /// </summary>
        public bool IsTokenExpiry { get; set; }

        public static LinkingResult CreateSuccess(string userId, LinkingContext context) {
            return new LinkingResult {
                Success = true,
                UserId = userId,
                Context = context
            };
        }

        public static LinkingResult CreateError(string errorMessage, LinkingContext context, Exception exception = null, bool isNetworkError = false, bool isTokenExpiry = false) {
            return new LinkingResult {
                Success = false,
                ErrorMessage = errorMessage,
                Context = context,
                Exception = exception,
                IsNetworkError = isNetworkError,
                IsTokenExpiry = isTokenExpiry
            };
        }
    }

    /// <summary>
    /// Service responsible for handling account linking functionality.
    /// Manages the process of linking Playnite plugin with GS user accounts.
    /// </summary>
    public class GsAccountLinkingService {
        private static readonly ILogger _logger = LogManager.GetLogger();
        private readonly IGsApiClient _apiClient;
        private readonly IPlayniteAPI _playniteApi;

        /// <summary>
        /// Event triggered when account linking status changes.
        /// </summary>
        public static event EventHandler LinkingStatusChanged;

        /// <summary>
        /// Initializes a new instance of the GsAccountLinkingService.
        /// </summary>
        /// <param name="apiClient">The API client for communicating with the GameScrobbler service.</param>
        /// <param name="playniteApi">The Playnite API instance for UI interactions.</param>
        public GsAccountLinkingService(IGsApiClient apiClient, IPlayniteAPI playniteApi) {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _playniteApi = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));
        }

        /// <summary>
        /// Performs account linking with the provided token.
        /// This is the main entry point for all account linking operations.
        /// </summary>
        /// <param name="token">The linking token</param>
        /// <param name="context">The context in which linking is being performed</param>
        /// <returns>A LinkingResult indicating the outcome</returns>
        public async Task<LinkingResult> LinkAccountAsync(string token, LinkingContext context) {
            // Block linking when user has opted out
            if (GsDataManager.IsOptedOut) {
                return LinkingResult.CreateError("Plugin is disabled. Opt back in to link your account.", context);
            }

            // Validate token
            if (!ValidateToken(token)) {
                return LinkingResult.CreateError("Please enter a valid token", context);
            }

            GsLogger.Info($"Starting {context} account linking (install_id={GsDataManager.Data.InstallID}).");
            GsSentry.AddBreadcrumb(
                message: $"Starting {context} account linking",
                category: "linking",
                data: new Dictionary<string, string> {
                    { "Context", context.ToString() },
                    { "TokenLength", token.Length.ToString() },
                    { "InstallID", GsDataManager.Data.InstallID }
                }
            );

            try {
                // Verify token with API
                var response = await _apiClient.VerifyToken(token, GsDataManager.Data.InstallID);

                if (response == null) {
                    string errorMessage = "Network error — could not reach the server. Please check your connection and try again.";
                    GsLogger.Error($"{context} linking failed: {errorMessage}");
                    return LinkingResult.CreateError(errorMessage, context, isNetworkError: true);
                }

                if (response.success) {
                    // A verify can succeed yet resolve to the "not_linked" sentinel (or an empty
                    // user id): the token was accepted but the server did NOT bind this install to
                    // an account. Reporting that as success made the settings UI show
                    // "Successfully linked!" while the connection status stayed "Disconnected" and
                    // the website's linking page kept polling "not linked" (issue #54). Treat it as
                    // a failed link, clear any local link so state matches the server, and route the
                    // user to fetch a fresh token (same recovery path as an expired token).
                    if (!IsLinkedUserId(response.userId)) {
                        GsDataManager.MutateAndSave(d => d.LinkedUserId = null);
                        OnLinkingStatusChanged();

                        GsLogger.Error($"{context} linking did not complete: token verified but the server returned a not-linked result (install_id={GsDataManager.Data.InstallID}, userId={response.userId ?? "null"}).");
                        GsSentry.CaptureMessage(
                            $"{context} linking verified but returned not-linked (install_id={GsDataManager.Data.InstallID})",
                            SentryLevel.Warning);

                        string notLinkedMessage = GsLocalization.Get(
                            "LOCGsPluginStatusTokenExpired",
                            "Token expired — click \"Open website to link\" to get a new one.");
                        return LinkingResult.CreateError(notLinkedMessage, context, isTokenExpiry: true);
                    }

                    if (response.userId.Length > 256) {
                        string errorMessage = GsLocalization.Get(
                            "LOCGsPluginInvalidUserIdFormat",
                            "Invalid user ID format received from server");
                        GsLogger.Error($"{context} linking failed: {errorMessage}");
                        return LinkingResult.CreateError(errorMessage, context);
                    }

                    GsDataManager.MutateAndSave(d => d.LinkedUserId = response.userId);
                    // Notify listeners of status change
                    OnLinkingStatusChanged();

                    GsLogger.Info($"Account successfully linked via {context} to User ID: {response.userId} (install_id={GsDataManager.Data.InstallID})");
                    GsSentry.AddBreadcrumb(
                        message: $"{context} account linking successful",
                        category: "linking",
                        data: new Dictionary<string, string> {
                            { "Context", context.ToString() },
                            { "UserId", response.userId },
                            { "InstallID", GsDataManager.Data.InstallID }
                        }
                    );

                    GsPostHog.Capture("account_linked", new Dictionary<string, object> {
                        { "context", context.ToString() }
                    });

                    return LinkingResult.CreateSuccess(response.userId, context);
                }
                else {
                    string serverMessage = response?.message ?? GsLocalization.Get("LOCGsPluginUnknownLinkingError", "Unknown error occurred during linking");
                    // Prefer structured errorCode; fall back to message matching
                    // only for older server versions that don't send errorCode.
                    string errorCode = response?.errorCode;
                    bool isTokenExpiry = errorCode == "TOKEN_EXPIRED"
                                      || errorCode == "TOKEN_INVALID"
                                      || (string.IsNullOrEmpty(errorCode)
                                          && (serverMessage.IndexOf("expired", StringComparison.OrdinalIgnoreCase) >= 0
                                              || serverMessage.IndexOf("invalid", StringComparison.OrdinalIgnoreCase) >= 0));

                    GsLogger.Error($"{context} linking failed: {serverMessage}");

                    // Expired/invalid tokens are expected user behavior (slow to click,
                    // reusing old links) — log as breadcrumb, not a Sentry issue.
                    if (isTokenExpiry) {
                        GsSentry.AddBreadcrumb(
                            message: $"{context} account linking failed: token expired/invalid",
                            category: "linking"
                        );
                    }
                    else {
                        GsSentry.CaptureMessage(
                            $"{context} account linking failed: {serverMessage}",
                            SentryLevel.Warning
                        );
                    }
                    return LinkingResult.CreateError(serverMessage, context, isTokenExpiry: isTokenExpiry);
                }
            }
            catch (Exception ex) {
                GsLogger.Error($"Exception during {context} linking", ex);
                GsSentry.CaptureException(ex, $"Exception during {context} linking");
                return LinkingResult.CreateError(
                    GsLocalization.Format("LOCGsPluginLinkingErrorFormat", $"Error during linking: {ex.Message}", ex.Message),
                    context, ex, isNetworkError: true);
            }
        }

        /// <summary>
        /// Validates the provided token.
        /// </summary>
        /// <param name="token">The token to validate</param>
        /// <returns>True if token is valid, false otherwise</returns>
        public static bool ValidateToken(string token) {
            if (string.IsNullOrWhiteSpace(token)) return false;
            if (token.Length > 512) return false;
            // Allow alphanumeric, hyphens, underscores, dots, plus, equals, slashes (covers JWT/base64 tokens)
            if (!Regex.IsMatch(token, @"^[a-zA-Z0-9\-_\.+=\/]+$")) return false;
            return true;
        }

        /// <summary>
        /// Returns true when a verify response's user id represents a real linked account.
        /// A successful verify that resolves to the <see cref="GsData.NotLinkedValue"/> sentinel
        /// (or an empty value) means the token was accepted without actually binding the install
        /// to an account, so callers must not treat it as a successful link (issue #54).
        /// </summary>
        internal static bool IsLinkedUserId(string userId) {
            return !string.IsNullOrWhiteSpace(userId) && userId != GsData.NotLinkedValue;
        }

        /// <summary>
        /// Checks if the user wants to proceed with relinking to a different account.
        /// </summary>
        /// <returns>True if the user wants to proceed, false otherwise</returns>
        public bool ShouldProceedWithRelinking() {
            if (!GsDataManager.IsAccountLinked) {
                return true;
            }

            var result = _playniteApi.Dialogs.ShowMessage(
                GsLocalization.Format("LOCGsPluginAlreadyLinkedBody",
                    $"Account is already linked to User ID: {GsDataManager.Data.LinkedUserId}\n\nDo you want to link to a different account?",
                    GsDataManager.Data.LinkedUserId),
                GsLocalization.Get("LOCGsPluginAlreadyLinkedTitle", "Account Already Linked"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            return result == MessageBoxResult.Yes;
        }

        /// <summary>
        /// Disconnects the current account link. Asks for confirmation,
        /// calls the server to remove the link, then clears local state.
        /// </summary>
        public async Task<LinkingResult> UnlinkAccountAsync() {
            if (!GsDataManager.IsAccountLinked) {
                return LinkingResult.CreateError(
                    GsLocalization.Get("LOCGsPluginDisconnectNoAccount", "No account is currently linked."),
                    LinkingContext.ManualSettings);
            }

            var confirm = _playniteApi.Dialogs.ShowMessage(
                GsLocalization.Get("LOCGsPluginDisconnectDialogBody", "Disconnect your account? Your game data will be kept on the server.\nYou can re-link anytime."),
                GsLocalization.Get("LOCGsPluginDisconnectDialogTitle", "Disconnect Account"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) {
                return LinkingResult.CreateError("Cancelled", LinkingContext.ManualSettings);
            }

            try {
                var response = await _apiClient.UnlinkAccount();

                if (response == null) {
                    return LinkingResult.CreateError(
                        GsLocalization.Get("LOCGsPluginNetworkError", "Network error — could not reach the server. Please try again."),
                        LinkingContext.ManualSettings, isNetworkError: true);
                }

                if (response.success) {
                    // Clear all identity-bound state to prevent data from bleeding
                    // across accounts after a re-link. Same fields as RotateInstallId
                    // but without rotating the InstallID/InstallToken themselves.
                    GsDataManager.MutateAndSave(d => {
                        d.LinkedUserId = null;
                        d.ActiveSessionsByGameId.Clear();
                        d.PendingStartGameIds.Clear();
                        d.PendingScrobbles.Clear();
                        d.LastLibraryHash = null;
                        d.LastAchievementHash = null;
                        d.LastSyncAt = null;
                        d.LastSyncGameCount = null;
                        d.SyncCooldownExpiresAt = null;
                        d.LibraryDiffSyncCooldownExpiresAt = null;
                        d.LastIntegrationAccountsHash = null;
                    });
                    GsSyncHashIndex.Reset();
                    OnLinkingStatusChanged();
                    // Refresh diagnostics widgets (pending scrobble count, last-sync text)
                    // since MutateAndSave does not emit DiagnosticsStateChanged.
                    GsDataManager.NotifyDiagnosticsChanged();

                    GsLogger.Info("Account unlinked; identity-bound state cleared.");
                    GsSentry.AddBreadcrumb(
                        message: "Account unlinked by user",
                        category: "linking"
                    );
                    GsPostHog.Capture("account_unlinked");

                    return LinkingResult.CreateSuccess(null, LinkingContext.ManualSettings);
                }
                else {
                    string errorMessage = response.error ?? GsLocalization.Get("LOCGsPluginUnlinkFailed", "Failed to disconnect account.");
                    GsLogger.Error($"Unlink failed: {errorMessage}");
                    return LinkingResult.CreateError(errorMessage, LinkingContext.ManualSettings);
                }
            }
            catch (Exception ex) {
                GsLogger.Error("Exception during account unlinking", ex);
                GsSentry.CaptureException(ex, "Exception during account unlinking");
                return LinkingResult.CreateError(
                    GsLocalization.Format("LOCGsPluginErrorFormat", $"Error: {ex.Message}", ex.Message),
                    LinkingContext.ManualSettings, ex, isNetworkError: true);
            }
        }

        /// <summary>
        /// Triggers the linking status changed event.
        /// </summary>
        public static void OnLinkingStatusChanged() {
            LinkingStatusChanged?.Invoke(null, EventArgs.Empty);
        }

    }
}
