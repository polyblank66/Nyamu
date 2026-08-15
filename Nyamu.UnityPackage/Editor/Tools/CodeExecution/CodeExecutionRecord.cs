using System;
using System.Collections.Generic;
using UnityEditor.Compilation;

namespace Nyamu.Tools.CodeExecution
{
    // Mutable state for a single code_execute run. Cross-thread field access (the HTTP polling
    // thread reads while the Unity main thread writes) goes through the lock; fields only ever
    // touched from the main thread (Builder, Dir, DllPath, PdbPath) are plain fields.
    internal sealed class CodeExecutionRecord
    {
        private readonly object _lock = new();

        public readonly string Id;
        public readonly CodeExecuteRequest Request;
        public readonly DateTime StartedUtc;

        string _generatedSource;
        string _resolvedMode;
        bool _fallbackUsed;
        int _prologueLineCount;
        readonly string _virtualFileName;

        string _phase = "queued";
        string _outcome = "none";
        string _resultType = "";
        string _result = "";
        readonly List<string> _logs = new();
        bool _logsTruncated;
        string _stdout = "";
        string _exceptionType = "";
        string _exceptionMessage = "";
        string _stackTrace = "";
        readonly List<CodeCompileMessage> _errors = new();
        readonly List<CodeCompileMessage> _warnings = new();
        double _compileSeconds;
        double _executeSeconds;
        DateTime _finishedUtc = DateTime.MinValue;
        string _assemblyName = "";
        string _message = "";

        // Main-thread-only working state; never read by the HTTP polling thread.
        public AssemblyBuilder Builder; // anchored here so it isn't collected while Unity compiles in the background
        public string Dir;
        public string DllPath;
        public string PdbPath;

        public CodeExecutionRecord(string id, CodeExecuteRequest request, string generatedSource,
            string resolvedMode, int prologueLineCount, string virtualFileName)
        {
            Id = id;
            Request = request;
            StartedUtc = DateTime.UtcNow;
            _generatedSource = generatedSource;
            _resolvedMode = resolvedMode;
            _prologueLineCount = prologueLineCount;
            _virtualFileName = virtualFileName;
        }

        public string GeneratedSource { get { lock (_lock) return _generatedSource; } }
        public string ResolvedMode { get { lock (_lock) return _resolvedMode; } }
        public bool FallbackUsed { get { lock (_lock) return _fallbackUsed; } }
        public int PrologueLineCount { get { lock (_lock) return _prologueLineCount; } }
        public string VirtualFileName => _virtualFileName;

        public void ApplyFallback(string generatedSource, string resolvedMode, int prologueLineCount)
        {
            lock (_lock)
            {
                _generatedSource = generatedSource;
                _resolvedMode = resolvedMode;
                _prologueLineCount = prologueLineCount;
                _fallbackUsed = true;
            }
        }

        public string Phase { get { lock (_lock) return _phase; } set { lock (_lock) _phase = value; } }
        public string Outcome { get { lock (_lock) return _outcome; } }
        public string AssemblyName { get { lock (_lock) return _assemblyName; } set { lock (_lock) _assemblyName = value; } }
        public double CompileSeconds { get { lock (_lock) return _compileSeconds; } set { lock (_lock) _compileSeconds = value; } }
        public double ExecuteSeconds { get { lock (_lock) return _executeSeconds; } set { lock (_lock) _executeSeconds = value; } }

        public bool IsDone
        {
            get { lock (_lock) return _phase == "completed" || _phase == "failed"; }
        }

        public void SetErrors(IEnumerable<CodeCompileMessage> errors)
        {
            lock (_lock) { _errors.Clear(); _errors.AddRange(errors); }
        }

        public void SetWarnings(IEnumerable<CodeCompileMessage> warnings)
        {
            lock (_lock) { _warnings.Clear(); _warnings.AddRange(warnings); }
        }

        public void AddLog(string entry, int maxEntries)
        {
            lock (_lock)
            {
                if (_logs.Count >= maxEntries)
                {
                    _logsTruncated = true;
                    return;
                }
                _logs.Add(entry);
            }
        }

        public void SetStdout(string text)
        {
            lock (_lock) _stdout = text ?? "";
        }

        public void SetResult(string resultType, string result)
        {
            lock (_lock)
            {
                _resultType = resultType ?? "";
                _result = result ?? "";
            }
        }

        public void SetException(string type, string message, string stackTrace)
        {
            lock (_lock)
            {
                _exceptionType = type ?? "";
                _exceptionMessage = message ?? "";
                _stackTrace = stackTrace ?? "";
            }
        }

        public void Complete(string outcome, string message)
        {
            lock (_lock)
            {
                _outcome = outcome;
                _message = message ?? "";
                _finishedUtc = DateTime.UtcNow;
                _phase = "completed";
            }
        }

        public void Fail(string outcome, string message)
        {
            lock (_lock)
            {
                _outcome = outcome;
                _message = message ?? "";
                _finishedUtc = DateTime.UtcNow;
                _phase = "failed";
            }
        }

        public CodeExecuteStatusResponse ToStatusResponse(int assembliesLoadedThisSession)
        {
            lock (_lock)
            {
                return new CodeExecuteStatusResponse
                {
                    status = "ok",
                    executionId = Id,
                    phase = _phase,
                    outcome = _outcome,
                    isDone = _phase == "completed" || _phase == "failed",
                    resolvedMode = _resolvedMode,
                    fallbackUsed = _fallbackUsed,
                    resultType = _resultType,
                    result = _result,
                    logs = string.Join("\n", _logs),
                    logsTruncated = _logsTruncated,
                    stdout = _stdout,
                    exceptionType = _exceptionType,
                    exceptionMessage = _exceptionMessage,
                    stackTrace = _stackTrace,
                    errors = _errors.ToArray(),
                    warnings = _warnings.ToArray(),
                    compileSeconds = _compileSeconds,
                    executeSeconds = _executeSeconds,
                    startedUtc = StartedUtc.ToString("o"),
                    finishedUtc = _finishedUtc > DateTime.MinValue ? _finishedUtc.ToString("o") : "",
                    assemblyName = _assemblyName,
                    assembliesLoadedThisSession = assembliesLoadedThisSession,
                    message = _message
                };
            }
        }
    }
}
