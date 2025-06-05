using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GsPlugin {
    public partial class GsPluginSettingsView : UserControl, INotifyPropertyChanged {
        private GsPluginSettingsViewModel _viewModel;

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public GsPluginSettingsView() {
            InitializeComponent();
            Loaded += GsPluginSettingsView_Loaded;
            Unloaded += GsPluginSettingsView_Unloaded;

            // Subscribe to static linking status changed event
            GsPluginSettingsViewModel.LinkingStatusChanged += OnLinkingStatusChanged;
        }

        private void GsPluginSettingsView_Unloaded(object sender, RoutedEventArgs e) {
            // Unsubscribe from events to prevent memory leaks
            GsPluginSettingsViewModel.LinkingStatusChanged -= OnLinkingStatusChanged;
        }

        private void OnLinkingStatusChanged(object sender, EventArgs e) {
            // Update the UI when linking status changes
            Dispatcher.Invoke(() => {
                UpdateConnectionStatus();
            });
        }

        private void GsPluginSettingsView_Loaded(object sender, RoutedEventArgs e) {
            // Find the TextBlock by name and update its text
            IDTextBlock.Text = GsDataManager.Data.InstallID;

            // Get the view model from DataContext
            _viewModel = DataContext as GsPluginSettingsViewModel;
            if (_viewModel != null) {
                // Subscribe to property changes to update UI
                _viewModel.PropertyChanged += ViewModel_PropertyChanged;
                UpdateConnectionStatus();
                UpdateLinkingState();
            }
        }

        private void ViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
            // ConnectionStatus is now static, so we don't need to listen for property changes
            if (e.PropertyName == "Settings") {
                var settings = _viewModel?.Settings;
                if (settings != null) {
                    settings.PropertyChanged += Settings_PropertyChanged;
                }
            }
        }

        private void Settings_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(GsPluginSettings.IsLinking)) {
                UpdateLinkingState();
                // When linking state changes, also check if connection status changed
                if (!_viewModel.Settings.IsLinking) {
                    UpdateConnectionStatus();
                }
            }
            else if (e.PropertyName == nameof(GsPluginSettings.LinkStatusMessage)) {
                UpdateStatusMessage();
            }
        }
        private void UpdateConnectionStatus() {
            // Update using static properties
            ConnectionStatusTextBlock.Text = GsPluginSettingsViewModel.ConnectionStatus;
            ConnectionStatusTextBlock.Foreground = GsPluginSettingsViewModel.IsLinked
                ? new SolidColorBrush(Colors.Green)
                : new SolidColorBrush(Colors.Red);

            // Update the visibility of linking controls directly
            LinkingControlsGrid.Visibility = GsPluginSettingsViewModel.ShowLinkingControls
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void UpdateLinkingState() {
            if (_viewModel?.Settings != null) {
                bool isLinking = _viewModel.Settings.IsLinking;
                TokenTextBox.IsEnabled = !isLinking;
                LinkAccountButton.IsEnabled = !isLinking;
                LinkAccountButton.Content = isLinking ? "Linking..." : "Link Account";
            }
        }

        private void UpdateStatusMessage() {
            if (_viewModel?.Settings != null) {
                string message = _viewModel.Settings.LinkStatusMessage;
                LinkStatusTextBlock.Text = message;
                LinkStatusTextBlock.Visibility = string.IsNullOrEmpty(message)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }
        }

        private void LinkAccount_Click(object sender, RoutedEventArgs e) {
            _viewModel?.LinkAccount();
        }

        private void TextBlock_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
            if (sender is TextBlock textBlock) {
                try {
                    // Copy text to clipboard
                    Clipboard.SetText(textBlock.Text);
                    MessageBox.Show("Text copied to clipboard!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex) {
                    MessageBox.Show($"Failed to copy text: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
