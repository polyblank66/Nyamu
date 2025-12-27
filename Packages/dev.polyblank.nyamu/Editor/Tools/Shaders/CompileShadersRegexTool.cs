using System;
using System.Threading;
using System.Threading.Tasks;
using Nyamu.Core.Interfaces;
using Nyamu.ShaderCompilation;

namespace Nyamu.Tools.Shaders
{
    // Tool for compiling shaders matching a regex pattern
    public class CompileShadersRegexTool : INyamuTool<CompileShadersRegexToolRequest, CompileShadersRegexResponse>
    {
        public string Name => "compile_shaders_regex";

        public Task<CompileShadersRegexResponse> ExecuteAsync(
            CompileShadersRegexToolRequest request,
            IExecutionContext context)
        {
            if (string.IsNullOrEmpty(request.pattern))
            {
                return Task.FromResult(new CompileShadersRegexResponse
                {
                    status = "error",
                    message = "Missing required parameter: pattern",
                    pattern = "",
                    totalShaders = 0,
                    successfulCompilations = 0,
                    failedCompilations = 0,
                    totalCompilationTime = 0,
                    results = new ShaderCompileResult[0]
                });
            }

            var state = context.ShaderState;

            // Check if shader compilation is already in progress
            lock (state.Lock)
            {
                if (state.IsCompiling)
                {
                    return Task.FromResult(new CompileShadersRegexResponse
                    {
                        status = "warning",
                        message = "Shader compilation already in progress.",
                        pattern = request.pattern,
                        totalShaders = 0,
                        successfulCompilations = 0,
                        failedCompilations = 0,
                        totalCompilationTime = 0,
                        results = new ShaderCompileResult[0]
                    });
                }

                state.IsCompiling = true;

                // Clear previous results
                state.LastSingleShaderResult = null;
                state.LastAllShadersResult = null;
                state.LastRegexShadersResult = null;
            }

            // Check if async mode is requested
            if (request.async)
            {
                // Async mode: queue compilation and return immediately
                context.UnityExecutor.Enqueue(() =>
                {
                    var result = ShaderCompilationService.CompileShadersRegex(request.pattern);

                    lock (state.ResultLock)
                    {
                        state.LastSingleShaderResult = null;
                        state.LastAllShadersResult = null;
                        state.LastRegexShadersResult = result;
                        state.LastCompilationType = "regex";
                        state.LastCompilationTime = DateTime.Now;
                    }

                    lock (state.Lock)
                    {
                        state.IsCompiling = false;
                    }
                });

                return Task.FromResult(new CompileShadersRegexResponse
                {
                    status = "ok",
                    message = "Shader compilation started.",
                    pattern = request.pattern,
                    totalShaders = 0,
                    successfulCompilations = 0,
                    failedCompilations = 0,
                    totalCompilationTime = 0,
                    results = new ShaderCompileResult[0]
                });
            }

            // Blocking mode: wait for compilation to complete
            CompileShadersRegexResponse response = null;

            context.UnityExecutor.Enqueue(() =>
            {
                response = ShaderCompilationService.CompileShadersRegex(request.pattern);

                lock (state.ResultLock)
                {
                    state.LastSingleShaderResult = null;
                    state.LastAllShadersResult = null;
                    state.LastRegexShadersResult = response;
                    state.LastCompilationType = "regex";
                    state.LastCompilationTime = DateTime.Now;
                }

                lock (state.Lock)
                {
                    state.IsCompiling = false;
                }
            });

            // Wait for compilation to complete with timeout
            var timeout = request.timeout > 0 ? request.timeout : 120;
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

            return Task.FromResult(new CompileShadersRegexResponse
            {
                status = "error",
                message = "Timeout.",
                pattern = request.pattern,
                totalShaders = 0,
                successfulCompilations = 0,
                failedCompilations = 0,
                totalCompilationTime = 0,
                results = new ShaderCompileResult[0]
            });
        }
    }
}
