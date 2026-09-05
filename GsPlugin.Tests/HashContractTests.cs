using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using GsPlugin.Api;
using GsPlugin.Services;
using Xunit;

namespace GsPlugin.Tests {
    /// <summary>
    /// Cross-repo hash contract. The backend keeps a matching suite that asserts
    /// the SAME digests from the SAME fixture against its own implementation of
    /// the recipe.
    ///
    /// Either implementation drifting turns one of the two suites red. Never
    /// re-baseline a digest to make a test pass: a changed digest means the two
    /// sides no longer agree on the snapshot hash, which is precisely the failure
    /// this file exists to catch.
    ///
    /// The vectors deliberately include offset-bearing timestamps, because that is
    /// the shape a Kind=Local DateTime takes on the wire and the shape most likely
    /// to normalize inconsistently between two implementations.
    /// </summary>
    public class HashContractTests {
        private static JsonElement LoadFixture() {
            var path = Path.Combine(
                Path.GetDirectoryName(new Uri(typeof(HashContractTests).Assembly.CodeBase).LocalPath),
                "Fixtures",
                "playnite-hash-vectors.json");
            Assert.True(File.Exists(path), $"fixture not found at {path}");
            using (var doc = JsonDocument.Parse(File.ReadAllText(path))) {
                return doc.RootElement.Clone();
            }
        }

        public static IEnumerable<object[]> DateVectors() {
            foreach (var vector in LoadFixture().GetProperty("dateNormalization").EnumerateArray()) {
                var wire = vector.GetProperty("wire");
                yield return new object[] {
                    vector.GetProperty("name").GetString(),
                    wire.ValueKind == JsonValueKind.Null ? null : wire.GetString(),
                    vector.GetProperty("csharp").GetString(),
                };
            }
        }

