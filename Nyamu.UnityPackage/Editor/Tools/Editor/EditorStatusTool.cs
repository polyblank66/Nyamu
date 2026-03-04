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
            var assetState = context.AssetState;

            var isCompiling = compilationState.IsCompiling;
            var isRunningTests = testState.IsRunningTests;
            var isPlaying = editorState.IsPlaying;
            var isRefreshing = assetState.IsRefreshing;
            var isWaitingForCompilation = assetState.IsWaitingForCompilation;

            var response = new EditorStatusResponse
            {
                isCompiling = isCompiling,
                isRunningTests = isRunningTests,
                isPlaying = isPlaying,
                isRefreshing = isRefreshing,
                isWaitingForCompilation = isWaitingForCompilation
            };

            return Task.FromResult(response);
        }
    }
}
