using Nyamu.Core.Interfaces;
using Nyamu.Core.StateManagers;

namespace Nyamu.Core
{
    // Execution context implementation providing access to all infrastructure
    public class ExecutionContext : IExecutionContext
    {
        public IUnityThreadExecutor UnityExecutor { get; }
        public CompilationStateManager CompilationState { get; }
        public TestStateManager TestState { get; }
        public ShaderStateManager ShaderState { get; }
        public AssetStateManager AssetState { get; }
        public EditorStateManager EditorState { get; }
        public SettingsStateManager SettingsState { get; }

        public ExecutionContext(
            IUnityThreadExecutor unityExecutor,
            CompilationStateManager compilationState,
            TestStateManager testState,
            ShaderStateManager shaderState,
            AssetStateManager assetState,
            EditorStateManager editorState,
            SettingsStateManager settingsState)
        {
            UnityExecutor = unityExecutor;
            CompilationState = compilationState;
            TestState = testState;
            ShaderState = shaderState;
            AssetState = assetState;
            EditorState = editorState;
            SettingsState = settingsState;
        }
    }
}
