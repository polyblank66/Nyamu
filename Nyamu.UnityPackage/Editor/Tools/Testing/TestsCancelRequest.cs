namespace Nyamu.Tools.Testing
{
    // Request DTO for cancelling test execution
    public class TestsCancelRequest
    {
        public string testRunGuid; // Optional, uses current run if not provided
    }
}
