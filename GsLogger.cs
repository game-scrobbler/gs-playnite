using System.Windows;
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
    }
}
