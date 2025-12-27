using System;

namespace Nyamu.Core.StateManagers
{
    // Manages settings cache state with thread-safe access
    public class SettingsStateManager
    {
        readonly object _lock = new object();
        McpSettingsResponse _cachedSettings;
        DateTime _lastSettingsRefresh = DateTime.MinValue;

        public object Lock => _lock;

        public McpSettingsResponse CachedSettings
        {
            get { lock (_lock) return _cachedSettings; }
            set { lock (_lock) _cachedSettings = value; }
        }

        public DateTime LastSettingsRefresh
        {
            get { lock (_lock) return _lastSettingsRefresh; }
            set { lock (_lock) _lastSettingsRefresh = value; }
        }
    }
}
