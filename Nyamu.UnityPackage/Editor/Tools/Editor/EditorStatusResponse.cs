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
    }
}
