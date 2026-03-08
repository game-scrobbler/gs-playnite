using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Playnite.SDK;
using GsPlugin.Infrastructure;

namespace GsPlugin.Services {
    /// <summary>
    /// Reads user identity information from installed Playnite library plugin config files.
    /// Each built-in library plugin stores its settings (including auth/user info) in
    /// {ExtensionsDataPath}/{plugin-guid}/config.json. This reader extracts known identity
    /// fields (e.g. Steam UserId) without reflection or runtime plugin dependencies.
    /// </summary>
    public class GsIntegrationAccountReader {
        private static readonly ILogger _logger = LogManager.GetLogger();
        private readonly string _extensionsDataPath;

        /// <summary>
        /// Known library plugin GUIDs mapped to their provider slug.
        /// Only plugins whose config.json is known to contain user identity fields are included.
        /// </summary>
        private static readonly Dictionary<string, string> PluginProviderMap = new Dictionary<string, string> {
            { "cb91dfc9-b977-43bf-8e70-55f46e410fab", "steam" },
        };

        public GsIntegrationAccountReader(IPlayniteAPI api) {
            _extensionsDataPath = api.Paths.ExtensionsDataPath;
        }

        /// <summary>
        /// Scans known library plugin config files and returns any discovered user identity info.
        /// Returns an empty list if no identities are found. Never throws.
        /// </summary>
        public List<IntegrationAccountDto> ReadAll() {
            var accounts = new List<IntegrationAccountDto>();

            foreach (var entry in PluginProviderMap) {
                try {
                    var account = ReadPluginAccount(entry.Key, entry.Value);
                    if (account != null) {
                        accounts.Add(account);
                    }
                }
                catch (Exception ex) {
                    _logger.Warn($"Failed to read integration account for {entry.Value} ({entry.Key}): {ex.Message}");
                }
            }

            return accounts;
        }

        private IntegrationAccountDto ReadPluginAccount(string pluginGuid, string providerId) {
            var configPath = Path.Combine(_extensionsDataPath, pluginGuid, "config.json");
            if (!File.Exists(configPath)) {
                return null;
            }

            var json = File.ReadAllText(configPath);
            using (var doc = JsonDocument.Parse(json)) {
                var root = doc.RootElement;

                switch (providerId) {
                    case "steam":
                        return ReadSteamAccount(root);
                    default:
                        return null;
                }
            }
        }

        /// <summary>
        /// Extracts Steam user identity from the Steam library plugin config.
        /// Known fields: UserId (Steam64 ID string), AdditionalAccounts (array).
        /// </summary>
        private static IntegrationAccountDto ReadSteamAccount(JsonElement root) {
            string userId = null;
            if (root.TryGetProperty("UserId", out var userIdProp)) {
                // UserId can be a string or a number (ulong)
                if (userIdProp.ValueKind == JsonValueKind.String) {
                    userId = userIdProp.GetString();
                }
                else if (userIdProp.ValueKind == JsonValueKind.Number) {
                    userId = userIdProp.GetRawText();
                }
            }

            if (string.IsNullOrEmpty(userId) || userId == "0") {
                return null;
            }

            return new IntegrationAccountDto {
                provider_id = "steam",
                account_id = userId,
                plugin_id = "cb91dfc9-b977-43bf-8e70-55f46e410fab"
            };
        }
    }

    /// <summary>
    /// Represents a discovered integration account identity sent to the backend during library sync.
    /// </summary>
    public class IntegrationAccountDto {
        /// <summary>Provider slug matching the backend's provider_id (e.g. "steam", "gog").</summary>
        public string provider_id { get; set; }
        /// <summary>External account identifier (e.g. Steam64 ID, GOG user ID).</summary>
        public string account_id { get; set; }
        /// <summary>Playnite library plugin GUID that owns this account.</summary>
        public string plugin_id { get; set; }
    }
}
