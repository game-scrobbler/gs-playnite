using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

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
    }
}
