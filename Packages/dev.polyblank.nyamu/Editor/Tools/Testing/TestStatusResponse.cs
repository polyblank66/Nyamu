using System;

namespace Nyamu.Tools.Testing
{
    [Serializable]
    public class TestStatusResponse
    {
        public string status;
        public bool isRunning;
        public string lastTestTime;
        public TestResults testResults;
        public string testRunId;
        public bool hasError;
        public string errorMessage;
        public TestProgressInfo progress;
    }
}
