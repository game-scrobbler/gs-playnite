using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GsPlugin.View {
    /// <summary>
    /// Maps a string onto <see cref="Visibility"/>: null or empty collapses the element,
    /// anything else shows it. Lets status message TextBlocks drive their own visibility
    /// from a binding instead of needing a code-behind handler per message.
    /// </summary>
    [ValueConversion(typeof(string), typeof(Visibility))]
    public class StringToVisibilityConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            return string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            // One-way only: nothing meaningful to write back to the source string.
            return Binding.DoNothing;
        }
    }
}
