using System;
using System.Collections.Generic;
using Xunit;

namespace GsPlugin.Tests {
    public class GsScrobblingServiceHashTests {
        [Fact]
        public void ComputeLibraryHash_EmptyLibrary_ReturnsConsistentHash() {
            var hash1 = GsScrobblingService.ComputeLibraryHash(new List<GsApiClient.GameSyncDto>());
            var hash2 = GsScrobblingService.ComputeLibraryHash(new List<GsApiClient.GameSyncDto>());
            Assert.Equal(hash1, hash2);
            Assert.Equal(64, hash1.Length); // SHA-256 hex = 64 chars
        }

        [Fact]
        public void ComputeLibraryHash_SameLibrary_ReturnsSameHash() {
            var library = new List<GsApiClient.GameSyncDto> {
                new GsApiClient.GameSyncDto {
                    playnite_id = "aaaaaaaa-0000-0000-0000-000000000001",
                    playtime_seconds = 3600,
                    play_count = 5,
                    last_activity = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc)
                },
                new GsApiClient.GameSyncDto {
                    playnite_id = "bbbbbbbb-0000-0000-0000-000000000002",
                    playtime_seconds = 0,
                    play_count = 0,
                    last_activity = null
                }
            };

            var hash1 = GsScrobblingService.ComputeLibraryHash(library);
            var hash2 = GsScrobblingService.ComputeLibraryHash(library);
            Assert.Equal(hash1, hash2);
        }

        [Fact]
        public void ComputeLibraryHash_OrderIndependent() {
            // The hash must be the same regardless of the order games appear in the list,
            // because keys are sorted before hashing (matching backend createLibraryHash behaviour).
            var game1 = new GsApiClient.GameSyncDto {
                playnite_id = "aaaaaaaa-0000-0000-0000-000000000001",
                playtime_seconds = 1000,
                play_count = 2,
                last_activity = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            };
            var game2 = new GsApiClient.GameSyncDto {
                playnite_id = "cccccccc-0000-0000-0000-000000000003",
                playtime_seconds = 500,
                play_count = 1,
                last_activity = null
            };

            var hashAB = GsScrobblingService.ComputeLibraryHash(new List<GsApiClient.GameSyncDto> { game1, game2 });
            var hashBA = GsScrobblingService.ComputeLibraryHash(new List<GsApiClient.GameSyncDto> { game2, game1 });
            Assert.Equal(hashAB, hashBA);
        }

        [Fact]
        public void ComputeLibraryHash_PlaytimeChange_ProducesDifferentHash() {
            var before = new List<GsApiClient.GameSyncDto> {
                new GsApiClient.GameSyncDto {
                    playnite_id = "aaaaaaaa-0000-0000-0000-000000000001",
                    playtime_seconds = 100,
                    play_count = 1,
                    last_activity = null
                }
            };
            var after = new List<GsApiClient.GameSyncDto> {
                new GsApiClient.GameSyncDto {
                    playnite_id = "aaaaaaaa-0000-0000-0000-000000000001",
                    playtime_seconds = 200,
                    play_count = 1,
                    last_activity = null
                }
            };

            Assert.NotEqual(
                GsScrobblingService.ComputeLibraryHash(before),
                GsScrobblingService.ComputeLibraryHash(after));
        }

        [Fact]
        public void ComputeLibraryHash_NewGame_ProducesDifferentHash() {
            var game = new GsApiClient.GameSyncDto {
                playnite_id = "aaaaaaaa-0000-0000-0000-000000000001",
                playtime_seconds = 500,
                play_count = 3,
                last_activity = null
            };
            var one = new List<GsApiClient.GameSyncDto> { game };
            var two = new List<GsApiClient.GameSyncDto> {
                game,
                new GsApiClient.GameSyncDto {
                    playnite_id = "bbbbbbbb-0000-0000-0000-000000000002",
                    playtime_seconds = 0,
                    play_count = 0,
                    last_activity = null
                }
            };

            Assert.NotEqual(
                GsScrobblingService.ComputeLibraryHash(one),
                GsScrobblingService.ComputeLibraryHash(two));
        }

        /// <summary>
        /// Validates the exact SHA-256 output against a known value.
        /// The expected hash was computed independently using the same algorithm as the backend
        /// (playniteUtils.ts createLibraryHash): keys sorted, each key + "|" fed to SHA-256 incrementally.
        ///
        /// Input: one game with playnite_id="abc", playtime_seconds=0, play_count=0, last_activity=null
        /// Key: "abc:0:0:"
        /// SHA-256("abc:0:0:" + "|") = verified against Node.js reference implementation.
        /// </summary>
        [Fact]
        public void ComputeLibraryHash_KnownInput_MatchesExpectedHash() {
            var library = new List<GsApiClient.GameSyncDto> {
                new GsApiClient.GameSyncDto {
                    playnite_id = "abc",
                    playtime_seconds = 0,
                    play_count = 0,
                    last_activity = null
                }
            };

            // Expected: SHA-256 of UTF-8 bytes of "abc:0:0:" followed by "|"
            // Computed reference: node -e "const c=require('crypto');const h=c.createHash('sha256');h.update('abc:0:0:');h.update('|');console.log(h.digest('hex'))"
            const string expected = "22cc6270f991e00a640c34e5ed6c1f31fa20a84cef195f110a328cac421a2c2d";
            Assert.Equal(expected, GsScrobblingService.ComputeLibraryHash(library));
        }
    }
}
