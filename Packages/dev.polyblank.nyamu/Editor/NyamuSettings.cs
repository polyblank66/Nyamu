using UnityEngine;
using UnityEditor;
using System.IO;

namespace Nyamu
{
    /// <summary>
    /// Settings for Nyamu MCP server configuration
    /// </summary>
    [System.Serializable]
    public class NyamuSettings : ScriptableObject
    {
        private const string SettingsPath = ".nyamu/NyamuSettings.json";
        private const NyamuLog.LogLevel DefaultMinLogLevel = NyamuLog.LogLevel.Info;

        public int responseCharacterLimit = 25000;

        public bool enableTruncation = true;

        [TextArea(2, 3)]
        public string truncationMessage = "\n\n... (response truncated due to length limit)";

        public NyamuLog.LogLevel minLogLevel = DefaultMinLogLevel;

        public int serverPort = 17932;

        // Singleton pattern for easy access
        private static NyamuSettings _instance;

        /// <summary>
        /// Get the singleton instance of NyamuSettings
        /// </summary>
        public static NyamuSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = LoadOrCreateSettings();
                }
                return _instance;
            }
        }

        /// <summary>
        /// Load existing settings or create new ones with defaults
        /// </summary>
        private static NyamuSettings LoadOrCreateSettings()
        {
            EnsureSettingsDirectory();

            var settings = CreateInstance<NyamuSettings>();

            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                EditorJsonUtility.FromJsonOverwrite(json, settings);
            }
            else
            {
                var json = EditorJsonUtility.ToJson(settings, true);
                File.WriteAllText(SettingsPath, json);
                NyamuLog.Info($"[Nyamu][Settings] Created new Nyamu settings with default values at {SettingsPath}");
            }

            return settings;
        }

        /// <summary>
        /// Save the current settings
        /// </summary>
        public void Save()
        {
            // Read old port from disk before saving
            var oldPort = serverPort;
            if (File.Exists(SettingsPath))
            {
                try
                {
                    var oldJson = File.ReadAllText(SettingsPath);
                    var oldSettings = CreateInstance<NyamuSettings>();
                    EditorJsonUtility.FromJsonOverwrite(oldJson, oldSettings);
                    oldPort = oldSettings.serverPort;
                }
                catch { }
            }

            EnsureSettingsDirectory();
            var json = EditorJsonUtility.ToJson(this, true);
            File.WriteAllText(SettingsPath, json);
            NyamuLog.Info("[Nyamu][Settings] Nyamu settings saved to " + SettingsPath);

#if UNITY_EDITOR_WIN
            // Regenerate bat file with updated port
            NyamuBatGenerator.RegenerateBatFile();
#endif

            // Restart server if port changed
            if (oldPort != serverPort)
                Server.Restart();
        }

        /// <summary>
        /// Reload settings from disk
        /// </summary>
        public void Reload()
        {
            if (File.Exists(SettingsPath))
            {
                var oldPort = serverPort;

                var json = File.ReadAllText(SettingsPath);
                EditorJsonUtility.FromJsonOverwrite(json, this);
                NyamuLog.Info("[Nyamu][Settings] Nyamu settings reloaded from " + SettingsPath);

#if UNITY_EDITOR_WIN
                // Regenerate bat file with updated port
                NyamuBatGenerator.RegenerateBatFile();
#endif

                // Restart server if port changed
                if (oldPort != serverPort)
                    Server.Restart();
            }
            else
            {
                NyamuLog.Warning("[Nyamu][Settings] Settings file not found at " + SettingsPath);
            }
        }

        private static void EnsureSettingsDirectory()
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        /// <summary>
        /// Reset settings to defaults
        /// </summary>
        public void ResetToDefaults()
        {
            responseCharacterLimit = 25000;
            enableTruncation = true;
            truncationMessage = "\n\n... (response truncated due to length limit)";
            minLogLevel = DefaultMinLogLevel;
            serverPort = 17932;
            Save();
        }

        /// <summary>
        /// Validate settings when changed in inspector
        /// </summary>
        private void OnValidate()
        {
            // Note: Validation is now handled in the UI layer (NyamuSettingsProvider)
            // to ensure proper error messages are shown to the user.
            // This method is kept empty to prevent auto-correction before UI validation.
        }
    }
}
