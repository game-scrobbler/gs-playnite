using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Playnite.SDK;

namespace GsPlugin {
    public partial class GsPluginSettingsView : UserControl {
        private readonly IPlayniteAPI PlayniteApi;
        private GsPluginSettings viewSettings { get; set; }

        public GsPluginSettingsView(IPlayniteAPI api, GsPluginSettings settings) {
            InitializeComponent();
            PlayniteApi = api; // Store Playnite API instance
            viewSettings = settings ?? new GsPluginSettings();
            Loaded += GsPluginSettingsView_Loaded;
        }

        private void GsPluginSettingsView_Loaded(object sender, RoutedEventArgs e) {
            // Find the TextBlock by name and update its text
            IDTextBlock.Text = viewSettings.InstallID;
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
    }
}
