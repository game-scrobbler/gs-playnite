using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GsPlugin {
    public partial class GsPluginSettingsView : UserControl {
        private GsPluginSettingsViewModel _viewModel;

        public GsPluginSettingsView() {
            InitializeComponent();
            Loaded += GsPluginSettingsView_Loaded;
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
            if (e.PropertyName == nameof(GsPluginSettingsViewModel.IsLinked) ||
                e.PropertyName == nameof(GsPluginSettingsViewModel.ConnectionStatus)) {
                UpdateConnectionStatus();
            }
            else if (e.PropertyName == "Settings") {
                var settings = _viewModel?.Settings;
                if (settings != null) {
                    settings.PropertyChanged += Settings_PropertyChanged;
                }
            }
        }

        private void Settings_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(GsPluginSettings.IsLinking)) {
                UpdateLinkingState();
            }
            else if (e.PropertyName == nameof(GsPluginSettings.LinkStatusMessage)) {
                UpdateStatusMessage();
            }
        }

        private void UpdateConnectionStatus() {
            if (_viewModel != null) {
                ConnectionStatusTextBlock.Text = _viewModel.ConnectionStatus;
                ConnectionStatusTextBlock.Foreground = _viewModel.IsLinked
                    ? new SolidColorBrush(Colors.Green)
                    : new SolidColorBrush(Colors.Red);
            }
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
