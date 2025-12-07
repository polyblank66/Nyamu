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
        [Header("Response Configuration")]
        [Tooltip("Maximum characters in complete MCP response (default: 25000)")]
        public int responseCharacterLimit = 25000;

        [Tooltip("Enable response truncation when limit is exceeded")]
        public bool enableTruncation = true;

        [Header("Truncation Settings")]
        [Tooltip("Message appended when response is truncated")]
        [TextArea(2, 3)]
        public string truncationMessage = "\n\n... (response truncated due to length limit)";

        [Header("Debug Configuration")]
        [Tooltip("Enable debug logging for NyamuServer HTTP handlers")]
        public bool enableDebugLogs = false;

        [Header("Server Configuration")]
        [Tooltip("TCP port for the Nyamu MCP server (default: 17932)")]
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
            // Try to load existing settings from Editor folder
            var settings = AssetDatabase.LoadAssetAtPath<NyamuSettings>("Assets/Editor/NyamuSettings.asset");

            if (settings == null)
            {
                // Create new settings with defaults
                settings = CreateInstance<NyamuSettings>();

                // Ensure Editor directory exists
                var editorPath = "Assets/Editor";
                if (!Directory.Exists(editorPath))
                {
                    Directory.CreateDirectory(editorPath);
                    AssetDatabase.Refresh();
                }

                // Save to Assets/Editor folder
                var assetPath = Path.Combine(editorPath, "NyamuSettings.asset");
                AssetDatabase.CreateAsset(settings, assetPath);
                AssetDatabase.SaveAssets();

                Debug.Log("Created new Nyamu settings with default values at " + assetPath);
            }

            return settings;
        }

        /// <summary>
        /// Save the current settings
        /// </summary>
        public void Save()
        {
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Reset settings to defaults
        /// </summary>
        public void ResetToDefaults()
        {
            responseCharacterLimit = 25000;
            enableTruncation = true;
            truncationMessage = "\n\n... (response truncated due to length limit)";
            enableDebugLogs = false;
            serverPort = 17932;
            Save();
        }

        /// <summary>
        /// Validate settings when changed in inspector
        /// </summary>
        private void OnValidate()
        {
            // Ensure minimum limits
            if (responseCharacterLimit < 1000)
                responseCharacterLimit = 1000;

            // Ensure truncation message is not too long (reserve space for content)
            if (truncationMessage.Length > 500)
                truncationMessage = truncationMessage.Substring(0, 500);

            // Ensure valid port range
            if (serverPort < 1024 || serverPort > 65535)
                serverPort = 17932;
        }
    }
}
