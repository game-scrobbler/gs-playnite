using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace GsPlugin.Tests {
    /// <summary>
    /// Guards key parity across the Fluent localization files. Playnite falls back to en_US
    /// whenever a key is missing from the active locale, so a key added to one file only would
    /// ship silently as untranslated English.
    ///
    /// Playnite 11 ships only en_US.ftl today; the other locales were not carried over from the
    /// Playnite 10 XAML dictionaries yet. Until they are, the parity check degenerates to
    /// "en_US.ftl parses and declares keys", which still catches an emptied or corrupted file,
    /// and starts comparing automatically the moment a second .ftl lands.
    /// </summary>
    public class LocalizationKeyParityTests {
        private const string BaseLocaleFileName = "en_US.ftl";

        /// <summary>
        /// A Fluent message identifier: left-anchored, then "=" . Continuation lines of a
        /// multi-line value are indented, so anchoring at column 0 skips them. Comment lines
        /// start with "#" and are excluded by the identifier character class.
        /// </summary>
        private static readonly Regex MessageId = new Regex(@"^(?<id>[A-Za-z][A-Za-z0-9_-]*)\s*=", RegexOptions.Multiline);

        [Fact]
        public void EveryLocale_DeclaresTheSameKeysAsEnUs() {
            var folder = FindLocalizationFolder();
            Assert.NotNull(folder);

            var baseKeys = ReadKeys(Path.Combine(folder, BaseLocaleFileName));
            Assert.NotEmpty(baseKeys);

            var problems = new List<string>();
            foreach (var file in LocaleFiles(folder)) {
                var name = Path.GetFileName(file);
                if (string.Equals(name, BaseLocaleFileName, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
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
                "Localization key parity is broken. Every .ftl must declare the same set of message ids as "
                    + BaseLocaleFileName + "." + Environment.NewLine
                    + string.Join(Environment.NewLine, problems));
        }

        [Fact]
        public void EveryLocale_DeclaresEachKeyOnlyOnce() {
            var folder = FindLocalizationFolder();
            Assert.NotNull(folder);

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
                "Duplicate message ids found. A Fluent file must not declare the same id twice."
                    + Environment.NewLine + string.Join(Environment.NewLine, problems));
        }

        /// <summary>
        /// Every locale file in the folder, en_US included. Discovered rather than hard coded so
        /// a newly added locale is covered without touching this test.
        /// </summary>
        private static List<string> LocaleFiles(string folder) {
            return Directory.GetFiles(folder, "*.ftl")
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static HashSet<string> ReadKeys(string path) {
            return new HashSet<string>(ReadKeyList(path), StringComparer.Ordinal);
        }

        private static List<string> ReadKeyList(string path) {
            return MessageId.Matches(File.ReadAllText(path))
                .Cast<Match>()
                .Select(m => m.Groups["id"].Value)
                .ToList();
        }

        /// <summary>
        /// Walks up from the test assembly location looking for the repository's Localization
        /// folder. The binary normally sits at GsPlugin.Tests\bin\{Configuration}\net10.0-windows,
        /// so the folder is a few levels up. Returns null when it cannot be found.
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
