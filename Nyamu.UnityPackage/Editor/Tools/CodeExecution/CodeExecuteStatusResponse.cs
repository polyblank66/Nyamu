using System;

namespace Nyamu.Tools.CodeExecution
{
    [Serializable]
    public class CodeExecuteStatusResponse
    {
        public string status;              // ok | error
        public string executionId;
        public string phase;               // queued|compiling|compiled|executing|completed|failed
        public string outcome;             // see CodeExecutionRecord.Fail/Complete callers; "none" while running
        public bool isDone;
        public string resolvedMode;        // expression|statements|class
        public bool fallbackUsed;
        public string resultType;
        public string result;
        public string logs;
        public bool logsTruncated;
        public string stdout;
        public string exceptionType;
        public string exceptionMessage;
        public string stackTrace;
        public CodeCompileMessage[] errors;
        public CodeCompileMessage[] warnings;
        public double compileSeconds;
        public double executeSeconds;
        public string startedUtc;
        public string finishedUtc;
        public string assemblyName;
        public int assembliesLoadedThisSession;
        public string message;
    }
}
