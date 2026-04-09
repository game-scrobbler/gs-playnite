using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Playnite;
using GsPlugin.Infrastructure;

namespace GsPlugin.Services {
    /// <summary>
    /// Retrieves per-game achievement data from SuccessStory by reading its on-disk JSON files.
    /// Each game's achievements are stored in {ExtensionsDataPath}/{PluginGuid}/SuccessStory/{GameId}.json.
    /// All methods return null if SuccessStory is not installed or the game has no data.
    /// </summary>
    public class GsSuccessStoryHelper : IAchievementProvider {
        private static readonly Guid SuccessStoryId = new Guid(
            "cebe6d32-8c46-4459-b993-5a5189d60788"
        );

        private readonly IPlayniteApi? _api;
        private readonly string _dataPath;

        public GsSuccessStoryHelper(IPlayniteApi api) {
            _api = api;
            // In P11, UserDataDir is the plugin-specific data dir.
            // Parent directory is the shared ExtensionsData folder.
            _dataPath = ResolveDataPath(Path.GetDirectoryName(api.UserDataDir)!);
        }

        internal GsSuccessStoryHelper(string dataPathOverride) {
            _api = null;
            _dataPath = dataPathOverride;
        }

        public string ProviderName => "SuccessStory";

        public bool IsInstalled {
            get {
                // In P11, check data directory existence only (Addons.Plugins API changed).
                return _dataPath != null && Directory.Exists(_dataPath);
            }
        }

        public (int unlocked, int total)? GetCounts(Guid gameId) {
            var achievements = GetAchievements(gameId);
            if (achievements == null || achievements.Count == 0) return null;
            return (achievements.Count(a => a.IsUnlocked), achievements.Count);
        }

        public int? GetUnlockedCount(Guid gameId) => GetCounts(gameId)?.unlocked;

        public int? GetTotalCount(Guid gameId) => GetCounts(gameId)?.total;

        public List<AchievementItem> GetAchievements(Guid gameId) {
            try {
                if (_dataPath == null || !Directory.Exists(_dataPath)) return null;

                var filePath = Path.Combine(_dataPath, $"{gameId}.json");
                if (!File.Exists(filePath)) return null;

                byte[] fileBytes;
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                           FileShare.ReadWrite | FileShare.Delete)) {
                    using (var ms = new MemoryStream()) {
                        stream.CopyTo(ms);
                        fileBytes = ms.ToArray();
                    }
                }

                using (var doc = JsonDocument.Parse(fileBytes)) {
                    var root = doc.RootElement;

                    if (root.TryGetProperty("IsIgnored", out var ignored) && ignored.GetBoolean())
                        return null;

                    if (!root.TryGetProperty("Items", out var items) || items.ValueKind != JsonValueKind.Array)
                        return null;

                    var result = new List<AchievementItem>();
                    foreach (var item in items.EnumerateArray()) {
                        var name = item.TryGetProperty("Name", out var n) ? n.GetString() : null;
                        var description = item.TryGetProperty("Description", out var d) ? d.GetString() : null;

                        DateTime? dateUnlocked = null;
                        if (item.TryGetProperty("DateUnlocked", out var du) && du.ValueKind != JsonValueKind.Null) {
                            if (du.TryGetDateTime(out var parsed)
                                && parsed > DateTime.MinValue && parsed.Year > 1) {
                                dateUnlocked = parsed;
                            }
                        }

                        float? rarityPercent = null;
                        if (item.TryGetProperty("Percent", out var pct) && pct.ValueKind == JsonValueKind.Number) {
                            rarityPercent = (float)pct.GetDouble();
                        }

                        bool isUnlocked = dateUnlocked.HasValue;

                        result.Add(new AchievementItem {
                            Name = name,
                            Description = description,
                            DateUnlocked = isUnlocked ? dateUnlocked : null,
                            IsUnlocked = isUnlocked,
                            RarityPercent = rarityPercent
                        });
                    }

                    return result.Count > 0 ? result : null;
                }
            }
            catch (JsonException ex) {
                GsLogger.Warn($"[GsSuccessStoryHelper] JSON parse error for game {gameId}: {ex.Message}");
                return null;
            }
            catch (IOException ex) {
                GsLogger.Warn($"[GsSuccessStoryHelper] File read error for game {gameId}: {ex.Message}");
                return null;
            }
            catch (Exception ex) {
                GsLogger.Warn($"[GsSuccessStoryHelper] Achievement lookup failed for game {gameId}: {ex.Message}");
                return null;
            }
        }

        public string GetVersion() {
            // TODO P11: Addons.Plugins API removed — version lookup not yet implemented.
            return null;
        }

        private static string ResolveDataPath(string extensionsDataPath) {
            if (string.IsNullOrEmpty(extensionsDataPath)) return null;

            // SuccessStory stores data under {pluginGuid}/SuccessStory/{gameId}.json
            var path = Path.Combine(extensionsDataPath, SuccessStoryId.ToString(), "SuccessStory");
            if (Directory.Exists(path)) return path;

            return path;
        }
    }
}
