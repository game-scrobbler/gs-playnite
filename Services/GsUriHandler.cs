using System;
using System.Threading.Tasks;
using System.Windows;
using Playnite.SDK;
using Playnite.SDK.Events;
using Sentry;
using GsPlugin.Infrastructure;
using GsPlugin.Models;

namespace GsPlugin.Services {
    /// <summary>
    /// Handles URI requests for automatic account linking functionality.
    /// This service manages deep link processing from web applications.
    /// </summary>
    public class GsUriHandler {
        private readonly IPlayniteAPI _playniteApi;
        private readonly GsAccountLinkingService _linkingService;
        private static readonly ILogger _logger = LogManager.GetLogger();

        public GsUriHandler(IPlayniteAPI playniteApi, GsAccountLinkingService linkingService) {
            _playniteApi = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));
            _linkingService = linkingService ?? throw new ArgumentNullException(nameof(linkingService));
        }

        /// <summary>
        /// Registers the URI handler for automatic account linking.
        /// </summary>
        public void RegisterUriHandler() {
            try {
                _playniteApi.UriHandler.RegisterSource("gamescrobbler", HandleUriRequest);
                GsLogger.Info("Successfully registered URI handler for playnite://gamescrobbler links");

                GsSentry.AddBreadcrumb(
                    message: "URI handler registered",
                    category: "initialization"
                );
            }
            catch (Exception ex) {
                GsLogger.Error("Failed to register URI handler", ex);
                GsSentry.CaptureException(ex, "Failed to register URI handler");
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

                // Log the arguments for debugging (mask sensitive token values)
                for (int i = 0; i < args.Arguments.Length; i++) {
                    string logValue = args.Arguments[i];
                    // Mask token values (argument after "link" command)
                    if (i == 1 && args.Arguments.Length >= 2 && args.Arguments[0].Equals("link", StringComparison.OrdinalIgnoreCase)) {
                        logValue = logValue.Length > 4 ? logValue.Substring(0, 4) + "****" : "****";
                    }
                    GsLogger.Info($"Argument {i}: {logValue}");
                }

                // Expected format: playnite://gamescrobbler/link/[token]
                if (args.Arguments.Length >= 2 && args.Arguments[0].Equals("link", StringComparison.OrdinalIgnoreCase)) {
                    string token = args.Arguments[1];

                    if (string.IsNullOrWhiteSpace(token)) {
                        HandleEmptyToken();
                        return;
                    }

                    // Check if already linked and get user confirmation if needed
                    bool isLinked = GsDataManager.IsAccountLinked;
                    if (isLinked && !_linkingService.ShouldProceedWithRelinking()) {
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
                // Use the centralized linking service
                var result = await _linkingService.LinkAccountAsync(token, LinkingContext.AutomaticUri);

                if (result.Success) {
                    _playniteApi?.Dialogs?.ShowMessage($"Account successfully linked!\nUser ID: {result.UserId}", "Account Linking Success", MessageBoxButton.OK, MessageBoxImage.Information
                    );
                }
                else if (result.IsNetworkError) {
                    var retry = _playniteApi?.Dialogs?.ShowMessage(
                        $"Account linking failed due to a network error:\n{result.ErrorMessage}\n\nWould you like to retry?",
                        "Account Linking Failed — Retry?",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                    if (retry == MessageBoxResult.Yes) {
                        await ProcessAutomaticLinking(token);
                    }
                }
                else {
                    _playniteApi?.Dialogs?.ShowMessage($"Account linking failed: {result.ErrorMessage}", "Account Linking Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex) {
                HandleLinkingException(ex);
            }
        }


        /// <summary>
        /// Handles empty token scenario.
        /// </summary>
        private void HandleEmptyToken() {
            GsLogger.Warn("Empty token received in URI request");
            _playniteApi?.Dialogs?.ShowMessage(
                "Invalid linking token received.",
                "Account Linking Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
        }

        /// <summary>
        /// Handles exceptions during the linking process.
        /// </summary>
        /// <param name="ex">The exception that occurred</param>
        private void HandleLinkingException(Exception ex) {
            GsLogger.Error("Exception during automatic linking", ex);
            GsSentry.CaptureException(ex, "Exception during automatic linking via URI handler");

            _playniteApi?.Dialogs?.ShowMessage(
                $"Error during automatic linking: {ex.Message}",
                "Account Linking Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }

        /// <summary>
        /// Handles invalid URI format scenario.
        /// </summary>
        /// <param name="argumentCount">Number of arguments received</param>
        private static void HandleInvalidUriFormat(int argumentCount) {
            GsLogger.Warn($"Invalid URI format. Expected 'link/[token]', received {argumentCount} arguments");
        }

        /// <summary>
        /// Shows a dialog indicating that linking is in progress.
        /// </summary>
        private static void ShowLinkingInProgressDialog() {
            // Note: This is a fire-and-forget notification
            // In a production environment, you might want to show a proper progress dialog
            GsLogger.Info("Account linking initiated via URI handler");
        }

        /// <summary>
        /// Handles general URI request exceptions.
        /// </summary>
        /// <param name="ex">The exception that occurred</param>
        private void HandleUriRequestException(Exception ex) {
            GsLogger.Error("Unexpected error handling URI request", ex);
            GsSentry.CaptureException(ex, "Unexpected error handling URI request");

            // Null check to prevent cascading exceptions
            if (_playniteApi?.Dialogs != null) {
                _playniteApi.Dialogs.ShowMessage($"Unexpected error processing URI request: {ex.Message}", "Account Linking Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
