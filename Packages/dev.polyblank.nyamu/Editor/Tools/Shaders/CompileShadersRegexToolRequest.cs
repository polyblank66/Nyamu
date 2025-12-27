namespace Nyamu.Tools.Shaders
{
    // Request DTO for compiling shaders matching a regex pattern
    [System.Serializable]
    public class CompileShadersRegexToolRequest
    {
        public string pattern;
        public bool async;  // If true, return immediately after queuing compilation
        public int timeout; // Timeout in seconds, default 120
    }
}
