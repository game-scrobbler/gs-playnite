using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using GsPlugin.Infrastructure;

namespace GsPlugin.Models {
    /// <summary>
    /// One compact fingerprint index file (the library half or the achievements half). Owns its
    /// path and its in-memory <see cref="GsSyncHashIndexFile"/>. The two halves are otherwise
    /// identical: they differ only in the file name, which half of the legacy fat
    /// <see cref="GsSnapshot"/> they migrate from, the fingerprint recipe applied during that
    /// migration, and the log labels. All four are supplied by the constructor.
    ///
    /// Threading: this type takes no locks of its own. Every instance is owned by
    /// <see cref="GsSyncHashIndex"/>, which serializes all access on one shared lock, and callers
    /// must already hold that lock. Keeping the lock in the facade is what makes the operations
    /// that span both halves (Initialize, ClearAll) atomic: no thread can observe one half
    /// updated while the other is still stale.
    /// </summary>
    internal sealed class GsHashIndexStore {
        private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions {
            WriteIndented = false
        };

        private readonly string _fileName;
        /// <summary>Capitalized half name used in log messages, e.g. "Library" / "Achievement".</summary>
        private readonly string _label;
        /// <summary>Lower-case half name as used in the legacy migration messages.</summary>
        private readonly string _legacyHalfLabel;
        private readonly string _sentryOperation;
        /// <summary>
        /// Derives this half's index from a legacy combined snapshot, or returns null when the
        /// snapshot carries no data for this half. Encapsulates both the half selection and the
        /// fingerprint recipe.
        /// </summary>
        private readonly Func<GsSnapshot, GsSyncHashIndexFile> _fromLegacySnapshot;

        private string _filePath;
        private GsSyncHashIndexFile _index;

        public GsHashIndexStore(
            string fileName,
            string label,
            string legacyHalfLabel,
            string sentryOperation,
            Func<GsSnapshot, GsSyncHashIndexFile> fromLegacySnapshot) {
            _fileName = fileName;
            _label = label;
            _legacyHalfLabel = legacyHalfLabel;
            _sentryOperation = sentryOperation;
            _fromLegacySnapshot = fromLegacySnapshot;
        }

        /// <summary>False until <see cref="SetLocation"/> has run, i.e. before Initialize.</summary>
        public bool HasFilePath => !string.IsNullOrEmpty(_filePath);

        public void SetLocation(string folderPath) {
            _filePath = Path.Combine(folderPath, _fileName);
        }

        /// <summary>
        /// Recovers a crash-interrupted write of this half's file. Must run before
        /// <see cref="LoadOrMigrate"/> so an upgrade after a crash between temp-write and rename
        /// does not silently lose a baseline and force a full re-upload.
        /// </summary>
        public void RecoverTemp() {
            GsAtomicFile.RecoverTemp(_filePath);
        }

        /// <summary>
        /// Loads this half from its compact file, or derives it once from the legacy fat combined
        /// snapshot (<c>gs_snapshot.json</c>, the only format any shipped build wrote). Never
        /// writes or deletes: the caller persists and clears the legacy file after the generation
        /// check. Returns whether the index came from a migration.
        /// </summary>
        public bool LoadOrMigrate(string legacyCombinedPath) {
            if (File.Exists(_filePath)) {
                _index = GsAtomicFile.LoadJson<GsSyncHashIndexFile>(_filePath, jsonOptions)
                    ?? new GsSyncHashIndexFile();
                return false;
            }

            if (File.Exists(legacyCombinedPath)) {
                try {
                    var legacy = GsAtomicFile.LoadJson<GsSnapshot>(legacyCombinedPath, jsonOptions);
                    var derived = legacy == null ? null : _fromLegacySnapshot(legacy);
                    if (derived != null) {
                        GsLogger.Info($"[GsSyncHashIndex] Migrating {_legacyHalfLabel} half of gs_snapshot.json");
                        _index = derived;
                        return true;
                    }
                }
                catch (Exception ex) {
                    GsLogger.Warn($"[GsSyncHashIndex] Combined snapshot {_legacyHalfLabel} migrate failed: {ex.Message}");
                }
            }

            _index = new GsSyncHashIndexFile();
            return false;
        }

