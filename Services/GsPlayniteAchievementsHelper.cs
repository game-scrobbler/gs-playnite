using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using Playnite.SDK;
using Playnite.SDK.Plugins;
using GsPlugin.Infrastructure;

namespace GsPlugin.Services {
    /// <summary>
    /// Retrieves per-game achievement data from Playnite Achievements by reading its SQLite database.
    /// The database is at {ExtensionsDataPath}/{PluginGuid}/achievement_cache.db.
    /// All methods return null if Playnite Achievements is not installed or the game has no data.
    /// </summary>
    public class GsPlayniteAchievementsHelper : IAchievementProvider {
        private static readonly Guid PlayniteAchievementsId = new Guid(
            "e6aad2c9-6e06-4d8d-ac55-ac3b252b5f7b"
        );

        private readonly IPlayniteAPI _api;
        private readonly string _dbPath;

        public GsPlayniteAchievementsHelper(IPlayniteAPI api) {
            _api = api;
            _dbPath = Path.Combine(api.Paths.ExtensionsDataPath,
                PlayniteAchievementsId.ToString(), "achievement_cache.db");
        }

        internal GsPlayniteAchievementsHelper(string dbPathOverride) {
            _api = null;
            _dbPath = dbPathOverride;
        }

        public string ProviderName => "Playnite Achievements";

        public bool IsInstalled {
            get {
                if (File.Exists(_dbPath)) return true;
                return _api?.Addons?.Plugins?.Any(p => p.Id == PlayniteAchievementsId) == true;
            }
        }

        public (int unlocked, int total)? GetCounts(Guid gameId) {
            try {
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
            }
            catch (Exception ex) {
                GsLogger.Warn($"[GsPlayniteAchievementsHelper] Count lookup failed for game {gameId}: {ex.Message}");
                return null;
            }
        }

        public List<AchievementItem> GetAchievements(Guid gameId) {
            try {
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
                                    if (DateTime.TryParse(unlockStr, null,
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
            catch (SQLiteException ex) {
                GsLogger.Warn($"[GsPlayniteAchievementsHelper] SQLite error for game {gameId}: {ex.Message}");
                return null;
            }
            catch (IOException ex) {
                GsLogger.Warn($"[GsPlayniteAchievementsHelper] DB file access error for game {gameId}: {ex.Message}");
                return null;
            }
            catch (Exception ex) {
                GsLogger.Warn($"[GsPlayniteAchievementsHelper] Achievement lookup failed for game {gameId}: {ex.Message}");
                return null;
            }
        }

        public string GetVersion() {
            try {
                var plugin = _api?.Addons?.Plugins?.FirstOrDefault(p => p.Id == PlayniteAchievementsId);
                if (plugin == null) return null;
                return PluginVersionHelper.GetExtensionYamlVersion(plugin)
                    ?? plugin.GetType().Assembly.GetName().Version?.ToString(3);
            }
            catch (Exception ex) {
                GsLogger.Warn(
                    $"[GsPlayniteAchievementsHelper] Version lookup failed: {ex.Message}"
                );
                return null;
            }
        }
    }
}
