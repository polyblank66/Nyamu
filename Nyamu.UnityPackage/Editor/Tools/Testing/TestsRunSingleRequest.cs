using System;

namespace Nyamu.Tools.Testing
{
    // Request DTO for running a single test
    [Serializable]
    public class TestsRunSingleRequest
    {
        public string testName;
        public string testMode; // "EditMode" or "PlayMode"
    }
}
