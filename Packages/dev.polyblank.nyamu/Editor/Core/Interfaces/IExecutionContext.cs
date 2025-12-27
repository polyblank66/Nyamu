using Nyamu.Core.Monitors;
using Nyamu.Core.StateManagers;
using Nyamu.TestExecution;

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

        CompilationMonitor CompilationMonitor { get; }
        EditorMonitor EditorMonitor { get; }
        SettingsMonitor SettingsMonitor { get; }
        TestExecutionService TestExecutionService { get; }
    }
}
