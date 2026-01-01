using System;

namespace Nyamu.Tools.Shaders
{
    [Serializable]
    public class CompileShaderResponse
    {
        public string status;
        public string message;
        public ShaderMatch[] allMatches;
        public ShaderMatch bestMatch;
        public ShaderCompileResult result;
    }
}
