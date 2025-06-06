using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using Playnite.SDK;
using Playnite.SDK.Events;
using Sentry;

namespace GsPlugin {
    /// <summary>
    /// Handles URI requests for automatic account linking functionality.
    /// This service manages deep link processing from web applications.
    /// </summary>
    public class GsUriHandler {
        private readonly IPlayniteAPI _playniteApi;
        private readonly GsApiClient _apiClient;
        private static readonly ILogger _logger = LogManager.GetLogger();

        public GsUriHandler(IPlayniteAPI playniteApi, GsApiClient apiClient) {
            _playniteApi = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        }

        /// <summary>
        /// Registers the URI handler for automatic account linking.
        /// </summary>
        public void RegisterUriHandler() {
            try {
                _playniteApi.UriHandler.RegisterSource("gamescrobbler", HandleUriRequest);
                GsLogger.Info("Successfully registered URI handler for playnite://gamescrobbler links");

                SentrySdk.AddBreadcrumb(
                    message: "URI handler registered",
                    category: "initialization"
                );
            }
            catch (Exception ex) {
                GsLogger.Error("Failed to register URI handler", ex);
                SentrySdk.CaptureException(ex);
            }
        }

        /// <summary>
        /// Handles URI requests for automatic account linking.
        /// Expected format: playnite://gamescrobbler/link/[token]
        /// </summary>
        /// <param name="args">URI arguments containing the token</param>
        private async void HandleUriRequest(PlayniteUriEventArgs args) {
            try {
                GsLogger.Info($"Received URI request with {args.Arguments.Length} arguments");

                // Log the arguments for debugging
                for (int i = 0; i < args.Arguments.Length; i++) {
                    GsLogger.Info($"Argument {i}: {args.Arguments[i]}");
                }

                // Expected format: playnite://gamescrobbler/link/[token]
                if (args.Arguments.Length >= 2 && args.Arguments[0].Equals("link", StringComparison.OrdinalIgnoreCase)) {
                    string token = args.Arguments[1];

                    if (string.IsNullOrWhiteSpace(token)) {
                        HandleEmptyToken();
                        return;
                    }

                    // Check if already linked and get user confirmation if needed
                    if (GsDataManager.Data.IsLinked !== "not_linked" && !ShouldProceedWithRelinking()) {
                        return;
                    }

                    // Show linking in progress dialog
                    ShowLinkingInProgressDialog();

                    // Attempt to link the account
                    await ProcessAutomaticLinking(token);
                }
                else {
                    HandleInvalidUriFormat(args.Arguments.Length);
                }
            }
            catch (Exception ex) {
                HandleUriRequestException(ex);
            }
        }

        /// <summary>
        /// Processes automatic account linking using the provided token.
        /// </summary>
        /// <param name="token">The linking token from the web app</param>
        private async Task ProcessAutomaticLinking(string token) {
            try {
                LogLinkingAttempt(token);

                // Use the existing API client to verify the token
                var response = await _apiClient.VerifyToken(token, GsDataManager.Data.InstallID);

                if (response != null && response.status == "success") {
                    await HandleSuccessfulLinking(response);
                }
                else {
                    HandleFailedLinking(response);
                }
            }
            catch (Exception ex) {
                HandleLinkingException(ex);
            }
        }

        /// <summary>
        /// Handles successful account linking.
        /// </summary>
        /// <param name="response">The successful API response</param>
        private async Task HandleSuccessfulLinking(GsApiClient.TokenVerificationRes response) {
            // Update the linking state
            GsDataManager.Data.IsLinked = true;
            GsDataManager.Data.LinkedUserId = response.userId;
            GsDataManager.Save();

            // Notify any listening UI components
            GsPluginSettingsViewModel.OnLinkingStatusChanged();

            // Show success message
            _playniteApi.Dialogs.ShowMessage(
                $"Account successfully linked!\nUser ID: {response.userId}",
                "Account Linking Success",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );

            GsLogger.Info($"Account successfully linked automatically to User ID: {response.userId}");

            SentrySdk.AddBreadcrumb(
                message: "Automatic account linking successful",
                category: "linking",
                data: new Dictionary<string, string> {
                    { "UserId", response.userId },
                    { "InstallID", GsDataManager.Data.InstallID }
                }
            );
        }

