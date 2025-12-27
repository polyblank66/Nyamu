using System;
using System.Collections.Generic;
using Nyamu.Tools.Compilation;

namespace Nyamu.Core.StateManagers
{
    // Manages compilation state with thread-safe access
    public class CompilationStateManager
    {
        readonly object _lock = new object();
        List<CompileError> _errors = new();
        bool _isCompiling;
        DateTime _lastCompileTime = DateTime.MinValue;
        DateTime _compileRequestTime = DateTime.MinValue;

        public object Lock => _lock;

        public bool IsCompiling
        {
            get { lock (_lock) return _isCompiling; }
            set { lock (_lock) _isCompiling = value; }
        }

        public DateTime LastCompileTime
        {
            get { lock (_lock) return _lastCompileTime; }
            set { lock (_lock) _lastCompileTime = value; }
        }

        public DateTime CompileRequestTime
        {
            get { lock (_lock) return _compileRequestTime; }
            set { lock (_lock) _compileRequestTime = value; }
        }

        public List<CompileError> Errors
        {
            get { lock (_lock) return _errors; }
            set { lock (_lock) _errors = value; }
        }

        public void ClearErrors()
        {
            lock (_lock) _errors.Clear();
        }

        public void AddError(CompileError error)
        {
            lock (_lock) _errors.Add(error);
        }

        public CompileError[] GetErrorsSnapshot()
        {
            lock (_lock) return _errors.ToArray();
        }
    }
}
