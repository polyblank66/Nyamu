using System.Threading.Tasks;
using Nyamu.Core.Interfaces;

namespace Nyamu.Tools.Editor
{
    // Tool for retrieving Unity editor status
    public class EditorStatusTool : INyamuTool<EditorStatusRequest, EditorStatusResponse>
    {
        public string Name => "editor_status";

        public Task<EditorStatusResponse> ExecuteAsync(
            EditorStatusRequest request,
            IExecutionContext context)
        {
            var compilationState = context.CompilationState;
            var testState = context.TestState;
            var editorState = context.EditorState;

            bool isCompiling;
            bool isRunningTests;
            bool isPlaying;

            lock (compilationState.Lock)
            {
                isCompiling = compilationState.IsCompiling;
            }

            lock (testState.Lock)
            {
                isRunningTests = testState.IsRunningTests;
            }

            lock (editorState.Lock)
            {
                isPlaying = editorState.IsPlaying;
            }

            var response = new EditorStatusResponse
            {
                isCompiling = isCompiling,
                isRunningTests = isRunningTests,
                isPlaying = isPlaying
            };

            return Task.FromResult(response);
        }
    }
}
