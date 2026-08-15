using System;

namespace Nyamu.Tools.CodeExecution
{
    [Serializable]
    public class CodeCompileMessage
    {
        public string file;
        public int line;
        public int column;
        public string severity; // error | warning
        public string message;
    }
}
