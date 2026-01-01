using System;

namespace Nyamu.Tools.Shaders
{
    [Serializable]
    public class CompileAllShadersResponse
    {
        public string status;
        public int totalShaders;
        public int successfulCompilations;
        public int failedCompilations;
        public double totalCompilationTime;
        public ShaderCompileResult[] results;
    }
}
