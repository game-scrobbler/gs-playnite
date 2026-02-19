using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;

namespace GsPlugin {
    /// <summary>
    /// Interaction logic for GsPluginSettingsView.xaml
    /// </summary>
    public partial class GsPluginSettingsView : UserControl, INotifyPropertyChanged {
        private GsPluginSettingsViewModel _viewModel;

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

                if (_viewModel.Settings != null) {
                    _viewModel.Settings.PropertyChanged -= Settings_PropertyChanged;
                }
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
        }

        /// <summary>
        /// Sets up data binding and event subscriptions with the view model.
        /// </summary>
        private void SetupViewModelBinding() {
            _viewModel = DataContext as GsPluginSettingsViewModel;
            if (_viewModel != null) {
                // Subscribe to view model property changes
                _viewModel.PropertyChanged += ViewModel_PropertyChanged;

                // Initialize UI state
                UpdateConnectionStatus();
                UpdateLinkingState();
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
            });
        }

        /// <summary>
        /// Handles property changes on the main view model.
        /// </summary>
        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e) {
            if (e.PropertyName == "Settings") {
                // Subscribe to the new settings object property changes
                var settings = _viewModel?.Settings;
                if (settings != null) {
                    settings.PropertyChanged += Settings_PropertyChanged;
                }
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
            }
        }

        #region UI Update Methods
        /// <summary>
        /// Updates the connection status display and related UI elements.
        /// </summary>
        private void UpdateConnectionStatus() {
            // Update status text and color
            ConnectionStatusTextBlock.Text = GsPluginSettingsViewModel.ConnectionStatus;
            ConnectionStatusTextBlock.Foreground = GsPluginSettingsViewModel.IsLinked
                ? new SolidColorBrush(Colors.Green)
                : new SolidColorBrush(Colors.Red);

            // Show/hide linking controls based on connection status
            var linkingVisibility = GsPluginSettingsViewModel.ShowLinkingControls
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
