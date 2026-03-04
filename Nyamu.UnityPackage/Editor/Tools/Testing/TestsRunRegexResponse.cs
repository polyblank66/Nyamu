using System;

namespace Nyamu.Tools.Testing
{
    // Response DTO for running tests matching a regex pattern
    [Serializable]
    public class TestsRunRegexResponse
    {
        public string status;
        public string message;
    }
}
