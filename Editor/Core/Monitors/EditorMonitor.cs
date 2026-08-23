using System;
using System.Diagnostics;
using UnityEditor;
using Nyamu.Core.StateManagers;
using Nyamu.Core.Interfaces;

namespace Nyamu.Core.Monitors
{
    // Monitors Unity Editor state and processes main thread actions
    public class EditorMonitor
    {
        private readonly EditorStateManager _state;
        private readonly IUnityThreadExecutor _unityThreadExecutor;
        private readonly SettingsMonitor _settingsMonitor;
        private readonly PlayModeBackgroundGuard _backgroundGuard;

        public EditorMonitor(EditorStateManager state, IUnityThreadExecutor unityThreadExecutor,
            SettingsMonitor settingsMonitor, PlayModeBackgroundGuard backgroundGuard)
        {
            _state = state;
            _unityThreadExecutor = unityThreadExecutor;
            _settingsMonitor = settingsMonitor;
            _backgroundGuard = backgroundGuard;
        }

        public void Initialize()
        {
            _backgroundGuard.Initialize();
            EditorApplication.update += OnEditorUpdate;
        }

        public void Cleanup()
        {
            EditorApplication.update -= OnEditorUpdate;
            _backgroundGuard.Cleanup();
        }

        private void OnEditorUpdate()
        {
            // Execute main thread actions first, so a play mode request issued in
            // this same tick is already reflected in the sample taken below.
            _unityThreadExecutor.Process();

            // Sample editor state. Unity only exposes the transition as the pair
            // (isPlaying, isPlayingOrWillChangePlaymode); read together, within
            // this single tick, they identify the phase unambiguously:
            //
            //   isPlaying | willChange | phase                | reported as
            //   ----------|------------|----------------------|---------------------
            //   false     | false      | idle in Edit Mode    | both flags false
            //   false     | true       | entering Play Mode   | isEnteringPlayMode
            //   true      | true       | playing              | both flags false
            //   true      | false      | exiting Play Mode    | isExitingPlayMode
            var isPlaying = EditorApplication.isPlaying;
            var willChangePlaymode = EditorApplication.isPlayingOrWillChangePlaymode;
            var isPaused = EditorApplication.isPaused;
            var isEnteringPlayMode = willChangePlaymode && !isPlaying;
            var isExitingPlayMode = isPlaying && !willChangePlaymode;

            // Publish atomically (thread-safe)
            _state.SetSnapshot(isPlaying, isPaused, isEnteringPlayMode, isExitingPlayMode,
                DateTime.UtcNow, Stopwatch.GetTimestamp());

            // Reuses this tick's sample. Without it Unity suspends the loop that runs this very
            // method as soon as a playing Editor loses focus, stranding the action queue.
            _backgroundGuard.Tick(isPlaying);

            // Refresh cached settings periodically
            _settingsMonitor.Update();
        }
    }
}
