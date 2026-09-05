using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using Playnite.SDK;

namespace GsPlugin.Services {
    /// <summary>
    /// Retrieves per-game achievement data from Playnite Achievements by reading its SQLite database.
    /// The database is at {ExtensionsDataPath}/{PluginGuid}/achievement_cache.db.
    /// All methods return null if Playnite Achievements is not installed or the game has no data.
    /// </summary>
    public class GsPlayniteAchievementsHelper : AchievementProviderBase {
        private static readonly Guid PlayniteAchievementsId = new Guid(
            "e6aad2c9-6e06-4d8d-ac55-ac3b252b5f7b"
        );

        private readonly string _dbPath;

        public GsPlayniteAchievementsHelper(IPlayniteAPI api) : base(PlayniteAchievementsId, api) {
            _dbPath = Path.Combine(api.Paths.ExtensionsDataPath,
                PlayniteAchievementsId.ToString(), "achievement_cache.db");
        }

        internal GsPlayniteAchievementsHelper(string dbPathOverride) : base(PlayniteAchievementsId, null) {
            _dbPath = dbPathOverride;
        }

        public override string ProviderName => "Playnite Achievements";

        protected override bool HasLocalData => File.Exists(_dbPath);

        public override (int unlocked, int total)? GetCounts(Guid gameId) {
            return SafeReadValue<(int unlocked, int total)>("Count lookup", gameId, () => {
                if (!File.Exists(_dbPath)) return null;

                using (var conn = new SQLiteConnection($"Data Source={_dbPath};Read Only=True;Pooling=True;")) {
                    conn.Open();
                    using (var cmd = conn.CreateCommand()) {
                        cmd.CommandText = @"
                            SELECT ugp.AchievementsUnlocked, ugp.TotalAchievements
                            FROM UserGameProgress ugp
                            INNER JOIN Users u ON ugp.UserId = u.Id
                            WHERE ugp.CacheKey = @playniteId
                              AND u.IsCurrentUser = 1
                              AND ugp.HasAchievements = 1
                            LIMIT 1
                        ";
                        cmd.Parameters.AddWithValue("@playniteId", gameId.ToString());

                        using (var reader = cmd.ExecuteReader()) {
                            if (reader.Read()) {
                                var unlocked = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                                var total = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                                return total > 0 ? (unlocked, total) : ((int, int)?)null;
                            }
                        }
                    }
                }
                return null;
            });
        }

        protected override List<AchievementItem> ReadAchievementsCore(Guid gameId) {
            if (!File.Exists(_dbPath)) return null;

            using (var conn = new SQLiteConnection($"Data Source={_dbPath};Read Only=True;Pooling=True;")) {
                conn.Open();
                using (var cmd = conn.CreateCommand()) {
                    cmd.CommandText = @"
                        SELECT
                            ad.DisplayName,
                            ad.Description,
                            ua.Unlocked,
                            ua.UnlockTimeUtc,
                            ad.GlobalPercentUnlocked
                        FROM UserAchievements ua
                        INNER JOIN AchievementDefinitions ad
                            ON ua.AchievementDefinitionId = ad.Id
                        INNER JOIN UserGameProgress ugp
                            ON ua.UserGameProgressId = ugp.Id
                        INNER JOIN Users u
                            ON ugp.UserId = u.Id
                        WHERE ugp.CacheKey = @playniteId
                          AND u.IsCurrentUser = 1
                          AND ugp.HasAchievements = 1
                    ";
                    cmd.Parameters.AddWithValue("@playniteId", gameId.ToString());

                    var result = new List<AchievementItem>();
                    using (var reader = cmd.ExecuteReader()) {
                        while (reader.Read()) {
                            var displayName = reader.IsDBNull(0) ? null : reader.GetString(0);
                            var description = reader.IsDBNull(1) ? null : reader.GetString(1);
                            var unlocked = !reader.IsDBNull(2) && reader.GetBoolean(2);

                            DateTime? dateUnlocked = null;
                            if (!reader.IsDBNull(3)) {
                                var unlockStr = reader.GetString(3);
                                // InvariantCulture, not null: a null provider means CurrentCulture,
                                // so an ISO timestamp read under a non-Gregorian calendar (th-TH,
                                // ar-SA) parses to a wildly wrong year and is then uploaded.
                                if (DateTime.TryParse(unlockStr, System.Globalization.CultureInfo.InvariantCulture,
                                        System.Globalization.DateTimeStyles.RoundtripKind,
                                        out var parsed)
                                    && parsed > DateTime.MinValue && parsed.Year > 1) {
                                    dateUnlocked = parsed;
                                }
                            }

                            float? rarityPercent = null;
                            if (!reader.IsDBNull(4)) {
                                rarityPercent = (float)reader.GetDouble(4);
                            }

                            result.Add(new AchievementItem {
                                Name = displayName,
                                Description = description,
                                DateUnlocked = unlocked ? dateUnlocked : null,
                                IsUnlocked = unlocked,
                                RarityPercent = rarityPercent
                            });
                        }
                    }

                    return result.Count > 0 ? result : null;
                }
            }
        }

        /// <summary>
        /// Wording for the failure types these database reads distinguish; null means the
        /// generic "{operation} failed" message applies.
        /// </summary>
        protected override string DescribeAchievementReadFailure(Exception ex) {
            if (ex is SQLiteException) return "SQLite error";
            if (ex is IOException) return "DB file access error";
            return null;
        }
    }
}
