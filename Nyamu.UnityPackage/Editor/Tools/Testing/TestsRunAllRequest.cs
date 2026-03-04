using System;

namespace Nyamu.Tools.Testing
{
    // Request DTO for running all tests
    [Serializable]
    public class TestsRunAllRequest
    {
        public string testMode; // "EditMode" or "PlayMode"
    }
}
