using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

#pragma warning disable CS8618 // JSON DTOs: properties are populated by the deserializer, not constructors

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
        public object metadata { get; set; }
        public string started_at { get; set; }
    }

    // ──────────────────────────────────────────────────────────
    // v3 standard API envelope
    // ──────────────────────────────────────────────────────────

    public enum ApiOutcome {
        Success,
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
        public object metadata { get; set; }
        public string finished_at { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string session_id { get; set; }
    }

    public class ScrobbleFinishRes { }

    // ──────────────────────────────────────────────────────────
    // Library Sync DTOs
    // ──────────────────────────────────────────────────────────

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
        public List<string> genres { get; set; }
        public List<string> platforms { get; set; }
        public List<string> developers { get; set; }
        public List<string> publishers { get; set; }
        public List<string> tags { get; set; }
        public List<string> features { get; set; }
        public List<string> categories { get; set; }
        public List<string> series { get; set; }
        public int? user_score { get; set; }
        public int? critic_score { get; set; }
        public int? community_score { get; set; }
        public int? release_year { get; set; }
        public DateTime? date_added { get; set; }
        public bool is_favorite { get; set; }
        public bool is_hidden { get; set; }
        public string source_name { get; set; }
        public string release_date { get; set; }
        public DateTime? modified { get; set; }
        public List<string> age_ratings { get; set; }
        public List<string> regions { get; set; }
    }

    public class LibraryFullSyncReq {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string user_id { get; set; }
        public List<GameSyncDto> library { get; set; }
        public string[] flags { get; set; }
        public List<Services.IntegrationAccountDto> integration_accounts { get; set; }
    }

    public class LibraryDiffSyncReq {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string user_id { get; set; }
        public List<GameSyncDto> added { get; set; }
        public List<GameSyncDto> updated { get; set; }
        public List<string> removed { get; set; }
        public string base_snapshot_hash { get; set; }
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
    }

    public class AchievementSyncRes {
        public bool success { get; set; }
        public string status { get; set; }
        public string reason { get; set; }
        public string message { get; set; }
        public string timestamp { get; set; }
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
        public List<AllowedPluginEntry> plugins { get; set; }
        public string source { get; set; }
    }

    public class AllowedPluginEntry {
        public string pluginId { get; set; }
        public string libraryName { get; set; }
        public string sourceSlug { get; set; }
        public string status { get; set; }
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
