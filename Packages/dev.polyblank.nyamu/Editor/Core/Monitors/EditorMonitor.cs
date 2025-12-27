using System;
using System.Collections.Generic;
using UnityEditor;
using Nyamu.Core.StateManagers;

namespace Nyamu.Core.Monitors
{
    // Monitors Unity Editor state and processes main thread actions
    public class EditorMonitor
    {
        readonly EditorStateManager _state;
        readonly Queue<Action> _mainThreadActionQueue;
        readonly SettingsMonitor _settingsMonitor;

        public EditorMonitor(EditorStateManager state, Queue<Action> mainThreadActionQueue, SettingsMonitor settingsMonitor)
        {
            _state = state;
            _mainThreadActionQueue = mainThreadActionQueue;
            _settingsMonitor = settingsMonitor;
        }

        public void Initialize()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        public void Cleanup()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        void OnEditorUpdate()
        {
            // Execute main thread actions
            while (_mainThreadActionQueue.Count > 0)
                _mainThreadActionQueue.Dequeue().Invoke();

            // Update play mode state (thread-safe)
            _state.IsPlaying = EditorApplication.isPlaying;

            // Refresh cached settings periodically
            _settingsMonitor.Update();
        }
    }
}
