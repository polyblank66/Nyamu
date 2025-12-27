using System;

namespace Nyamu.Tools.Shaders
{
    [Serializable]
    public class CompileShadersRegexResponse
    {
        public string status;
        public string message;
        public string pattern;
        public int totalShaders;
        public int successfulCompilations;
        public int failedCompilations;
        public double totalCompilationTime;
        public ShaderCompileResult[] results;
    }
}
