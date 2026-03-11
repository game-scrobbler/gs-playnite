using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using GsPlugin.Models;
using GsPlugin.Services;

namespace GsPlugin.View {
    /// <summary>
    /// Interaction logic for GsPluginSettingsView.xaml
    /// </summary>
    public partial class GsPluginSettingsView : UserControl, INotifyPropertyChanged {
        private GsPluginSettingsViewModel _viewModel;
        private GsPluginSettings _subscribedSettings;

        public event PropertyChangedEventHandler PropertyChanged;

        #region Constructor
        public GsPluginSettingsView() {
            InitializeComponent();
            InitializeEventHandlers();
        }

        /// <summary>
        /// Sets up event handlers for the view lifecycle.
        /// </summary>
        private void InitializeEventHandlers() {
            Loaded += GsPluginSettingsView_Loaded;
            Unloaded += GsPluginSettingsView_Unloaded;

            // Subscribe to static linking status changes (single source of truth)
            GsAccountLinkingService.LinkingStatusChanged += OnLinkingStatusChanged;
        }
        #endregion

        protected virtual void OnPropertyChanged(string propertyName = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #region View Lifecycle Events
        /// <summary>
        /// Handles cleanup when the view is unloaded.
        /// </summary>
        private void GsPluginSettingsView_Unloaded(object sender, RoutedEventArgs e) {
            // Unsubscribe from events to prevent memory leaks
            GsAccountLinkingService.LinkingStatusChanged -= OnLinkingStatusChanged;

            if (_viewModel != null) {
                _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            }

            if (_subscribedSettings != null) {
                _subscribedSettings.PropertyChanged -= Settings_PropertyChanged;
                _subscribedSettings = null;
            }
        }

        /// <summary>
        /// Handles initialization when the view is loaded.
        /// </summary>
        private void GsPluginSettingsView_Loaded(object sender, RoutedEventArgs e) {
            InitializeViewData();
            SetupViewModelBinding();
        }

        /// <summary>
        /// Initializes view-specific data that doesn't depend on the view model.
        /// </summary>
        private void InitializeViewData() {
            // Display the installation ID
            IDTextBlock.Text = GsDataManager.Data.InstallID;
            // Display last sync status (static property — cannot use XAML {Binding})
            LastSyncStatusTextBlock.Text = GsPluginSettingsViewModel.LastSyncStatus;
        }

        /// <summary>
        /// Sets up data binding and event subscriptions with the view model.
        /// </summary>
        private void SetupViewModelBinding() {
            _viewModel = DataContext as GsPluginSettingsViewModel;
            if (_viewModel != null) {
                // Subscribe to view model property changes
                _viewModel.PropertyChanged += ViewModel_PropertyChanged;

                // Subscribe to the current settings object immediately so we don't
                // miss property changes (e.g. IsDeleting) that fire before the
                // ViewModel_PropertyChanged handler re-subscribes on a "Settings" change.
                if (_viewModel.Settings != null) {
                    _viewModel.Settings.PropertyChanged += Settings_PropertyChanged;
                    _subscribedSettings = _viewModel.Settings;
                }

                // Initialize UI state
                UpdateConnectionStatus();
                UpdateLinkingState();
                UpdateOptOutState();
            }
        }
        #endregion

        /// <summary>
        /// Handles changes to the linking status from external sources.
        /// </summary>
        private void OnLinkingStatusChanged(object sender, EventArgs e) {
            // Ensure UI updates happen on the UI thread
            Dispatcher.Invoke(() => {
                UpdateConnectionStatus();
                UpdateOptOutState();
            });
        }

        /// <summary>
        /// Handles property changes on the main view model.
        /// </summary>
        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e) {
            if (e.PropertyName == "Settings") {
                // Unsubscribe from old settings to prevent duplicate handlers
                if (_subscribedSettings != null) {
                    _subscribedSettings.PropertyChanged -= Settings_PropertyChanged;
                }

                // Subscribe to the new settings object property changes
                var settings = _viewModel?.Settings;
                if (settings != null) {
                    settings.PropertyChanged += Settings_PropertyChanged;
                }
                _subscribedSettings = settings;
            }
        }

        /// <summary>
        /// Handles property changes on the settings object.
        /// </summary>
        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e) {
            switch (e.PropertyName) {
                case nameof(GsPluginSettings.IsLinking):
                    UpdateLinkingState();
                    // Also check connection status when linking completes
                    if (!_viewModel.Settings.IsLinking) {
                        UpdateConnectionStatus();
                    }
                    break;

                case nameof(GsPluginSettings.LinkStatusMessage):
                    UpdateStatusMessage();
                    break;

                case nameof(GsPluginSettings.IsDeleting):
                    UpdateDeletingState();
                    break;

                case nameof(GsPluginSettings.DeleteStatusMessage):
                    UpdateDeleteStatusMessage();
                    break;
            }
        }

