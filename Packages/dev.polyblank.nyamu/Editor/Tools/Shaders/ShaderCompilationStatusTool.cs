using System.Threading.Tasks;
using Nyamu.Core.Interfaces;

namespace Nyamu.Tools.Shaders
{
    // Tool for retrieving shader compilation status without triggering compilation
    public class ShaderCompilationStatusTool : INyamuTool<ShaderCompilationStatusRequest, object>
    {
        public string Name => "shader_compilation_status";

        public Task<object> ExecuteAsync(
            ShaderCompilationStatusRequest request,
            IExecutionContext context)
        {
            var state = context.ShaderState;

            bool isCompiling;
            string lastCompilationType;
            System.DateTime lastCompilationTime;
            object lastResult;

            lock (state.Lock)
            {
                isCompiling = state.IsCompiling;
            }

            lock (state.ResultLock)
            {
                lastCompilationType = state.LastCompilationType;
                lastCompilationTime = state.LastCompilationTime;

                // Return the appropriate result based on compilation type
                lastResult = lastCompilationType switch
                {
                    "single" => state.LastSingleShaderResult,
                    "all" => state.LastAllShadersResult,
                    "regex" => state.LastRegexShadersResult,
                    _ => null
                };
            }

            // Build generic response
            var response = new
            {
                status = isCompiling ? "compiling" : "idle",
                isCompiling = isCompiling,
                lastCompilationType = lastCompilationType,
                lastCompilationTime = lastCompilationTime.ToString("yyyy-MM-dd HH:mm:ss"),
                lastCompilationResult = lastResult
            };

            return Task.FromResult<object>(response);
        }
    }
}
