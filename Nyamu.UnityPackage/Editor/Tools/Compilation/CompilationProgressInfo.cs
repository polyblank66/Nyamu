using System;

namespace Nyamu.Tools.Compilation
{
    [Serializable]
    public class CompilationProgressInfo
    {
        public int totalAssemblies;
        public int completedAssemblies;
        public string currentAssembly;
        public double elapsedSeconds;
    }
}
