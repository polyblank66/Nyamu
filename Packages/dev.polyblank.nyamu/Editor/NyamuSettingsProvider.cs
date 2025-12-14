using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

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
                    "Nyamu", "MCP", "Response", "Limit", "Character", "Truncation", "Message", "Server", "Port", "Debug", "Logs"
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

            // Debug Configuration section
            EditorGUILayout.LabelField("Debug Configuration", EditorStyles.boldLabel);

            var enableDebugLogsProp = _settings.FindProperty("enableDebugLogs");
            EditorGUILayout.PropertyField(enableDebugLogsProp, new GUIContent(
                "Enable Debug Logs",
                "Enable debug logging for NyamuServer HTTP handlers"));

            EditorGUILayout.Space();

            // Server Configuration section
            EditorGUILayout.LabelField("Server Configuration", EditorStyles.boldLabel);

            var serverPortProp = _settings.FindProperty("serverPort");
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serverPortProp, new GUIContent(
                "Server Port",
                "TCP port for the Nyamu MCP server (default: 17932)"));

            // Validate server port range
            if (serverPortProp.intValue < 1024 || serverPortProp.intValue > 65535)
            {
                EditorGUILayout.HelpBox("Server Port must be between 1024 and 65535.", MessageType.Error);
            }

            EditorGUILayout.Space();

            // Information about current settings
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Current Settings:", EditorStyles.boldLabel, GUILayout.Width(120));

                var settings = NyamuSettings.Instance;
                var overhead = EstimateResponseOverhead(settings);
                var availableForContent = Mathf.Max(0, settings.responseCharacterLimit - overhead);

                EditorGUILayout.LabelField($"~{availableForContent} chars available for content", GUILayout.ExpandWidth(true));
            }

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
