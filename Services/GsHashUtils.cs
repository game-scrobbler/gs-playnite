using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using GsPlugin.Api;

namespace GsPlugin.Services {
    /// <summary>
    /// Pure static hash utilities for change detection during library and achievement syncs.
    /// All hashes use SHA-256 and must match the corresponding server-side implementations.
    /// </summary>
    public static class GsHashUtils {
        /// <summary>
        /// Format a DateTime for hashing — strips fractional seconds for deterministic
        /// cross-platform matching between C# and TypeScript.
        /// Both sides normalize to "yyyy-MM-ddTHH:mm:ssZ" (no fractional seconds, UTC).
        /// </summary>
        internal static string FormatDateForHash(DateTime? dt) =>
            dt?.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ") ?? "";

        /// <summary>
        /// Computes a SHA-256 hex digest of the library for change detection.
        /// Includes both activity fields (playtime, play_count, last_activity) and a per-game
        /// metadata hash so that metadata-only changes (renames, genre edits, etc.) are also detected.
        /// </summary>
        public static string ComputeLibraryHash(List<GameSyncDto> library) {
            var keys = library
                .Select(g =>
                    $"{g.playnite_id ?? ""}:{g.playtime_seconds}:{g.play_count}:{FormatDateForHash(g.last_activity)}:{ComputeGameMetadataHash(g)}")
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToArray();

            var separator = new byte[] { (byte)'|' };
            using (var sha256 = SHA256.Create()) {
                foreach (var key in keys) {
                    var bytes = Encoding.UTF8.GetBytes(key);
                    sha256.TransformBlock(bytes, 0, bytes.Length, null, 0);
                    sha256.TransformBlock(separator, 0, 1, null, 0);
                }
                sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return BitConverter.ToString(sha256.Hash).Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>
        /// Computes a SHA-256 hex digest of per-game metadata for diff detection.
        /// Operates over the slim v3 GameSyncDto field set — the server's IGDB
        /// canonical layer owns genre/theme/company/score/release-date metadata
        /// (per ADR-011 in gs-mono), so those fields are not sent or hashed.
        /// Includes all DTO fields except activity fields (playtime, play_count,
        /// last_activity) which are already covered by the library-level hash key.
        ///
        /// Must produce the **exact same output** as server-side
        /// computeGameMetadataHashV3() in
        /// gs-mono/apps/backend/src/services/playnite/playniteUtils/hashUtils.ts.
        /// </summary>
        public static string ComputeGameMetadataHash(GameSyncDto g) {
            var sb = new StringBuilder();
            sb.Append(g.game_name ?? "");
            sb.Append('|');
            sb.Append(g.completion_status_id ?? "");
            sb.Append('|');
            sb.Append(g.completion_status_name ?? "");
            sb.Append('|');
            sb.Append(g.is_installed ? "1" : "0");
            sb.Append('|');
            sb.Append(g.user_score?.ToString() ?? "");
            sb.Append('|');
            sb.Append(g.source_name ?? "");
            sb.Append('|');
            sb.Append(g.is_favorite ? "1" : "0");
            sb.Append('|');
            sb.Append(g.is_hidden ? "1" : "0");
            sb.Append('|');
            sb.Append(FormatDateForHash(g.date_added));
            sb.Append('|');
            sb.Append(FormatDateForHash(g.modified));
            sb.Append('|');
            sb.Append(g.achievement_count_unlocked?.ToString() ?? "");
            sb.Append('|');
            sb.Append(g.achievement_count_total?.ToString() ?? "");

            using (var sha256 = SHA256.Create()) {
                var bytes = Encoding.UTF8.GetBytes(sb.ToString());
                var hash = sha256.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>
        /// Compute a SHA-256 hash of achievement data for change detection.
        /// Per-game key: {playnite_id}:{achievement_count}:{unlocked_count}:{sorted_names_hash}
        /// Keys are sorted ordinally, then hashed with "|" separator.
        /// Must match server's createAchievementHashV2() exactly.
        /// </summary>
        public static string ComputeAchievementHash(List<GameAchievementsDto> games) {
            var keys = games
                .Select(g => {
                    var achs = g.achievements ?? new List<AchievementItemDto>();
                    var unlockedCount = achs.Count(a => a.is_unlocked);
                    var sortedNames = string.Join(",", achs.Select(a => a.name).OrderBy(n => n, StringComparer.Ordinal));
                    string namesHash;
                    using (var sha = SHA256.Create()) {
                        var bytes = Encoding.UTF8.GetBytes(sortedNames);
                        var hash = sha.ComputeHash(bytes);
                        namesHash = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                    }
                    return $"{g.playnite_id}:{achs.Count}:{unlockedCount}:{namesHash}";
                })
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToArray();

            var separator = new byte[] { (byte)'|' };
            using (var sha256 = SHA256.Create()) {
                foreach (var key in keys) {
                    var bytes = Encoding.UTF8.GetBytes(key);
                    sha256.TransformBlock(bytes, 0, bytes.Length, null, 0);
                    sha256.TransformBlock(separator, 0, 1, null, 0);
                }
                sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return BitConverter.ToString(sha256.Hash).Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>
        /// Computes a stable hash of integration account identities so we can detect
        /// when accounts change even if the library itself hasn't.
        /// </summary>
        internal static string ComputeIntegrationAccountsHash(List<IntegrationAccountDto> accounts) {
            if (accounts == null || accounts.Count == 0) {
                return "";
            }
            var sorted = accounts.OrderBy(a => a.provider_id).ThenBy(a => a.account_id);
            var sb = new StringBuilder();
            foreach (var a in sorted) {
                sb.Append(a.provider_id).Append(':').Append(a.account_id).Append(';');
            }
            using (var sha = SHA256.Create()) {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
