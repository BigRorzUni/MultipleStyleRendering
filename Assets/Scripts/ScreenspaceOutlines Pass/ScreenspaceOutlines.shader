Shader "Custom/ScreenspaceOutlines"
{
    Properties
    {
        _OutlineColour ("Outline Colour", Color) = (0,0,0,1)
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "ScreenspaceOutlines"
            ZTest Always
            ZWrite Off
            Cull Off

            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_NprEdgesTexture);

            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColour;
            CBUFFER_END

            struct Attributes 
            { 
                uint vertexID : SV_VertexID; 
            };

            struct Varyings  
            { 
                float4 posCS : SV_POSITION; 
                float2 uv : TEXCOORD0; 
            };

            Varyings Vert (Attributes v)
            {
                Varyings o;
                o.posCS = GetFullScreenTriangleVertexPosition(v.vertexID);
                o.uv    = GetFullScreenTriangleTexCoord(v.vertexID);
                return o;
            }

            float4 Frag (Varyings i) : SV_Target
            {
                float edge = SAMPLE_TEXTURE2D(_NprEdgesTexture, sampler_PointClamp, i.uv).r;

                // return the highlighted edges only
                float a = saturate(edge) * _OutlineColour.a;
                return float4(_OutlineColour.rgb, a);
            }
            ENDHLSL
        }
    }
}