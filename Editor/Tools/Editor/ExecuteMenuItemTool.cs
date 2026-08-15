using System;
using System.Threading.Tasks;
using Nyamu.Core.Interfaces;
using UnityEditor;

namespace Nyamu.Tools.Editor
{
    internal enum MenuItemExecutionOutcome
    {
        Executed,    // Unity ran the menu item
        NotExecuted, // ExecuteMenuItem returned false: bad path, or validate function disabled it
        Timeout,     // main thread did not run the closure within the deadline
        Error        // the Unity API threw
    }

    internal sealed class MenuItemExecutionResult
    {
        public MenuItemExecutionOutcome Outcome;
        public string ErrorMessage;
    }

    // Tool for executing Unity Editor menu items
    public class ExecuteMenuItemTool : INyamuTool<ExecuteMenuItemRequest, ExecuteMenuItemResponse>
    {
        // Deadline rationale:
        //  - Must stay well under the Node client's 5s HTTP timeout (mcp-server.js,
        //    makeHttpRequest's req.setTimeout) so the agent always receives
        //    structured JSON, never an opaque timeout.
        //  - Server.Cleanup() unsubscribes EditorMonitor.OnEditorUpdate before
        //    stopping TcpHttpServer. During a domain reload the queue is therefore
        //    guaranteed never to drain - an unbounded Wait() blocks forever.
        internal const int MainThreadDeadlineMs = 3000;

        public string Name => "execute_menu_item";

        public Task<ExecuteMenuItemResponse> ExecuteAsync(
            ExecuteMenuItemRequest request,
            IExecutionContext context)
        {
            if (string.IsNullOrEmpty(request.menuItemPath))
            {
                return Task.FromResult(new ExecuteMenuItemResponse
                {
                    success = false,
                    status = "error",
                    message = "Missing required parameter: menuItemPath",
                    menuItemPath = ""
                });
            }

            var result = new MenuItemExecutionResult();
            var tcs = new TaskCompletionSource<bool>();

            context.UnityExecutor.Enqueue(() =>
            {
                try
                {
                    var executed = EditorApplication.ExecuteMenuItem(request.menuItemPath);
                    result.Outcome = executed ? MenuItemExecutionOutcome.Executed : MenuItemExecutionOutcome.NotExecuted;
                }
                catch (Exception ex)
                {
                    result.Outcome = MenuItemExecutionOutcome.Error;
                    result.ErrorMessage = ex.Message;
                    NyamuLogger.LogError(
                        $"[Nyamu][ExecuteMenuItemTool] Failed to execute '{request.menuItemPath}': {ex.Message}");
                }
                finally
                {
                    tcs.TrySetResult(true);
                }
            });

            // On timeout return a FRESH result: the queued closure may still be
            // writing into `result` later, and reading it here would be a data
            // race. The `true` path of Wait() establishes the happens-before edge.
            var completedInTime = tcs.Task.Wait(MainThreadDeadlineMs);
            if (!completedInTime)
                result = new MenuItemExecutionResult { Outcome = MenuItemExecutionOutcome.Timeout };

            var response = new ExecuteMenuItemResponse { menuItemPath = request.menuItemPath };

            switch (result.Outcome)
            {
                case MenuItemExecutionOutcome.Executed:
                    response.success = true;
                    response.status = "ok";
                    response.message = "Menu item executed successfully";
                    break;

                case MenuItemExecutionOutcome.NotExecuted:
                    response.success = false;
                    response.status = "not_executed";
                    response.message = "Unity refused to execute this menu item. The path may not exist, or " +
                        "the item's validate function disabled it right now. Menu paths are case-sensitive " +
                        "and must match the menu bar exactly (e.g. 'GameObject/Create Empty').";
                    break;

                case MenuItemExecutionOutcome.Timeout:
                    response.success = false;
                    response.status = "main_thread_timeout";
                    response.message = $"Unity's main thread did not finish the menu item within " +
                        $"{MainThreadDeadlineMs}ms. The Editor is most likely compiling, reloading the " +
                        "domain, blocked by a modal dialog, or the item itself is long-running. It may " +
                        "still complete - check editor_status or the Editor log before retrying.";
                    break;

                default:
                    response.success = false;
                    response.status = "error";
                    response.message = result.ErrorMessage ?? "Failed to execute menu item.";
                    break;
            }

            return Task.FromResult(response);
        }
    }
}
