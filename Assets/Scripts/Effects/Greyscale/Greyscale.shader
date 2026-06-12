Shader "Custom/Greyscale"
{
    Properties
    {
        _SourceTex("Source", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Greyscale"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"


            TEXTURE2D(_NprIdTexture);
            SAMPLER(sampler_NprIdTexture);

            TEXTURE2D(_SourceTex);
            SAMPLER(sampler_SourceTex);
            float4 _SourceTex_TexelSize;

            float4 _Rect;  // xy origin, zw width height
            float2 _ScreenTexelSize;

            StructuredBuffer<uint> _BboxVisibilityFlags;
            int _UseOcclusion;
            int _CurrentBboxIndex;
            uint _GreyscaleBit;
            

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
                o.uv = GetFullScreenTriangleTexCoord(v.vertexID);
                return o;
            }

            uint ReadMask32(float2 uv)
            {
                float4 s = SAMPLE_TEXTURE2D(_NprIdTexture, sampler_PointClamp, uv);

                uint r = (uint)round(saturate(s.r) * 255.0);
                uint g = (uint)round(saturate(s.g) * 255.0);
                uint b = (uint)round(saturate(s.b) * 255.0);
                uint a = (uint)round(saturate(s.a) * 255.0);

                return r | (g << 8) | (b << 16) | (a << 24);
            }

            float4 Frag (Varyings i) : SV_Target
            {
                if (_UseOcclusion != 0)
                {
                    uint visible = _BboxVisibilityFlags[_CurrentBboxIndex];
                    // hidden (0) -> kill this fullscreen triangle inside the current scissor rect
                    if (visible == 0)
                        clip(-1);
                }

                float4 col = SAMPLE_TEXTURE2D(_SourceTex, sampler_SourceTex, i.uv);
                uint mask = ReadMask32(i.uv);

                if ((mask & _GreyscaleBit) == 0u)
                    clip(-1);

                float grey = dot(col.rgb, float3(0.299, 0.587, 0.114));

                return float4(grey, grey, grey, col.a);
            }
            ENDHLSL
        }
    }
}