        /// <summary>
        /// Every wire shape must normalize to the same canonical string both sides
        /// agree on. `wire` is what System.Text.Json emits for a C# DateTime;
        /// parsing it back with RoundtripKind and re-rendering is exactly what
        /// GsHashUtils.FormatDateForHash does to the live DTO value.
        /// </summary>
        [Theory]
        [MemberData(nameof(DateVectors))]
        public void FormatDateForHash_matches_the_shared_vector(string name, string wire, string expected) {
            var actual = wire == null
                ? GsHashUtils.FormatDateForHash(null)
                : GsHashUtils.FormatDateForHash(DateTime.Parse(
                    wire, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

            Assert.True(
                expected == actual,
                $"{name}: wire '{wire ?? "(null)"}' hashed as '{actual}', expected '{expected}'");
        }

        /// <summary>
        /// The digests the backend suite asserts for the same single-game library.
        /// These are the load-bearing values: they prove the two independent
        /// implementations of the recipe agree end to end, not merely that each is
        /// self-consistent.
        /// </summary>
        [Fact]
        public void Library_hashes_match_the_shared_digests() {
            var fixture = LoadFixture().GetProperty("library");
            var games = new List<GameSyncDto>();
            foreach (var game in fixture.GetProperty("games").EnumerateArray()) {
                games.Add(ReadGame(game));
            }

            Assert.Single(games);
            Assert.Equal(
                fixture.GetProperty("metadataHashV3").GetString(),
                GsHashUtils.ComputeGameMetadataHash(games[0]));
            Assert.Equal(
                fixture.GetProperty("libraryHashV3").GetString(),
                GsHashUtils.ComputeLibraryHash(games));
        }

        /// <summary>
        /// The load-bearing plugin-side invariant: what we hash is what we send.
        /// The recipient can only recompute the snapshot hash from the payload, so a
        /// date that hashes as one string and serializes as another makes the hash
        /// unverifiable. Before CanonicalDateTimeConverter, System.Text.Json emitted
        /// `2025-02-19T14:51:26.897-08:00` for a value hashed as
        /// `2025-02-19T22:51:26Z`.
        /// </summary>
        [Fact]
        public void Serialized_dates_are_byte_identical_to_the_hashed_dates() {
            var game = ReadGame(LoadFixture().GetProperty("library").GetProperty("games")[0]);

            using (var doc = JsonDocument.Parse(JsonSerializer.Serialize(game))) {
                var wire = doc.RootElement;
                Assert.Equal(
                    GsHashUtils.FormatDateForHash(game.last_activity),
                    wire.GetProperty("last_activity").GetString());
                Assert.Equal(
                    GsHashUtils.FormatDateForHash(game.date_added),
                    wire.GetProperty("date_added").GetString());
                Assert.Equal(
                    GsHashUtils.FormatDateForHash(game.modified),
                    wire.GetProperty("modified").GetString());

                // And specifically: no offset survives onto the wire.
                foreach (var field in new[] { "last_activity", "date_added", "modified" }) {
                    var value = wire.GetProperty(field).GetString();
                    Assert.True(
                        value.EndsWith("Z", StringComparison.Ordinal),
                        $"{field} serialized as '{value}', which is not canonical UTC");
                }
            }
        }

        /// <summary>
        /// A null date must stay null on the wire rather than becoming "", so both
        /// sides continue to map an absent date to the same empty hash input.
        /// </summary>
        [Fact]
        public void Null_dates_serialize_as_null() {
            var game = new GameSyncDto { playnite_id = "g", last_activity = null };
            using (var doc = JsonDocument.Parse(JsonSerializer.Serialize(game))) {
                Assert.Equal(
                    JsonValueKind.Null, doc.RootElement.GetProperty("last_activity").ValueKind);
            }
            Assert.Equal("", GsHashUtils.FormatDateForHash(null));
        }

        /// <summary>
        /// The achievement recipe had no vector on either side of the contract, while
        /// the library recipe had two. It gates the same force-full-sync loop: a
        /// disagreement here makes every achievements diff mismatch its baseline, and
        /// the client re-uploads in full forever without surfacing an error.
        ///
        /// The names deliberately sort differently under ordinal and linguistic
        /// comparers, so a comparer swap on either side turns this red.
        /// </summary>
        [Fact]
        public void Achievement_hash_matches_the_shared_digest() {
            var fixture = LoadFixture().GetProperty("achievements");
            var games = ReadAchievementGames(fixture);

            Assert.Equal(
                fixture.GetProperty("achievementHashV2").GetString(),
                GsHashUtils.ComputeAchievementHash(games));
        }

        /// <summary>
        /// Per-game keys are ordinal-sorted before hashing, so the order games arrive
        /// in must not change the digest. Providers do not guarantee an order.
        /// </summary>
        [Fact]
        public void Achievement_hash_is_independent_of_game_order() {
            var fixture = LoadFixture().GetProperty("achievements");
            var games = ReadAchievementGames(fixture);
            var reversed = new List<GameAchievementsDto>(games);
            reversed.Reverse();

            Assert.Equal(
                GsHashUtils.ComputeAchievementHash(games),
                GsHashUtils.ComputeAchievementHash(reversed));
        }

        /// <summary>
        /// A null name and an empty name must hash identically. string.Join renders a
        /// null as "", but JS Array.sort coerces null via ToString to the literal
        /// "null" and orders it among the n's -- so a null reaching the wire would
        /// give the two sides different digests. The DTO builders normalize to "";
        /// this pins that the recipe agrees with that choice.
        /// </summary>
        [Fact]
        public void Achievement_hash_treats_a_null_name_as_empty() {
            var withNull = new List<GameAchievementsDto> {
                new GameAchievementsDto {
                    playnite_id = "g",
                    achievements = new List<AchievementItemDto> {
                        new AchievementItemDto { name = null, is_unlocked = false },
                        new AchievementItemDto { name = "A", is_unlocked = true }
                    }
                }
            };
            var withEmpty = new List<GameAchievementsDto> {
                new GameAchievementsDto {
                    playnite_id = "g",
                    achievements = new List<AchievementItemDto> {
                        new AchievementItemDto { name = "", is_unlocked = false },
                        new AchievementItemDto { name = "A", is_unlocked = true }
                    }
                }
            };

            Assert.Equal(
                GsHashUtils.ComputeAchievementHash(withEmpty),
                GsHashUtils.ComputeAchievementHash(withNull));
        }

        private static List<GameAchievementsDto> ReadAchievementGames(JsonElement fixture) {
            var games = new List<GameAchievementsDto>();
            foreach (var g in fixture.GetProperty("games").EnumerateArray()) {
                var achievements = new List<AchievementItemDto>();
                foreach (var a in g.GetProperty("achievements").EnumerateArray()) {
                    achievements.Add(new AchievementItemDto {
                        name = Str(a, "name"),
                        is_unlocked = a.GetProperty("is_unlocked").GetBoolean()
                    });
                }
                games.Add(new GameAchievementsDto {
                    playnite_id = Str(g, "playnite_id"),
                    achievements = achievements
                });
            }
            return games;
        }

        private static GameSyncDto ReadGame(JsonElement e) => new GameSyncDto {
            playnite_id = Str(e, "playnite_id"),
            game_name = Str(e, "game_name"),
            playtime_seconds = e.GetProperty("playtime_seconds").GetInt64(),
            play_count = e.GetProperty("play_count").GetInt32(),
            last_activity = Date(e, "last_activity"),
            is_installed = e.GetProperty("is_installed").GetBoolean(),
            completion_status_id = Str(e, "completion_status_id"),
            completion_status_name = Str(e, "completion_status_name"),
            user_score = Int(e, "user_score"),
            source_name = Str(e, "source_name"),
            is_favorite = e.GetProperty("is_favorite").GetBoolean(),
            is_hidden = e.GetProperty("is_hidden").GetBoolean(),
            date_added = Date(e, "date_added"),
            modified = Date(e, "modified"),
            achievement_count_unlocked = Int(e, "achievement_count_unlocked"),
            achievement_count_total = Int(e, "achievement_count_total"),
        };

        private static string Str(JsonElement e, string name) {
            var p = e.GetProperty(name);
            return p.ValueKind == JsonValueKind.Null ? null : p.GetString();
        }

        private static int? Int(JsonElement e, string name) {
            var p = e.GetProperty(name);
            return p.ValueKind == JsonValueKind.Null ? (int?)null : p.GetInt32();
        }

        private static DateTime? Date(JsonElement e, string name) {
            var raw = Str(e, name);
            if (raw == null) {
                return null;
            }
            return DateTime.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }
    }
}
