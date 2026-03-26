using System;
using System.Windows;

namespace GsPlugin.Infrastructure {
    /// <summary>
    /// Helper to access XAML resource dictionary strings from C# code.
    /// Falls back to the provided key if the resource is not found.
    /// </summary>
    public static class GsLocalization {
        /// <summary>
        /// Looks up a localized string from the merged resource dictionaries.
        /// Returns <paramref name="fallback"/> (or the key itself) if not found.
        /// </summary>
        public static string Get(string key, string fallback = null) {
            try {
                var app = Application.Current;
                if (app != null && app.Resources.Contains(key)) {
                    return app.Resources[key] as string ?? fallback ?? key;
                }
            }
            catch {
                // Silently fall back — resource lookup should never crash the plugin.
            }
            return fallback ?? key;
        }

        /// <summary>
        /// Looks up a localized format string and applies arguments.
        /// </summary>
        public static string Format(string key, string fallback, params object[] args) {
            string template = Get(key, fallback);
            try {
                return string.Format(template, args);
            }
            catch {
                return template;
            }
        }
    }
}
