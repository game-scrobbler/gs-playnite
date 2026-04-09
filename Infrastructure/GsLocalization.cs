namespace GsPlugin.Infrastructure {
    /// <summary>
    /// Thin localization shim for C# code-behind.
    /// P11 uses Fluent (FTL) localization via <c>Loc.GetString()</c> for XAML and
    /// typed generated methods for code. The legacy LOCGsPlugin* keys used throughout
    /// the codebase are not FTL keys, so this helper returns the hardcoded English
    /// fallback directly. Non-English localization for code-side strings is a TODO.
    /// XAML bindings using <c>{p:LocalizedString}</c> are unaffected.
    /// </summary>
    public static class GsLocalization {
        /// <summary>
        /// Returns the English fallback string (or key if no fallback provided).
        /// </summary>
        public static string Get(string key, string? fallback = null) => fallback ?? key;

        /// <summary>
        /// Formats the English fallback string with the provided arguments.
        /// </summary>
        public static string Format(string key, string fallback, params object[] args) {
            try {
                return string.Format(fallback ?? key, args);
            }
            catch {
                return fallback ?? key;
            }
        }
    }
}
