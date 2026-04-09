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

            uint ReadMask32(float2 uv)
            {
                float4 s = SAMPLE_TEXTURE2D(_NprIdTexture, sampler_PointClamp, uv);

                uint r = (uint)round(saturate(s.r) * 255.0);
                uint g = (uint)round(saturate(s.g) * 255.0);
                uint b = (uint)round(saturate(s.b) * 255.0);
                uint a = (uint)round(saturate(s.a) * 255.0);

                return r | (g << 8) | (b << 16) | (a << 24);
            }

            // copilot generated function
            uint HashUint(uint x)
            {
                x ^= x >> 16;
                x *= 0x7feb352du;
                x ^= x >> 15;
                x *= 0x846ca68bu;
                x ^= x >> 16;
                return x;
            }

            // copilot generated function
            float3 HashColour(uint x)
            {
                uint h1 = HashUint(x);
                uint h2 = HashUint(x ^ 0x68bc21ebu);
                uint h3 = HashUint(x ^ 0x02e5be93u);

                return float3(
                    (h1 & 255u) / 255.0,
                    (h2 & 255u) / 255.0,
                    (h3 & 255u) / 255.0
                );
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

                uint mask = ReadMask32(i.uv);

                // if no style applied then do nothing
                if ((mask & _RequiredBit) == 0u)
                    clip(-1);

                float3 debugCol = HashColour(mask);
                return float4(debugCol, 1.0);
            }
            ENDHLSL
        }
    }
}