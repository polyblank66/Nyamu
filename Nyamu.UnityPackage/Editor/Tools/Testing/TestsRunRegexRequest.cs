namespace Nyamu.Tools.Testing
{
    // Request DTO for running tests matching a regex pattern
    public class TestsRunRegexRequest
    {
        public string testFilterRegex;
        public string testMode; // "EditMode" or "PlayMode"
    }
}
