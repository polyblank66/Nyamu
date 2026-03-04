namespace Nyamu.Core.StateManagers
{
    // Manages editor state with thread-safe access
    public class EditorStateManager
    {
        private readonly object _lock = new();
        private bool _isPlaying;

        public object Lock => _lock;

        public bool IsPlaying
        {
            get { lock (_lock) return _isPlaying; }
            set { lock (_lock) _isPlaying = value; }
        }
    }
}
