using System;
using System.Collections.Generic;
using System.Linq;
using Playnite.SDK;
using GsPlugin.Infrastructure;

namespace GsPlugin.Services {
    /// <summary>
    /// Shared plumbing for achievement providers that read an optional Playnite addon's
    /// on-disk data: addon presence, version lookup, and the "log a warning and report no
    /// data" wrapper that guards every read. Subclasses supply only the read logic.
    /// </summary>
    public abstract class AchievementProviderBase : IAchievementProvider, IReliableAchievementProvider {
        /// <summary>
        /// Id of the Playnite addon this provider reads data from.
        /// </summary>
        protected Guid PluginId { get; }

        /// <summary>
        /// Playnite API handle. Null when the provider is constructed with a path override
        /// (tests), so every use of it must stay null-conditional.
        /// </summary>
        protected IPlayniteAPI Api { get; }

        /// <summary>
        /// Prefix used in warning logs. Taken from the concrete type so it always names the
        /// class that performed the read.
        /// </summary>
        protected string LogPrefix { get; }

        protected AchievementProviderBase(Guid pluginId, IPlayniteAPI api) {
            PluginId = pluginId;
            Api = api;
            LogPrefix = GetType().Name;
        }

        public abstract string ProviderName { get; }

        /// <summary>
        /// True when the provider's data is present on disk (data directory, database file, ...).
        /// Checked before <see cref="IsPluginLoaded"/> so data left behind by an uninstalled
        /// addon still counts as installed.
        /// </summary>
        protected abstract bool HasLocalData { get; }

        public virtual bool IsInstalled {
            get {
                if (HasLocalData) return true;
                return IsPluginLoaded;
            }
        }

        public bool IsPluginLoaded =>
            Api?.Addons?.Plugins?.Any(p => p.Id == PluginId) == true;

        public abstract (int unlocked, int total)? GetCounts(Guid gameId);

        protected abstract List<AchievementItem> ReadAchievementsCore(Guid gameId);

        protected virtual string DescribeAchievementReadFailure(Exception ex) => null;

        public List<AchievementItem> GetAchievements(Guid gameId) {
            var result = ReadAchievements(gameId);
            return result.IsAvailable && result.Achievements.Count > 0 ? result.Achievements : null;
        }

        public AchievementReadResult ReadAchievements(Guid gameId) {
            try {
                return AchievementReadResult.Available(ReadAchievementsCore(gameId), ProviderName);
            }
            catch (Exception ex) {
                LogReadFailure("Achievement lookup", gameId, ex, DescribeAchievementReadFailure);
                return AchievementReadResult.Unavailable(ProviderName);
            }
        }

        public string GetVersion() {
            try {
                var plugin = Api?.Addons?.Plugins?.FirstOrDefault(p => p.Id == PluginId);
                if (plugin == null) return null;
                return PluginVersionHelper.GetExtensionYamlVersion(plugin)
                    ?? plugin.GetType().Assembly.GetName().Version?.ToString(3);
            }
            catch (Exception ex) {
                GsLogger.Warn($"[{LogPrefix}] Version lookup failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Runs a read that yields a reference type, turning any failure into a warning log
        /// and a null result. <paramref name="describeError"/> supplies the wording for
        /// exception types that deserve their own message; when it returns null the message
        /// falls back to "{operation} failed".
        /// </summary>
        protected T SafeRead<T>(string operation, Guid gameId, Func<Exception, string> describeError, Func<T> read) where T : class {
            try {
                return read();
            }
            catch (Exception ex) {
                LogReadFailure(operation, gameId, ex, describeError);
                return null;
            }
        }

        /// <summary>
        /// Value-type counterpart of <see cref="SafeRead{T}"/>: any failure is logged as a
        /// warning and reported as no data.
        /// </summary>
        protected TValue? SafeReadValue<TValue>(string operation, Guid gameId, Func<TValue?> read) where TValue : struct {
            try {
                return read();
            }
            catch (Exception ex) {
                LogReadFailure(operation, gameId, ex, null);
                return null;
            }
        }

        private void LogReadFailure(string operation, Guid gameId, Exception ex, Func<Exception, string> describeError) {
            var what = describeError?.Invoke(ex) ?? $"{operation} failed";
            GsLogger.Warn($"[{LogPrefix}] {what} for game {gameId}: {ex.Message}");
        }
    }
}
