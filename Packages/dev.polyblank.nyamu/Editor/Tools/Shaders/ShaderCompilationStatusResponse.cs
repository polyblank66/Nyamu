using System;

namespace Nyamu.Tools.Shaders
{
    [Serializable]
    public class ShaderCompilationStatusResponse
    {
        public string status;
        public bool isCompiling;
        public string lastCompilationType;
        public string lastCompilationTime;
        public object lastCompilationResult;
    }
}
