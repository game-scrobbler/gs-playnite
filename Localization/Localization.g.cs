namespace Playnite;

public static partial class Loc
{

    /// <summary>
    /// Installation ID
    /// </summary>
    public static string install_id_section_title()
    {
        return GetString("install_id_section_title");
    }
    /// <summary>
    /// Your unique plugin installation identifier, used to link your account.
    /// </summary>
    public static string install_id_description()
    {
        return GetString("install_id_description");
    }
    /// <summary>
    /// Click the ID above to copy it to your clipboard.
    /// </summary>
    public static string install_id_copy_hint()
    {
        return GetString("install_id_copy_hint");
    }
    /// <summary>
    /// Click to copy ID to clipboard
    /// </summary>
    public static string install_id_copy_tooltip()
    {
        return GetString("install_id_copy_tooltip");
    }
    /// <summary>
    /// Theme & Dashboard
    /// </summary>
    public static string theme_section_title()
    {
        return GetString("theme_section_title");
    }
    /// <summary>
    /// Controls the visual theme applied to the Game Scrobbler sidebar and views.
    /// </summary>
    public static string theme_description()
    {
        return GetString("theme_description");
    }
    /// <summary>
    /// New Dashboard Experience
    /// </summary>
    public static string new_dashboard_experience_label()
    {
        return GetString("new_dashboard_experience_label");
    }
    /// <summary>
    /// Enable the new dashboard experience with enhanced features
    /// </summary>
    public static string new_dashboard_experience_tooltip()
    {
        return GetString("new_dashboard_experience_tooltip");
    }
    /// <summary>
    /// Account Linking
    /// </summary>
    public static string account_linking_section_title()
    {
        return GetString("account_linking_section_title");
    }
    /// <summary>
    /// Link Account on Website
    /// </summary>
    public static string link_on_website_button()
    {
        return GetString("link_on_website_button");
    }
    /// <summary>
    /// or enter your token manually
    /// </summary>
    public static string manual_token_separator()
    {
        return GetString("manual_token_separator");
    }
    /// <summary>
    /// Link Account
    /// </summary>
    public static string link_account_button()
    {
        return GetString("link_account_button");
    }
    /// <summary>
    /// Disconnect Account
    /// </summary>
    public static string disconnect_account_button()
    {
        return GetString("disconnect_account_button");
    }
    /// <summary>
    /// Disconnects your account from GameScrobbler. Your game data is kept on the server.
    /// </summary>
    public static string disconnect_account_tooltip()
    {
        return GetString("disconnect_account_tooltip");
    }
    /// <summary>
    /// Disconnect Account
    /// </summary>
    public static string disconnect_dialog_title()
    {
        return GetString("disconnect_dialog_title");
    }
    /// <summary>
    /// Disconnect your account? Your game data will be kept on the server.
    /// You can re-link anytime.
    /// </summary>
    public static string disconnect_dialog_body()
    {
        return GetString("disconnect_dialog_body");
    }
    /// <summary>
    /// No account is currently linked.
    /// </summary>
    public static string disconnect_no_account()
    {
        return GetString("disconnect_no_account");
    }
    /// <summary>
    /// Account disconnected.
    /// </summary>
    public static string status_disconnected()
    {
        return GetString("status_disconnected");
    }
    /// <summary>
    /// Token expired — click "Open website to link" to get a new one.
    /// </summary>
    public static string status_token_expired()
    {
        return GetString("status_token_expired");
    }
    /// <summary>
    /// Token expires in ~{$minutes}:{$seconds}
    /// </summary>
    public static string token_countdown_format(object minutes, object seconds)
    {
        return GetString("token_countdown_format", ("minutes", minutes), ("seconds", seconds));
    }
    /// <summary>
    /// Token Expired
    /// </summary>
    public static string token_expired_dialog_title()
    {
        return GetString("token_expired_dialog_title");
    }
    /// <summary>
    /// Your link token has expired.
    /// 
    /// Would you like to open the linking page to get a new one?
    /// </summary>
    public static string token_expired_dialog_body()
    {
        return GetString("token_expired_dialog_body");
    }
    /// <summary>
    /// Account Linking Success
    /// </summary>
    public static string link_success_dialog_title()
    {
        return GetString("link_success_dialog_title");
    }
    /// <summary>
    /// Account successfully linked!
    /// User ID: {$userId}
    /// </summary>
    public static string link_success_dialog_body(object userId)
    {
        return GetString("link_success_dialog_body", ("userId", userId));
    }
    /// <summary>
    /// Account Linking Failed
    /// </summary>
    public static string link_failed_dialog_title()
    {
        return GetString("link_failed_dialog_title");
    }
    /// <summary>
    /// Account Linking Failed — Retry?
    /// </summary>
    public static string link_failed_retry_dialog_title()
    {
        return GetString("link_failed_retry_dialog_title");
    }
    /// <summary>
    /// Account linking failed after multiple attempts:
    /// {$error}
    /// 
    /// Please try again later.
    /// </summary>
    public static string link_failed_retries_body(object error)
    {
        return GetString("link_failed_retries_body", ("error", error));
    }
    /// <summary>
    /// Account linking failed due to a network error:
    /// {$error}
    /// 
    /// Would you like to retry?
    /// </summary>
    public static string link_failed_network_body(object error)
    {
        return GetString("link_failed_network_body", ("error", error));
    }
    /// <summary>
    /// Account linking failed: {$error}
    /// </summary>
    public static string link_failed_body(object error)
    {
        return GetString("link_failed_body", ("error", error));
    }
    /// <summary>
    /// Sync Settings
    /// </summary>
    public static string sync_settings_section_title()
    {
        return GetString("sync_settings_section_title");
    }
    /// <summary>
    /// Library Sync:
    /// </summary>
    public static string library_sync_label()
    {
        return GetString("library_sync_label");
    }
    /// <summary>
    /// Notifications
    /// </summary>
    public static string notifications_section_title()
    {
        return GetString("notifications_section_title");
    }
    /// <summary>
    /// Show version update notifications
    /// </summary>
    public static string show_update_notifications_label()
    {
        return GetString("show_update_notifications_label");
    }
    /// <summary>
    /// Show important notifications from GameScrobbler
    /// </summary>
    public static string show_important_notifications_label()
    {
        return GetString("show_important_notifications_label");
    }
    /// <summary>
    /// Privacy Controls
    /// </summary>
    public static string privacy_section_title()
    {
        return GetString("privacy_section_title");
    }
    /// <summary>
    /// Disable error reporting (Sentry)
    /// </summary>
    public static string disable_sentry_label()
    {
        return GetString("disable_sentry_label");
    }
    /// <summary>
    /// Disable analytics (PostHog)
    /// </summary>
    public static string disable_posthog_label()
    {
        return GetString("disable_posthog_label");
    }
    /// <summary>
    /// Disable game activity tracking (Scrobbling)
    /// </summary>
    public static string disable_scrobbling_label()
    {
        return GetString("disable_scrobbling_label");
    }
    /// <summary>
    /// You will not be able to benefit from features that rely on session data
    /// </summary>
    public static string scrobbling_disabled_warning()
    {
        return GetString("scrobbling_disabled_warning");
    }
    /// <summary>
    /// Danger Zone
    /// </summary>
    public static string danger_zone_section_title()
    {
        return GetString("danger_zone_section_title");
    }
    /// <summary>
    /// Please only use this option if you want to delete your Playnite data in the GameScrobbler servers. After your delete request you cannot continue using the GameScrobbler addon without opting in again.
    /// </summary>
    public static string danger_zone_warning()
    {
        return GetString("danger_zone_warning");
    }
    /// <summary>
    /// Delete My Data
    /// </summary>
    public static string delete_my_data_button()
    {
        return GetString("delete_my_data_button");
    }
    /// <summary>
    /// Opt Back In
    /// </summary>
    public static string opt_back_in_button()
    {
        return GetString("opt_back_in_button");
    }
    /// <summary>
    /// Link your plugin to your GS account to own and persist your data. Visit
    /// </summary>
    public static string account_linking_hint_prefix()
    {
        return GetString("account_linking_hint_prefix");
    }
    /// <summary>
    /// to get started.
    /// </summary>
    public static string account_linking_hint_suffix()
    {
        return GetString("account_linking_hint_suffix");
    }
    /// <summary>
    /// Status:
    /// </summary>
    public static string status_label()
    {
        return GetString("status_label");
    }
    /// <summary>
    /// Connected to GameScrobbler
    /// </summary>
    public static string connection_status_connected()
    {
        return GetString("connection_status_connected");
    }
    /// <summary>
    /// Disconnected
    /// </summary>
    public static string connection_status_disconnected()
    {
        return GetString("connection_status_disconnected");
    }
    /// <summary>
    /// Opted Out
    /// </summary>
    public static string connection_status_opted_out()
    {
        return GetString("connection_status_opted_out");
    }
    /// <summary>
    /// Linking...
    /// </summary>
    public static string linking_in_progress()
    {
        return GetString("linking_in_progress");
    }
    /// <summary>
    /// Opens gamescrobbler.com in your browser to link your account automatically
    /// </summary>
    public static string open_website_to_link_tooltip()
    {
        return GetString("open_website_to_link_tooltip");
    }
    /// <summary>
    /// Enter your account linking token here
    /// </summary>
    public static string token_input_tooltip()
    {
        return GetString("token_input_tooltip");
    }
    /// <summary>
    /// ✓ Token: Active
    /// </summary>
    public static string token_status_active()
    {
        return GetString("token_status_active");
    }
    /// <summary>
    /// ⚠ Token: Pending registration
    /// </summary>
    public static string token_status_pending()
    {
        return GetString("token_status_pending");
    }
    /// <summary>
    /// {$count} scrobbles queued — will retry automatically
    /// </summary>
    public static string pending_scrobbles_format(object count)
    {
        return GetString("pending_scrobbles_format", ("count", count));
    }
    /// <summary>
    /// {$count} scrobbles lost due to server errors
    /// </summary>
    public static string dropped_scrobbles_format(object count)
    {
        return GetString("dropped_scrobbles_format", ("count", count));
    }
    /// <summary>
    /// Never synced
    /// </summary>
    public static string never_synced()
    {
        return GetString("never_synced");
    }
    /// <summary>
    /// Last synced: {$count} games · {$time}
    /// </summary>
    public static string last_synced_format(object count, object time)
    {
        return GetString("last_synced_format", ("count", count), ("time", time));
    }
    /// <summary>
    /// just now
    /// </summary>
    public static string elapsed_just_now()
    {
        return GetString("elapsed_just_now");
    }
    /// <summary>
    /// {$count} minutes ago
    /// </summary>
    public static string elapsed_minutes_format(object count)
    {
        return GetString("elapsed_minutes_format", ("count", count));
    }
    /// <summary>
    /// {$count} hours ago
    /// </summary>
    public static string elapsed_hours_format(object count)
    {
        return GetString("elapsed_hours_format", ("count", count));
    }
    /// <summary>
    /// {$count} days ago
    /// </summary>
    public static string elapsed_days_format(object count)
    {
        return GetString("elapsed_days_format", ("count", count));
    }
    /// <summary>
    /// less than a minute
    /// </summary>
    public static string remaining_less_than_minute()
    {
        return GetString("remaining_less_than_minute");
    }
    /// <summary>
    /// {$count} minutes
    /// </summary>
    public static string remaining_minutes_format(object count)
    {
        return GetString("remaining_minutes_format", ("count", count));
    }
    /// <summary>
    /// {$hours} hours {$minutes} minutes
    /// </summary>
    public static string remaining_hours_minutes_format(object hours, object minutes)
    {
        return GetString("remaining_hours_minutes_format", ("hours", hours), ("minutes", minutes));
    }
    /// <summary>
    /// {$count} hours
    /// </summary>
    public static string remaining_hours_format(object count)
    {
        return GetString("remaining_hours_format", ("count", count));
    }
    /// <summary>
    /// Show a notification in Playnite when a new plugin version is available
    /// </summary>
    public static string show_update_notifications_tooltip()
    {
        return GetString("show_update_notifications_tooltip");
    }
    /// <summary>
    /// Show notifications about important updates and announcements
    /// </summary>
    public static string show_important_notifications_tooltip()
    {
        return GetString("show_important_notifications_tooltip");
    }
    /// <summary>
    /// Prevents sending crash reports and diagnostic data to improve the plugin
    /// </summary>
    public static string disable_sentry_tooltip()
    {
        return GetString("disable_sentry_tooltip");
    }
    /// <summary>
    /// Prevents sending anonymous usage analytics to help improve the plugin
    /// </summary>
    public static string disable_posthog_tooltip()
    {
        return GetString("disable_posthog_tooltip");
    }
    /// <summary>
    /// Prevents scrobbling of your game playing activity
    /// </summary>
    public static string disable_scrobbling_tooltip()
    {
        return GetString("disable_scrobbling_tooltip");
    }
    /// <summary>
    /// Re-enable the plugin and resume syncing
    /// </summary>
    public static string opt_back_in_tooltip()
    {
        return GetString("opt_back_in_tooltip");
    }
    /// <summary>
    /// Text copied to clipboard!
    /// </summary>
    public static string copied_to_clipboard()
    {
        return GetString("copied_to_clipboard");
    }
    /// <summary>
    /// Success
    /// </summary>
    public static string copied_dialog_title()
    {
        return GetString("copied_dialog_title");
    }
    /// <summary>
    /// Failed to copy text: {$error}
    /// </summary>
    public static string copy_failed_format(object error)
    {
        return GetString("copy_failed_format", ("error", error));
    }
    /// <summary>
    /// Failed to open URL: {$error}
    /// </summary>
    public static string open_url_failed_format(object error)
    {
        return GetString("open_url_failed_format", ("error", error));
    }
    /// <summary>
    /// Error
    /// </summary>
    public static string error_dialog_title()
    {
        return GetString("error_dialog_title");
    }
    /// <summary>
    /// Are you sure you want to delete all your data from GameScrobbler servers?
    /// 
    /// This will:
    /// • Remove your library, sessions, and achievements from our servers
    /// • Disable all plugin features
    /// • Require you to opt in again to resume using the plugin
    /// 
    /// This action cannot be undone.
    /// </summary>
    public static string delete_confirm_body()
    {
        return GetString("delete_confirm_body");
    }
    /// <summary>
    /// Delete My Data
    /// </summary>
    public static string delete_confirm_title()
    {
        return GetString("delete_confirm_title");
    }
    /// <summary>
    /// Are you absolutely sure? Your data will be permanently deleted from the GameScrobbler servers.
    /// </summary>
    public static string delete_final_body()
    {
        return GetString("delete_final_body");
    }
    /// <summary>
    /// Final Confirmation
    /// </summary>
    public static string delete_final_title()
    {
        return GetString("delete_final_title");
    }
    /// <summary>
    /// Deleting...
    /// </summary>
    public static string deleting_in_progress()
    {
        return GetString("deleting_in_progress");
    }
    /// <summary>
    /// Re-enable the GameScrobbler plugin?
    /// 
    /// You will need to restart Playnite for all features to resume.
    /// </summary>
    public static string opt_back_in_confirm_body()
    {
        return GetString("opt_back_in_confirm_body");
    }
    /// <summary>
    /// Opt Back In
    /// </summary>
    public static string opt_back_in_confirm_title()
    {
        return GetString("opt_back_in_confirm_title");
    }
    /// <summary>
    /// Please enter a token
    /// </summary>
    public static string please_enter_token()
    {
        return GetString("please_enter_token");
    }
    /// <summary>
    /// Verifying token...
    /// </summary>
    public static string verifying_token()
    {
        return GetString("verifying_token");
    }
    /// <summary>
    /// Successfully linked account!
    /// </summary>
    public static string link_success()
    {
        return GetString("link_success");
    }
    /// <summary>
    /// {$error} Click "Link Account" to retry.
    /// </summary>
    public static string network_error_retry_format(object error)
    {
        return GetString("network_error_retry_format", ("error", error));
    }
    /// <summary>
    /// Error: {$error} Click "Link Account" to retry.
    /// </summary>
    public static string error_retry_format(object error)
    {
        return GetString("error_retry_format", ("error", error));
    }
    /// <summary>
    /// Error: {$error}
    /// </summary>
    public static string error_format(object error)
    {
        return GetString("error_format", ("error", error));
    }
    /// <summary>
    /// Unknown error occurred during linking
    /// </summary>
    public static string unknown_linking_error()
    {
        return GetString("unknown_linking_error");
    }
    /// <summary>
    /// Error during linking: {$error}
    /// </summary>
    public static string linking_error_format(object error)
    {
        return GetString("linking_error_format", ("error", error));
    }
    /// <summary>
    /// Network error — could not reach the server. Please try again.
    /// </summary>
    public static string network_error()
    {
        return GetString("network_error");
    }
    /// <summary>
    /// Failed to disconnect account.
    /// </summary>
    public static string unlink_failed()
    {
        return GetString("unlink_failed");
    }
    /// <summary>
    /// Account is already linked to User ID: {$userId}
    /// 
    /// Do you want to link to a different account?
    /// </summary>
    public static string already_linked_body(object userId)
    {
        return GetString("already_linked_body", ("userId", userId));
    }
    /// <summary>
    /// Account Already Linked
    /// </summary>
    public static string already_linked_title()
    {
        return GetString("already_linked_title");
    }
    /// <summary>
    /// Requesting data deletion...
    /// </summary>
    public static string deleting_requesting()
    {
        return GetString("deleting_requesting");
    }
    /// <summary>
    /// Your data has been deleted. The plugin is now disabled.
    /// </summary>
    public static string delete_success()
    {
        return GetString("delete_success");
    }
    /// <summary>
    /// Too many deletion requests. Please wait 15 minutes and try again.
    /// </summary>
    public static string delete_rate_limited()
    {
        return GetString("delete_rate_limited");
    }
    /// <summary>
    /// Failed to request data deletion. Please try again later.
    /// </summary>
    public static string delete_failed()
    {
        return GetString("delete_failed");
    }
    /// <summary>
    /// An error occurred. Please try again later.
    /// </summary>
    public static string delete_error()
    {
        return GetString("delete_error");
    }
    /// <summary>
    /// Plugin re-enabled. Please restart Playnite to resume syncing.
    /// </summary>
    public static string opt_back_in_success()
    {
        return GetString("opt_back_in_success");
    }
    /// <summary>
    /// Too many attempts. Please wait and try again.
    /// </summary>
    public static string opt_back_in_rate_limited()
    {
        return GetString("opt_back_in_rate_limited");
    }
    /// <summary>
    /// Failed to re-enable. Please restart Playnite to try again.
    /// </summary>
    public static string opt_back_in_failed()
    {
        return GetString("opt_back_in_failed");
    }
    /// <summary>
    /// Invalid linking token received.
    /// </summary>
    public static string invalid_linking_token()
    {
        return GetString("invalid_linking_token");
    }
    /// <summary>
    /// Unexpected error processing URI request: {$error}
    /// </summary>
    public static string unexpected_uri_error_format(object error)
    {
        return GetString("unexpected_uri_error_format", ("error", error));
    }
    /// <summary>
    /// Library sync completed.
    /// </summary>
    public static string sync_completed()
    {
        return GetString("sync_completed");
    }
    /// <summary>
    /// Library is already up to date.
    /// </summary>
    public static string sync_up_to_date()
    {
        return GetString("sync_up_to_date");
    }
    /// <summary>
    /// Library was already synced recently. Try again in {$time}.
    /// </summary>
    public static string sync_cooldown_format(object time)
    {
        return GetString("sync_cooldown_format", ("time", time));
    }
    /// <summary>
    /// Library was already synced recently. Please try again later.
    /// </summary>
    public static string sync_cooldown_generic()
    {
        return GetString("sync_cooldown_generic");
    }
    /// <summary>
    /// Library sync failed. Check logs for details.
    /// </summary>
    public static string sync_failed()
    {
        return GetString("sync_failed");
    }
    /// <summary>
    /// Library sync encountered an error.
    /// </summary>
    public static string sync_error()
    {
        return GetString("sync_error");
    }
    /// <summary>
    /// Sync Library Now
    /// </summary>
    public static string menu_sync_library()
    {
        return GetString("menu_sync_library");
    }
    /// <summary>
    /// Open Settings
    /// </summary>
    public static string menu_open_settings()
    {
        return GetString("menu_open_settings");
    }
    /// <summary>
    /// Confirm Account Linking
    /// </summary>
    public static string confirm_linking_title()
    {
        return GetString("confirm_linking_title");
    }
    /// <summary>
    /// A website is requesting to link this Playnite installation to a GameScrobbler account.
    /// 
    /// Only continue if you just clicked "Link Playnite" on gamescrobbler.com yourself.
    /// </summary>
    public static string confirm_linking_body()
    {
        return GetString("confirm_linking_body");
    }
    /// <summary>
    /// Invalid user ID format received from server
    /// </summary>
    public static string invalid_user_id_format()
    {
        return GetString("invalid_user_id_format");
    }
    /// <summary>
    /// The installation changed or was disabled during this request. Please try again.
    /// </summary>
    public static string identity_changed_during_request()
    {
        return GetString("identity_changed_during_request");
    }
    /// <summary>
    /// Couldn't reach GameScrobbler to authorize the deletion. Check your internet connection and try again.
    /// </summary>
    public static string delete_no_token()
    {
        return GetString("delete_no_token");
    }
    /// <summary>
    /// Your data has already been deleted. The plugin is now disabled.
    /// </summary>
    public static string delete_already_done()
    {
        return GetString("delete_already_done");
    }
    /// <summary>
    /// This installation couldn't be verified. Reconnect your account, then try deleting again.
    /// </summary>
    public static string delete_auth_failed()
    {
        return GetString("delete_auth_failed");
    }
    /// <summary>
    /// Game Scrobbler could not read its saved data and is disabled for this session. The file was left untouched at {$path} so it can be repaired or removed.
    /// </summary>
    public static string data_unreadable(object path)
    {
        return GetString("data_unreadable", ("path", path));
    }
    /// <summary>
    /// Game Scrobbler could not open a private browser profile for the dashboard, so it was not loaded. Restart Playnite to try again.
    /// </summary>
    public static string dashboard_profile_failed()
    {
        return GetString("dashboard_profile_failed");
    }
}

