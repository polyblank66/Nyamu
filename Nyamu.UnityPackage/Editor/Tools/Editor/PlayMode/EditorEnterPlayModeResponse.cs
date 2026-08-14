using System;

namespace Nyamu.Tools.Editor.PlayMode
{
    [Serializable]
    public class EditorEnterPlayModeResponse
    {
        public bool success;
        public string message;
        public string status;   // requested | already_playing | blocked | main_thread_timeout | error
        public bool wasPlaying;
    }
}
