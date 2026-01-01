using System;

namespace Nyamu.Tools.Testing
{
    [Serializable]
    public class TestProgressInfo
    {
        public int totalTests;
        public int completedTests;
        public string currentTest;
    }
}
