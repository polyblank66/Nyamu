using System;

namespace Nyamu.Tools.Editor
{
    [Serializable]
    public class EditorStatusResponse
    {
        public bool isCompiling;
        public bool isRunningTests;
        public bool isPlaying;
        public bool isRefreshing;
        public bool isWaitingForCompilation;
        public bool isPaused;
        public bool isEnteringPlayMode;     // transition requested, Play Mode not yet running
        public bool isExitingPlayMode;      // leaving Play Mode, Edit Mode not yet restored
        public double stateAgeSeconds;      // seconds since the last EditorApplication.update tick; -1 if never sampled
        public bool isStateStale;           // true when the cached state can no longer be trusted
        public string lastEditorUpdateUtc;  // ISO 8601 ("o"); "" if never sampled
    }
}
