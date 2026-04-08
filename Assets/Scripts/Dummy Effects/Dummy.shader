Shader "Custom/Dummy"
{
    Properties
    {
        _SourceTex ("Source", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Dummy"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            // Blend One One

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_NprIdTexture);
            SAMPLER(sampler_NprIdTexture);

            uint _RequiredBit;

            StructuredBuffer<uint> _BboxVisibilityFlags;
            int _UseOcclusion;
            int _CurrentBboxIndex;

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

            uint ReadMask8(float2 uv)
            {
                float m = SAMPLE_TEXTURE2D(_NprIdTexture, sampler_NprIdTexture, uv).r;
                return (uint)round(saturate(m) * 255.0); // unnormalise texture
            }

            float3 HashColour(float x)
            {
                float3 p = frac(float3(0.1031, 0.11369, 0.13787) * x);
                p += dot(p, p.yzx + 19.19);
                return frac((p.xxy + p.yzz) * p.zyx);
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

                uint mask = ReadMask8(i.uv);

                // if no style applied then do nothing
                if ((mask & _RequiredBit) == 0u)
                    clip(-1);

                float3 debugCol = HashColour((float)_RequiredBit);
                return float4(debugCol, 1.0);
            }
            ENDHLSL
        }
    }
}