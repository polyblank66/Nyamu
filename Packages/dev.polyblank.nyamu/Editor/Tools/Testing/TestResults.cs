using System;

namespace Nyamu.Tools.Testing
{
    [Serializable]
    public class TestResults
    {
        public int totalTests;
        public int passedTests;
        public int failedTests;
        public int skippedTests;
        public double duration;
        public TestResult[] results;
    }
}
