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

        // Compilation progress tracking
        int _totalAssemblies;
        int _completedAssemblies;
        string _currentAssembly = "";
        DateTime _compilationStartTime = DateTime.MinValue;

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

        // Compilation progress properties
        public int TotalAssemblies
        {
            get { lock (_lock) return _totalAssemblies; }
            set { lock (_lock) _totalAssemblies = value; }
        }

        public int CompletedAssemblies
        {
            get { lock (_lock) return _completedAssemblies; }
            set { lock (_lock) _completedAssemblies = value; }
        }

        public string CurrentAssembly
        {
            get { lock (_lock) return _currentAssembly; }
            set { lock (_lock) _currentAssembly = value; }
        }

        public DateTime CompilationStartTime
        {
            get { lock (_lock) return _compilationStartTime; }
            set { lock (_lock) _compilationStartTime = value; }
        }
    }
}
