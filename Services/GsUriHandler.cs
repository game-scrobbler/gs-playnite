using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using Playnite;
using GsPlugin.Infrastructure;
using GsPlugin.Models;

namespace GsPlugin.Services {
    /// <summary>
    /// Handles URI requests for automatic account linking functionality.
    /// This service manages deep link processing from web applications.
    /// </summary>
    public class GsUriHandler {
        private readonly IPlayniteApi _playniteApi;
        private readonly GsAccountLinkingService _linkingService;
        private static readonly ILogger _logger = LogManager.GetLogger();

        public GsUriHandler(IPlayniteApi playniteApi, GsAccountLinkingService linkingService) {
            _playniteApi = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));
            _linkingService = linkingService ?? throw new ArgumentNullException(nameof(linkingService));
        }

        /// <summary>
        /// Registers the URI handler for automatic account linking.
        /// </summary>
        public void RegisterUriHandler() {
            try {
                _playniteApi.UriHandler.RegisterSource("gamescrobbler", HandleUriRequest);
                GsLogger.Info("Successfully registered URI handler for playnite11://gamescrobbler links");

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
        /// Optional query param: ?expires_at=[unix_seconds]
        /// </summary>
        /// <param name="args">URI arguments containing the token</param>
        private async Task HandleUriRequest(PlayniteUriEventArgs args) {
            try {
                if (GsDataManager.IsOptedOut) {
                    GsLogger.Info("URI request ignored: plugin is opted out");
                    return;
                }

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
                // or: playnite://gamescrobbler/link/[token]?expires_at=[unix_seconds]
                if (args.Arguments.Length >= 2 && args.Arguments[0].Equals("link", StringComparison.OrdinalIgnoreCase)) {
                    string rawToken = args.Arguments[1];

                    // Parse optional expires_at query param from the token argument
                    string token = rawToken;
                    DateTimeOffset? expiresAt = null;
                    int qIndex = rawToken.IndexOf('?');
                    if (qIndex >= 0) {
                        token = rawToken.Substring(0, qIndex);
                        expiresAt = ParseExpiresAt(rawToken.Substring(qIndex + 1));
                    }

                    if (string.IsNullOrWhiteSpace(token)) {
                        HandleEmptyToken();
                        return;
                    }

                    // expires_at is a UX hint only — server is authoritative.
                    // Log if it looks expired but still attempt verification.
                    if (expiresAt.HasValue && DateTimeOffset.UtcNow >= expiresAt.Value) {
                        GsLogger.Info("Deep link expires_at suggests token may have expired; proceeding with server verification");
                    }

                    // Check if already linked and get user confirmation if needed
                    bool isLinked = GsDataManager.IsAccountLinked;
                    if (isLinked && !GsAccountLinkingService.ShouldProceedWithRelinking()) {
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
        /// Parses the expires_at value from a query string (e.g. "expires_at=1711459200").
        /// Returns null if the param is missing or unparseable.
        /// </summary>
        private static DateTimeOffset? ParseExpiresAt(string queryString) {
            try {
                const string prefix = "expires_at=";
                int start = queryString.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
                if (start < 0) return null;

                string value = queryString.Substring(start + prefix.Length);
                int ampIndex = value.IndexOf('&');
                if (ampIndex >= 0) value = value.Substring(0, ampIndex);

                if (long.TryParse(value, out long unixSeconds)) {
                    return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
                }
            }
            catch (Exception ex) {
                GsLogger.Warn($"Failed to parse expires_at from deep link: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Processes automatic account linking using the provided token.
        /// </summary>
        /// <param name="token">The linking token from the web app</param>
        private const int MaxLinkRetries = 3;

        private async Task ProcessAutomaticLinking(string token) {
            for (int attempt = 0; attempt < MaxLinkRetries; attempt++) {
                try {
                    var result = await _linkingService.LinkAccountAsync(token, LinkingContext.AutomaticUri);

                    if (result.Success) {
                        if (_playniteApi?.Dialogs != null)
                            await _playniteApi.Dialogs.ShowMessageAsync(
                                Loc.link_success_dialog_body(result.UserId),
                                Loc.link_success_dialog_title());
                        return;
                    }

                    if (result.IsTokenExpiry) {
                        OfferOpenLinkingPage();
                        return;
                    }

                    if (result.IsNetworkError) {
                        bool isLastAttempt = attempt == MaxLinkRetries - 1;
                        if (isLastAttempt) {
                            if (_playniteApi?.Dialogs != null)
                                await _playniteApi.Dialogs.ShowMessageAsync(
                                    Loc.link_failed_retries_body(result.ErrorMessage),
                                    Loc.link_failed_dialog_title());
                            return;
                        }

                        var retry = System.Windows.MessageBox.Show(
                            Loc.link_failed_network_body(result.ErrorMessage),
                            Loc.link_failed_retry_dialog_title(),
                            System.Windows.MessageBoxButton.YesNo,
                            System.Windows.MessageBoxImage.Warning);
                        if (retry != System.Windows.MessageBoxResult.Yes) {
                            return;
                        }
                        // Loop continues to next attempt
                    }
                    else {
                        if (_playniteApi?.Dialogs != null)
                            await _playniteApi.Dialogs.ShowMessageAsync(
                                Loc.link_failed_body(result.ErrorMessage),
                                Loc.link_failed_dialog_title());
                        return;
                    }
                }
                catch (Exception ex) {
                    HandleLinkingException(ex);
                    return;
                }
            }
        }

        /// <summary>
        /// Offers to open the linking page in the browser when a token has expired.
        /// </summary>
        private static void OfferOpenLinkingPage() {
            var answer = System.Windows.MessageBox.Show(
                Loc.token_expired_dialog_body(),
                Loc.token_expired_dialog_title(),
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (answer == System.Windows.MessageBoxResult.Yes) {
                try {
                    var installId = GsDataManager.Data.InstallID;
                    var url = $"https://gamescrobbler.com/link?install_id={installId}";
                    Process.Start(new ProcessStartInfo {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex) {
                    GsLogger.Error("Failed to open linking page", ex);
                }
            }
        }


        /// <summary>
        /// Handles empty token scenario.
        /// </summary>
        private void HandleEmptyToken() {
            GsLogger.Warn("Empty token received in URI request");
            _ = _playniteApi?.Dialogs?.ShowMessageAsync(
                Loc.invalid_linking_token(),
                Loc.link_failed_dialog_title());
        }

        /// <summary>
        /// Handles exceptions during the linking process.
        /// </summary>
        /// <param name="ex">The exception that occurred</param>
        private void HandleLinkingException(Exception ex) {
            GsLogger.Error("Exception during automatic linking", ex);
            GsSentry.CaptureException(ex, "Exception during automatic linking via URI handler");

            _ = _playniteApi?.Dialogs?.ShowMessageAsync(
                Loc.linking_error_format(ex.Message),
                Loc.link_failed_dialog_title());
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
            _ = _playniteApi?.Dialogs?.ShowMessageAsync(
                Loc.unexpected_uri_error_format(ex.Message),
                Loc.error_dialog_title());
        }
    }
}
