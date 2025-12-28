using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Nyamu.Core;

namespace Nyamu
{
    /// <summary>
    /// Project Settings provider for Nyamu MCP server configuration
    /// </summary>
    public class NyamuSettingsProvider : SettingsProvider
    {
        private SerializedObject _settings;

        public NyamuSettingsProvider(string path, SettingsScope scope)
            : base(path, scope) { }

        /// <summary>
        /// Check if settings are available
        /// </summary>
        public static bool IsSettingsAvailable()
        {
            return NyamuSettings.Instance != null;
        }

        /// <summary>
        /// Create the settings provider for Unity's Project Settings window
        /// </summary>
        [SettingsProvider]
        public static SettingsProvider CreateNyamuSettingsProvider()
        {
            if (IsSettingsAvailable())
            {
                var provider = new NyamuSettingsProvider("Project/Nyamu MCP Server", SettingsScope.Project);

                // Keywords for search functionality in Project Settings
                provider.keywords = new[] {
                    "Nyamu", "MCP", "Response", "Limit", "Character", "Truncation", "Message", "Server", "Port", "Log", "Logging", "Level", "Minimum"
                };

                return provider;
            }

            // Settings not available, don't create provider
            return null;
        }

        /// <summary>
        /// Render the settings UI
        /// </summary>
        public override void OnGUI(string searchContext)
        {
            // Initialize serialized object if needed
            if (_settings == null)
            {
                var settings = NyamuSettings.Instance;
                _settings = new SerializedObject(settings);
            }

            _settings.Update();

            // Response Configuration section
            EditorGUILayout.LabelField("Response Configuration", EditorStyles.boldLabel);

            // Help box with information
            EditorGUILayout.HelpBox(
                "Configure response character limits for the Nyamu MCP server. " +
                "The MCP server will truncate responses that exceed the specified limit to prevent overwhelming AI agents.",
                MessageType.Info);

            EditorGUILayout.Space();

            var characterLimitProp = _settings.FindProperty("responseCharacterLimit");
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(characterLimitProp, new GUIContent(
                "Response Character Limit",
                "Maximum characters in complete MCP response including JSON structure. Default: 25000"));

            // Validate response character limit
            if (characterLimitProp.intValue < 1000)
            {
                EditorGUILayout.HelpBox("Response Character Limit must be at least 1000 characters.", MessageType.Error);
            }

            var enableTruncationProp = _settings.FindProperty("enableTruncation");
            EditorGUILayout.PropertyField(enableTruncationProp, new GUIContent(
                "Enable Truncation",
                "When enabled, responses exceeding the limit will be truncated. When disabled, no limits are applied."));

            // Truncation Settings section
            var truncationMessageProp = _settings.FindProperty("truncationMessage");
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(truncationMessageProp, new GUIContent(
                "Truncation Message",
                "Message appended to truncated responses to indicate content was cut off."));

            // Validate truncation message length
            if (truncationMessageProp.stringValue.Length > 500)
            {
                EditorGUILayout.HelpBox("Truncation Message must be 500 characters or less.", MessageType.Error);
            }

            EditorGUILayout.Space();

            // Information about current settings
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Current Settings:", EditorStyles.label, GUILayout.Width(120));

                var settings = NyamuSettings.Instance;
                var overhead = EstimateResponseOverhead(settings);
                var availableForContent = Mathf.Max(0, settings.responseCharacterLimit - overhead);

                EditorGUILayout.LabelField($"~{availableForContent} chars available for content", GUILayout.ExpandWidth(true));
            }

            EditorGUILayout.Space();

            // Logging Configuration section
            EditorGUILayout.LabelField("Logging Configuration", EditorStyles.boldLabel);

            var minLogLevelProp = _settings.FindProperty("minLogLevel");
            if (minLogLevelProp != null)
            {
                EditorGUILayout.PropertyField(minLogLevelProp, new GUIContent(
                    "Minimum Log Level",
                    "Only logs at or above this level will be emitted."));
            }
            else
            {
                // Fallback (prevents NullReferenceException if Unity can't resolve the SerializedProperty)
                var settings = NyamuSettings.Instance;
                EditorGUI.BeginChangeCheck();
                var newValue = (NyamuLogger.LogLevel)EditorGUILayout.EnumPopup(
                    new GUIContent("Minimum Log Level", "Only logs at or above this level will be emitted."),
                    settings.minLogLevel);

                if (EditorGUI.EndChangeCheck())
                {
                    settings.minLogLevel = newValue;
                    EditorUtility.SetDirty(settings);
                }
            }

            EditorGUILayout.Space();

            // Server Configuration section
            EditorGUILayout.LabelField("Server Configuration", EditorStyles.boldLabel);

            // Help box explaining port management
            EditorGUILayout.HelpBox(
                "Nyamu automatically assigns unique ports to each project. Enable 'Manual Port' " +
                "to specify a custom port.",
                MessageType.Info);

            EditorGUILayout.Space(5);

            // Manual Port Mode checkbox
            var manualPortModeProp = _settings.FindProperty("manualPortMode");
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(manualPortModeProp, new GUIContent(
                "Manual Port",
                "Enable to manually specify the server port. When disabled, Nyamu automatically assigns a unique port."));

            var manualModeChanged = EditorGUI.EndChangeCheck();

            EditorGUILayout.Space(5);

