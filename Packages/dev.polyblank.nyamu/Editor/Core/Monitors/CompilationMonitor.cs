using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Compilation;
using Nyamu.Core.StateManagers;
using Nyamu.Tools.Compilation;

namespace Nyamu.Core.Monitors
{
    // Monitors Unity compilation events and updates CompilationStateManager
    public class CompilationMonitor
    {
        readonly CompilationStateManager _state;
        readonly object _timestampLock = new object();

        // Exposed for use by external services (e.g., TestCallbacks)
        public object TimestampLock => _timestampLock;

        public CompilationMonitor(CompilationStateManager state)
        {
            _state = state;
        }

        public void Initialize()
        {
            CompilationPipeline.assemblyCompilationFinished += OnCompilationFinished;
            CompilationPipeline.compilationStarted += OnCompilationStarted;
        }

        public void Cleanup()
        {
            CompilationPipeline.assemblyCompilationFinished -= OnCompilationFinished;
            CompilationPipeline.compilationStarted -= OnCompilationStarted;
        }

        void OnCompilationStarted(object obj)
        {
            _state.IsCompiling = true;
        }

        void OnCompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            _state.IsCompiling = false;

            // Update last compile time with thread-safe timestamp lock
            lock (_timestampLock)
            {
                _state.LastCompileTime = DateTime.Now;
            }

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
        }
    }
}
