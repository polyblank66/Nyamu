using System;
using System.Threading;
using System.Threading.Tasks;
using Nyamu.Core.Interfaces;

namespace Nyamu.Tools.Shaders
{
    // Tool for compiling all shaders in the project
    public class CompileAllShadersTool : INyamuTool<CompileAllShadersRequest, CompileAllShadersResponse>
    {
        public string Name => "compile_all_shaders";

        public Task<CompileAllShadersResponse> ExecuteAsync(
            CompileAllShadersRequest request,
            IExecutionContext context)
        {
            var state = context.ShaderState;

            // Check if shader compilation is already in progress
            lock (state.Lock)
            {
                if (state.IsCompiling)
                {
                    return Task.FromResult(new CompileAllShadersResponse
                    {
                        status = "warning",
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

            CompileAllShadersResponse response = null;

            // Enqueue shader compilation on main thread
            context.UnityExecutor.Enqueue(() =>
            {
                response = Server.CompileAllShaders();

                lock (state.ResultLock)
                {
                    state.LastSingleShaderResult = null;
                    state.LastAllShadersResult = response;
                    state.LastRegexShadersResult = null;
                    state.LastCompilationType = "all";
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

            return Task.FromResult(new CompileAllShadersResponse
            {
                status = "error",
                totalShaders = 0,
                successfulCompilations = 0,
                failedCompilations = 0,
                totalCompilationTime = 0,
                results = new ShaderCompileResult[0]
            });
        }
    }
}
