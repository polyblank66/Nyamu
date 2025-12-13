Shader "Custom/BrokenShader"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }

        CGPROGRAM
        #pragma surface surf Standard

        // Intentional error: missing semicolon
        fixed4 _Color

        struct Input
        {
            float2 uv_MainTex;
        };

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            o.Albedo = _Color.rgb;
            // Another error: undefined variable
            o.Alpha = undefinedVariable;
        }
        ENDCG
    }
}
