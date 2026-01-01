using System;
using System.Threading.Tasks;
using Nyamu.Core.Interfaces;
using UnityEditor.Compilation;

namespace Nyamu.Tools.Compilation
{
    // Tool for triggering script compilation
    public class CompilationTriggerTool : INyamuTool<CompilationTriggerRequest, CompilationTriggerResponse>
    {
        public string Name => "compilation_trigger";

        public Task<CompilationTriggerResponse> ExecuteAsync(
            CompilationTriggerRequest request,
            IExecutionContext context)
        {
            var state = context.CompilationState;
            DateTime compileRequestTime;

            lock (state.Lock)
            {
                state.CompileRequestTime = DateTime.Now;
                compileRequestTime = state.CompileRequestTime;
            }

            // Queue compilation on main thread
            context.UnityExecutor.Enqueue(() =>
                CompilationPipeline.RequestScriptCompilation()
            );

            // Wait for compilation to start (delegated to helper in Server)
            var (success, message) = Server.WaitForCompilationToStart(
                compileRequestTime,
                TimeSpan.FromSeconds(Constants.CompileTimeoutSeconds)
            );

            var response = new CompilationTriggerResponse
            {
                status = success ? "ok" : "warning",
                message = success ? "Compilation started." : message
            };

            return Task.FromResult(response);
        }
    }
}