        #region UI Update Methods
        /// <summary>
        /// Updates the connection status display and related UI elements.
        /// </summary>
        private void UpdateConnectionStatus() {
            bool isOptedOut = GsDataManager.IsOptedOut;

            if (isOptedOut) {
                ConnectionStatusTextBlock.Text = "Opted Out";
                ConnectionStatusTextBlock.Foreground = new SolidColorBrush(Colors.Gray);
            }
            else {
                ConnectionStatusTextBlock.Text = GsPluginSettingsViewModel.ConnectionStatus;
                ConnectionStatusTextBlock.Foreground = GsPluginSettingsViewModel.IsLinked
                    ? new SolidColorBrush(Colors.Green)
                    : new SolidColorBrush(Colors.Red);
            }

            // Hide linking controls when opted out or already linked
            var linkingVisibility = (!isOptedOut && GsPluginSettingsViewModel.ShowLinkingControls)
                ? Visibility.Visible
                : Visibility.Collapsed;
            OpenWebsiteToLinkButton.Visibility = linkingVisibility;
            ManualTokenSeparator.Visibility = linkingVisibility;
            LinkingControlsGrid.Visibility = linkingVisibility;
        }

        /// <summary>
        /// Updates the UI state during linking operations.
        /// </summary>
        private void UpdateLinkingState() {
            if (_viewModel?.Settings == null) return;

            bool isLinking = _viewModel.Settings.IsLinking;
            // Disable controls during linking
            TokenTextBox.IsEnabled = !isLinking;
            LinkAccountButton.IsEnabled = !isLinking;
            // Update button text
            LinkAccountButton.Content = isLinking ? "Linking..." : "Link Account";
        }

        /// <summary>
        /// Updates the status message display.
        /// </summary>
        private void UpdateStatusMessage() {
            if (_viewModel?.Settings == null) return;

            string message = _viewModel.Settings.LinkStatusMessage;
            LinkStatusTextBlock.Text = message;
            LinkStatusTextBlock.Visibility = string.IsNullOrEmpty(message)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
        #endregion

        #region User Interaction Handlers
        /// <summary>
        /// Handles the link account button click event.
        /// </summary>
        private void LinkAccount_Click(object sender, RoutedEventArgs e) {
            _viewModel?.LinkAccount();
        }

        /// <summary>
        /// Opens the gamescrobbler.com account linking page with the InstallID pre-filled.
        /// </summary>
        private void OpenWebsiteToLink_Click(object sender, RoutedEventArgs e) {
            try {
                var installId = GsDataManager.Data.InstallID;
                var url = $"https://gamescrobbler.com/link?install_id={installId}";
                Process.Start(new ProcessStartInfo {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex) {
                MessageBox.Show(
                    $"Failed to open URL: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Handles clicking on text blocks to copy their content to clipboard.
        /// </summary>
        private void TextBlock_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
            if (!(sender is TextBlock textBlock)) return;

            try {
                Clipboard.SetText(textBlock.Text);
                MessageBox.Show(
                    "Text copied to clipboard!",
                    "Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex) {
                MessageBox.Show(
                    $"Failed to copy text: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Handles the Delete My Data button click with two-stage confirmation.
        /// </summary>
        private void DeleteMyData_Click(object sender, RoutedEventArgs e) {
            var result = MessageBox.Show(
                "Are you sure you want to delete all your data from GameScrobbler servers?\n\n" +
                "This will:\n" +
                "• Remove your library, sessions, and achievements from our servers\n" +
                "• Disable all plugin features\n" +
                "• Require you to opt in again to resume using the plugin\n\n" +
                "This action cannot be undone.",
                "Delete My Data",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            var confirmResult = MessageBox.Show(
                "Are you absolutely sure? Your data will be permanently deleted from the GameScrobbler servers.",
                "Final Confirmation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Exclamation);

            if (confirmResult != MessageBoxResult.Yes) return;

            _viewModel?.DeleteMyData();
        }

        /// <summary>
        /// Updates the delete button state during deletion.
        /// </summary>
        private void UpdateDeletingState() {
            if (_viewModel?.Settings == null) return;

            bool isDeleting = _viewModel.Settings.IsDeleting;
            DeleteMyDataButton.IsEnabled = !isDeleting;
            DeleteMyDataButton.Content = isDeleting ? "Deleting..." : "Delete My Data";
        }

        /// <summary>
        /// Toggles Delete / Opt-Back-In button visibility based on opt-out state.
        /// </summary>
        private void UpdateOptOutState() {
            bool isOptedOut = GsDataManager.IsOptedOut;
            DeleteMyDataButton.Visibility = isOptedOut ? Visibility.Collapsed : Visibility.Visible;
            OptBackInButton.Visibility = isOptedOut ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Handles the Opt Back In button click.
        /// </summary>
        private void OptBackIn_Click(object sender, RoutedEventArgs e) {
            var result = MessageBox.Show(
                "Re-enable the GameScrobbler plugin?\n\n" +
                "You will need to restart Playnite for all features to resume.",
                "Opt Back In",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            _viewModel?.OptBackIn();
            UpdateOptOutState();
            UpdateConnectionStatus();
        }

        /// <summary>
        /// Updates the delete status message display.
        /// </summary>
        private void UpdateDeleteStatusMessage() {
            if (_viewModel?.Settings == null) return;

            string message = _viewModel.Settings.DeleteStatusMessage;
            DeleteStatusTextBlock.Text = message;
            DeleteStatusTextBlock.Visibility = string.IsNullOrEmpty(message)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        /// <summary>
        /// Handles hyperlink navigation requests.
        /// </summary>
        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e) {
            try {
                Process.Start(new ProcessStartInfo {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch (Exception ex) {
                MessageBox.Show(
                    $"Failed to open URL: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        #endregion
    }
}
