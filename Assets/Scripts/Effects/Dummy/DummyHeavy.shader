Shader "Custom/DummyHeavy"
{
    Properties
    {
        _Iterations ("Iterations", Int) = 64
        _Scale ("Scale", Float) = 6.0
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "DummyHeavy"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_NprIdTexture);

            uint _RequiredBit;

            StructuredBuffer<uint> _BboxVisibilityFlags;
            int _UseOcclusion;
            int _CurrentBboxIndex;

            int _Iterations;
            float _Scale;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 posCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes v)
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

            float2 Rotate(float2 p, float a)
            {
                float s = sin(a);
                float c = cos(a);
                return float2(c * p.x - s * p.y, s * p.x + c * p.y);
            }

            float4 Frag(Varyings i) : SV_Target
            {
                if (_UseOcclusion != 0)
                {
                    uint visible = _BboxVisibilityFlags[_CurrentBboxIndex];
                    if (visible == 0)
                        clip(-1);
                }

                uint mask = ReadMask32(i.uv);
                if ((mask & _RequiredBit) == 0u)
                    clip(-1);

                float2 p = (i.uv * 2.0 - 1.0) * _Scale;
                float acc = 0.0;
                float2 z = p;

                [loop]
                for (int iter = 0; iter < _Iterations; iter++)
                {
                    z = Rotate(z, 0.15 + 0.01 * iter);
                    z += float2(
                        sin(z.y * 1.7 + iter * 0.1),
                        cos(z.x * 1.3 - iter * 0.07)
                    );

                    float d = dot(z, z);
                    acc += sin(d) + cos(z.x) * sin(z.y);
                    z /= (1.0 + 0.05 * d);
                }

                float v = 0.5 + 0.5 * sin(acc * 0.05);
                return float4(v, v, v, 1.0);
            }
            ENDHLSL
        }
    }
}