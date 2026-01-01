namespace Nyamu.Core.StateManagers
{
    // Manages editor state with thread-safe access
    public class EditorStateManager
    {
        readonly object _lock = new object();
        bool _isPlaying = false;

        public object Lock => _lock;

        public bool IsPlaying
        {
            get { lock (_lock) return _isPlaying; }
            set { lock (_lock) _isPlaying = value; }
        }
    }
}
