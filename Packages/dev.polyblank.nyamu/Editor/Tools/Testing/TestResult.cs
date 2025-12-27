using System;

namespace Nyamu.Tools.Testing
{
    [Serializable]
    public class TestResult
    {
        public string name;
        public string outcome;
        public string message;
        public double duration;
    }
}
