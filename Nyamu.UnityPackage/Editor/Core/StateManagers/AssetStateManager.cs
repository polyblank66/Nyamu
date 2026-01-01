namespace Nyamu.Core.StateManagers
{
    // Manages asset refresh state with thread-safe access
    public class AssetStateManager
    {
        readonly object _lock = new object();
        bool _isRefreshing = false;
        bool _isMonitoringRefresh = false;
        bool _unityIsUpdating = false;
        bool _isWaitingForCompilation = false;
        System.DateTime _refreshRequestTime = System.DateTime.MinValue;
        System.DateTime _refreshCompletedTime = System.DateTime.MinValue;

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

        public bool IsWaitingForCompilation
        {
            get { lock (_lock) return _isWaitingForCompilation; }
            set { lock (_lock) _isWaitingForCompilation = value; }
        }

        public System.DateTime RefreshRequestTime
        {
            get { lock (_lock) return _refreshRequestTime; }
            set { lock (_lock) _refreshRequestTime = value; }
        }

        public System.DateTime RefreshCompletedTime
        {
            get { lock (_lock) return _refreshCompletedTime; }
            set { lock (_lock) _refreshCompletedTime = value; }
        }
    }
}
