using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace GsPlugin.Tests {
    /// <summary>
    /// Guards key parity across the Localization resource dictionaries.
    /// Playnite falls back to en_US whenever a key is missing from the active locale,
    /// so a key added to one file only would ship silently as untranslated English.
    /// </summary>
    public class LocalizationKeyParityTests {
        /// <summary>The XAML namespace that owns the x:Key attribute.</summary>
        private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

        private const string BaseLocaleFileName = "en_US.xaml";

        [Fact]
        public void EveryLocale_DeclaresTheSameKeysAsEnUs() {
            var folder = FindLocalizationFolder();
            if (folder == null) {
                // The source tree is not next to the test binary (packaging or a copied
                // test assembly). There is nothing to verify, so pass rather than fail.
                return;
            }

            var baseKeys = ReadKeys(Path.Combine(folder, BaseLocaleFileName));
            Assert.NotEmpty(baseKeys);

            var localeFiles = LocaleFiles(folder)
                .Where(f => !string.Equals(Path.GetFileName(f), BaseLocaleFileName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.NotEmpty(localeFiles);

            var problems = new List<string>();
            foreach (var file in localeFiles) {
                var name = Path.GetFileName(file);
                var keys = ReadKeys(file);

                var missing = baseKeys.Except(keys).OrderBy(k => k, StringComparer.Ordinal).ToList();
                if (missing.Count > 0) {
                    problems.Add($"{name} is missing {missing.Count} key(s) present in {BaseLocaleFileName}: {string.Join(", ", missing)}");
                }

                var extra = keys.Except(baseKeys).OrderBy(k => k, StringComparer.Ordinal).ToList();
                if (extra.Count > 0) {
                    problems.Add($"{name} declares {extra.Count} key(s) absent from {BaseLocaleFileName}: {string.Join(", ", extra)}");
                }
            }

            Assert.True(
                problems.Count == 0,
                "Localization key parity is broken. Every locale file must declare the same set of x:Key entries as "
                    + BaseLocaleFileName + "." + Environment.NewLine
                    + string.Join(Environment.NewLine, problems));
        }

        [Fact]
        public void EveryLocale_DeclaresEachKeyOnlyOnce() {
            var folder = FindLocalizationFolder();
            if (folder == null) {
                return;
            }

            var problems = new List<string>();
            foreach (var file in LocaleFiles(folder)) {
                var duplicates = ReadKeyList(file)
                    .GroupBy(k => k, StringComparer.Ordinal)
                    .Where(g => g.Count() > 1)
                    .Select(g => $"{g.Key} (x{g.Count()})")
                    .OrderBy(k => k, StringComparer.Ordinal)
                    .ToList();
                if (duplicates.Count > 0) {
                    problems.Add($"{Path.GetFileName(file)}: {string.Join(", ", duplicates)}");
                }
            }

            Assert.True(
                problems.Count == 0,
                "Duplicate x:Key entries found. A ResourceDictionary must not declare the same key twice."
                    + Environment.NewLine + string.Join(Environment.NewLine, problems));
        }

        /// <summary>
        /// Every locale resource dictionary in the folder, en_US included. Discovered rather
        /// than hard coded so a newly added locale is covered without touching this test.
        /// </summary>
        private static List<string> LocaleFiles(string folder) {
            return Directory.GetFiles(folder, "*.xaml")
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static HashSet<string> ReadKeys(string path) {
            return new HashSet<string>(ReadKeyList(path), StringComparer.Ordinal);
        }

        private static List<string> ReadKeyList(string path) {
            var document = XDocument.Load(path);
            return document.Descendants()
                .Select(e => (string)e.Attribute(XamlNamespace + "Key"))
                .Where(key => key != null)
                .ToList();
        }

        /// <summary>
        /// Walks up from the test assembly location looking for the repository's
        /// Localization folder. The binary normally sits at
        /// GsPlugin.Tests\bin\{Configuration}\net462, so the folder is a few levels up.
        /// Returns null when it cannot be found.
        /// </summary>
        private static string FindLocalizationFolder() {
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (var depth = 0; directory != null && depth < 8; depth++) {
                var candidate = Path.Combine(directory.FullName, "Localization");
                if (File.Exists(Path.Combine(candidate, BaseLocaleFileName))) {
                    return candidate;
                }
                directory = directory.Parent;
            }
            return null;
        }
    }
}
