using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using Playnite.SDK;
using Sentry;

namespace GsPlugin {
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

        public static LinkingResult CreateSuccess(string userId, LinkingContext context) {
            return new LinkingResult {
                Success = true,
                UserId = userId,
                Context = context
            };
        }

        public static LinkingResult CreateError(string errorMessage, LinkingContext context, Exception exception = null) {
            return new LinkingResult {
                Success = false,
                ErrorMessage = errorMessage,
                Context = context,
                Exception = exception
            };
        }
    }

    /// <summary>
    /// Service responsible for handling account linking functionality.
    /// Manages the process of linking Playnite plugin with GS user accounts.
    /// </summary>
    public class GsAccountLinkingService {
        private static readonly ILogger _logger = LogManager.GetLogger();
        private readonly GsApiClient _apiClient;
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
        public GsAccountLinkingService(GsApiClient apiClient, IPlayniteAPI playniteApi) {
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
            // Validate token
            if (!ValidateToken(token)) {
                return LinkingResult.CreateError("Please enter a valid token", context);
            }

            GsLogger.Info($"Starting {context} account linking.");
            SentrySdk.AddBreadcrumb(
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
                // log the response
                GsLogger.ShowDebugInfoBox($"!!!!Linking response: {response}", "Debug - Linking Response");

                if (response != null && response.status == "success") {
                    if (response.userId == "not_linked" || string.IsNullOrEmpty(response.userId)) {
                        GsDataManager.Data.LinkedUserId = null;
                    } else {
                        GsDataManager.Data.LinkedUserId = response.userId;
                    }
                    GsDataManager.Save();
                    // Notify listeners of status change
                    OnLinkingStatusChanged();

                   GsLogger.Info($"Account successfully linked via {context} to User ID: {response.userId}");

                    SentrySdk.AddBreadcrumb(
                        message: $"{context} account linking successful",
                        category: "linking",
                        data: new Dictionary<string, string> {
                            { "Context", context.ToString() },
                            { "UserId", response.userId },
                            { "InstallID", GsDataManager.Data.InstallID }
                        }
                    );

                    return LinkingResult.CreateSuccess(response.userId, context);
                }
                else {
                    string errorMessage = response?.message ?? "Unknown error occurred during linking";
                    GsLogger.Error($"{context} linking failed: {errorMessage}");
                    SentrySdk.CaptureMessage(
                        $"{context} account linking failed",
                        scope => {
                            scope.Level = SentryLevel.Warning;
                            scope.SetExtra("Context", context.ToString());
                            scope.SetExtra("ErrorMessage", errorMessage);
                        }
                    );
                    return LinkingResult.CreateError(errorMessage, context);
                }
            }
            catch (Exception ex) {
                GsLogger.Error($"Exception during {context} linking", ex);
                SentrySdk.CaptureException(ex, scope => {
                    scope.SetExtra("Context", context.ToString());
                });
                return LinkingResult.CreateError($"Error during linking: {ex.Message}", context, ex);
            }
        }

        /// <summary>
        /// Validates the provided token.
        /// </summary>
        /// <param name="token">The token to validate</param>
        /// <returns>True if token is valid, false otherwise</returns>
        public static bool ValidateToken(string token) {
            return !string.IsNullOrWhiteSpace(token);
        }

        /// <summary>
        /// Checks if the user wants to proceed with relinking to a different account.
        /// </summary>
        /// <returns>True if the user wants to proceed, false otherwise</returns>
        public bool ShouldProceedWithRelinking() {
            if (string.IsNullOrEmpty(GsDataManager.Data.LinkedUserId) || GsDataManager.Data.LinkedUserId == "not_linked") {
                return true;
            }

            var result = _playniteApi.Dialogs.ShowMessage(
                $"Account is already linked to User ID: {GsDataManager.Data.LinkedUserId}\n\nDo you want to link to a different account?",
                "Account Already Linked",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            return result == MessageBoxResult.Yes;
        }

        /// <summary>
        /// Triggers the linking status changed event.
        /// </summary>
        public static void OnLinkingStatusChanged() {
            LinkingStatusChanged?.Invoke(null, EventArgs.Empty);
        }

    }
}
