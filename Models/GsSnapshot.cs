using System;
using System.Collections.Generic;

namespace GsPlugin.Models {
    // Legacy fat-snapshot data types. Retained only so GsSyncHashIndex can deserialize a
    // pre-existing gs_snapshot.json once during migration and derive compact fingerprints
    // from it. The former GsSnapshotManager (persistence/diff logic) was removed when
    // GsSyncHashIndex replaced it — do not reintroduce write logic here.
    public class GameSnapshot {
        public string playnite_id { get; set; }
        public string game_id { get; set; }
        public string plugin_id { get; set; }
        public long playtime_seconds { get; set; }
        public int play_count { get; set; }
        public string last_activity { get; set; }
        public string metadata_hash { get; set; }
        public int? achievement_count_unlocked { get; set; }
        public int? achievement_count_total { get; set; }
    }

    public class AchievementSnapshot {
        public string name { get; set; }
        public bool is_unlocked { get; set; }
        public string date_unlocked { get; set; }
        public float? rarity_percent { get; set; }
    }

    public class GameAchievementSnapshot {
        public string playnite_id { get; set; }
        public List<AchievementSnapshot> achievements { get; set; }
    }

    /// <summary>
    /// Legacy on-disk combined snapshot shape.
    /// </summary>
    public class GsSnapshot {
        public Dictionary<string, GameSnapshot> Library { get; set; } = new Dictionary<string, GameSnapshot>();
        public Dictionary<string, GameAchievementSnapshot> Achievements { get; set; } = new Dictionary<string, GameAchievementSnapshot>();
        public DateTime? LibraryFullSyncAt { get; set; }
        public DateTime? AchievementsFullSyncAt { get; set; }
        /// <summary>
        /// Matched GsData.IdentityGeneration at write time. GsSyncHashIndex.Initialize compares
        /// the migrated generation against the current one and discards a stale-identity baseline.
        /// </summary>
        public int IdentityGeneration { get; set; } = 0;
    }
}
