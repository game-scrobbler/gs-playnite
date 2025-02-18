using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Collections.Generic;

namespace GsPlugin {
    public partial class GsPluginSettingsView : UserControl {

        public GsPluginSettingsView() {
            InitializeComponent();
            Loaded += GsPluginSettingsView_Loaded;
        }

        private void GsPluginSettingsView_Loaded(object sender, RoutedEventArgs e) {
            // Find the TextBlock by name and update its text
            IDTextBlock.Text = GsDataManager.Data.InstallID;
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
