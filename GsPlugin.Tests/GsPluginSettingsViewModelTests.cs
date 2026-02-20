using System;
using System.IO;
using Xunit;

namespace GsPlugin.Tests {
    public class GsPluginSettingsViewModelTests : IDisposable {
        private readonly string _tempDir;

        public GsPluginSettingsViewModelTests() {
            _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDir);
            GsDataManager.Initialize(_tempDir, null);
        }

        public void Dispose() {
            try {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch { }
        }

        private void SetSyncData(DateTime? lastSyncAt, int? lastSyncGameCount) {
            GsDataManager.Data.LastSyncAt = lastSyncAt;
            GsDataManager.Data.LastSyncGameCount = lastSyncGameCount;
        }

        [Fact]
        public void LastSyncStatus_ReturnsNeverSynced_WhenBothNull() {
            SetSyncData(null, null);
            Assert.Equal("Never synced", GsPluginSettingsViewModel.LastSyncStatus);
        }

        [Fact]
        public void LastSyncStatus_ReturnsNeverSynced_WhenOnlyLastSyncAtIsNull() {
            SetSyncData(null, 10);
            Assert.Equal("Never synced", GsPluginSettingsViewModel.LastSyncStatus);
        }

        [Fact]
        public void LastSyncStatus_ReturnsNeverSynced_WhenOnlyGameCountIsNull() {
            SetSyncData(DateTime.UtcNow.AddMinutes(-5), null);
            Assert.Equal("Never synced", GsPluginSettingsViewModel.LastSyncStatus);
        }

        [Fact]
        public void LastSyncStatus_ReturnsJustNow_WhenElapsedLessThan1Minute() {
            SetSyncData(DateTime.UtcNow.AddSeconds(-30), 50);
            Assert.Equal("Last synced: 50 games · just now", GsPluginSettingsViewModel.LastSyncStatus);
        }

        [Fact]
        public void LastSyncStatus_ReturnsMinutesAgo_WhenElapsed1Minute() {
            SetSyncData(DateTime.UtcNow.AddMinutes(-1), 100);
            Assert.Equal("Last synced: 100 games · 1 minute ago", GsPluginSettingsViewModel.LastSyncStatus);
        }

        [Fact]
        public void LastSyncStatus_ReturnsMinutesAgo_WhenElapsed30Minutes() {
            SetSyncData(DateTime.UtcNow.AddMinutes(-30), 200);
            Assert.Equal("Last synced: 200 games · 30 minutes ago", GsPluginSettingsViewModel.LastSyncStatus);
        }

        [Fact]
        public void LastSyncStatus_ReturnsMinutesAgo_WhenElapsed59Minutes() {
            SetSyncData(DateTime.UtcNow.AddMinutes(-59), 5);
            Assert.Equal("Last synced: 5 games · 59 minutes ago", GsPluginSettingsViewModel.LastSyncStatus);
        }

        [Fact]
        public void LastSyncStatus_ReturnsHoursAgo_WhenElapsed1Hour() {
            SetSyncData(DateTime.UtcNow.AddHours(-1), 300);
            Assert.Equal("Last synced: 300 games · 1 hour ago", GsPluginSettingsViewModel.LastSyncStatus);
        }

        [Fact]
        public void LastSyncStatus_ReturnsHoursAgo_WhenElapsed5Hours() {
            SetSyncData(DateTime.UtcNow.AddHours(-5), 75);
            Assert.Equal("Last synced: 75 games · 5 hours ago", GsPluginSettingsViewModel.LastSyncStatus);
        }

        [Fact]
        public void LastSyncStatus_ReturnsHoursAgo_WhenElapsed23Hours() {
            SetSyncData(DateTime.UtcNow.AddHours(-23), 10);
            Assert.Equal("Last synced: 10 games · 23 hours ago", GsPluginSettingsViewModel.LastSyncStatus);
        }

        [Fact]
        public void LastSyncStatus_ReturnsDaysAgo_WhenElapsed1Day() {
            SetSyncData(DateTime.UtcNow.AddDays(-1), 500);
            Assert.Equal("Last synced: 500 games · 1 day ago", GsPluginSettingsViewModel.LastSyncStatus);
        }

        [Fact]
        public void LastSyncStatus_ReturnsDaysAgo_WhenElapsed7Days() {
            SetSyncData(DateTime.UtcNow.AddDays(-7), 999);
            Assert.Equal("Last synced: 999 games · 7 days ago", GsPluginSettingsViewModel.LastSyncStatus);
        }

        [Fact]
        public void LastSyncStatus_FormatsGameCountWithThousandsSeparator() {
            SetSyncData(DateTime.UtcNow.AddSeconds(-10), 1234);
            Assert.Equal("Last synced: 1,234 games · just now", GsPluginSettingsViewModel.LastSyncStatus);
        }

        [Fact]
        public void LastSyncStatus_FormatsZeroGames() {
            SetSyncData(DateTime.UtcNow.AddSeconds(-10), 0);
            Assert.Equal("Last synced: 0 games · just now", GsPluginSettingsViewModel.LastSyncStatus);
        }
    }
}
