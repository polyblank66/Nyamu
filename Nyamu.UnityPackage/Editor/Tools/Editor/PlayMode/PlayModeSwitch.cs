using System;
using System.Threading.Tasks;
using Nyamu.Core.Interfaces;
using UnityEditor;

namespace Nyamu.Tools.Editor.PlayMode
{
    internal enum PlayModeSwitchOutcome
    {
        Requested,   // delivered to the main thread; Unity applies it at end of frame
        NoChange,    // already in the requested state
        Blocked,     // Unity will not honour the request right now (compiling)
        Timeout,     // main thread did not drain the queue within the deadline
        Error        // the Unity API threw
    }

    internal sealed class PlayModeSwitchResult
    {
        public PlayModeSwitchOutcome Outcome;
        public bool WasPlaying;
        public string ErrorMessage;
    }

    // Shared enqueue + bounded-wait for the enter/exit Play Mode tools.
    //
    // Deadline rationale:
    //  - Must stay well under the Node client's 5s HTTP timeout (mcp-server.js,
    //    makeHttpRequest's req.setTimeout) so the agent always receives
    //    structured JSON, never an opaque timeout.
    //  - EditorApplication.update throttles when the Editor is unfocused, so 3s
    //    still buys several ticks.
    //  - Server.Cleanup() unsubscribes EditorMonitor.OnEditorUpdate before
    //    stopping TcpHttpServer. During a domain reload the queue is therefore
    //    guaranteed never to drain - an unbounded Wait() blocks forever.
    internal static class PlayModeSwitch
    {
        internal const int MainThreadDeadlineMs = 3000;

        internal static PlayModeSwitchResult Request(IExecutionContext context, bool targetIsPlaying)
        {
            var result = new PlayModeSwitchResult();
            var tcs = new TaskCompletionSource<bool>();

            context.UnityExecutor.Enqueue(() =>
            {
                try
                {
                    // Authoritative read: main thread only. isPlayingOrWillChangePlaymode
                    // (not isPlaying) so a request already pending this frame counts.
                    result.WasPlaying = EditorApplication.isPlayingOrWillChangePlaymode;

                    if (result.WasPlaying == targetIsPlaying)
                    {
                        result.Outcome = PlayModeSwitchOutcome.NoChange;
                    }
                    else if (targetIsPlaying && EditorApplication.isCompiling)
                    {
                        result.Outcome = PlayModeSwitchOutcome.Blocked;
                        result.ErrorMessage =
                            "Unity is compiling. It will not enter Play Mode until compilation finishes.";
                    }
                    else
                    {
                        EditorApplication.isPlaying = targetIsPlaying;
                        result.Outcome = PlayModeSwitchOutcome.Requested;
                    }
                }
                catch (Exception ex)
                {
                    result.Outcome = PlayModeSwitchOutcome.Error;
                    result.ErrorMessage = ex.Message;
                    NyamuLogger.LogError(
                        $"[Nyamu][PlayModeSwitch] Failed to set isPlaying={targetIsPlaying}: {ex.Message}");
                }
                finally
                {
                    tcs.TrySetResult(true);
                }
            });

            // On timeout return a FRESH result: the queued closure may still be
            // writing into `result` later, and reading it here would be a data
            // race. The `true` path of Wait() establishes the happens-before edge.
            return tcs.Task.Wait(MainThreadDeadlineMs)
                ? result
                : new PlayModeSwitchResult { Outcome = PlayModeSwitchOutcome.Timeout };
        }
    }
}
