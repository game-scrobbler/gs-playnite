using System;
using System.Collections.Generic;
using System.Globalization;
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
        /// Normalizes a date string persisted in a legacy fat snapshot (written with
        /// DateTime.ToString("o"), i.e. fractional seconds + offset) into the same
        /// second-precision UTC form <see cref="FormatDateForHash"/> produces for live DTOs.
        /// Without this, a migrated fingerprint could never equal its live counterpart and
        /// every played game would be flagged 'updated' on the first post-migration diff.
        /// </summary>
        internal static string NormalizeSnapshotDateForHash(string raw) {
            if (string.IsNullOrEmpty(raw)) {
                return "";
            }
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var dt)) {
                return FormatDateForHash(dt);
            }
            return raw;
        }

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
        /// Per-game library fingerprint used by the local hash index for diffs.
        /// Must stay aligned with the activity + metadata fields in ComputeLibraryHash.
        /// </summary>
        public static string ComputeLibraryItemFingerprint(GameSyncDto g) {
            return $"{g.playtime_seconds}|{g.play_count}|{FormatDateForHash(g.last_activity)}|{ComputeGameMetadataHash(g)}";
        }

        /// <summary>
        /// Rebuild fingerprint from a legacy fat GameSnapshot row (migration only).
        /// </summary>
        public static string LibraryFingerprintFromSnapshot(Models.GameSnapshot snap) {
            var last = NormalizeSnapshotDateForHash(snap.last_activity);
            var meta = snap.metadata_hash ?? "";
            return $"{snap.playtime_seconds}|{snap.play_count}|{last}|{meta}";
        }

        /// <summary>
        /// Per-game achievement fingerprint used by the local hash index for diff detection.
        /// Unlike <see cref="ComputeAchievementHash"/> — which is a server contract
        /// (createAchievementHashV2, names + counts only) and must NOT change — this local
        /// fingerprint also folds in each achievement's unlock state and rarity so the diff path
        /// re-sends a game when its rarity changes or its unlock set swaps without a count change.
        /// Kept in one place so the live and migration recipes never drift apart.
        /// </summary>
        public static string ComputeAchievementGameFingerprint(GameAchievementsDto g) {
            var achs = g.achievements ?? new List<AchievementItemDto>();
            return AchievementFingerprint(
                g.playnite_id,
                achs.Select(a => (a.name, a.is_unlocked, a.rarity_percent)));
        }

        /// <summary>
        /// Rebuild the local fingerprint from a legacy fat GameAchievementSnapshot (migration only).
        /// Must produce the same value as <see cref="ComputeAchievementGameFingerprint"/> for the
        /// same underlying achievement set.
        /// </summary>
        public static string AchievementFingerprintFromSnapshot(Models.GameAchievementSnapshot snap) {
            var achs = snap.achievements ?? new List<Models.AchievementSnapshot>();
            return AchievementFingerprint(
                snap.playnite_id,
                achs.Select(a => (a.name, a.is_unlocked, a.rarity_percent)));
        }

        private static string AchievementFingerprint(
            string playniteId,
            IEnumerable<(string name, bool unlocked, float? rarity)> achievements) {
            var list = achievements.ToList();
            var unlockedCount = list.Count(a => a.unlocked);
            // Per-achievement name + unlock state + rarity, field-separated with a control
            // character so names containing digits/delimiters cannot collide, then ordered so
            // the digest is stable regardless of the provider's iteration order.
            var detail = string.Join("", list
                .Select(a => $"{a.name}{(a.unlocked ? "1" : "0")}{FormatRarity(a.rarity)}")
                .OrderBy(s => s, StringComparer.Ordinal));
            string detailHash;
            using (var sha = SHA256.Create()) {
                var bytes = Encoding.UTF8.GetBytes(detail);
                detailHash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            }
            return $"{playniteId}:{list.Count}:{unlockedCount}:{detailHash}";
        }

        private static string FormatRarity(float? rarity) =>
            rarity?.ToString("0.####", CultureInfo.InvariantCulture) ?? "";

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
