using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GsPlugin.Api {
    // ──────────────────────────────────────────────────────────
    // Scrobble DTOs
    // ──────────────────────────────────────────────────────────

    public class ScrobbleStartReq {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string user_id { get; set; }
        public string game_name { get; set; }
        public string game_id { get; set; }
        public string plugin_id { get; set; }
        public string external_game_id { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string source_name { get; set; }
        public object metadata { get; set; }
        public string started_at { get; set; }
    }

    // ──────────────────────────────────────────────────────────
    // v3 standard API envelope
    // ──────────────────────────────────────────────────────────

    public enum ApiOutcome {
        Success,
        Queued,
        Fail,
        Error,
    }

    public class ApiResponse<T> {
        public string status { get; set; }
        public T data { get; set; }
        public string code { get; set; }
        public string message { get; set; }

        public ApiOutcome Outcome =>
            status == "success" ? ApiOutcome.Success :
            status == "queued" ? ApiOutcome.Queued :
            status == "fail" ? ApiOutcome.Fail :
                                  ApiOutcome.Error;
    }

    public class ScrobbleStartData {
        public string session_id { get; set; }
    }

    public class ScrobbleFinishData {
        public int duration_seconds { get; set; }
    }

    // Kept for pending-scrobble flush path which still reads session_id
    public class ScrobbleStartRes {
        public string session_id { get; set; }
    }

    public class AsyncQueuedResponse {
        public bool success { get; set; }
        public string status { get; set; }
        public string queueId { get; set; }
        public string message { get; set; }
        public string timestamp { get; set; }
        public string estimatedProcessingTime { get; set; }
        public string reason { get; set; }
        public string cooldownExpiresAt { get; set; }
        public string lastSyncAt { get; set; }
    }

    public class ScrobbleFinishReq {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string user_id { get; set; }
        public string game_name { get; set; }
        public string game_id { get; set; }
        public string plugin_id { get; set; }
        public string external_game_id { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string source_name { get; set; }
        public object metadata { get; set; }
        public string finished_at { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string session_id { get; set; }
    }

    public class ScrobbleFinishRes { }

    // ──────────────────────────────────────────────────────────
    // Library Sync DTOs
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Slim sync DTO sent to the v3 library sync endpoints.
    ///
    /// Per ADR-011 (gs-mono), IGDB is the single source of truth for game
    /// metadata (genres, themes, companies, scores, release dates, etc.).
    /// The server reads those fields from the canonical `games` layer via
    /// IGDB joins; the plugin no longer needs to send them.
    ///
    /// Fields removed vs the previous v2 GameSyncDto:
    ///   genres, platforms, developers, publishers, tags, features,
    ///   categories, series, age_ratings, regions, critic_score,
    ///   community_score, release_year, release_date.
    /// </summary>
    public class GameSyncDto {
        public string game_id { get; set; }
        public string plugin_id { get; set; }
        public string game_name { get; set; }
        public string playnite_id { get; set; }
        public long playtime_seconds { get; set; }
        public int play_count { get; set; }
        public DateTime? last_activity { get; set; }
        public bool is_installed { get; set; }
        public string completion_status_id { get; set; }
        public string completion_status_name { get; set; }
        public int? achievement_count_unlocked { get; set; }
        public int? achievement_count_total { get; set; }
        public int? user_score { get; set; }
        public DateTime? date_added { get; set; }
        public bool is_favorite { get; set; }
        public bool is_hidden { get; set; }
        public string source_name { get; set; }
        public DateTime? modified { get; set; }
    }

    public class LibraryFullSyncReq {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string user_id { get; set; }
        public List<GameSyncDto> library { get; set; }
        public string[] flags { get; set; }
        public List<Services.IntegrationAccountDto> integration_accounts { get; set; }
    }

    // ──────────────────────────────────────────────────────────
    // v4 chunked full sync
    // ──────────────────────────────────────────────────────────

    public class LibraryV4FullSyncBeginReq {
        public int expected_total_items { get; set; }
        public string result_snapshot_hash { get; set; }
        public string[] flags { get; set; }
        public List<Services.IntegrationAccountDto> integration_accounts { get; set; }
    }

    public class LibraryV4ChunkReq {
        public string sync_id { get; set; }
        public int chunk_index { get; set; }
        public List<GameSyncDto> items { get; set; }
    }

    public class LibraryV4CommitReq {
        public string sync_id { get; set; }
        public string result_snapshot_hash { get; set; }
        public int chunk_count { get; set; }
        public int item_count { get; set; }
    }

    public class AchievementsV4FullSyncBeginReq {
        public int expected_total_items { get; set; }
        public string result_snapshot_hash { get; set; }
    }

    public class AchievementsV4ChunkReq {
        public string sync_id { get; set; }
        public int chunk_index { get; set; }
        public List<GameAchievementsDto> items { get; set; }
    }

    public class AchievementsV4CommitReq {
        public string sync_id { get; set; }
        public string result_snapshot_hash { get; set; }
        public int chunk_count { get; set; }
        public int item_count { get; set; }
    }

    public class V4SyncAbortReq {
        public string sync_id { get; set; }
    }

    public class V4SyncBeginRes {
        public bool success { get; set; }
        public string status { get; set; }
        public string sync_id { get; set; }
        public int max_chunk_items { get; set; }
        public string expires_at { get; set; }
        public string timestamp { get; set; }
        public string error { get; set; }
        public string reason { get; set; }
        public string message { get; set; }
    }

    public class V4SyncChunkRes {
        public bool success { get; set; }
        public string status { get; set; }
        public string sync_id { get; set; }
        public int chunk_index { get; set; }
        public int items_accepted { get; set; }
        public int max_chunk_items { get; set; }
        public string timestamp { get; set; }
        public string error { get; set; }
        public string message { get; set; }
    }

    public class LibraryDiffSyncReq {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string user_id { get; set; }
        public List<GameSyncDto> added { get; set; }
        public List<GameSyncDto> updated { get; set; }
        public List<string> removed { get; set; }
        /// <summary>Hash of the library *before* this diff (the previous synced baseline).</summary>
        public string base_snapshot_hash { get; set; }
        /// <summary>
        /// Hash of the library *after* this diff (the current full-library state).
        /// The server stores this verbatim as the next baseline instead of
        /// reconstructing it from persisted rows — exact even for games the
        /// server never persists (e.g. Humble bundle extras). Old plugins omit
        /// this field and fall back to server-side reconstruction.
        /// </summary>
        public string result_snapshot_hash { get; set; }
        public string[] flags { get; set; }
        public List<Services.IntegrationAccountDto> integration_accounts { get; set; }
    }

    // ──────────────────────────────────────────────────────────
    // Achievement DTOs
    // ──────────────────────────────────────────────────────────

    public class AchievementItemDto {
        public string name { get; set; }
        public string description { get; set; }
        public DateTime? date_unlocked { get; set; }
        public bool is_unlocked { get; set; }
        public float? rarity_percent { get; set; }
    }

    public class GameAchievementsDto {
        public string playnite_id { get; set; }
        public string game_id { get; set; }
        public string plugin_id { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string source_name { get; set; }
        public List<AchievementItemDto> achievements { get; set; }
    }

    public class AchievementsFullSyncReq {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string user_id { get; set; }
        public List<GameAchievementsDto> games { get; set; }
    }

    public class AchievementsDiffSyncReq {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string user_id { get; set; }
        public List<GameAchievementsDto> changed { get; set; }
        public string base_snapshot_hash { get; set; }
        /// <summary>
        /// Hash of the achievement snapshot *after* this diff. Stored verbatim
        /// as the next server baseline (mirrors library diff sync).
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string result_snapshot_hash { get; set; }
    }

    // ──────────────────────────────────────────────────────────
    // Install Token / Registration DTOs
    // ──────────────────────────────────────────────────────────

    public class RegisterInstallTokenReq {
        public string playnite_user_id { get; set; }
    }

    public class RegisterInstallTokenRes {
        public bool success { get; set; }
        public string token { get; set; }
        public string message { get; set; }
        public string error { get; set; }
        public string error_code { get; set; }
    }

    public class DashboardTokenRes {
        public bool success { get; set; }
        public string token { get; set; }
        public int? expires_in { get; set; }
    }

    // ──────────────────────────────────────────────────────────
    // Allowed Plugins DTOs
    // ──────────────────────────────────────────────────────────

    public class AllowedPluginsRes {
        public int schemaVersion { get; set; }
        public bool supportsSourceAliases { get; set; }
        public List<AllowedPluginEntry> plugins { get; set; }
        public string source { get; set; }
    }

    public class AllowedPluginEntry {
        public string pluginId { get; set; }
        public string libraryName { get; set; }
        public string sourceSlug { get; set; }
        public string status { get; set; }
        public List<string> sourceAliases { get; set; }
    }

    // ──────────────────────────────────────────────────────────
    // Token Verification DTOs
    // ──────────────────────────────────────────────────────────

    public class TokenVerificationReq {
        public string token { get; set; }
        public string playniteId { get; set; }
    }

    public class TokenVerificationRes {
        public bool success { get; set; }
        public string message { get; set; }
        public string userId { get; set; }
        public string error { get; set; }
        public string errorCode { get; set; }
    }

    // ──────────────────────────────────────────────────────────
    // Account Unlinking DTOs
    // ──────────────────────────────────────────────────────────

    public class UnlinkRes {
        public bool success { get; set; }
        public string error { get; set; }
    }

    // ──────────────────────────────────────────────────────────
    // Data Deletion DTOs
    // ──────────────────────────────────────────────────────────

    public class DeleteDataReq {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string user_id { get; set; }
    }

    public class DeleteDataRes {
        public bool success { get; set; }
        public string message { get; set; }
        public bool rateLimited { get; set; }

        /// <summary>
        /// Server resolved the install as already opted out (HTTP 403). The data is
        /// already gone — the client should sync its local opt-out state rather than
        /// treat this as a retryable failure.
        /// </summary>
        [JsonIgnore]
        public bool alreadyOptedOut { get; set; }

        /// <summary>
        /// Server rejected the install token (HTTP 401). The stored token does not
        /// resolve to an install, so retrying with the same token cannot succeed —
        /// the user needs to reconnect rather than keep hitting the same wall.
        /// </summary>
        [JsonIgnore]
        public bool authFailed { get; set; }
    }

    public class OptInReq {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string user_id { get; set; }
    }

    public class OptInRes {
        public bool success { get; set; }
        public string message { get; set; }
        public bool rateLimited { get; set; }
    }

    // ──────────────────────────────────────────────────────────
    // Notification DTOs
    // ──────────────────────────────────────────────────────────

    public class PlayniteNotificationDto {
        public string id { get; set; }
        public string title { get; set; }
        public string message { get; set; }
        public string notification_type { get; set; }
        public string priority { get; set; }
        public string action_url { get; set; }
        public string action_label { get; set; }
        public string created_at { get; set; }
    }

    public class PlayniteNotificationsRes {
        public bool success { get; set; }
        public List<PlayniteNotificationDto> notifications { get; set; }
    }
}
