using System;
using System.Collections.Generic;
using UnityEditor.Compilation;
using Nyamu.Core.StateManagers;
using Nyamu.Tools.Compilation;

namespace Nyamu.Core.Monitors
{
    // Monitors Unity compilation events and updates CompilationStateManager
    public class CompilationMonitor
    {
        private readonly CompilationStateManager _state;
        private readonly object _timestampLock = new();

        // Exposed for use by external services (e.g., TestCallbacks)
        public object TimestampLock => _timestampLock;

        public CompilationMonitor(CompilationStateManager state)
        {
            _state = state;
        }

        public void Initialize()
        {
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
        }

        public void Cleanup()
        {
            CompilationPipeline.compilationStarted -= OnCompilationStarted;
            CompilationPipeline.compilationFinished -= OnCompilationFinished;
            CompilationPipeline.assemblyCompilationFinished -= OnAssemblyCompilationFinished;
        }

        private void OnCompilationStarted(object obj)
        {
            _state.IsCompiling = true;
            _state.CompilationStartTime = DateTime.Now;

            // Get total assembly count for progress tracking
            var assemblies = CompilationPipeline.GetAssemblies();
            _state.TotalAssemblies = assemblies.Length;
            _state.CompletedAssemblies = 0;
            _state.CurrentAssembly = "";

            NyamuLogger.LogDebug($"[Nyamu][Compilation] Started - Total assemblies: {assemblies.Length}");
        }

        private void OnCompilationFinished(object obj)
        {
            _state.IsCompiling = false;

            // Update last compile time with thread-safe timestamp lock
            lock (_timestampLock)
            {
                _state.LastCompileTime = DateTime.Now;
            }

            // Clear progress tracking
            _state.TotalAssemblies = 0;
            _state.CompletedAssemblies = 0;
            _state.CurrentAssembly = "";

            NyamuLogger.LogDebug($"[Nyamu][Compilation] Finished");
        }

        private void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            var assemblyName = System.IO.Path.GetFileName(assemblyPath);

            _state.CompletedAssemblies++;
            _state.CurrentAssembly = assemblyName;

            // Update compilation errors
            var errors = new List<CompileError>();
            foreach (var msg in messages)
            {
                if (msg.type == CompilerMessageType.Error)
                    errors.Add(new CompileError
                    {
                        file = msg.file,
                        line = msg.line,
                        message = msg.message
                    });
            }
            _state.Errors = errors;

            NyamuLogger.LogDebug($"[Nyamu][Compilation] Assembly finished: {assemblyName} ({_state.CompletedAssemblies}/{_state.TotalAssemblies})");
        }
    }
}