        /// <summary>
        /// Handles failed account linking.
        /// </summary>
        /// <param name="response">The failed API response</param>
        private void HandleFailedLinking(GsApiClient.TokenVerificationRes response) {
            string errorMessage = response?.message ?? "Unknown error occurred during linking";
            GsLogger.Error($"Automatic linking failed: {errorMessage}");

            _playniteApi.Dialogs.ShowMessage(
                $"Account linking failed: {errorMessage}",
                "Account Linking Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );

            SentrySdk.CaptureMessage(
                "Automatic account linking failed",
                scope => {
                    scope.Level = SentryLevel.Warning;
                    scope.SetExtra("ErrorMessage", errorMessage);
                    scope.SetExtra("ResponseStatus", response?.status ?? "null");
                }
            );
        }

        /// <summary>
        /// Handles empty token scenario.
        /// </summary>
        private void HandleEmptyToken() {
            GsLogger.Warn("Empty token received in URI request");
            _playniteApi.Dialogs.ShowMessage(
                "Invalid linking token received.",
                "Account Linking Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
        }

        /// <summary>
        /// Checks if the user wants to proceed with relinking to a different account.
        /// </summary>
        /// <returns>True if the user wants to proceed, false otherwise</returns>
        private bool ShouldProceedWithRelinking() {
            var result = _playniteApi.Dialogs.ShowMessage(
                $"Account is already linked to User ID: {GsDataManager.Data.LinkedUserId}\n\nDo you want to link to a different account?",
                "Account Already Linked",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            return result == MessageBoxResult.Yes;
        }

        /// <summary>
        /// Shows the linking in progress dialog.
        /// </summary>
        private void ShowLinkingInProgressDialog() {
            _playniteApi.Dialogs.ShowMessage(
                "Processing account linking request...",
                "Account Linking",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }

        /// <summary>
        /// Handles invalid URI format scenario.
        /// </summary>
        /// <param name="argumentCount">The number of arguments received</param>
        private void HandleInvalidUriFormat(int argumentCount) {
            GsLogger.Warn($"Invalid URI format. Expected: playnite://gamescrobbler/link/[token], got {argumentCount} arguments");
            _playniteApi.Dialogs.ShowMessage(
                "Invalid linking request format.",
                "Account Linking Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
        }

        /// <summary>
        /// Handles exceptions during URI request processing.
        /// </summary>
        /// <param name="ex">The exception that occurred</param>
        private void HandleUriRequestException(Exception ex) {
            GsLogger.Error("Error handling URI request", ex);
            SentrySdk.CaptureException(ex);

            _playniteApi.Dialogs.ShowMessage(
                $"Error processing linking request: {ex.Message}",
                "Account Linking Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }

        /// <summary>
        /// Handles exceptions during the linking process.
        /// </summary>
        /// <param name="ex">The exception that occurred</param>
        private void HandleLinkingException(Exception ex) {
            GsLogger.Error("Exception during automatic linking", ex);
            SentrySdk.CaptureException(ex);

            _playniteApi.Dialogs.ShowMessage(
                $"Error during automatic linking: {ex.Message}",
                "Account Linking Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }

        /// <summary>
        /// Logs the linking attempt with partial token for security.
        /// </summary>
        /// <param name="token">The linking token</param>
        private void LogLinkingAttempt(string token) {
            GsLogger.Info($"Processing automatic linking with token: {token.Substring(0, Math.Min(8, token.Length))}...");

            SentrySdk.AddBreadcrumb(
                message: "Starting automatic account linking",
                category: "linking",
                data: new Dictionary<string, string> {
                    { "TokenLength", token.Length.ToString() },
                    { "InstallID", GsDataManager.Data.InstallID }
                }
            );
        }
    }
}
