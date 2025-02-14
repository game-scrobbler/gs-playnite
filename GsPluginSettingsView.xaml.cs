using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GsPlugin {
    public partial class GsPluginSettingsView : UserControl {

        public GsPluginSettingsView() {
            InitializeComponent();
            Loaded += GsPluginSettingsView_Loaded;
        }

        private void GsPluginSettingsView_Loaded(object sender, RoutedEventArgs e) {
            // Find the TextBlock by name and update its text
            IDTextBlock.Text = GSDataManager.Data.InstallID;
        }
        private void TextBlock_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
            if (sender is TextBlock textBlock) {
                try {
                    // Copy text to clipboard
                    Clipboard.SetText(textBlock.Text);

                    // Show success pop-up
                    MessageBox.Show("Text copied to clipboard!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex) {
                    // Show failure pop-up
                    MessageBox.Show($"Failed to copy text: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void HandleCheck(object sender, RoutedEventArgs e) {
                // Dark mode is enabled
            GSDataManager.Data.IsDark = true;
            GSDataManager.Save();
            MessageBox.Show("Dark Mode Enabled!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private void HandleUnchecked(object sender, RoutedEventArgs e) {
            // Dark mode is not enabled
            GSDataManager.Data.IsDark = false;
            GSDataManager.Save();
            MessageBox.Show("Dark Mode Disabled!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
