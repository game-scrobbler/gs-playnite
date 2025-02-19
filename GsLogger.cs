using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Playnite.SDK;

namespace GsPlugin {
    public static class GsLogger {
        private static readonly ILogger _logger = LogManager.GetLogger();

        public static void Info(string message) {
            _logger.Info(message);
        }

        public static void Warn(string message) {
            _logger.Warn(message);
#if DEBUG
            ShowDebugMessage(message, "Warning");
#endif
        }

        public static void Error(string message) {
            _logger.Error(message);
#if DEBUG
            ShowDebugMessage(message, "Error");
#endif
        }

        public static void Error(string message, System.Exception ex) {
            _logger.Error(ex, message);
#if DEBUG
            ShowDebugMessage($"{message}\n\nException: {ex.Message}", "Error");
#endif
        }

        private static void ShowDebugMessage(string message, string type) {
            Application.Current.Dispatcher.BeginInvoke(new System.Action(() => {
                MessageBox.Show(
                    message,
                    $"Debug - {type}",
                    MessageBoxButton.OK,
                    type == "Error" ? MessageBoxImage.Error : MessageBoxImage.Warning
                );
            }));
        }

#if DEBUG
        public static void ShowHTTPDebugBox(string requestData, string responseData, bool isError = false) {
            Application.Current.Dispatcher.BeginInvoke(new Action(() => {
                var requestBox = new TextBox {
                    Text = requestData,
                    AcceptsReturn = true,
                    AcceptsTab = true,
                    IsReadOnly = true,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = new FontFamily("Consolas"),
                    Background = Brushes.Black,
                    Foreground = Brushes.LightGreen,
                    Margin = new Thickness(5),
                    Height = 150
                };

                var responseBox = new TextBox {
                    Text = responseData,
                    AcceptsReturn = true,
                    AcceptsTab = true,
                    IsReadOnly = true,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = new FontFamily("Consolas"),
                    Background = Brushes.Black,
                    Foreground = isError ? Brushes.Red : Brushes.White,
                    Margin = new Thickness(5),
                    Height = 150
                };

                var stackPanel = new StackPanel();
                stackPanel.Children.Add(new TextBlock {
                    Text = "Request:",
                    Margin = new Thickness(5),
                    Foreground = Brushes.White
                });
                stackPanel.Children.Add(requestBox);
                stackPanel.Children.Add(new TextBlock {
                    Text = "Response:",
                    Margin = new Thickness(5),
                    Foreground = Brushes.White
                });
                stackPanel.Children.Add(responseBox);

                var window = new Window {
                    Title = isError ? "HTTP Debug - Error" : "HTTP Debug",
                    Content = stackPanel,
                    Width = 800,
                    Height = 400,
                    WindowStyle = WindowStyle.SingleBorderWindow,
                    Background = Brushes.Black,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    ShowInTaskbar = true,
                    Topmost = true,
                    ResizeMode = ResizeMode.CanResizeWithGrip
                };

                var fadeOut = new System.Windows.Media.Animation.DoubleAnimation {
                    From = 1.0,
                    To = 0.0,
                    Duration = new Duration(TimeSpan.FromSeconds(0.3)),
                    BeginTime = TimeSpan.FromSeconds(5)
                };

                bool isMouseOver = false;
                window.MouseEnter += (s, e) => {
                    isMouseOver = true;
                    window.BeginAnimation(UIElement.OpacityProperty, null);
                    window.Opacity = 1.0;
                };

                window.MouseLeave += (s, e) => {
                    isMouseOver = false;
                    window.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                };

                fadeOut.Completed += (s, e) => {
                    if (!isMouseOver) {
                        window.Close();
                    }
                };

                window.Show();
                window.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }));
        }
#endif
    }
}
