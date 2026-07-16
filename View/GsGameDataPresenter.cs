using System;
using System.Threading;
using System.Threading.Tasks;
using GsPlugin.Api;
using GsPlugin.Infrastructure;
using Playnite;

namespace GsPlugin.View {
    /// <summary>
    /// Exposes this install's Game Scrobbler data for a single game so a theme can
    /// render it in its own visual language.
    ///
    /// Playnite wraps this in a PluginGameDataPresenterWrapper and exposes it on the
    /// game presenter's dynamic GameData/GameDataExists objects, so a theme binds
    /// roughly as:
    ///
    ///   Text="{Binding GameData.GsGameDataPresenter.Impl.PlaytimeHours}"
    ///   Visibility="{Binding GameDataExists.GsGameDataPresenter}"
    ///
    /// Data is fetched lazily on LoadedInDetailsView rather than in the constructor:
    /// Playnite may construct a presenter per game while scrolling, and issuing an
    /// HTTP request per constructed instance would hammer the API for games the user
    /// never actually looks at.
    /// </summary>
    public class GsGameDataPresenter : PluginGameDataPresenter {
        private readonly IGsApiClient _apiClient;
        private readonly string _playniteGameId;
        private CancellationTokenSource _cts;
        private bool _loadStarted;

        private bool _hasData;
        private bool _isLoading;
        private long _playtimeSeconds;
        private long _playCount;
        private string _completionStatus;
        private int? _achievementsUnlocked;
        private int? _achievementsTotal;

        public GsGameDataPresenter(IGsApiClient apiClient, string playniteGameId) {
            _apiClient = apiClient;
            _playniteGameId = playniteGameId;
        }

        /// <summary>True once the server returned a synced record for this game.</summary>
        public bool HasData {
            get => _hasData;
            private set => SetProperty(ref _hasData, value);
        }

        /// <summary>True while the fetch is in flight, so themes can show a spinner.</summary>
        public bool IsLoading {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        public long PlaytimeSeconds {
            get => _playtimeSeconds;
            private set {
                if (SetProperty(ref _playtimeSeconds, value)) {
                    OnPropertyChanged(nameof(PlaytimeHours));
                }
            }
        }

        /// <summary>Playtime in hours, rounded to one decimal, for direct display.</summary>
        public double PlaytimeHours => Math.Round(_playtimeSeconds / 3600.0, 1);

        public long PlayCount {
            get => _playCount;
            private set => SetProperty(ref _playCount, value);
        }

        public string CompletionStatus {
            get => _completionStatus;
            private set => SetProperty(ref _completionStatus, value);
        }

        public int? AchievementsUnlocked {
            get => _achievementsUnlocked;
            private set {
                if (SetProperty(ref _achievementsUnlocked, value)) {
                    OnPropertyChanged(nameof(HasAchievements));
                }
            }
        }

        public int? AchievementsTotal {
            get => _achievementsTotal;
            private set {
                if (SetProperty(ref _achievementsTotal, value)) {
                    OnPropertyChanged(nameof(HasAchievements));
                }
            }
        }

        /// <summary>
        /// True only when the game actually has achievements, so a theme can hide the
        /// block instead of rendering a meaningless "0 of 0".
        /// </summary>
        public bool HasAchievements => (_achievementsTotal ?? 0) > 0;

        public override void LoadedInDetailsView(LoadedInDetailsViewArgs args) {
            base.LoadedInDetailsView(args);
            // Guard: Playnite can load the same presenter more than once (view
            // switches, theme reloads). The data is per-game and effectively static
            // between syncs, so fetching once is enough.
            if (_loadStarted) {
                return;
            }
            _loadStarted = true;
            _ = LoadAsync();
        }

        private async Task LoadAsync() {
            if (_apiClient == null || string.IsNullOrEmpty(_playniteGameId)) {
                return;
            }

            var cts = new CancellationTokenSource();
            _cts = cts;

            try {
                IsLoading = true;
                var res = await _apiClient.GetGameData(_playniteGameId).ConfigureAwait(false);

                // The view was torn down (user scrolled on) before the response landed.
                if (cts.IsCancellationRequested) {
                    return;
                }

                var game = res?.game;
                if (game == null) {
                    // Either a transport failure or an honest "not synced". Both mean
                    // the same thing to a theme: render nothing.
                    HasData = false;
                    return;
                }

                PlaytimeSeconds = game.playtime_sec;
                PlayCount = game.play_count;
                CompletionStatus = game.completion_status_name;
                AchievementsUnlocked = game.achievement_count_unlocked;
                AchievementsTotal = game.achievement_count_total;
                HasData = true;
            }
            catch (Exception ex) {
                // Never let a passive theme surface take down the details view.
                GsLogger.Warn($"GsGameDataPresenter load failed: {ex.Message}");
                HasData = false;
            }
            finally {
                IsLoading = false;
            }
        }

        public override ValueTask DisposeAsync() {
            try {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;
            }
            catch (Exception ex) {
                GsLogger.Warn($"GsGameDataPresenter dispose failed: {ex.Message}");
            }
            return base.DisposeAsync();
        }
    }
}
