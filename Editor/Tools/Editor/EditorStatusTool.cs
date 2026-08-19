using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Nyamu.Core.Interfaces;

namespace Nyamu.Tools.Editor
{
    // Tool for retrieving Unity editor status
    public class EditorStatusTool : INyamuTool<EditorStatusRequest, EditorStatusResponse>
    {
        public string Name => "editor_status";

        // Anything older than this means EditorApplication.update is not ticking:
        // mid-domain-reload, mid-compile, a modal dialog, or a script infinite loop.
        // Unity throttles update to a few Hz when unfocused, so 2s is several
        // missed ticks even in the slowest normal case.
        private const double StaleThresholdSeconds = 2.0;

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
            var isRefreshing = assetState.IsRefreshing;
            var isWaitingForCompilation = assetState.IsWaitingForCompilation;

            var snapshot = editorState.Snapshot;
            var neverSampled = snapshot.LastUpdateStamp == 0;
            var stateAgeSeconds = neverSampled
                ? -1.0
                : Math.Max(0.0, (Stopwatch.GetTimestamp() - snapshot.LastUpdateStamp) / (double)Stopwatch.Frequency);

            var response = new EditorStatusResponse
            {
                isCompiling = isCompiling,
                isRunningTests = isRunningTests,
                isPlaying = snapshot.IsPlaying,
                isRefreshing = isRefreshing,
                isWaitingForCompilation = isWaitingForCompilation,
                isPaused = snapshot.IsPaused,
                isEnteringPlayMode = snapshot.IsEnteringPlayMode,
                isExitingPlayMode = snapshot.IsExitingPlayMode,
                stateAgeSeconds = stateAgeSeconds,
                isStateStale = neverSampled || stateAgeSeconds > StaleThresholdSeconds,
                lastEditorUpdateUtc = neverSampled ? "" : snapshot.LastUpdateUtc.ToString("o"),
                // Identity of the answering process: one port must belong to one Editor,
                // and a caller that suddenly talks to a different one has to notice.
                processId = NyamuProcess.Id,
                projectPath = NyamuProcess.ProjectPath
            };

            return Task.FromResult(response);
        }
    }
}
