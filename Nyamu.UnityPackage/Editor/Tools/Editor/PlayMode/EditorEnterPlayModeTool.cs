using System.Threading.Tasks;
using Nyamu.Core.Interfaces;

namespace Nyamu.Tools.Editor.PlayMode
{
    // Tool for entering Unity PlayMode
    public class EditorEnterPlayModeTool : INyamuTool<EditorEnterPlayModeRequest, EditorEnterPlayModeResponse>
    {
        public string Name => "editor_enter_play_mode";

        public Task<EditorEnterPlayModeResponse> ExecuteAsync(
            EditorEnterPlayModeRequest request,
            IExecutionContext context)
        {
            var result = PlayModeSwitch.Request(context, true);
            var response = new EditorEnterPlayModeResponse { wasPlaying = result.WasPlaying };

            switch (result.Outcome)
            {
                case PlayModeSwitchOutcome.Requested:
                    response.success = true;
                    response.status = "requested";
                    response.message = "Play Mode entry requested. Unity applies it at the end of the " +
                        "current editor frame and then reloads the script domain, so the Nyamu HTTP " +
                        "server is briefly unreachable. Poll editor_status until isPlaying is true.";
                    break;

                case PlayModeSwitchOutcome.NoChange:
                    response.success = true;
                    response.status = "already_playing";
                    response.message = "Editor was already in Play Mode. No change was made.";
                    break;

                case PlayModeSwitchOutcome.Blocked:
                    response.success = false;
                    response.status = "blocked";
                    response.message = result.ErrorMessage ?? "Unity is not ready to enter Play Mode.";
                    break;

                case PlayModeSwitchOutcome.Timeout:
                    response.success = false;
                    response.status = "main_thread_timeout";
                    response.message = $"Unity's main thread did not process the request within " +
                        $"{PlayModeSwitch.MainThreadDeadlineMs}ms. The Editor is most likely compiling, " +
                        "reloading the domain, or blocked by a modal dialog. The request may still be " +
                        "applied later - check editor_status before retrying.";
                    break;

                default:
                    response.success = false;
                    response.status = "error";
                    response.message = result.ErrorMessage ?? "Failed to enter Play Mode.";
                    break;
            }

            return Task.FromResult(response);
        }
    }
}
