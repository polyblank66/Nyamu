using System;
using System.Threading.Tasks;
using Nyamu.Core.Interfaces;

namespace Nyamu.Tools.CodeExecution
{
    // Tool for compiling and executing an ad-hoc C# snippet in the Unity Editor. Always
    // asynchronous on the Unity side - this only enqueues the work and returns the executionId;
    // callers poll code_execute_status for the result.
    public class CodeExecuteTool : INyamuTool<CodeExecuteRequest, CodeExecuteResponse>
    {
        public string Name => "code_execute";

        public Task<CodeExecuteResponse> ExecuteAsync(CodeExecuteRequest request, IExecutionContext context)
        {
            if (request == null || string.IsNullOrEmpty(request.code))
            {
                return Task.FromResult(new CodeExecuteResponse
                {
                    status = "error",
                    executionId = "",
                    phase = "",
                    message = "The 'code' field is required."
                });
            }

            string generatedSource, resolvedMode, virtualFileName;
            int prologueLineCount;
            try
            {
                CodeSnippetBuilder.Build(request, out generatedSource, out resolvedMode, out prologueLineCount, out virtualFileName);
            }
            catch (Exception ex)
            {
                return Task.FromResult(new CodeExecuteResponse
                {
                    status = "error",
                    executionId = "",
                    phase = "",
                    message = $"Failed to prepare source: {ex.Message}"
                });
            }

            var id = Guid.NewGuid().ToString("N");
            var record = new CodeExecutionRecord(id, request, generatedSource, resolvedMode, prologueLineCount, virtualFileName);

            if (!context.CodeExecutionState.TryBegin(record))
            {
                return Task.FromResult(new CodeExecuteResponse
                {
                    status = "error",
                    executionId = "",
                    phase = "",
                    message = "Another code_execute is already running. Poll code_execute_status or wait for it to finish."
                });
            }

            context.UnityExecutor.Enqueue(() => CodeExecutionOrchestrator.Start(record, context));

            return Task.FromResult(new CodeExecuteResponse
            {
                status = "ok",
                executionId = id,
                phase = "queued",
                message = "Code execution queued."
            });
        }
    }
}
