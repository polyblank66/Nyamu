namespace Nyamu.Tools.Shaders
{
    // Request DTO for compiling a single shader
    [System.Serializable]
    public class CompileShaderRequest
    {
        public string shaderName;
        public int timeout; // Timeout in seconds, default 30
    }
}
