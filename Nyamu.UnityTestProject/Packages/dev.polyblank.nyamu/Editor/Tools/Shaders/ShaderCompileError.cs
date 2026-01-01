using System;

namespace Nyamu.Tools.Shaders
{
    [Serializable]
    public class ShaderCompileError
    {
        public string message;
        public string messageDetails;
        public string file;
        public int line;
        public string platform;
    }
}
