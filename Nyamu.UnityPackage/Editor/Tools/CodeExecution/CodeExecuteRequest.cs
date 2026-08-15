using System;

namespace Nyamu.Tools.CodeExecution
{
    [Serializable]
    public class CodeExecuteRequest
    {
        public string code;
        public string mode;            // auto | expression | statements | class
        public string[] usings;
        public string entryPoint;
        public bool runOnMainThread;
        public bool background;
        public int timeout;
        public int maxLogEntries;
        public int maxResultChars;
        public int asyncBudgetMs;
    }
}
