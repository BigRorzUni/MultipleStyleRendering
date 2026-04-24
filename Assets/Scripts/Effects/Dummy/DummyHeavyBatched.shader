Shader "Custom/DummyHeavyBatched"
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
            Name "DummyHeavyBatched"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            StructuredBuffer<float4> _InstanceData;
            StructuredBuffer<uint> _BboxVisibilityFlags;
            StructuredBuffer<uint> _BboxIndices;
            StructuredBuffer<uint> _BBoxMasks;

            float4 _NprScreenSize;
            int _UseOcclusion;
            int _UseBboxIndices;

            TEXTURE2D(_NprIdTexture);

            uint _RequiredBit;
            int _Iterations;
            float _Scale;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 posCS : SV_POSITION;
                float2 screenUV : TEXCOORD0;
            };

            float2 GetQuadUV(uint vertexID)
            {
                switch (vertexID)
                {
                    case 0: return float2(0, 0);
                    case 1: return float2(1, 0);
                    case 2: return float2(1, 1);
                    case 3: return float2(0, 0);
                    case 4: return float2(1, 1);
                    case 5: return float2(0, 1);
                    default: return float2(0, 0);
                }
            }

            Varyings Vert(Attributes v)
            {
                Varyings o;

                uint bboxIndex = v.instanceID;
                if (_UseBboxIndices != 0)
                    bboxIndex = _BboxIndices[v.instanceID];

                uint bboxMask = _BBoxMasks[bboxIndex];

                if ((bboxMask & _RequiredBit) == 0u)
                {
                    o.posCS = float4(-2.0, -2.0, 0.0, 1.0);
                    o.screenUV = float2(0.0, 0.0);
                    return o;
                }

                if (_UseOcclusion != 0)
                {
                    uint visible = _BboxVisibilityFlags[bboxIndex];
                    if (visible == 0)
                    {
                        o.posCS = float4(-2.0, -2.0, 0.0, 1.0);
                        o.screenUV = float2(0.0, 0.0);
                        return o;
                    }
                }

                float2 uv = GetQuadUV(v.vertexID);
                float4 rect = _InstanceData[v.instanceID];

                float2 pixelPos = rect.xy + uv * rect.zw;

                float2 ndc;
                ndc.x = pixelPos.x * _NprScreenSize.z * 2.0 - 1.0;
                ndc.y = 1.0 - pixelPos.y * _NprScreenSize.w * 2.0;

                o.posCS = float4(ndc, 0.0, 1.0);
                o.screenUV = pixelPos * _NprScreenSize.zw;

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
                uint mask = ReadMask32(i.screenUV);

                if ((mask & _RequiredBit) == 0u)
                    clip(-1);

                float2 p = (i.screenUV * 2.0 - 1.0) * _Scale;
                float2 z = p;
                float acc = 0.0;

                [loop]
                for (int iter = 0; iter < _Iterations; iter++)
                {
                    float angle = 0.15 + 0.01 * iter;
                    z = Rotate(z, angle);

                    float sx = sin(z.y * 1.7 + iter * 0.1);
                    float cy = cos(z.x * 1.3 - iter * 0.07);
                    z += float2(sx, cy);

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