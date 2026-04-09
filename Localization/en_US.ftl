# ── Section 1: Installation ID ──
install_id_section_title = Installation ID
install_id_description = Your unique plugin installation identifier, used to link your account.
install_id_copy_hint = Click the ID above to copy it to your clipboard.
install_id_copy_tooltip = Click to copy ID to clipboard

# ── Section 2: Theme & Dashboard ──
theme_section_title = Theme & Dashboard
theme_description = Controls the visual theme applied to the Game Scrobbler sidebar and views.
new_dashboard_experience_label = New Dashboard Experience
new_dashboard_experience_tooltip = Enable the new dashboard experience with enhanced features

# ── Section 3: Account Linking ──
account_linking_section_title = Account Linking
link_on_website_button = Link Account on Website
manual_token_separator = or enter your token manually
link_account_button = Link Account
disconnect_account_button = Disconnect Account
disconnect_account_tooltip = Disconnects your account from GameScrobbler. Your game data is kept on the server.
disconnect_dialog_title = Disconnect Account
disconnect_dialog_body =
    Disconnect your account? Your game data will be kept on the server.
    You can re-link anytime.
disconnect_no_account = No account is currently linked.
status_disconnected = Account disconnected.
status_token_expired = Token expired — click "Open website to link" to get a new one.
token_countdown_format = Token expires in ~{ $minutes }:{ $seconds }
token_expired_dialog_title = Token Expired
token_expired_dialog_body =
    Your link token has expired.

    Would you like to open the linking page to get a new one?
link_success_dialog_title = Account Linking Success
link_success_dialog_body =
    Account successfully linked!
    User ID: { $userId }
link_failed_dialog_title = Account Linking Failed
link_failed_retry_dialog_title = Account Linking Failed — Retry?
link_failed_retries_body =
    Account linking failed after multiple attempts:
    { $error }

    Please try again later.
link_failed_network_body =
    Account linking failed due to a network error:
    { $error }

    Would you like to retry?
link_failed_body = Account linking failed: { $error }

# ── Section 4: Sync Settings ──
sync_settings_section_title = Sync Settings
library_sync_label = Library Sync:

# ── Section 5: Notifications ──
notifications_section_title = Notifications
show_update_notifications_label = Show version update notifications
show_important_notifications_label = Show important notifications from GameScrobbler

# ── Section 6: Privacy Controls ──
privacy_section_title = Privacy Controls
disable_sentry_label = Disable error reporting (Sentry)
disable_posthog_label = Disable analytics (PostHog)
disable_scrobbling_label = Disable game activity tracking (Scrobbling)
scrobbling_disabled_warning = You will not be able to benefit from features that rely on session data

# ── Section 7: Danger Zone ──
danger_zone_section_title = Danger Zone
danger_zone_warning = Please only use this option if you want to delete your Playnite data in the GameScrobbler servers. After your delete request you cannot continue using the GameScrobbler addon without opting in again.
delete_my_data_button = Delete My Data
opt_back_in_button = Opt Back In

# ── Account Linking: Additional UI strings ──
account_linking_hint_prefix = Link your plugin to your GS account to own and persist your data. Visit
account_linking_hint_suffix =  to get started.
status_label = Status:
connection_status_connected = Connected to GameScrobbler
connection_status_disconnected = Disconnected
connection_status_opted_out = Opted Out
linking_in_progress = Linking...
open_website_to_link_tooltip = Opens gamescrobbler.com in your browser to link your account automatically
token_input_tooltip = Enter your account linking token here

# ── Install Token & Diagnostics ──
token_status_active = ✓ Token: Active
token_status_pending = ⚠ Token: Pending registration
pending_scrobbles_format = { $count } scrobbles queued — will retry automatically
dropped_scrobbles_format = { $count } scrobbles lost due to server errors

# ── Sync Status ──
never_synced = Never synced
last_synced_format = Last synced: { $count } games · { $time }

# ── Elapsed Time ──
elapsed_just_now = just now
elapsed_minutes_format = { $count } minutes ago
elapsed_hours_format = { $count } hours ago
elapsed_days_format = { $count } days ago
remaining_less_than_minute = less than a minute
remaining_minutes_format = { $count } minutes
remaining_hours_minutes_format = { $hours } hours { $minutes } minutes
remaining_hours_format = { $count } hours

# ── Notification Tooltips ──
show_update_notifications_tooltip = Show a notification in Playnite when a new plugin version is available
show_important_notifications_tooltip = Show notifications about important updates and announcements

# ── Privacy Tooltips ──
disable_sentry_tooltip = Prevents sending crash reports and diagnostic data to improve the plugin
disable_posthog_tooltip = Prevents sending anonymous usage analytics to help improve the plugin
disable_scrobbling_tooltip = Prevents scrobbling of your game playing activity
opt_back_in_tooltip = Re-enable the plugin and resume syncing

# ── Clipboard ──
copied_to_clipboard = Text copied to clipboard!
copied_dialog_title = Success
copy_failed_format = Failed to copy text: { $error }
open_url_failed_format = Failed to open URL: { $error }
error_dialog_title = Error

# ── Delete My Data Dialogs ──
delete_confirm_body =
    Are you sure you want to delete all your data from GameScrobbler servers?

    This will:
    • Remove your library, sessions, and achievements from our servers
    • Disable all plugin features
    • Require you to opt in again to resume using the plugin

    This action cannot be undone.
delete_confirm_title = Delete My Data
delete_final_body = Are you absolutely sure? Your data will be permanently deleted from the GameScrobbler servers.
delete_final_title = Final Confirmation
deleting_in_progress = Deleting...

# ── Opt Back In Dialog ──
opt_back_in_confirm_body =
    Re-enable the GameScrobbler plugin?

    You will need to restart Playnite for all features to resume.
opt_back_in_confirm_title = Opt Back In

# ── Account Linking: Status Messages ──
please_enter_token = Please enter a token
verifying_token = Verifying token...
link_success = Successfully linked account!
network_error_retry_format = { $error } Click "Link Account" to retry.
error_retry_format = Error: { $error } Click "Link Account" to retry.
error_format = Error: { $error }
unknown_linking_error = Unknown error occurred during linking
linking_error_format = Error during linking: { $error }
network_error = Network error — could not reach the server. Please try again.
unlink_failed = Failed to disconnect account.
already_linked_body =
    Account is already linked to User ID: { $userId }

    Do you want to link to a different account?
already_linked_title = Account Already Linked

# ── Data Deletion: Status Messages ──
deleting_requesting = Requesting data deletion...
delete_success = Your data has been deleted. The plugin is now disabled.
delete_rate_limited = Too many deletion requests. Please wait 15 minutes and try again.
delete_failed = Failed to request data deletion. Please try again later.
delete_error = An error occurred. Please try again later.
opt_back_in_success = Plugin re-enabled. Please restart Playnite to resume syncing.

# ── URI Handler ──
invalid_linking_token = Invalid linking token received.
unexpected_uri_error_format = Unexpected error processing URI request: { $error }

# ── Library Sync Messages ──
sync_completed = Library sync completed.
sync_up_to_date = Library is already up to date.
sync_cooldown_format = Library was already synced recently. Try again in { $time }.
sync_cooldown_generic = Library was already synced recently. Please try again later.
sync_failed = Library sync failed. Check logs for details.
sync_error = Library sync encountered an error.

# ── Menu Items ──
menu_sync_library = Sync Library Now
menu_open_settings = Open Settings
