using System;
using Nyamu.Tools.Compilation;

namespace Nyamu.Tools.Assets
{
    [Serializable]
    public class AssetsRefreshStatusResponse
    {
        public bool isRefreshing;
        public bool isCompiling;
        public bool isWaitingForCompilation;
        public bool unityIsUpdating;
        public string status;
        public string refreshRequestTime;
        public string refreshCompletedTime;

        // Compilation report fields
        public bool hadCompilation;
        public CompileError[] compilationErrors;
        public string lastCompilationTime;
    }
}
