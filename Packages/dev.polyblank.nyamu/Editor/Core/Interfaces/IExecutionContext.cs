using Nyamu.Core.StateManagers;

namespace Nyamu.Core.Interfaces
{
    // Execution context providing access to infrastructure and state
    public interface IExecutionContext
    {
        IUnityThreadExecutor UnityExecutor { get; }
        CompilationStateManager CompilationState { get; }
        TestStateManager TestState { get; }
        ShaderStateManager ShaderState { get; }
        AssetStateManager AssetState { get; }
        EditorStateManager EditorState { get; }
        SettingsStateManager SettingsState { get; }
    }
}
