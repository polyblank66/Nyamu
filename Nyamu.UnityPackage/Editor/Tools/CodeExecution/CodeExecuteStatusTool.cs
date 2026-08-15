using System.Threading.Tasks;
using Nyamu.Core.Interfaces;

namespace Nyamu.Tools.CodeExecution
{
    public class CodeExecuteStatusTool : INyamuTool<CodeExecuteStatusRequest, CodeExecuteStatusResponse>
    {
        public string Name => "code_execute_status";

        public Task<CodeExecuteStatusResponse> ExecuteAsync(CodeExecuteStatusRequest request, IExecutionContext context)
        {
            var state = context.CodeExecutionState;
            var record = state.Get(request?.executionId);

            if (record == null)
            {
                return Task.FromResult(new CodeExecuteStatusResponse
                {
                    status = "error",
                    executionId = request?.executionId ?? "",
                    phase = "",
                    outcome = "none",
                    message = "No matching execution was found. It may have been evicted (only the " +
                        "last few executions are kept) or the Editor restarted."
                });
            }

            return Task.FromResult(record.ToStatusResponse(state.AssembliesLoadedThisSession));
        }
    }
}
