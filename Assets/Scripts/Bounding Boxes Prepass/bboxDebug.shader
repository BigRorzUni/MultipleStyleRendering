Shader "Custom/bboxDebug"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Overlay" }

        Pass
        {
            Name "bboxDebug"
            Cull Off
            ZWrite Off
            ZTest Always
            // Blend SrcAlpha OneMinusSrcAlpha
            Blend One One

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct QuadInstanceData
            {
                float4 rect;
            };

            StructuredBuffer<QuadInstanceData> _InstanceData;
            StructuredBuffer<uint> _BBoxMasks;
            float4 _NprScreenSize;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 posCS : SV_POSITION;
                float2 localUV : TEXCOORD0;
                uint instanceID : TEXCOORD1;
            };

            // each quad is made of 2 triangles based on the rect of the instance data
            float2 GetQuadUV(uint vertexID)
            {
                switch (vertexID)
                {
                    case 0: 
                        return float2(0, 0);
                    case 1: 
                        return float2(1, 0);
                    case 2: 
                        return float2(1, 1);
                    case 3: 
                        return float2(0, 0);
                    case 4: 
                        return float2(1, 1);
                    case 5: 
                        return float2(0, 1);

                    default:
                        return float2(0, 0); // should never happen
                }
            }

            uint HashUint(uint x)
            {
                x ^= x >> 16;
                x *= 0x7feb352du;
                x ^= x >> 15;
                x *= 0x846ca68bu;
                x ^= x >> 16;
                return x;
            }

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

            Varyings Vert(Attributes input)
            {
                Varyings o;

                float2 uv = GetQuadUV(input.vertexID);
                float4 rect = _InstanceData[input.instanceID].rect;

                float2 pixelPos = rect.xy + uv * rect.zw;

                float2 ndc;
                ndc.x = pixelPos.x * _NprScreenSize.z * 2.0 - 1.0;
                ndc.y = 1.0 - pixelPos.y * _NprScreenSize.w * 2.0;

                o.posCS = float4(ndc, 0, 1);
                o.localUV = uv;
                o.instanceID = input.instanceID;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                uint mask = _BBoxMasks[i.instanceID];
                float3 colour = HashColour(mask);

                // float thickness = 0.03;

                // bool border =
                //     i.localUV.x < thickness ||
                //     i.localUV.x > 1.0 - thickness ||
                //     i.localUV.y < thickness ||
                //     i.localUV.y > 1.0 - thickness;

                // if (!border)
                //     clip(-1);

                return half4(colour / 5, 1.0);
            }
            ENDHLSL
        }
    }
}