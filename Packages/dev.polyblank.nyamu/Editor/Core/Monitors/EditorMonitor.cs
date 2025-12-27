using System;
using System.Collections.Generic;
using UnityEditor;
using Nyamu.Core.StateManagers;
using Nyamu.Core.Interfaces;

namespace Nyamu.Core.Monitors
{
    // Monitors Unity Editor state and processes main thread actions
    public class EditorMonitor
    {
        readonly EditorStateManager _state;
        readonly IUnityThreadExecutor _unityThreadExecutor;
        readonly SettingsMonitor _settingsMonitor;

        public EditorMonitor(EditorStateManager state, IUnityThreadExecutor unityThreadExecutor, SettingsMonitor settingsMonitor)
        {
            _state = state;
            _unityThreadExecutor = unityThreadExecutor;
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
            var actionQueue = ((UnityThreadExecutor)_unityThreadExecutor).ActionQueue;
            while (actionQueue.Count > 0)
                actionQueue.Dequeue().Invoke();

            // Update play mode state (thread-safe)
            _state.IsPlaying = EditorApplication.isPlaying;

            // Refresh cached settings periodically
            _settingsMonitor.Update();
        }
    }
}
