using System;
using Nyamu.Tools.Shaders;

namespace Nyamu.Core.StateManagers
{
    // Manages shader compilation state with thread-safe access
    public class ShaderStateManager
    {
        readonly object _compileLock = new object();
        readonly object _resultLock = new object();

        bool _isCompilingShaders = false;
        CompileShaderResponse _lastSingleShaderResult = null;
        CompileAllShadersResponse _lastAllShadersResult = null;
        CompileShadersRegexResponse _lastRegexShadersResult = null;
        string _lastShaderCompilationType = "none";
        DateTime _lastShaderCompilationTime = DateTime.MinValue;

        // Regex shader compilation progress tracking
        string _regexShadersPattern = "";
        int _regexShadersTotal = 0;
        int _regexShadersCompleted = 0;
        string _regexShadersCurrentShader = "";

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
    }
}
