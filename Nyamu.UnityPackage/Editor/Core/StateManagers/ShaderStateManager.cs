using System;
using Nyamu.Tools.Shaders;

namespace Nyamu.Core.StateManagers
{
    // Manages shader compilation state with thread-safe access
    public class ShaderStateManager
    {
        private readonly object _compileLock = new object();
        private readonly object _resultLock = new object();

        private bool _isCompilingShaders;
        private CompileShaderResponse _lastSingleShaderResult;
        private CompileAllShadersResponse _lastAllShadersResult;
        private CompileShadersRegexResponse _lastRegexShadersResult;
        private string _lastShaderCompilationType = "none";
        private DateTime _lastShaderCompilationTime = DateTime.MinValue;

        // Regex shader compilation progress tracking
        private string _regexShadersPattern = "";
        private int _regexShadersTotal;
        private int _regexShadersCompleted;
        private string _regexShadersCurrentShader = "";

        // All shaders compilation progress tracking
        private int _allShadersTotal;
        private int _allShadersCompleted;
        private string _allShadersCurrentShader = "";

        public object Lock => _compileLock;
        public object ResultLock => _resultLock;

        public bool IsCompiling
        {
            get { lock (_compileLock) return _isCompilingShaders; }
            set { lock (_compileLock) _isCompilingShaders = value; }
        }

        public CompileShaderResponse LastSingleShaderResult
        {
            get { lock (_resultLock) return _lastSingleShaderResult; }
            set { lock (_resultLock) _lastSingleShaderResult = value; }
        }

        public CompileAllShadersResponse LastAllShadersResult
        {
            get { lock (_resultLock) return _lastAllShadersResult; }
            set { lock (_resultLock) _lastAllShadersResult = value; }
        }

        public CompileShadersRegexResponse LastRegexShadersResult
        {
            get { lock (_resultLock) return _lastRegexShadersResult; }
            set { lock (_resultLock) _lastRegexShadersResult = value; }
        }

        public string LastCompilationType
        {
            get { lock (_resultLock) return _lastShaderCompilationType; }
            set { lock (_resultLock) _lastShaderCompilationType = value; }
        }

        public DateTime LastCompilationTime
        {
            get { lock (_resultLock) return _lastShaderCompilationTime; }
            set { lock (_resultLock) _lastShaderCompilationTime = value; }
        }

        // Regex compilation progress (thread-safe access)
        public string RegexShadersPattern
        {
            get { lock (_compileLock) return _regexShadersPattern; }
            set { lock (_compileLock) _regexShadersPattern = value; }
        }

        public int RegexShadersTotal
        {
            get { lock (_compileLock) return _regexShadersTotal; }
            set { lock (_compileLock) _regexShadersTotal = value; }
        }

        public int RegexShadersCompleted
        {
            get { lock (_compileLock) return _regexShadersCompleted; }
            set { lock (_compileLock) _regexShadersCompleted = value; }
        }

        public string RegexShadersCurrentShader
        {
            get { lock (_compileLock) return _regexShadersCurrentShader; }
            set { lock (_compileLock) _regexShadersCurrentShader = value; }
        }

        // All shaders compilation progress (thread-safe access)
        public int AllShadersTotal
        {
            get { lock (_compileLock) return _allShadersTotal; }
            set { lock (_compileLock) _allShadersTotal = value; }
        }

        public int AllShadersCompleted
        {
            get { lock (_compileLock) return _allShadersCompleted; }
            set { lock (_compileLock) _allShadersCompleted = value; }
        }

        public string AllShadersCurrentShader
        {
            get { lock (_compileLock) return _allShadersCurrentShader; }
            set { lock (_compileLock) _allShadersCurrentShader = value; }
        }
    }
}