            // Server Port field
            var serverPortProp = _settings.FindProperty("serverPort");
            var settingsInstance = NyamuSettings.Instance;

            if (settingsInstance.manualPortMode)
            {
                // Manual mode - editable field
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(serverPortProp, new GUIContent(
                    "Server Port",
                    "TCP port for the Nyamu MCP server"));

                // Validate port range
                if (serverPortProp.intValue < 1024 || serverPortProp.intValue > 65535)
                {
                    EditorGUILayout.HelpBox("Server Port must be between 1024 and 65535.", MessageType.Error);
                }

                // Check if port is potentially in use
                if (!NyamuProjectRegistry.IsPortAvailable(serverPortProp.intValue))
                {
                    EditorGUILayout.HelpBox(
                        $"Warning: Port {serverPortProp.intValue} may be in use by another project or application. " +
                        "The server may fail to start.",
                        MessageType.Warning);
                }
            }
            else
            {
                // Auto mode - read-only display
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.PropertyField(serverPortProp, new GUIContent(
                    "Server Port",
                    "Port automatically assigned for this project"));
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.LabelField("Auto-Assigned", EditorStyles.miniLabel);
            }

            // "Get Free Port" button
            EditorGUILayout.Space(5);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Get Free Port", GUILayout.Width(120)))
                {
                    settingsInstance.AssignFreePort();
                    _settings.Update();

                    EditorUtility.DisplayDialog("Free Port Assigned",
                        $"Port {settingsInstance.serverPort} has been assigned.\n\n" +
                        "Remember to save settings and restart your coding agent or reconnect the Nyamu MCP tool.",
                        "OK");
                }
            }

            EditorGUILayout.Space();

            // Reconnection reminder
            EditorGUILayout.HelpBox(
                "After changing the server port, you must:\n" +
                "1. Save the settings\n" +
                "2. Restart your coding agent (e.g., Claude Code)\n" +
                "   OR reconnect the Nyamu MCP tool in your agent",
                MessageType.Warning);

            // Buttons section
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reset to Defaults", GUILayout.Width(150)))
                {
                    if (EditorUtility.DisplayDialog("Reset Settings",
                        "Reset all Nyamu settings to default values?", "Reset", "Cancel"))
                    {
                        NyamuSettings.Instance.ResetToDefaults();
                        _settings.Update(); // Refresh the serialized object
                    }
                }

                if (GUILayout.Button("Reload from Disk", GUILayout.Width(150)))
                {
                    NyamuSettings.Instance.Reload();
                    _settings.Update(); // Refresh the serialized object
                }

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Save Settings", GUILayout.Width(100)))
                {
                    // Get current values before validation
                    var currentSettings = NyamuSettings.Instance;
                    int originalPort = currentSettings.serverPort;
                    int originalLimit = currentSettings.responseCharacterLimit;
                    string originalMessage = currentSettings.truncationMessage;

                    // Validate settings before saving
                    string validationMessage = ValidateSettings(originalPort, originalLimit, originalMessage);
                    if (!string.IsNullOrEmpty(validationMessage))
                    {
                        EditorUtility.DisplayDialog("Validation Error",
                            validationMessage, "OK");
                    }
                    else
                    {
                        NyamuSettings.Instance.Save();
                        EditorUtility.DisplayDialog("Settings Saved",
                            "Nyamu settings have been saved successfully.", "OK");
                    }
                }
            }

            // Apply any changes made in the inspector
            _settings.ApplyModifiedProperties();
        }

        /// <summary>
        /// Validate settings before saving
        /// </summary>
        private string ValidateSettings(int port, int characterLimit, string truncationMessage)
        {
            string errorMessage = "";

            // Validate response character limit
            if (characterLimit < 1000)
            {
                errorMessage += "- Response Character Limit must be at least 1000 characters.\n";
            }

            // Validate truncation message length
            if (truncationMessage.Length > 500)
            {
                errorMessage += "- Truncation Message must be 500 characters or less.\n";
            }

            // Validate server port range
            if (port < 1024 || port > 65535)
            {
                errorMessage += "- Server Port must be between 1024 and 65535.\n";
            }

            return string.IsNullOrEmpty(errorMessage) ? null : errorMessage.Trim();
        }

        /// <summary>
        /// Estimate the overhead of MCP response structure
        /// </summary>
        private int EstimateResponseOverhead(NyamuSettings settings)
        {
            // Estimate JSON structure overhead for typical MCP response
            var sampleResponse = "{\"jsonrpc\":\"2.0\",\"id\":999999,\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"\"}]}}";
            var jsonOverhead = sampleResponse.Length;

            // Add truncation message length
            var truncationOverhead = settings.enableTruncation ? settings.truncationMessage.Length : 0;

            // Add some buffer for safety
            return jsonOverhead + truncationOverhead + 50;
        }

        /// <summary>
        /// Called when the provider is activated
        /// </summary>
        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            // Ensure settings instance exists when provider is activated
            var settings = NyamuSettings.Instance;
            _settings = new SerializedObject(settings);
        }

        /// <summary>
        /// Called when the provider is deactivated
        /// </summary>
        public override void OnDeactivate()
        {
            // Save any pending changes when leaving the settings page
            if (_settings != null && _settings.hasModifiedProperties)
            {
                _settings.ApplyModifiedProperties();
                NyamuSettings.Instance.Save();
            }
        }
    }
}
