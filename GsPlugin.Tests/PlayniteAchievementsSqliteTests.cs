using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using Xunit;
using GsPlugin.Services;

namespace GsPlugin.Tests {
    public class PlayniteAchievementsSqliteTests : IDisposable {
        private readonly string _tempDir;
        private readonly string _dbPath;

        public PlayniteAchievementsSqliteTests() {
            _tempDir = Path.Combine(Path.GetTempPath(), "gs-test-pa-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _dbPath = Path.Combine(_tempDir, "achievement_cache.db");
        }

        public void Dispose() {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        private GsPlayniteAchievementsHelper CreateHelper() {
            return new GsPlayniteAchievementsHelper(_dbPath);
        }

        private void CreateTestDatabase(Action<SQLiteConnection> setup = null) {
            using (var conn = new SQLiteConnection($"Data Source={_dbPath};")) {
                conn.Open();
                using (var cmd = conn.CreateCommand()) {
                    cmd.CommandText = @"
                        CREATE TABLE Users (
                            Id INTEGER PRIMARY KEY,
                            ProviderKey TEXT,
                            ExternalUserId TEXT,
                            IsCurrentUser INTEGER
                        );
                        CREATE TABLE Games (
                            Id INTEGER PRIMARY KEY,
                            PlayniteGameId TEXT,
                            GameName TEXT,
                            ProviderKey TEXT
                        );
                        CREATE TABLE AchievementDefinitions (
                            Id INTEGER PRIMARY KEY,
                            GameId INTEGER,
                            ApiName TEXT,
                            DisplayName TEXT,
                            Description TEXT,
                            Hidden INTEGER DEFAULT 0,
                            GlobalPercentUnlocked REAL,
                            Points INTEGER
                        );
                        CREATE TABLE UserGameProgress (
                            Id INTEGER PRIMARY KEY,
                            UserId INTEGER,
                            GameId INTEGER,
                            CacheKey TEXT,
                            HasAchievements INTEGER,
                            AchievementsUnlocked INTEGER,
                            TotalAchievements INTEGER
                        );
                        CREATE TABLE UserAchievements (
                            Id INTEGER PRIMARY KEY,
                            UserGameProgressId INTEGER,
                            AchievementDefinitionId INTEGER,
                            Unlocked INTEGER,
                            UnlockTimeUtc TEXT
                        );
                    ";
                    cmd.ExecuteNonQuery();
                }
                setup?.Invoke(conn);
            }
        }

        private static void InsertTestData(SQLiteConnection conn, Guid gameId, string gameName,
            (string name, string desc, bool unlocked, string unlockTime, double? rarity)[] achievements) {
            using (var cmd = conn.CreateCommand()) {
                cmd.CommandText = "INSERT INTO Users (Id, ProviderKey, ExternalUserId, IsCurrentUser) VALUES (1, 'Steam', '12345', 1)";
                cmd.ExecuteNonQuery();
            }

            using (var cmd = conn.CreateCommand()) {
                cmd.CommandText = "INSERT INTO Games (Id, PlayniteGameId, GameName, ProviderKey) VALUES (1, @gid, @name, 'Steam')";
                cmd.Parameters.AddWithValue("@gid", gameId.ToString());
                cmd.Parameters.AddWithValue("@name", gameName);
                cmd.ExecuteNonQuery();
            }

            int unlockedCount = achievements.Count(a => a.unlocked);
            using (var cmd = conn.CreateCommand()) {
                cmd.CommandText = @"INSERT INTO UserGameProgress (Id, UserId, GameId, CacheKey, HasAchievements, AchievementsUnlocked, TotalAchievements)
                                    VALUES (1, 1, 1, @key, 1, @unlocked, @total)";
                cmd.Parameters.AddWithValue("@key", gameId.ToString());
                cmd.Parameters.AddWithValue("@unlocked", unlockedCount);
                cmd.Parameters.AddWithValue("@total", achievements.Length);
                cmd.ExecuteNonQuery();
            }

            for (int i = 0; i < achievements.Length; i++) {
                var a = achievements[i];
                using (var cmd = conn.CreateCommand()) {
                    cmd.CommandText = @"INSERT INTO AchievementDefinitions (Id, GameId, ApiName, DisplayName, Description, GlobalPercentUnlocked)
                                        VALUES (@id, 1, @api, @name, @desc, @rarity)";
                    cmd.Parameters.AddWithValue("@id", i + 1);
                    cmd.Parameters.AddWithValue("@api", $"ach_{i}");
                    cmd.Parameters.AddWithValue("@name", a.name);
                    cmd.Parameters.AddWithValue("@desc", (object)a.desc ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@rarity", a.rarity.HasValue ? (object)a.rarity.Value : DBNull.Value);
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = conn.CreateCommand()) {
                    cmd.CommandText = @"INSERT INTO UserAchievements (UserGameProgressId, AchievementDefinitionId, Unlocked, UnlockTimeUtc)
                                        VALUES (1, @adId, @unlocked, @time)";
                    cmd.Parameters.AddWithValue("@adId", i + 1);
                    cmd.Parameters.AddWithValue("@unlocked", a.unlocked ? 1 : 0);
                    cmd.Parameters.AddWithValue("@time", a.unlockTime != null ? (object)a.unlockTime : DBNull.Value);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        [Fact]
        public void IsInstalled_ReturnsTrueWhenDbExists() {
            CreateTestDatabase();
            var helper = CreateHelper();
            Assert.True(helper.IsInstalled);
        }

        [Fact]
        public void IsInstalled_ReturnsFalseWhenDbMissing() {
            var helper = new GsPlayniteAchievementsHelper(Path.Combine(_tempDir, "nonexistent.db"));
            Assert.False(helper.IsInstalled);
        }

        [Fact]
        public void GetAchievements_ReturnsNullForMissingGame() {
            CreateTestDatabase();
            var helper = CreateHelper();
            Assert.Null(helper.GetAchievements(Guid.NewGuid()));
        }

        [Fact]
        public void GetAchievements_ParsesUnlockedAchievement() {
            var gameId = Guid.NewGuid();
            CreateTestDatabase(conn => InsertTestData(conn, gameId, "Test Game",
                new (string name, string desc, bool unlocked, string unlockTime, double? rarity)[] {
                    ("First Blood", "Win a match", true, "2025-06-01T12:00:00Z", 42.5)
                }));

            var helper = CreateHelper();
            var result = helper.GetAchievements(gameId);

            Assert.NotNull(result);
            Assert.Single(result);
            Assert.Equal("First Blood", result[0].Name);
            Assert.Equal("Win a match", result[0].Description);
            Assert.True(result[0].IsUnlocked);
            Assert.NotNull(result[0].DateUnlocked);
            Assert.Equal(42.5f, result[0].RarityPercent);
        }

        [Fact]
        public void GetAchievements_ParsesLockedAchievement() {
            var gameId = Guid.NewGuid();
            CreateTestDatabase(conn => InsertTestData(conn, gameId, "Test Game",
                new (string name, string desc, bool unlocked, string unlockTime, double? rarity)[] {
                    ("Locked One", "Do something", false, null, 100.0)
                }));

            var helper = CreateHelper();
            var result = helper.GetAchievements(gameId);

            Assert.NotNull(result);
            Assert.Single(result);
            Assert.Equal("Locked One", result[0].Name);
            Assert.False(result[0].IsUnlocked);
            Assert.Null(result[0].DateUnlocked);
        }

        [Fact]
        public void GetAchievements_MixedUnlockedAndLocked() {
            var gameId = Guid.NewGuid();
            CreateTestDatabase(conn => InsertTestData(conn, gameId, "Test Game",
                new (string name, string desc, bool unlocked, string unlockTime, double? rarity)[] {
                    ("A", "desc A", true, "2025-03-10T08:00:00Z", 10.0),
                    ("B", "desc B", false, null, 80.0),
                    ("C", "desc C", true, "2025-05-20T16:30:00Z", 5.0)
                }));

            var helper = CreateHelper();
            var result = helper.GetAchievements(gameId);

            Assert.NotNull(result);
            Assert.Equal(3, result.Count);

            var unlocked = result.Where(a => a.IsUnlocked).ToList();
            var locked = result.Where(a => !a.IsUnlocked).ToList();
            Assert.Equal(2, unlocked.Count);
            Assert.Single(locked);
        }

        [Fact]
        public void GetCounts_ReturnsCorrectValues() {
            var gameId = Guid.NewGuid();
            CreateTestDatabase(conn => InsertTestData(conn, gameId, "Test Game",
                new (string name, string desc, bool unlocked, string unlockTime, double? rarity)[] {
                    ("A", null, true, "2025-03-10T08:00:00Z", null),
                    ("B", null, false, null, null),
                    ("C", null, true, "2025-05-20T16:30:00Z", null)
                }));

            var helper = CreateHelper();
            var counts = helper.GetCounts(gameId);

            Assert.NotNull(counts);
            Assert.Equal(2, counts.Value.unlocked);
            Assert.Equal(3, counts.Value.total);
        }

        [Fact]
        public void GetAchievements_ReturnsNullForCorruptDb() {
            File.WriteAllText(_dbPath, "this is not a database");
            var helper = CreateHelper();
            Assert.Null(helper.GetAchievements(Guid.NewGuid()));
        }

        [Fact]
        public void GetCounts_ReturnsNullWhenDbMissing() {
            var helper = new GsPlayniteAchievementsHelper(Path.Combine(_tempDir, "nonexistent.db"));
            Assert.Null(helper.GetCounts(Guid.NewGuid()));
        }

        [Fact]
        public void ProviderName_IsPlayniteAchievements() {
            CreateTestDatabase();
            var helper = CreateHelper();
            Assert.Equal("Playnite Achievements", helper.ProviderName);
        }

        [Fact]
        public void GetAchievements_IgnoresNonCurrentUser() {
            var gameId = Guid.NewGuid();
            CreateTestDatabase(conn => {
                // Insert a non-current user with achievements
                using (var cmd = conn.CreateCommand()) {
                    cmd.CommandText = "INSERT INTO Users (Id, ProviderKey, ExternalUserId, IsCurrentUser) VALUES (1, 'Steam', '999', 0)";
                    cmd.ExecuteNonQuery();
                }
                using (var cmd = conn.CreateCommand()) {
                    cmd.CommandText = "INSERT INTO Games (Id, PlayniteGameId, GameName, ProviderKey) VALUES (1, @gid, 'Test', 'Steam')";
                    cmd.Parameters.AddWithValue("@gid", gameId.ToString());
                    cmd.ExecuteNonQuery();
                }
                using (var cmd = conn.CreateCommand()) {
                    cmd.CommandText = @"INSERT INTO UserGameProgress (Id, UserId, GameId, CacheKey, HasAchievements, AchievementsUnlocked, TotalAchievements)
                                        VALUES (1, 1, 1, @key, 1, 1, 1)";
                    cmd.Parameters.AddWithValue("@key", gameId.ToString());
                    cmd.ExecuteNonQuery();
                }
                using (var cmd = conn.CreateCommand()) {
                    cmd.CommandText = "INSERT INTO AchievementDefinitions (Id, GameId, ApiName, DisplayName) VALUES (1, 1, 'a', 'Test Ach')";
                    cmd.ExecuteNonQuery();
                }
                using (var cmd = conn.CreateCommand()) {
                    cmd.CommandText = "INSERT INTO UserAchievements (UserGameProgressId, AchievementDefinitionId, Unlocked, UnlockTimeUtc) VALUES (1, 1, 1, '2025-01-01T00:00:00Z')";
                    cmd.ExecuteNonQuery();
                }
            });

            var helper = CreateHelper();
            Assert.Null(helper.GetAchievements(gameId));
        }
    }
}
