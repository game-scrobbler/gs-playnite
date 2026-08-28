using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Playnite.SDK;

namespace GsPlugin.Services {
    /// <summary>
    /// Retrieves per-game achievement data from SuccessStory by reading its on-disk JSON files.
    /// Each game's achievements are stored in {ExtensionsDataPath}/{PluginGuid}/SuccessStory/{GameId}.json.
    /// All methods return null if SuccessStory is not installed or the game has no data.
    /// </summary>
    public class GsSuccessStoryHelper : AchievementProviderBase {
        private static readonly Guid SuccessStoryId = new Guid(
            "cebe6d32-8c46-4459-b993-5a5189d60788"
        );

        private readonly string _dataPath;

        public GsSuccessStoryHelper(IPlayniteAPI api) : base(SuccessStoryId, api) {
            _dataPath = ResolveDataPath(api.Paths.ExtensionsDataPath);
        }

        internal GsSuccessStoryHelper(string dataPathOverride) : base(SuccessStoryId, null) {
            _dataPath = dataPathOverride;
        }

        public override string ProviderName => "SuccessStory";

        protected override bool HasLocalData => _dataPath != null && Directory.Exists(_dataPath);

        public override (int unlocked, int total)? GetCounts(Guid gameId) {
            var achievements = GetAchievements(gameId);
            if (achievements == null || achievements.Count == 0) return null;
            return (achievements.Count(a => a.IsUnlocked), achievements.Count);
        }

        public override List<AchievementItem> GetAchievements(Guid gameId) {
            return SafeRead("Achievement lookup", gameId, DescribeReadFailure, () => {
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
            });
        }

        /// <summary>
        /// Wording for the failure types these file reads distinguish; null means the
        /// generic "{operation} failed" message applies.
        /// </summary>
        private static string DescribeReadFailure(Exception ex) {
            if (ex is JsonException) return "JSON parse error";
            if (ex is IOException) return "File read error";
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
