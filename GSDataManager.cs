using System;

namespace GsPlugin {
    public static class GSDataManager {
        private static GSDataStorage storage;
        private static GSData data;

        /// <summary>
        /// Initializes the custom data manager.
        /// You must call this method (typically on plugin initialization)
        /// and pass in your plugin's user data folder.
        /// </summary>
        /// <param name="folderPath">The folder path where the custom data file will be stored.</param>
        public static void Initialize(string folderPath, string oldID) {
            storage = new GSDataStorage(folderPath);
            data = storage.Load();

            if (string.IsNullOrEmpty(oldID)) {
                if (string.IsNullOrEmpty(data.InstallID)) {
                    data.InstallID = Guid.NewGuid().ToString();
                    Save();
                }
            }
            else {
                data.InstallID = oldID;
                Save();
            }
            // If InstallID is missing or empty, create a new one and save.
           
        }

        /// <summary>
        /// Gets the current custom data.
        /// </summary>
        public static GSData Data => data;

        /// <summary>
        /// Saves the current custom data to disk.
        /// </summary>
        public static void Save() {
            storage?.Save(data);
        }
    }
}
