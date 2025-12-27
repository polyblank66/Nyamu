using System;
using Nyamu.Core.StateManagers;
using Nyamu.Tools.Settings;

namespace Nyamu.Core.Monitors
{
    // Monitors and refreshes cached Nyamu settings periodically
    public class SettingsMonitor
    {
        readonly SettingsStateManager _state;

        public SettingsMonitor(SettingsStateManager state)
        {
            _state = state;
        }

        public void Update()
        {
            // Refresh cached settings periodically (every 2 seconds)
            if ((DateTime.Now - _state.LastSettingsRefresh).TotalSeconds >= 2.0)
            {
                RefreshCachedSettings();
                _state.LastSettingsRefresh = DateTime.Now;
            }
        }

        void RefreshCachedSettings()
        {
            try
            {
                var settings = NyamuSettings.Instance;
                _state.CachedSettings = new McpSettingsResponse
                {
                    responseCharacterLimit = settings.responseCharacterLimit,
                    enableTruncation = settings.enableTruncation,
                    truncationMessage = settings.truncationMessage
                };

                // Refresh logger's cached min log level to avoid thread-safety issues
                NyamuLogger.RefreshMinLogLevel();
            }
            catch (Exception ex)
            {
                NyamuLogger.LogError($"[Nyamu][Server] Failed to refresh cached Nyamu settings: {ex.Message}");
            }
        }
    }
}
