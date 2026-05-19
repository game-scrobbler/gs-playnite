using System;
using System.Collections.Generic;
using Xunit;
using GsPlugin.Api;
using GsPlugin.Services;

namespace GsPlugin.Tests {
    public class GsScrobblingServiceHashTests {
        [Fact]
        public void ComputeLibraryHash_EmptyLibrary_ReturnsConsistentHash() {
            var hash1 = GsHashUtils.ComputeLibraryHash(new List<GameSyncDto>());
            var hash2 = GsHashUtils.ComputeLibraryHash(new List<GameSyncDto>());
            Assert.Equal(hash1, hash2);
            Assert.Equal(64, hash1.Length); // SHA-256 hex = 64 chars
        }

        [Fact]
        public void ComputeLibraryHash_SameLibrary_ReturnsSameHash() {
            var library = new List<GameSyncDto> {
                new GameSyncDto {
                    playnite_id = "aaaaaaaa-0000-0000-0000-000000000001",
                    playtime_seconds = 3600,
                    play_count = 5,
                    last_activity = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc)
                },
                new GameSyncDto {
                    playnite_id = "bbbbbbbb-0000-0000-0000-000000000002",
                    playtime_seconds = 0,
                    play_count = 0,
                    last_activity = null
                }
            };

            var hash1 = GsHashUtils.ComputeLibraryHash(library);
            var hash2 = GsHashUtils.ComputeLibraryHash(library);
            Assert.Equal(hash1, hash2);
        }

        [Fact]
        public void ComputeLibraryHash_OrderIndependent() {
            // The hash must be the same regardless of the order games appear in the list,
            // because keys are sorted before hashing (matching backend createLibraryHash behaviour).
            var game1 = new GameSyncDto {
                playnite_id = "aaaaaaaa-0000-0000-0000-000000000001",
                playtime_seconds = 1000,
                play_count = 2,
                last_activity = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            };
            var game2 = new GameSyncDto {
                playnite_id = "cccccccc-0000-0000-0000-000000000003",
                playtime_seconds = 500,
                play_count = 1,
                last_activity = null
            };

            var hashAB = GsHashUtils.ComputeLibraryHash(new List<GameSyncDto> { game1, game2 });
            var hashBA = GsHashUtils.ComputeLibraryHash(new List<GameSyncDto> { game2, game1 });
            Assert.Equal(hashAB, hashBA);
        }

        [Fact]
        public void ComputeLibraryHash_PlaytimeChange_ProducesDifferentHash() {
            var before = new List<GameSyncDto> {
                new GameSyncDto {
                    playnite_id = "aaaaaaaa-0000-0000-0000-000000000001",
                    playtime_seconds = 100,
                    play_count = 1,
                    last_activity = null
                }
            };
            var after = new List<GameSyncDto> {
                new GameSyncDto {
                    playnite_id = "aaaaaaaa-0000-0000-0000-000000000001",
                    playtime_seconds = 200,
                    play_count = 1,
                    last_activity = null
                }
            };

            Assert.NotEqual(
                GsHashUtils.ComputeLibraryHash(before),
                GsHashUtils.ComputeLibraryHash(after));
        }

        [Fact]
        public void ComputeLibraryHash_NewGame_ProducesDifferentHash() {
            var game = new GameSyncDto {
                playnite_id = "aaaaaaaa-0000-0000-0000-000000000001",
                playtime_seconds = 500,
                play_count = 3,
                last_activity = null
            };
            var one = new List<GameSyncDto> { game };
            var two = new List<GameSyncDto> {
                game,
                new GameSyncDto {
                    playnite_id = "bbbbbbbb-0000-0000-0000-000000000002",
                    playtime_seconds = 0,
                    play_count = 0,
                    last_activity = null
                }
            };

            Assert.NotEqual(
                GsHashUtils.ComputeLibraryHash(one),
                GsHashUtils.ComputeLibraryHash(two));
        }

        [Fact]
        public void ComputeLibraryHash_MetadataOnlyChange_ProducesDifferentHash() {
            // Validates that metadata-only changes (e.g., game_name rename, genre edit)
            // are detected by ComputeLibraryHash even when activity fields are identical.
            var before = new List<GameSyncDto> {
                new GameSyncDto {
                    playnite_id = "aaaaaaaa-0000-0000-0000-000000000001",
                    playtime_seconds = 100,
                    play_count = 1,
                    last_activity = null,
                    game_name = "Old Name"
                }
            };
            var after = new List<GameSyncDto> {
                new GameSyncDto {
                    playnite_id = "aaaaaaaa-0000-0000-0000-000000000001",
                    playtime_seconds = 100,
                    play_count = 1,
                    last_activity = null,
                    game_name = "New Name"
                }
            };

            Assert.NotEqual(
                GsHashUtils.ComputeLibraryHash(before),
                GsHashUtils.ComputeLibraryHash(after));
        }

        /// <summary>
        /// Validates the exact SHA-256 output against a known value.
        /// The library hash key format is "{playnite_id}:{playtime_seconds}:{play_count}:{last_activity}:{metadata_hash}".
        /// Keys are sorted, each key + "|" fed to SHA-256 incrementally.
        ///
        /// Input: one game with playnite_id="abc", playtime_seconds=0, play_count=0,
        /// last_activity=null, all metadata defaults. The v3 slim metadata hash is
        /// computed over 12 pipe-separated fields (game_name, completion_status_id,
        /// completion_status_name, is_installed, user_score, source_name, is_favorite,
        /// is_hidden, date_added, modified, achievement_count_unlocked,
        /// achievement_count_total); booleans default to "0".
        ///
        /// Verified against Node.js reference implementation (server-side
        /// createLibraryHashV3 in gs-mono).
        /// </summary>
        [Fact]
        public void ComputeLibraryHash_KnownInput_MatchesExpectedHash() {
            var library = new List<GameSyncDto> {
                new GameSyncDto {
                    playnite_id = "abc",
                    playtime_seconds = 0,
                    play_count = 0,
                    last_activity = null
                }
            };

            const string expected = "428b0b563048984da73ee30ef54036b677950d3139834b615f3b409d54a167dd";
            Assert.Equal(expected, GsHashUtils.ComputeLibraryHash(library));
        }
    }
}