public static partial class LocId
{

    /// <summary>
    /// Installation ID
    /// </summary>
    public const string install_id_section_title = "install_id_section_title";
    /// <summary>
    /// Your unique plugin installation identifier, used to link your account.
    /// </summary>
    public const string install_id_description = "install_id_description";
    /// <summary>
    /// Click the ID above to copy it to your clipboard.
    /// </summary>
    public const string install_id_copy_hint = "install_id_copy_hint";
    /// <summary>
    /// Click to copy ID to clipboard
    /// </summary>
    public const string install_id_copy_tooltip = "install_id_copy_tooltip";
    /// <summary>
    /// Theme & Dashboard
    /// </summary>
    public const string theme_section_title = "theme_section_title";
    /// <summary>
    /// Controls the visual theme applied to the Game Scrobbler sidebar and views.
    /// </summary>
    public const string theme_description = "theme_description";
    /// <summary>
    /// New Dashboard Experience
    /// </summary>
    public const string new_dashboard_experience_label = "new_dashboard_experience_label";
    /// <summary>
    /// Enable the new dashboard experience with enhanced features
    /// </summary>
    public const string new_dashboard_experience_tooltip = "new_dashboard_experience_tooltip";
    /// <summary>
    /// Account Linking
    /// </summary>
    public const string account_linking_section_title = "account_linking_section_title";
    /// <summary>
    /// Link Account on Website
    /// </summary>
    public const string link_on_website_button = "link_on_website_button";
    /// <summary>
    /// or enter your token manually
    /// </summary>
    public const string manual_token_separator = "manual_token_separator";
    /// <summary>
    /// Link Account
    /// </summary>
    public const string link_account_button = "link_account_button";
    /// <summary>
    /// Disconnect Account
    /// </summary>
    public const string disconnect_account_button = "disconnect_account_button";
    /// <summary>
    /// Disconnects your account from GameScrobbler. Your game data is kept on the server.
    /// </summary>
    public const string disconnect_account_tooltip = "disconnect_account_tooltip";
    /// <summary>
    /// Disconnect Account
    /// </summary>
    public const string disconnect_dialog_title = "disconnect_dialog_title";
    /// <summary>
    /// Disconnect your account? Your game data will be kept on the server.
    /// You can re-link anytime.
    /// </summary>
    public const string disconnect_dialog_body = "disconnect_dialog_body";
    /// <summary>
    /// No account is currently linked.
    /// </summary>
    public const string disconnect_no_account = "disconnect_no_account";
    /// <summary>
    /// Account disconnected.
    /// </summary>
    public const string status_disconnected = "status_disconnected";
    /// <summary>
    /// Token expired — click "Open website to link" to get a new one.
    /// </summary>
    public const string status_token_expired = "status_token_expired";
    /// <summary>
    /// Token expires in ~{$minutes}:{$seconds}
    /// </summary>
    public const string token_countdown_format = "token_countdown_format";
    /// <summary>
    /// Token Expired
    /// </summary>
    public const string token_expired_dialog_title = "token_expired_dialog_title";
    /// <summary>
    /// Your link token has expired.
    /// 
    /// Would you like to open the linking page to get a new one?
    /// </summary>
    public const string token_expired_dialog_body = "token_expired_dialog_body";
    /// <summary>
    /// Account Linking Success
    /// </summary>
    public const string link_success_dialog_title = "link_success_dialog_title";
    /// <summary>
    /// Account successfully linked!
    /// User ID: {$userId}
    /// </summary>
    public const string link_success_dialog_body = "link_success_dialog_body";
    /// <summary>
    /// Account Linking Failed
    /// </summary>
    public const string link_failed_dialog_title = "link_failed_dialog_title";
    /// <summary>
    /// Account Linking Failed — Retry?
    /// </summary>
    public const string link_failed_retry_dialog_title = "link_failed_retry_dialog_title";
    /// <summary>
    /// Account linking failed after multiple attempts:
    /// {$error}
    /// 
    /// Please try again later.
    /// </summary>
    public const string link_failed_retries_body = "link_failed_retries_body";
    /// <summary>
    /// Account linking failed due to a network error:
    /// {$error}
    /// 
    /// Would you like to retry?
    /// </summary>
    public const string link_failed_network_body = "link_failed_network_body";
    /// <summary>
    /// Account linking failed: {$error}
    /// </summary>
    public const string link_failed_body = "link_failed_body";
    /// <summary>
    /// Sync Settings
    /// </summary>
    public const string sync_settings_section_title = "sync_settings_section_title";
    /// <summary>
    /// Library Sync:
    /// </summary>
    public const string library_sync_label = "library_sync_label";
    /// <summary>
    /// Notifications
    /// </summary>
    public const string notifications_section_title = "notifications_section_title";
    /// <summary>
    /// Show version update notifications
    /// </summary>
    public const string show_update_notifications_label = "show_update_notifications_label";
    /// <summary>
    /// Show important notifications from GameScrobbler
    /// </summary>
    public const string show_important_notifications_label = "show_important_notifications_label";
    /// <summary>
    /// Privacy Controls
    /// </summary>
    public const string privacy_section_title = "privacy_section_title";
    /// <summary>
    /// Disable error reporting (Sentry)
    /// </summary>
    public const string disable_sentry_label = "disable_sentry_label";
    /// <summary>
    /// Disable analytics (PostHog)
    /// </summary>
    public const string disable_posthog_label = "disable_posthog_label";
    /// <summary>
    /// Disable game activity tracking (Scrobbling)
    /// </summary>
    public const string disable_scrobbling_label = "disable_scrobbling_label";
    /// <summary>
    /// You will not be able to benefit from features that rely on session data
    /// </summary>
    public const string scrobbling_disabled_warning = "scrobbling_disabled_warning";
    /// <summary>
    /// Danger Zone
    /// </summary>
    public const string danger_zone_section_title = "danger_zone_section_title";
    /// <summary>
    /// Please only use this option if you want to delete your Playnite data in the GameScrobbler servers. After your delete request you cannot continue using the GameScrobbler addon without opting in again.
    /// </summary>
    public const string danger_zone_warning = "danger_zone_warning";
    /// <summary>
    /// Delete My Data
    /// </summary>
    public const string delete_my_data_button = "delete_my_data_button";
    /// <summary>
    /// Opt Back In
    /// </summary>
    public const string opt_back_in_button = "opt_back_in_button";
    /// <summary>
    /// Link your plugin to your GS account to own and persist your data. Visit
    /// </summary>
    public const string account_linking_hint_prefix = "account_linking_hint_prefix";
    /// <summary>
    /// to get started.
    /// </summary>
    public const string account_linking_hint_suffix = "account_linking_hint_suffix";
    /// <summary>
    /// Status:
    /// </summary>
    public const string status_label = "status_label";
    /// <summary>
    /// Connected to GameScrobbler
    /// </summary>
    public const string connection_status_connected = "connection_status_connected";
    /// <summary>
    /// Disconnected
    /// </summary>
    public const string connection_status_disconnected = "connection_status_disconnected";
    /// <summary>
    /// Opted Out
    /// </summary>
    public const string connection_status_opted_out = "connection_status_opted_out";
    /// <summary>
    /// Linking...
    /// </summary>
    public const string linking_in_progress = "linking_in_progress";
    /// <summary>
    /// Opens gamescrobbler.com in your browser to link your account automatically
    /// </summary>
    public const string open_website_to_link_tooltip = "open_website_to_link_tooltip";
    /// <summary>
    /// Enter your account linking token here
    /// </summary>
    public const string token_input_tooltip = "token_input_tooltip";
    /// <summary>
    /// ✓ Token: Active
    /// </summary>
    public const string token_status_active = "token_status_active";
    /// <summary>
    /// ⚠ Token: Pending registration
    /// </summary>
    public const string token_status_pending = "token_status_pending";
    /// <summary>
    /// {$count} scrobbles queued — will retry automatically
    /// </summary>
    public const string pending_scrobbles_format = "pending_scrobbles_format";
    /// <summary>
    /// {$count} scrobbles lost due to server errors
    /// </summary>
    public const string dropped_scrobbles_format = "dropped_scrobbles_format";
    /// <summary>
    /// Never synced
    /// </summary>
    public const string never_synced = "never_synced";
    /// <summary>
    /// Last synced: {$count} games · {$time}
    /// </summary>
    public const string last_synced_format = "last_synced_format";
    /// <summary>
    /// just now
    /// </summary>
    public const string elapsed_just_now = "elapsed_just_now";
    /// <summary>
    /// {$count} minutes ago
    /// </summary>
    public const string elapsed_minutes_format = "elapsed_minutes_format";
    /// <summary>
    /// {$count} hours ago
    /// </summary>
    public const string elapsed_hours_format = "elapsed_hours_format";
    /// <summary>
    /// {$count} days ago
    /// </summary>
    public const string elapsed_days_format = "elapsed_days_format";
    /// <summary>
    /// less than a minute
    /// </summary>
    public const string remaining_less_than_minute = "remaining_less_than_minute";
    /// <summary>
    /// {$count} minutes
    /// </summary>
    public const string remaining_minutes_format = "remaining_minutes_format";
    /// <summary>
    /// {$hours} hours {$minutes} minutes
    /// </summary>
    public const string remaining_hours_minutes_format = "remaining_hours_minutes_format";
    /// <summary>
    /// {$count} hours
    /// </summary>
    public const string remaining_hours_format = "remaining_hours_format";
    /// <summary>
    /// Show a notification in Playnite when a new plugin version is available
    /// </summary>
    public const string show_update_notifications_tooltip = "show_update_notifications_tooltip";
    /// <summary>
    /// Show notifications about important updates and announcements
    /// </summary>
    public const string show_important_notifications_tooltip = "show_important_notifications_tooltip";
    /// <summary>
    /// Prevents sending crash reports and diagnostic data to improve the plugin
    /// </summary>
    public const string disable_sentry_tooltip = "disable_sentry_tooltip";
    /// <summary>
    /// Prevents sending anonymous usage analytics to help improve the plugin
    /// </summary>
    public const string disable_posthog_tooltip = "disable_posthog_tooltip";
    /// <summary>
    /// Prevents scrobbling of your game playing activity
    /// </summary>
    public const string disable_scrobbling_tooltip = "disable_scrobbling_tooltip";
    /// <summary>
    /// Re-enable the plugin and resume syncing
    /// </summary>
    public const string opt_back_in_tooltip = "opt_back_in_tooltip";
    /// <summary>
    /// Text copied to clipboard!
    /// </summary>
    public const string copied_to_clipboard = "copied_to_clipboard";
    /// <summary>
    /// Success
    /// </summary>
    public const string copied_dialog_title = "copied_dialog_title";
    /// <summary>
    /// Failed to copy text: {$error}
    /// </summary>
    public const string copy_failed_format = "copy_failed_format";
    /// <summary>
    /// Failed to open URL: {$error}
    /// </summary>
    public const string open_url_failed_format = "open_url_failed_format";
    /// <summary>
    /// Error
    /// </summary>
    public const string error_dialog_title = "error_dialog_title";
    /// <summary>
    /// Are you sure you want to delete all your data from GameScrobbler servers?
    /// 
    /// This will:
    /// • Remove your library, sessions, and achievements from our servers
    /// • Disable all plugin features
    /// • Require you to opt in again to resume using the plugin
    /// 
    /// This action cannot be undone.
    /// </summary>
    public const string delete_confirm_body = "delete_confirm_body";
    /// <summary>
    /// Delete My Data
    /// </summary>
    public const string delete_confirm_title = "delete_confirm_title";
    /// <summary>
    /// Are you absolutely sure? Your data will be permanently deleted from the GameScrobbler servers.
    /// </summary>
    public const string delete_final_body = "delete_final_body";
    /// <summary>
    /// Final Confirmation
    /// </summary>
    public const string delete_final_title = "delete_final_title";
    /// <summary>
    /// Deleting...
    /// </summary>
    public const string deleting_in_progress = "deleting_in_progress";
    /// <summary>
    /// Re-enable the GameScrobbler plugin?
    /// 
    /// You will need to restart Playnite for all features to resume.
    /// </summary>
    public const string opt_back_in_confirm_body = "opt_back_in_confirm_body";
    /// <summary>
    /// Opt Back In
    /// </summary>
    public const string opt_back_in_confirm_title = "opt_back_in_confirm_title";
    /// <summary>
    /// Please enter a token
    /// </summary>
    public const string please_enter_token = "please_enter_token";
    /// <summary>
    /// Verifying token...
    /// </summary>
    public const string verifying_token = "verifying_token";
    /// <summary>
    /// Successfully linked account!
    /// </summary>
    public const string link_success = "link_success";
    /// <summary>
    /// {$error} Click "Link Account" to retry.
    /// </summary>
    public const string network_error_retry_format = "network_error_retry_format";
    /// <summary>
    /// Error: {$error} Click "Link Account" to retry.
    /// </summary>
    public const string error_retry_format = "error_retry_format";
    /// <summary>
    /// Error: {$error}
    /// </summary>
    public const string error_format = "error_format";
    /// <summary>
    /// Unknown error occurred during linking
    /// </summary>
    public const string unknown_linking_error = "unknown_linking_error";
    /// <summary>
    /// Error during linking: {$error}
    /// </summary>
    public const string linking_error_format = "linking_error_format";
    /// <summary>
    /// Network error — could not reach the server. Please try again.
    /// </summary>
    public const string network_error = "network_error";
    /// <summary>
    /// Failed to disconnect account.
    /// </summary>
    public const string unlink_failed = "unlink_failed";
    /// <summary>
    /// Account is already linked to User ID: {$userId}
    /// 
    /// Do you want to link to a different account?
    /// </summary>
    public const string already_linked_body = "already_linked_body";
    /// <summary>
    /// Account Already Linked
    /// </summary>
    public const string already_linked_title = "already_linked_title";
    /// <summary>
    /// Requesting data deletion...
    /// </summary>
    public const string deleting_requesting = "deleting_requesting";
    /// <summary>
    /// Your data has been deleted. The plugin is now disabled.
    /// </summary>
    public const string delete_success = "delete_success";
    /// <summary>
    /// Too many deletion requests. Please wait 15 minutes and try again.
    /// </summary>
    public const string delete_rate_limited = "delete_rate_limited";
    /// <summary>
    /// Failed to request data deletion. Please try again later.
    /// </summary>
    public const string delete_failed = "delete_failed";
    /// <summary>
    /// An error occurred. Please try again later.
    /// </summary>
    public const string delete_error = "delete_error";
    /// <summary>
    /// Plugin re-enabled. Please restart Playnite to resume syncing.
    /// </summary>
    public const string opt_back_in_success = "opt_back_in_success";
    /// <summary>
    /// Too many attempts. Please wait and try again.
    /// </summary>
    public const string opt_back_in_rate_limited = "opt_back_in_rate_limited";
    /// <summary>
    /// Failed to re-enable. Please restart Playnite to try again.
    /// </summary>
    public const string opt_back_in_failed = "opt_back_in_failed";
    /// <summary>
    /// Invalid linking token received.
    /// </summary>
    public const string invalid_linking_token = "invalid_linking_token";
    /// <summary>
    /// Unexpected error processing URI request: {$error}
    /// </summary>
    public const string unexpected_uri_error_format = "unexpected_uri_error_format";
    /// <summary>
    /// Library sync completed.
    /// </summary>
    public const string sync_completed = "sync_completed";
    /// <summary>
    /// Library is already up to date.
    /// </summary>
    public const string sync_up_to_date = "sync_up_to_date";
    /// <summary>
    /// Library was already synced recently. Try again in {$time}.
    /// </summary>
    public const string sync_cooldown_format = "sync_cooldown_format";
    /// <summary>
    /// Library was already synced recently. Please try again later.
    /// </summary>
    public const string sync_cooldown_generic = "sync_cooldown_generic";
    /// <summary>
    /// Library sync failed. Check logs for details.
    /// </summary>
    public const string sync_failed = "sync_failed";
    /// <summary>
    /// Library sync encountered an error.
    /// </summary>
    public const string sync_error = "sync_error";
    /// <summary>
    /// Sync Library Now
    /// </summary>
    public const string menu_sync_library = "menu_sync_library";
    /// <summary>
    /// Open Settings
    /// </summary>
    public const string menu_open_settings = "menu_open_settings";
    /// <summary>
    /// Confirm Account Linking
    /// </summary>
    public const string confirm_linking_title = "confirm_linking_title";
    /// <summary>
    /// A website is requesting to link this Playnite installation to a GameScrobbler account.
    /// 
    /// Only continue if you just clicked "Link Playnite" on gamescrobbler.com yourself.
    /// </summary>
    public const string confirm_linking_body = "confirm_linking_body";
    /// <summary>
    /// Invalid user ID format received from server
    /// </summary>
    public const string invalid_user_id_format = "invalid_user_id_format";
    /// <summary>
    /// The installation changed or was disabled during this request. Please try again.
    /// </summary>
    public const string identity_changed_during_request = "identity_changed_during_request";
    /// <summary>
    /// Couldn't reach GameScrobbler to authorize the deletion. Check your internet connection and try again.
    /// </summary>
    public const string delete_no_token = "delete_no_token";
    /// <summary>
    /// Your data has already been deleted. The plugin is now disabled.
    /// </summary>
    public const string delete_already_done = "delete_already_done";
    /// <summary>
    /// This installation couldn't be verified. Reconnect your account, then try deleting again.
    /// </summary>
    public const string delete_auth_failed = "delete_auth_failed";
    /// <summary>
    /// Game Scrobbler could not read its saved data and is disabled for this session. The file was left untouched at {$path} so it can be repaired or removed.
    /// </summary>
    public const string data_unreadable = "data_unreadable";
    /// <summary>
    /// Game Scrobbler could not open a private browser profile for the dashboard, so it was not loaded. Restart Playnite to try again.
    /// </summary>
    public const string dashboard_profile_failed = "dashboard_profile_failed";
}
