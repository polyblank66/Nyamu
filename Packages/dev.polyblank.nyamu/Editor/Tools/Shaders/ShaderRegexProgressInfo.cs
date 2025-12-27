using System;

namespace Nyamu.Tools.Shaders
{
    [Serializable]
    public class ShaderRegexProgressInfo
    {
        public string pattern;
        public int totalShaders;
        public int completedShaders;
        public string currentShader;
    }
}
