namespace Nyamu.Core.StateManagers
{
    // Manages asset refresh state with thread-safe access
    public class AssetStateManager
    {
        readonly object _lock = new object();
        bool _isRefreshing = false;
        bool _isMonitoringRefresh = false;
        bool _unityIsUpdating = false;

        public object Lock => _lock;

        public bool IsRefreshing
        {
            get { lock (_lock) return _isRefreshing; }
            set { lock (_lock) _isRefreshing = value; }
        }

        public bool IsMonitoringRefresh
        {
            get { lock (_lock) return _isMonitoringRefresh; }
            set { lock (_lock) _isMonitoringRefresh = value; }
        }

        public bool UnityIsUpdating
        {
            get { lock (_lock) return _unityIsUpdating; }
            set { lock (_lock) _unityIsUpdating = value; }
        }
    }
}
