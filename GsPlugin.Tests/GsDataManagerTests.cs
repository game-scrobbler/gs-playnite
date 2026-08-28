using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using GsPlugin.Models;

namespace GsPlugin.Tests {
    [Collection("StaticManagerTests")]
    public class GsDataManagerTests {
        [Fact]
        public void IsAccountLinked_WhenLinkedUserIdIsNull_ReturnsFalse() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                GsDataManager.Data.LinkedUserId = null;

                Assert.False(GsDataManager.IsAccountLinked);
            }
        }

        [Fact]
        public void IsAccountLinked_WhenLinkedUserIdIsSentinel_ReturnsFalse() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                GsDataManager.Data.LinkedUserId = GsData.NotLinkedValue;

                Assert.False(GsDataManager.IsAccountLinked);
            }
        }

        [Fact]
        public void IsAccountLinked_WhenLinkedUserIdIsEmpty_ReturnsFalse() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                GsDataManager.Data.LinkedUserId = "";

                Assert.False(GsDataManager.IsAccountLinked);
            }
        }

        [Fact]
        public void IsAccountLinked_WhenLinkedUserIdIsRealId_ReturnsTrue() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                GsDataManager.Data.LinkedUserId = "user-abc-123";

                Assert.True(GsDataManager.IsAccountLinked);
            }
        }

        [Fact]
        public void EnqueuePendingScrobble_AddsItemToQueue() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                GsDataManager.Data.PendingScrobbles.Clear();

                var item = new PendingScrobble {
                    Type = "start",
                    QueuedAt = DateTime.UtcNow
                };
                GsDataManager.EnqueuePendingScrobble(item);

                Assert.Single(GsDataManager.Data.PendingScrobbles);
                Assert.Equal("start", GsDataManager.Data.PendingScrobbles[0].Type);
            }
        }

        [Fact]
        public void Initialize_GeneratesInstallId_WhenNotPresent() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                Assert.False(string.IsNullOrEmpty(GsDataManager.Data.InstallID));
                // InstallID should be a valid GUID
                Assert.True(Guid.TryParse(GsDataManager.Data.InstallID, out _));
            }
        }

        [Fact]
        public void Initialize_WhenFileExists_LoadsExistingData() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                // The fixture performed the first initialization, which created the InstallID.
                var originalInstallId = GsDataManager.Data.InstallID;
                GsDataManager.Data.LinkedUserId = "persisted-user";
                GsDataManager.Save();

                // Re-initialize from the same directory
                GsDataManager.Initialize(temp.Path, null);

                Assert.Equal(originalInstallId, GsDataManager.Data.InstallID);
                Assert.Equal("persisted-user", GsDataManager.Data.LinkedUserId);
            }
        }

        [Fact]
        public void Save_PersistsDataToDisk() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                GsDataManager.Data.Theme = "Light";
                GsDataManager.Data.LastSyncGameCount = 99;
                GsDataManager.Save();

                var filePath = Path.Combine(temp.Path, "gs_data.json");
                Assert.True(File.Exists(filePath));

                var json = File.ReadAllText(filePath);
                Assert.Contains("Light", json);
                Assert.Contains("99", json);
            }
        }
        [Fact]
        public void IsOptedOut_DefaultsFalse() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                Assert.False(GsDataManager.IsOptedOut);
            }
        }

        [Fact]
        public void PerformOptOut_SetsOptedOutAndClearsState() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                GsDataManager.Data.LinkedUserId = "user-123";
                GsDataManager.Data.ActiveSessionsByGameId["game-1"] = "session-1";
                GsDataManager.Data.LastLibraryHash = "abc";
                GsDataManager.Data.LastSyncAt = DateTime.UtcNow;
                GsDataManager.EnqueuePendingScrobble(new PendingScrobble { Type = "start", QueuedAt = DateTime.UtcNow });

                GsDataManager.PerformOptOut();

                Assert.True(GsDataManager.IsOptedOut);
                Assert.True(GsDataManager.Data.OptedOut);
                Assert.Null(GsDataManager.Data.LinkedUserId);
                Assert.Empty(GsDataManager.Data.ActiveSessionsByGameId);
                Assert.Null(GsDataManager.Data.LastLibraryHash);
                Assert.Null(GsDataManager.Data.LastSyncAt);
                Assert.Empty(GsDataManager.Data.PendingScrobbles);
            }
        }

        [Fact]
        public void PerformOptOut_PersistsToDisk() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                GsDataManager.PerformOptOut();

                // Re-initialize from disk
                GsDataManager.Initialize(temp.Path, null);

                Assert.True(GsDataManager.IsOptedOut);
            }
        }

        [Fact]
        public void PerformOptIn_ClearsOptedOut() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                GsDataManager.PerformOptOut();
                Assert.True(GsDataManager.IsOptedOut);

                GsDataManager.PerformOptIn();

                Assert.False(GsDataManager.IsOptedOut);
                Assert.False(GsDataManager.Data.OptedOut);
            }
        }

        [Fact]
        public void PerformOptIn_PersistsToDisk() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                GsDataManager.PerformOptOut();
                GsDataManager.PerformOptIn();

                // Re-initialize from disk
                GsDataManager.Initialize(temp.Path, null);

                Assert.False(GsDataManager.IsOptedOut);
            }
        }
        [Fact]
        public void Initialize_FreshInstall_BumpsIdentityGeneration() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                // A fresh install (no existing gs_data.json) should bump IdentityGeneration
                // so that any surviving stale gs_snapshot.json is invalidated.
                Assert.True(GsDataManager.Data.IdentityGeneration >= 1);
            }
        }

        [Fact]
        public void Initialize_ExistingInstallId_DoesNotBumpGeneration() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                // The fixture's first init created an InstallID and bumped generation.
                var gen = GsDataManager.Data.IdentityGeneration;
                GsDataManager.Save();

                // Re-initialize from same directory — InstallID already exists, no bump
                GsDataManager.Initialize(temp.Path, null);

                Assert.Equal(gen, GsDataManager.Data.IdentityGeneration);
            }
        }

        [Fact]
        public void RotateInstallId_ChangesInstallIdAndClearsToken() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                var originalId = GsDataManager.Data.InstallID;
                GsDataManager.Data.InstallToken = "old-token";
                GsDataManager.Save();

                var newId = GsDataManager.RotateInstallId();

                Assert.NotEqual(originalId, newId);
                Assert.Equal(newId, GsDataManager.Data.InstallID);
                Assert.Null(GsDataManager.Data.InstallToken);
            }
        }

        [Fact]
        public void RotateInstallId_IncrementsIdentityGeneration() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                var genBefore = GsDataManager.Data.IdentityGeneration;

                GsDataManager.RotateInstallId();

                Assert.Equal(genBefore + 1, GsDataManager.Data.IdentityGeneration);
            }
        }

        [Fact]
        public void RotateInstallId_ClearsLinkedUserId() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                GsDataManager.Data.LinkedUserId = "linked-user-123";
                GsDataManager.Save();

                GsDataManager.RotateInstallId();

                Assert.Null(GsDataManager.Data.LinkedUserId);
            }
        }

        [Fact]
        public void RotateInstallId_ClearsIdentityBoundState() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                GsDataManager.Data.ActiveSessionsByGameId["game-1"] = "session-1";
                GsDataManager.Data.PendingStartGameIds.Add("game-1");
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

                Assert.Empty(GsDataManager.Data.ActiveSessionsByGameId);
                Assert.Empty(GsDataManager.Data.PendingStartGameIds);
                Assert.Null(GsDataManager.Data.LastLibraryHash);
                Assert.Null(GsDataManager.Data.LastAchievementHash);
                Assert.Null(GsDataManager.Data.LastSyncAt);
                Assert.Null(GsDataManager.Data.LastSyncGameCount);
                Assert.Null(GsDataManager.Data.SyncCooldownExpiresAt);
                Assert.Null(GsDataManager.Data.LibraryDiffSyncCooldownExpiresAt);
                Assert.Null(GsDataManager.Data.LastIntegrationAccountsHash);
                Assert.Empty(GsDataManager.Data.PendingScrobbles);
            }
        }

        [Fact]
        public void SetInstallTokenIfActive_WhenNotOptedOut_StoresToken() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                var stored = GsDataManager.SetInstallTokenIfActive("new-token-abc");

                Assert.True(stored);
                Assert.Equal("new-token-abc", GsDataManager.Data.InstallToken);
            }
        }

        [Fact]
        public void SetInstallTokenIfActive_WhenOptedOut_RejectsToken() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                GsDataManager.PerformOptOut();

                var stored = GsDataManager.SetInstallTokenIfActive("should-not-persist");

                Assert.False(stored);
                Assert.Null(GsDataManager.Data.InstallToken);
            }
        }

        [Fact]
        public void InstallIdForBody_WithNoToken_ReturnsInstallId() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                GsDataManager.Data.InstallToken = null;

                Assert.Equal(GsDataManager.Data.InstallID, GsDataManager.InstallIdForBody);
            }
        }

        [Fact]
        public void InstallIdForBody_WithToken_ReturnsNull() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                GsDataManager.Data.InstallToken = "active-token";

                Assert.Null(GsDataManager.InstallIdForBody);
            }
        }

        [Fact]
        public void PerformOptOut_ClearsInstallToken() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                GsDataManager.Data.InstallToken = "token-to-clear";
                GsDataManager.Save();

                GsDataManager.PerformOptOut();

                Assert.Null(GsDataManager.Data.InstallToken);
            }
        }
        [Fact]
        public void MutateAndSave_AtomicallyUpdatesMultipleFields() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                GsDataManager.MutateAndSave(d => {
                    d.LastLibraryHash = "abc123";
                    d.LastSyncGameCount = 42;
                    d.SyncCooldownExpiresAt = null;
                });

                Assert.Equal("abc123", GsDataManager.Data.LastLibraryHash);
                Assert.Equal(42, GsDataManager.Data.LastSyncGameCount);
                Assert.Null(GsDataManager.Data.SyncCooldownExpiresAt);

                // Verify persisted to disk by re-loading
                GsDataManager.Initialize(temp.Path, null);
                Assert.Equal("abc123", GsDataManager.Data.LastLibraryHash);
                Assert.Equal(42, GsDataManager.Data.LastSyncGameCount);
            }
        }

        [Fact]
        public void PeekPendingScrobbles_ReturnsSnapshotWithoutRemoving() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                GsDataManager.EnqueuePendingScrobble(new PendingScrobble {
                    Type = "start",
                    QueuedAt = DateTime.UtcNow
                });

                var peeked = GsDataManager.PeekPendingScrobbles();
                Assert.Single(peeked);

                // Items should still be in the queue
                var peekedAgain = GsDataManager.PeekPendingScrobbles();
                Assert.Single(peekedAgain);
            }
        }

        [Fact]
        public void RemovePendingScrobble_RemovesSingleItemAndPersists() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                var item1 = new PendingScrobble { Type = "start", QueuedAt = DateTime.UtcNow };
                var item2 = new PendingScrobble { Type = "finish", QueuedAt = DateTime.UtcNow };
                GsDataManager.EnqueuePendingScrobble(item1);
                GsDataManager.EnqueuePendingScrobble(item2);

                Assert.Equal(2, GsDataManager.PeekPendingScrobbles().Count);

                GsDataManager.RemovePendingScrobble(item1);
                var remaining = GsDataManager.PeekPendingScrobbles();
                Assert.Single(remaining);
                Assert.Equal("finish", remaining[0].Type);

                // Verify persisted to disk
                GsDataManager.Initialize(temp.Path, null);
                Assert.Single(GsDataManager.PeekPendingScrobbles());
            }
        }

        [Fact]
        public void RotateInstallId_ClearsShownNotificationIds() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                GsDataManager.RecordShownNotifications(new List<string> { "n1", "n2" }, 100);
                Assert.Equal(2, GsDataManager.GetShownNotificationIds().Count);

                GsDataManager.RotateInstallId();

                Assert.Empty(GsDataManager.GetShownNotificationIds());
            }
        }
    }
}
