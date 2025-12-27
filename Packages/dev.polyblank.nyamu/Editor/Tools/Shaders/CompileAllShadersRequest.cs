namespace Nyamu.Tools.Shaders
{
    // Request DTO for compiling all shaders
    [System.Serializable]
    public class CompileAllShadersRequest
    {
        public int timeout; // Timeout in seconds, default 120
    }
}
