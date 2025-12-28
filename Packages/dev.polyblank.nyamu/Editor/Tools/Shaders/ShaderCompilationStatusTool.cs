using System.Threading.Tasks;
using Nyamu.Core.Interfaces;

namespace Nyamu.Tools.Shaders
{
    // Tool for retrieving shader compilation status without triggering compilation
    public class ShaderCompilationStatusTool : INyamuTool<ShaderCompilationStatusRequest, ShaderCompilationStatusResponse>
    {
        public string Name => "shader_compilation_status";

        public Task<ShaderCompilationStatusResponse> ExecuteAsync(
            ShaderCompilationStatusRequest request,
            IExecutionContext context)
        {
            var state = context.ShaderState;

            bool isCompiling;
            string lastCompilationType;
            System.DateTime lastCompilationTime;
            CompileShaderResponse singleResult = null;
            CompileAllShadersResponse allResult = null;
            CompileShadersRegexResponse regexResult = null;
            ShaderRegexProgressInfo progressInfo = null;

            lock (state.Lock)
            {
                isCompiling = state.IsCompiling;

                // Get progress info if currently compiling regex shaders
                if (isCompiling)
                {
                    progressInfo = new ShaderRegexProgressInfo
                    {
                        pattern = state.RegexShadersPattern,
                        totalShaders = state.RegexShadersTotal,
                        completedShaders = state.RegexShadersCompleted,
                        currentShader = state.RegexShadersCurrentShader
                    };
                }
            }

            lock (state.ResultLock)
            {
                lastCompilationType = state.LastCompilationType;
                lastCompilationTime = state.LastCompilationTime;

                // Set the appropriate result based on compilation type
                switch (lastCompilationType)
                {
                    case "single":
                        singleResult = state.LastSingleShaderResult;
                        break;
                    case "all":
                        allResult = state.LastAllShadersResult;
                        break;
                    case "regex":
                        regexResult = state.LastRegexShadersResult;
                        break;
                }
            }

            // Build proper response DTO
            var response = new ShaderCompilationStatusResponse
            {
                status = isCompiling ? "compiling" : "idle",
                isCompiling = isCompiling,
                lastCompilationType = lastCompilationType,
                lastCompilationTime = lastCompilationTime.ToString("yyyy-MM-dd HH:mm:ss"),
                singleShaderResult = singleResult,
                allShadersResult = allResult,
                regexShadersResult = regexResult,
                progress = progressInfo
            };

            return Task.FromResult(response);
        }
    }
}
