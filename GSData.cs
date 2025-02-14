using System;

namespace GsPlugin {
    /// <summary>
    /// Holds custom persistent data.
    /// </summary>
    public class GSData {
        public string InstallID { get; set; } = null;
        public string SessionId { get; set; } = null;
        public Boolean IsDark { get; set; } = false;
    }
}
