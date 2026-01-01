using System;
using System.Threading;
using System.Threading.Tasks;
using Nyamu.Core.Interfaces;
using Nyamu.ShaderCompilation;

namespace Nyamu.Tools.Shaders
{
    // Tool for compiling a single shader with fuzzy name matching
    public class CompileShaderTool : INyamuTool<CompileShaderRequest, CompileShaderResponse>
    {
        public string Name => "compile_shader";

        public Task<CompileShaderResponse> ExecuteAsync(
            CompileShaderRequest request,
            IExecutionContext context)
        {
            if (string.IsNullOrEmpty(request.shaderName))
            {
                return Task.FromResult(new CompileShaderResponse
                {
                    status = "error",
                    message = "Shader name is required.",
                    allMatches = new ShaderMatch[0]
                });
            }

            var state = context.ShaderState;

            // Check if shader compilation is already in progress
            lock (state.Lock)
            {
                if (state.IsCompiling)
                {
                    return Task.FromResult(new CompileShaderResponse
                    {
                        status = "warning",
                        message = "Shader compilation already in progress.",
                        allMatches = new ShaderMatch[0]
                    });
                }

                state.IsCompiling = true;

                // Clear previous results
                state.LastSingleShaderResult = null;
                state.LastAllShadersResult = null;
                state.LastRegexShadersResult = null;
            }

            CompileShaderResponse response = null;

            // Enqueue shader compilation on main thread
            context.UnityExecutor.Enqueue(() =>
            {
                response = ShaderCompilationService.CompileSingleShader(request.shaderName);

                lock (state.ResultLock)
                {
                    state.LastSingleShaderResult = response;
                    state.LastAllShadersResult = null;
                    state.LastRegexShadersResult = null;
                    state.LastCompilationType = "single";
                    state.LastCompilationTime = DateTime.Now;
                }

                lock (state.Lock)
                {
                    state.IsCompiling = false;
                }
            });

            // Wait for compilation to complete with timeout
            var timeout = request.timeout > 0 ? request.timeout : 30;
            var endTime = DateTime.Now.AddSeconds(timeout);

            while (response == null && DateTime.Now < endTime)
                Thread.Sleep(100);

            if (response != null)
                return Task.FromResult(response);

            // Timeout occurred
            lock (state.Lock)
            {
                state.IsCompiling = false;
            }

            return Task.FromResult(new CompileShaderResponse
            {
                status = "error",
                message = "Timeout.",
                allMatches = new ShaderMatch[0]
            });
        }
    }
}
