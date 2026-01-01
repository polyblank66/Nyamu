using System.Threading.Tasks;
using Nyamu.Core.Interfaces;

namespace Nyamu.Tools.Compilation
{
    // Tool for retrieving compilation status without triggering compilation
    public class CompilationStatusTool : INyamuTool<CompilationStatusRequest, CompileStatusResponse>
    {
        public string Name => "compilation_status";

        public Task<CompileStatusResponse> ExecuteAsync(
            CompilationStatusRequest request,
            IExecutionContext context)
        {
            var state = context.CompilationState;

            string status;
            bool isCompiling;
            System.DateTime lastCompileTime;
            System.DateTime lastCompilationRequestTime;
            CompileError[] errors;
            CompilationProgressInfo progressInfo = null;

            lock (state.Lock)
            {
                isCompiling = state.IsCompiling;
                lastCompileTime = state.LastCompileTime;
                lastCompilationRequestTime = state.CompileRequestTime;
                errors = state.GetErrorsSnapshot();

                // Populate progress info if compilation is in progress
                if (isCompiling && state.TotalAssemblies > 0)
                {
                    var elapsed = (System.DateTime.Now - state.CompilationStartTime).TotalSeconds;
                    progressInfo = new CompilationProgressInfo
                    {
                        totalAssemblies = state.TotalAssemblies,
                        completedAssemblies = state.CompletedAssemblies,
                        currentAssembly = state.CurrentAssembly,
                        elapsedSeconds = elapsed
                    };
                }
            }

            status = isCompiling ? "compiling" : "idle";

            var response = new CompileStatusResponse
            {
                status = status,
                isCompiling = isCompiling,
                lastCompilationTime = lastCompileTime.ToString("yyyy-MM-dd HH:mm:ss"),
                lastCompilationRequestTime = lastCompilationRequestTime.ToString("yyyy-MM-dd HH:mm:ss"),
                errors = errors,
                progress = progressInfo
            };

            return Task.FromResult(response);
        }
    }
}
