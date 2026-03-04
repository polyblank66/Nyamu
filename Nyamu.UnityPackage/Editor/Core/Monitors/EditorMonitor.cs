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

        private void OnEditorUpdate()
        {
            // Execute main thread actions
            _unityThreadExecutor.Process();

            // Update play mode state (thread-safe)
            _state.IsPlaying = EditorApplication.isPlaying;

            // Refresh cached settings periodically
            _settingsMonitor.Update();
        }
    }
}
