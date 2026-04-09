using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GsPlugin.Models {
    /// <summary>
    /// Represents the settings data model for the GS Plugin.
    /// Contains all user-configurable options and runtime state.
    /// </summary>
    public class GsPluginSettings : ObservableObject {
        private string _theme = "Dark";
        public string Theme {
            get => _theme;
            set => SetProperty(ref _theme, value);
        }

        private bool _disableSentry = false;
        public bool DisableSentry {
            get => _disableSentry;
            set {
                _disableSentry = value;
                OnPropertyChanged();
            }
        }
        private bool _disableScrobbling = false;
        public bool DisableScrobbling {
            get => _disableScrobbling;
            set {
                _disableScrobbling = value;
                OnPropertyChanged();
            }
        }

        private bool _disablePostHog = false;
        public bool DisablePostHog {
            get => _disablePostHog;
            set {
                _disablePostHog = value;
                OnPropertyChanged();
            }
        }

        private bool _newDashboardExperience = false;
        public bool NewDashboardExperience {
            get => _newDashboardExperience;
            set {
                _newDashboardExperience = value;
                OnPropertyChanged();
            }
        }

        private bool _showUpdateNotifications = true;
        public bool ShowUpdateNotifications {
            get => _showUpdateNotifications;
            set {
                _showUpdateNotifications = value;
                OnPropertyChanged();
            }
        }

        private bool _showImportantNotifications = true;
        public bool ShowImportantNotifications {
            get => _showImportantNotifications;
            set {
                _showImportantNotifications = value;
                OnPropertyChanged();
            }
        }

        private string _linkToken = "";
        public string LinkToken {
            get => _linkToken;
            set {
                _linkToken = value;
                OnPropertyChanged();
            }
        }
        private bool _isLinking = false;
        public bool IsLinking {
            get => _isLinking;
            set {
                _isLinking = value;
                OnPropertyChanged();
            }
        }
        private string _linkStatusMessage = "";
        public string LinkStatusMessage {
            get => _linkStatusMessage;
            set {
                _linkStatusMessage = value;
                OnPropertyChanged();
            }
        }

        private string _tokenCountdown = "";
        public string TokenCountdown {
            get => _tokenCountdown;
            set {
                _tokenCountdown = value;
                OnPropertyChanged();
            }
        }

        private bool _isDeleting = false;
        public bool IsDeleting {
            get => _isDeleting;
            set {
                _isDeleting = value;
                OnPropertyChanged();
            }
        }
        private string _deleteStatusMessage = "";
        public string DeleteStatusMessage {
            get => _deleteStatusMessage;
            set {
                _deleteStatusMessage = value;
                OnPropertyChanged();
            }
        }
    }
}
