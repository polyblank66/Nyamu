using System;

namespace Nyamu.Tools.Compilation
{
    [Serializable]
    public class CompileError
    {
        public string file;
        public int line;
        public string message;
    }
}