        /// <summary>
        /// Discards the loaded index when it was written under a different install identity.
        /// Must run against the *loaded* generation, before any save re-stamps it to the current
        /// one. Returns true when the index was discarded and therefore needs persisting.
        /// </summary>
        public bool DiscardIfGenerationMismatch(int currentGeneration) {
            if (_index.IdentityGeneration == currentGeneration) {
                return false;
            }
            GsLogger.Warn($"[GsSyncHashIndex] {_label} index generation {_index.IdentityGeneration} != data generation {currentGeneration}; discarding");
            ResetToGeneration(currentGeneration);
            return true;
        }

        /// <summary>In-memory only reset to a clean index stamped with the given generation.</summary>
        public void ResetToGeneration(int generation) {
            _index = new GsSyncHashIndexFile { IdentityGeneration = generation };
        }

        public bool Save() {
            if (_index == null) {
                return false;
            }
            _index.IdentityGeneration = GsDataManager.DataOrNull?.IdentityGeneration ?? 0;
            try {
                GsAtomicFile.WriteJson(_filePath, _index, jsonOptions);
                return true;
            }
            catch (Exception ex) {
                GsLogger.Warn($"[GsSyncHashIndex] Failed to save {_label.ToLowerInvariant()} index: {ex.Message}");
                GsSentry.CaptureException(ex, _sentryOperation);
                return false;
            }
        }

        public bool HasBaseline {
            get {
                EnsureLoaded();
                return _index.FullSyncAt.HasValue;
            }
        }

        public int EntryCount {
            get {
                EnsureLoaded();
                return _index.Entries?.Count ?? 0;
            }
        }

        /// <summary>Shallow copy of this half's fingerprints for diff computation.</summary>
        public Dictionary<string, string> GetFingerprints() {
            EnsureLoaded();
            return new Dictionary<string, string>(_index.Entries ?? new Dictionary<string, string>());
        }

        public bool ReplaceIndex(Dictionary<string, string> entries) {
            EnsureLoaded();
            _index.Entries = entries ?? new Dictionary<string, string>();
            _index.FullSyncAt = DateTime.UtcNow;
            return Save();
        }

        public bool ApplyDiff(Dictionary<string, string> upserted, List<string> removed) {
            EnsureLoaded();
            if (_index.Entries == null) {
                _index.Entries = new Dictionary<string, string>();
            }
            if (upserted != null) {
                foreach (var kvp in upserted) {
                    _index.Entries[kvp.Key] = kvp.Value;
                }
            }
            if (removed != null) {
                foreach (var id in removed) {
                    _index.Entries.Remove(id);
                }
            }
            return Save();
        }

        public bool Clear() {
            EnsureLoaded();
            _index.Entries = new Dictionary<string, string>();
            _index.FullSyncAt = null;
            return Save();
        }

        /// <summary>
        /// Builds a compact index from one half of a legacy snapshot dictionary using the supplied
        /// fingerprint recipe. Shared by both halves: the element type and the recipe are the
        /// only things that differ.
        /// </summary>
        public static GsSyncHashIndexFile FromLegacyDict<TSnapshot>(
            Dictionary<string, TSnapshot> items,
            int generation,
            DateTime? fullSyncAt,
            Func<TSnapshot, string> fingerprint) where TSnapshot : class {
            var entries = new Dictionary<string, string>(items.Count);
            foreach (var kvp in items) {
                if (string.IsNullOrEmpty(kvp.Key) || kvp.Value == null) {
                    continue;
                }
                entries[kvp.Key] = fingerprint(kvp.Value);
            }
            return new GsSyncHashIndexFile {
                IdentityGeneration = generation,
                FullSyncAt = fullSyncAt,
                Entries = entries
            };
        }

        private void EnsureLoaded() {
            if (_index == null) {
                throw new InvalidOperationException("GsSyncHashIndex not initialized. Call Initialize() first.");
            }
        }
    }
}
