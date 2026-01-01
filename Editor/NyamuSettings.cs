using UnityEngine;
using UnityEditor;
using System.IO;
using Nyamu.Core;

namespace Nyamu
{
    /// <summary>
    /// Settings for Nyamu MCP server configuration
    /// </summary>
    [System.Serializable]
    public class NyamuSettings : ScriptableObject
    {
        private const string SettingsPath = ".nyamu/NyamuSettings.json";
        private const NyamuLogger.LogLevel DefaultMinLogLevel = NyamuLogger.LogLevel.Info;

        public int responseCharacterLimit = 25000;

        public bool enableTruncation = true;

        [TextArea(2, 3)]
        public string truncationMessage = "\n\n... (response truncated due to length limit)";

        public NyamuLogger.LogLevel minLogLevel = DefaultMinLogLevel;

        public int serverPort = 17932;

        public bool manualPortMode = false;

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

                // Auto port assignment on load
                if (!settings.manualPortMode)
                {
                    var registeredPort = NyamuProjectRegistry.GetRegisteredPort();

                    if (registeredPort.HasValue)
                    {
                        // Project already has registered port - use it
                        settings.serverPort = registeredPort.Value;
                    }
                    else
                    {
                        // First time for this project - find free port
                        var freePort = NyamuProjectRegistry.FindFreePort();
                        settings.serverPort = freePort;
                        NyamuProjectRegistry.RegisterProjectPort(freePort);

                        // Save immediately to persist auto-assigned port
                        var updatedJson = EditorJsonUtility.ToJson(settings, true);
                        File.WriteAllText(SettingsPath, updatedJson);

                        NyamuLogger.LogInfo($"[Nyamu][Settings] Auto-assigned port {freePort} for this project");
                    }
                }
                else
                {
                    // Manual mode - just register the configured port
                    NyamuProjectRegistry.RegisterProjectPort(settings.serverPort);
                }
            }
            else
            {
                // First time initialization
                if (!settings.manualPortMode)
                {
                    var freePort = NyamuProjectRegistry.FindFreePort();
                    settings.serverPort = freePort;
                    NyamuProjectRegistry.RegisterProjectPort(freePort);
                    NyamuLogger.LogInfo($"[Nyamu][Settings] Auto-assigned port {freePort} for new project");
                }

                var json = EditorJsonUtility.ToJson(settings, true);
                File.WriteAllText(SettingsPath, json);
                NyamuLogger.LogInfo($"[Nyamu][Settings] Created new Nyamu settings with default values at {SettingsPath}");
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

            // Validate and register port
            if (manualPortMode)
            {
                // Manual mode - validate port availability
                if (!NyamuProjectRegistry.IsPortAvailable(serverPort))
                {
                    NyamuLogger.LogWarning(
                        $"[Nyamu][Settings] Port {serverPort} may be in use by another project or application. " +
                        "Proceeding anyway.");
                }
            }

            // Register port in global registry
            NyamuProjectRegistry.RegisterProjectPort(serverPort);

            EnsureSettingsDirectory();
            var json = EditorJsonUtility.ToJson(this, true);
            File.WriteAllText(SettingsPath, json);
            NyamuLogger.LogInfo("[Nyamu][Settings] Nyamu settings saved to " + SettingsPath);

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
                NyamuLogger.LogInfo("[Nyamu][Settings] Nyamu settings reloaded from " + SettingsPath);

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
                NyamuLogger.LogWarning("[Nyamu][Settings] Settings file not found at " + SettingsPath);
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
        /// Find and apply a free port. Used by UI "Get Free Port" button.
        /// </summary>
        public void AssignFreePort()
        {
            var freePort = NyamuProjectRegistry.FindFreePort();
            serverPort = freePort;
            NyamuLogger.LogInfo($"[Nyamu][Settings] Assigned free port: {freePort}");
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

            // Reset to auto mode with free port
            manualPortMode = false;
            serverPort = NyamuProjectRegistry.FindFreePort();

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
