using System;

namespace Nyamu.Tools.Shaders
{
    [Serializable]
    public class ShaderCompileResult
    {
        public string shaderName;
        public string shaderPath;
        public bool hasErrors;
        public bool hasWarnings;
        public int errorCount;
        public int warningCount;
        public ShaderCompileError[] errors;
        public ShaderCompileError[] warnings;
        public double compilationTime;
        public string[] targetPlatforms;
    }
}
