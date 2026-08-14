using System;

namespace Nyamu.Core.StateManagers
{
    // Immutable snapshot of editor state, published atomically once per editor
    // update tick. All fields must come from the same tick: mixing values from
    // different ticks can manufacture a play mode transition that never happened
    // (see EditorMonitor.OnEditorUpdate for the derivation of the transition flags).
    public readonly struct EditorStateSnapshot
    {
        public readonly bool IsPlaying;
        public readonly bool IsPaused;
        public readonly bool IsEnteringPlayMode;
        public readonly bool IsExitingPlayMode;
        public readonly DateTime LastUpdateUtc;   // DateTime.MinValue = never sampled
        public readonly long LastUpdateStamp;     // Stopwatch.GetTimestamp(); 0 = never sampled

        public EditorStateSnapshot(bool isPlaying, bool isPaused, bool isEnteringPlayMode,
            bool isExitingPlayMode, DateTime lastUpdateUtc, long lastUpdateStamp)
        {
            IsPlaying = isPlaying;
            IsPaused = isPaused;
            IsEnteringPlayMode = isEnteringPlayMode;
            IsExitingPlayMode = isExitingPlayMode;
            LastUpdateUtc = lastUpdateUtc;
            LastUpdateStamp = lastUpdateStamp;
        }
    }

    // Manages editor state with thread-safe access
    public class EditorStateManager
    {
        private readonly object _lock = new();
        private EditorStateSnapshot _snapshot;

        // Whole-tuple read: prevents a handler thread from pairing fields sampled
        // on different editor update ticks.
        public EditorStateSnapshot Snapshot
        {
            get { lock (_lock) return _snapshot; }
        }

        // Called once per EditorApplication.update tick, from the Unity main thread.
        public void SetSnapshot(bool isPlaying, bool isPaused, bool isEnteringPlayMode,
            bool isExitingPlayMode, DateTime lastUpdateUtc, long lastUpdateStamp)
        {
            lock (_lock)
                _snapshot = new EditorStateSnapshot(isPlaying, isPaused, isEnteringPlayMode,
                    isExitingPlayMode, lastUpdateUtc, lastUpdateStamp);
        }
    }
}
