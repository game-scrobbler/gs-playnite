using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using GsPlugin.Api;
using GsPlugin.Services;
using Xunit;

namespace GsPlugin.Tests {
    /// <summary>
    /// Guards every value that crosses the wire or feeds a hash against the ambient
    /// Windows locale.
    ///
    /// WHY THIS EXISTS. In a .NET custom format string "-" and ":" are the *date and
    /// time separator specifiers*, taken from DateTimeFormatInfo.CurrentInfo, and
    /// "yyyy" renders through CurrentCulture.Calendar. So a timestamp formatted
    /// without an explicit provider silently becomes 2569-... under th-TH
    /// (ThaiBuddhist), 1447-11-14 under ar-SA (UmAlQura), and 14.47.53 under fi-FI.
    /// A hash built from those bytes can never match the server's, and a scrobble
    /// carrying one cannot be parsed at all -- for every user in those locales,
    /// permanently, with no error the user or the logs would attribute to locale.
    ///
    /// The cultures below are chosen for what each one breaks: a non-Gregorian
    /// calendar (th-TH, ar-SA), a non-":" time separator (fi-FI), and a locale whose
    /// casing rules differ (tr-TR).
    /// </summary>
    public class CultureInvarianceTests {
        public static IEnumerable<object[]> HostileCultures() {
            yield return new object[] { "en-US" };
            yield return new object[] { "th-TH" };
            yield return new object[] { "ar-SA" };
            yield return new object[] { "fi-FI" };
            yield return new object[] { "tr-TR" };
        }

        /// <summary>
        /// Runs <paramref name="body"/> with both CurrentCulture and CurrentUICulture
        /// swapped, restoring them even if the assertion throws.
        /// </summary>
        private static void UnderCulture(string name, Action body) {
            var thread = Thread.CurrentThread;
            var prevCulture = thread.CurrentCulture;
            var prevUiCulture = thread.CurrentUICulture;
            try {
                var culture = new CultureInfo(name);
                thread.CurrentCulture = culture;
                thread.CurrentUICulture = culture;
                body();
            }
            finally {
                thread.CurrentCulture = prevCulture;
                thread.CurrentUICulture = prevUiCulture;
            }
        }

        private static readonly DateTime SampleUtc =
            new DateTime(2026, 5, 1, 14, 47, 53, DateTimeKind.Utc);

        [Theory]
        [MemberData(nameof(HostileCultures))]
        public void FormatDateForHash_is_culture_invariant(string culture) {
            UnderCulture(culture, () =>
                Assert.Equal("2026-05-01T14:47:53Z", GsHashUtils.FormatDateForHash(SampleUtc)));
        }

        [Theory]
        [MemberData(nameof(HostileCultures))]
        public void FormatScrobbleTimestamp_is_culture_invariant(string culture) {
            // Local kind so the "K" specifier emits an offset, which is the production shape.
            var local = new DateTime(2026, 5, 1, 14, 47, 53, DateTimeKind.Local);
            var expected = GsScrobblingService.FormatScrobbleTimestamp(local);
            UnderCulture(culture, () => {
                var actual = GsScrobblingService.FormatScrobbleTimestamp(local);
                Assert.Equal(expected, actual);
                // Gregorian year and ":" separators regardless of calendar/locale.
                Assert.StartsWith("2026-05-01T14:47:53", actual, StringComparison.Ordinal);
            });
        }

        [Theory]
        [MemberData(nameof(HostileCultures))]
        public void Library_hashes_are_culture_invariant(string culture) {
            var game = new GameSyncDto {
                playnite_id = "11111111-1111-1111-1111-111111111111",
                game_name = "Test Game",
                playtime_seconds = 10,
                play_count = 20,
                last_activity = SampleUtc,
                date_added = SampleUtc,
                modified = SampleUtc
            };
            var expectedMetadata = GsHashUtils.ComputeGameMetadataHash(game);
            var expectedLibrary = GsHashUtils.ComputeLibraryHash(new List<GameSyncDto> { game });

            UnderCulture(culture, () => {
                Assert.Equal(expectedMetadata, GsHashUtils.ComputeGameMetadataHash(game));
                Assert.Equal(expectedLibrary, GsHashUtils.ComputeLibraryHash(new List<GameSyncDto> { game }));
            });
        }

        [Theory]
        [MemberData(nameof(HostileCultures))]
        public void Integration_accounts_hash_is_culture_invariant(string culture) {
            // Ordinal vs linguistic ordering disagree on these: ordinal puts "Steam"
            // (S=0x53) before "epic" (e=0x65); a linguistic sort interleaves by letter.
            var accounts = new List<IntegrationAccountDto> {
                new IntegrationAccountDto { provider_id = "epic", account_id = "b" },
                new IntegrationAccountDto { provider_id = "Steam", account_id = "a" },
                new IntegrationAccountDto { provider_id = "steam", account_id = "I" }
            };
            var expected = GsHashUtils.ComputeIntegrationAccountsHash(accounts);
            UnderCulture(culture, () =>
                Assert.Equal(expected, GsHashUtils.ComputeIntegrationAccountsHash(accounts)));
        }
    }
}
