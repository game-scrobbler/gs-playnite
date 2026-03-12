using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using GsPlugin.Models;

namespace GsPlugin.Tests {
    [Collection("StaticManagerTests")]
    public class GsDataManagerTests {
        private string CreateTempDir() {
            var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(dir);
            return dir;
        }

        [Fact]
        public void IsAccountLinked_WhenLinkedUserIdIsNull_ReturnsFalse() {
            var tempDir = CreateTempDir();
            try {
                GsDataManager.Initialize(tempDir, null);
                GsDataManager.Data.LinkedUserId = null;

                Assert.False(GsDataManager.IsAccountLinked);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void IsAccountLinked_WhenLinkedUserIdIsSentinel_ReturnsFalse() {
            var tempDir = CreateTempDir();
            try {
                GsDataManager.Initialize(tempDir, null);
                GsDataManager.Data.LinkedUserId = GsData.NotLinkedValue;

                Assert.False(GsDataManager.IsAccountLinked);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void IsAccountLinked_WhenLinkedUserIdIsEmpty_ReturnsFalse() {
            var tempDir = CreateTempDir();
            try {
                GsDataManager.Initialize(tempDir, null);
                GsDataManager.Data.LinkedUserId = "";

                Assert.False(GsDataManager.IsAccountLinked);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void IsAccountLinked_WhenLinkedUserIdIsRealId_ReturnsTrue() {
            var tempDir = CreateTempDir();
            try {
                GsDataManager.Initialize(tempDir, null);
                GsDataManager.Data.LinkedUserId = "user-abc-123";

                Assert.True(GsDataManager.IsAccountLinked);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void EnqueuePendingScrobble_AddsItemToQueue() {
            var tempDir = CreateTempDir();
            try {
                GsDataManager.Initialize(tempDir, null);
                GsDataManager.Data.PendingScrobbles.Clear();

                var item = new PendingScrobble {
                    Type = "start",
                    QueuedAt = DateTime.UtcNow
                };
                GsDataManager.EnqueuePendingScrobble(item);

                Assert.Single(GsDataManager.Data.PendingScrobbles);
                Assert.Equal("start", GsDataManager.Data.PendingScrobbles[0].Type);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void DequeuePendingScrobbles_ReturnsAllItemsAndClearsQueue() {
            var tempDir = CreateTempDir();
            try {
                GsDataManager.Initialize(tempDir, null);
                GsDataManager.Data.PendingScrobbles.Clear();

                GsDataManager.EnqueuePendingScrobble(new PendingScrobble { Type = "start", QueuedAt = DateTime.UtcNow });
                GsDataManager.EnqueuePendingScrobble(new PendingScrobble { Type = "finish", QueuedAt = DateTime.UtcNow });

                var dequeued = GsDataManager.DequeuePendingScrobbles();

                Assert.Equal(2, dequeued.Count);
                Assert.Equal("start", dequeued[0].Type);
                Assert.Equal("finish", dequeued[1].Type);
                Assert.Empty(GsDataManager.Data.PendingScrobbles);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void DequeuePendingScrobbles_EmptyQueue_ReturnsEmptyList() {
            var tempDir = CreateTempDir();
            try {
                GsDataManager.Initialize(tempDir, null);
                GsDataManager.Data.PendingScrobbles.Clear();

                var dequeued = GsDataManager.DequeuePendingScrobbles();

                Assert.Empty(dequeued);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void Initialize_GeneratesInstallId_WhenNotPresent() {
            var tempDir = CreateTempDir();
            try {
                GsDataManager.Initialize(tempDir, null);

                Assert.False(string.IsNullOrEmpty(GsDataManager.Data.InstallID));
                // InstallID should be a valid GUID
                Assert.True(Guid.TryParse(GsDataManager.Data.InstallID, out _));
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void Initialize_WhenFileExists_LoadsExistingData() {
            var tempDir = CreateTempDir();
            try {
                // First initialization — creates a file with an InstallID
                GsDataManager.Initialize(tempDir, null);
                var originalInstallId = GsDataManager.Data.InstallID;
                GsDataManager.Data.LinkedUserId = "persisted-user";
                GsDataManager.Save();

                // Re-initialize from the same directory
                GsDataManager.Initialize(tempDir, null);

                Assert.Equal(originalInstallId, GsDataManager.Data.InstallID);
                Assert.Equal("persisted-user", GsDataManager.Data.LinkedUserId);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void Save_PersistsDataToDisk() {
            var tempDir = CreateTempDir();
            try {
                GsDataManager.Initialize(tempDir, null);
                GsDataManager.Data.Theme = "Light";
                GsDataManager.Data.LastSyncGameCount = 99;
                GsDataManager.Save();

                var filePath = Path.Combine(tempDir, "gs_data.json");
                Assert.True(File.Exists(filePath));

                var json = File.ReadAllText(filePath);
                Assert.Contains("Light", json);
                Assert.Contains("99", json);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }
        [Fact]
        public void IsOptedOut_DefaultsFalse() {
            var tempDir = CreateTempDir();
            try {
                GsDataManager.Initialize(tempDir, null);

                Assert.False(GsDataManager.IsOptedOut);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void PerformOptOut_SetsOptedOutAndClearsState() {
            var tempDir = CreateTempDir();
            try {
                GsDataManager.Initialize(tempDir, null);
                GsDataManager.Data.LinkedUserId = "user-123";
                GsDataManager.Data.ActiveSessionId = "session-1";
                GsDataManager.Data.LastLibraryHash = "abc";
                GsDataManager.Data.LastSyncAt = DateTime.UtcNow;
                GsDataManager.EnqueuePendingScrobble(new PendingScrobble { Type = "start", QueuedAt = DateTime.UtcNow });

                GsDataManager.PerformOptOut();

                Assert.True(GsDataManager.IsOptedOut);
                Assert.True(GsDataManager.Data.OptedOut);
                Assert.Null(GsDataManager.Data.LinkedUserId);
                Assert.Null(GsDataManager.Data.ActiveSessionId);
                Assert.Null(GsDataManager.Data.LastLibraryHash);
                Assert.Null(GsDataManager.Data.LastSyncAt);
                Assert.Empty(GsDataManager.Data.PendingScrobbles);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void PerformOptOut_PersistsToDisk() {
            var tempDir = CreateTempDir();
            try {
                GsDataManager.Initialize(tempDir, null);
                GsDataManager.PerformOptOut();

                // Re-initialize from disk
                GsDataManager.Initialize(tempDir, null);

                Assert.True(GsDataManager.IsOptedOut);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void PerformOptIn_ClearsOptedOut() {
            var tempDir = CreateTempDir();
            try {
                GsDataManager.Initialize(tempDir, null);
                GsDataManager.PerformOptOut();
                Assert.True(GsDataManager.IsOptedOut);

                GsDataManager.PerformOptIn();

                Assert.False(GsDataManager.IsOptedOut);
                Assert.False(GsDataManager.Data.OptedOut);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void PerformOptIn_PersistsToDisk() {
            var tempDir = CreateTempDir();
            try {
                GsDataManager.Initialize(tempDir, null);
                GsDataManager.PerformOptOut();
                GsDataManager.PerformOptIn();

                // Re-initialize from disk
                GsDataManager.Initialize(tempDir, null);

                Assert.False(GsDataManager.IsOptedOut);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }
        [Fact]
        public void Initialize_FreshInstall_BumpsIdentityGeneration() {
            var tempDir = CreateTempDir();
            try {
                GsDataManager.Initialize(tempDir, null);

                // A fresh install (no existing gs_data.json) should bump IdentityGeneration
                // so that any surviving stale gs_snapshot.json is invalidated.
                Assert.True(GsDataManager.Data.IdentityGeneration >= 1);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void Initialize_ExistingInstallId_DoesNotBumpGeneration() {
            var tempDir = CreateTempDir();
            try {
                // First init creates an InstallID and bumps generation
                GsDataManager.Initialize(tempDir, null);
                var gen = GsDataManager.Data.IdentityGeneration;
                GsDataManager.Save();

                // Re-initialize from same directory — InstallID already exists, no bump
                GsDataManager.Initialize(tempDir, null);

                Assert.Equal(gen, GsDataManager.Data.IdentityGeneration);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void RotateInstallId_ChangesInstallIdAndClearsToken() {
            var tempDir = CreateTempDir();
            try {
                GsDataManager.Initialize(tempDir, null);
                var originalId = GsDataManager.Data.InstallID;
                GsDataManager.Data.InstallToken = "old-token";
                GsDataManager.Save();

                var newId = GsDataManager.RotateInstallId();

                Assert.NotEqual(originalId, newId);
                Assert.Equal(newId, GsDataManager.Data.InstallID);
                Assert.Null(GsDataManager.Data.InstallToken);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void RotateInstallId_IncrementsIdentityGeneration() {
            var tempDir = CreateTempDir();
            try {
                GsDataManager.Initialize(tempDir, null);
                var genBefore = GsDataManager.Data.IdentityGeneration;

                GsDataManager.RotateInstallId();

                Assert.Equal(genBefore + 1, GsDataManager.Data.IdentityGeneration);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void RotateInstallId_ClearsLinkedUserId() {
            var tempDir = CreateTempDir();
            try {
                GsDataManager.Initialize(tempDir, null);
                GsDataManager.Data.LinkedUserId = "linked-user-123";
                GsDataManager.Save();

                GsDataManager.RotateInstallId();

                Assert.Null(GsDataManager.Data.LinkedUserId);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void RotateInstallId_ClearsIdentityBoundState() {
            var tempDir = CreateTempDir();
            try {
                GsDataManager.Initialize(tempDir, null);
                GsDataManager.Data.ActiveSessionId = "session-1";
                GsDataManager.Data.PendingStartGameId = "game-1";
                GsDataManager.Data.LastLibraryHash = "hash-lib";
                GsDataManager.Data.LastAchievementHash = "hash-ach";
                GsDataManager.Data.LastSyncAt = DateTime.UtcNow;
                GsDataManager.Data.LastSyncGameCount = 50;
                GsDataManager.Data.SyncCooldownExpiresAt = DateTime.UtcNow.AddHours(1);
                GsDataManager.Data.LibraryDiffSyncCooldownExpiresAt = DateTime.UtcNow.AddHours(2);
                GsDataManager.Data.LastIntegrationAccountsHash = "hash-int";
                GsDataManager.EnqueuePendingScrobble(new PendingScrobble { Type = "start", QueuedAt = DateTime.UtcNow });
                GsDataManager.Save();

                GsDataManager.RotateInstallId();

                Assert.Null(GsDataManager.Data.ActiveSessionId);
                Assert.Null(GsDataManager.Data.PendingStartGameId);
                Assert.Null(GsDataManager.Data.LastLibraryHash);
                Assert.Null(GsDataManager.Data.LastAchievementHash);
                Assert.Null(GsDataManager.Data.LastSyncAt);
                Assert.Null(GsDataManager.Data.LastSyncGameCount);
                Assert.Null(GsDataManager.Data.SyncCooldownExpiresAt);
                Assert.Null(GsDataManager.Data.LibraryDiffSyncCooldownExpiresAt);
                Assert.Null(GsDataManager.Data.LastIntegrationAccountsHash);
                Assert.Empty(GsDataManager.Data.PendingScrobbles);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void SetInstallTokenIfActive_WhenNotOptedOut_StoresToken() {
            var tempDir = CreateTempDir();
            try {
                GsDataManager.Initialize(tempDir, null);

                var stored = GsDataManager.SetInstallTokenIfActive("new-token-abc");

                Assert.True(stored);
                Assert.Equal("new-token-abc", GsDataManager.Data.InstallToken);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void SetInstallTokenIfActive_WhenOptedOut_RejectsToken() {
            var tempDir = CreateTempDir();
            try {
                GsDataManager.Initialize(tempDir, null);
                GsDataManager.PerformOptOut();

                var stored = GsDataManager.SetInstallTokenIfActive("should-not-persist");

                Assert.False(stored);
                Assert.Null(GsDataManager.Data.InstallToken);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void InstallIdForBody_WithNoToken_ReturnsInstallId() {
            var tempDir = CreateTempDir();
            try {
                GsDataManager.Initialize(tempDir, null);
                GsDataManager.Data.InstallToken = null;

                Assert.Equal(GsDataManager.Data.InstallID, GsDataManager.InstallIdForBody);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void InstallIdForBody_WithToken_ReturnsNull() {
            var tempDir = CreateTempDir();
            try {
                GsDataManager.Initialize(tempDir, null);
                GsDataManager.Data.InstallToken = "active-token";

                Assert.Null(GsDataManager.InstallIdForBody);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void PerformOptOut_ClearsInstallToken() {
            var tempDir = CreateTempDir();
            try {
                GsDataManager.Initialize(tempDir, null);
                GsDataManager.Data.InstallToken = "token-to-clear";
                GsDataManager.Save();

                GsDataManager.PerformOptOut();

                Assert.Null(GsDataManager.Data.InstallToken);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
