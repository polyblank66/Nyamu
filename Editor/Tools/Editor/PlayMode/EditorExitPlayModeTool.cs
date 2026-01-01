using System.Threading.Tasks;
using Nyamu.Core.Interfaces;
using UnityEditor;
using UnityEngine;

namespace Nyamu.Tools.Editor.PlayMode
{
    // Tool for exiting Unity PlayMode
    public class EditorExitPlayModeTool : INyamuTool<EditorExitPlayModeRequest, EditorExitPlayModeResponse>
    {
        public string Name => "editor_exit_play_mode";

        public Task<EditorExitPlayModeResponse> ExecuteAsync(
            EditorExitPlayModeRequest request,
            IExecutionContext context)
        {
            // Exit PlayMode using Unity Editor API - must be called from main thread
            bool success = false;
            string errorMessage = null;

            // Use a task completion source to wait for the main thread execution
            var tcs = new TaskCompletionSource<bool>();

            context.UnityExecutor.Enqueue(() =>
            {
                try
                {
                    EditorApplication.isPlaying = false;
                    success = true;
                }
                catch (System.Exception ex)
                {
                    errorMessage = ex.Message;
                    NyamuLogger.LogError($"[Nyamu][EditorExitPlayMode] Failed to exit PlayMode: {ex.Message}");
                }
                finally
                {
                    tcs.SetResult(success);
                }
            });

            // Wait for the main thread to complete
            tcs.Task.Wait();

            var response = new EditorExitPlayModeResponse
            {
                success = success,
                message = success ? "Successfully exited PlayMode" :
                          errorMessage ?? "Failed to exit PlayMode"
            };

            return Task.FromResult(response);
        }
    }
}
