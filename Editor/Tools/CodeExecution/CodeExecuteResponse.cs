using System;

namespace Nyamu.Tools.CodeExecution
{
    [Serializable]
    public class CodeExecuteResponse
    {
        public string status;      // ok | error
        public string executionId;
        public string phase;       // queued | "" (on error)
        public string message;
    }
}
