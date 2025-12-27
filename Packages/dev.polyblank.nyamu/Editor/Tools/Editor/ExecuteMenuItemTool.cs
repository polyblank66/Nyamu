using System;
using System.Threading;
using System.Threading.Tasks;
using Nyamu.Core.Interfaces;
using UnityEditor;

namespace Nyamu.Tools.Editor
{
    // Tool for executing Unity Editor menu items
    public class ExecuteMenuItemTool : INyamuTool<ExecuteMenuItemRequest, ExecuteMenuItemResponse>
    {
        public string Name => "execute_menu_item";

        public Task<ExecuteMenuItemResponse> ExecuteAsync(
            ExecuteMenuItemRequest request,
            IExecutionContext context)
        {
            if (string.IsNullOrEmpty(request.menuItemPath))
            {
                return Task.FromResult(new ExecuteMenuItemResponse
                {
                    status = "error",
                    message = "Missing required parameter: menuItemPath",
                    menuItemPath = ""
                });
            }

            var result = new MenuItemExecutionResult();

            context.UnityExecutor.Enqueue(() =>
            {
                try
                {
                    result.success = EditorApplication.ExecuteMenuItem(request.menuItemPath);
                    if (!result.success)
                        result.errorMessage = "MenuItem not found or execution failed";
                }
                catch (Exception ex)
                {
                    result.errorMessage = ex.Message;
                }
                result.completed = true;
            });

            // Wait for main thread to execute (max 1 second)
            var startTime = DateTime.Now;
            while (!result.completed && (DateTime.Now - startTime).TotalMilliseconds < 1000)
                Thread.Sleep(10);

            var response = new ExecuteMenuItemResponse
            {
                status = result.success ? "ok" : "error",
                message = result.success ? "Menu item executed successfully" : (result.errorMessage ?? "MenuItem execution failed"),
                menuItemPath = request.menuItemPath
            };

            return Task.FromResult(response);
        }
    }

    // Helper class for capturing execution result
    class MenuItemExecutionResult
    {
        public bool success = false;
        public string errorMessage = null;
        public bool completed = false;
    }
}
