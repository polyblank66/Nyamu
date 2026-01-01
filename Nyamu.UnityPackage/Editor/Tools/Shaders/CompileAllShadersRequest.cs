namespace Nyamu.Tools.Shaders
{
    // Request DTO for compiling all shaders
    [System.Serializable]
    public class CompileAllShadersRequest
    {
        public bool async;  // If true, return immediately after queuing compilation
        public int timeout; // Timeout in seconds, default 120
    }
}
