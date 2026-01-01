using System;

namespace Nyamu.Tools.Compilation
{
    [Serializable]
    public class CompileStatusResponse
    {
        public string status;
        public bool isCompiling;
        public string lastCompilationTime;
        public string lastCompilationRequestTime;
        public CompileError[] errors;
        public CompilationProgressInfo progress;
    }
}
