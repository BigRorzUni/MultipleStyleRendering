Shader "Custom/OcclusionDebug"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Overlay" }

        Pass
        {
            Name "OcclusionDebug"
            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct QuadInstanceData
            {
                float4 rect;
                int index;
            };

            StructuredBuffer<QuadInstanceData> _InstanceData;
            StructuredBuffer<uint> _BBoxVisibilityFlags;
            float4 _NprScreenSize;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 posCS : SV_POSITION;
            };

            float2 GetQuadCorner(uint vertexID)
            {
                switch (vertexID)
                {
                    case 0: return float2(0, 0);
                    case 1: return float2(1, 0);
                    case 2: return float2(1, 1);
                    case 3: return float2(0, 0);
                    case 4: return float2(1, 1);
                    default: return float2(0, 1);
                }
            }

            Varyings Vert(Attributes input)
            {
                Varyings o;

                uint visible = _BBoxVisibilityFlags[input.instanceID];

                // for this debug pass:
                // visible -> collapse
                // hidden  -> draw red
                if (visible != 0)
                {
                    o.posCS = float4(-2, -2, 0, 1);
                    return o;
                }

                float2 uv = GetQuadCorner(input.vertexID);
                float4 rect = _InstanceData[input.instanceID].rect;

                float2 pixelPos = rect.xy + uv * rect.zw;

                float2 ndc;
                ndc.x = pixelPos.x * _NprScreenSize.z * 2.0 - 1.0;
                ndc.y = 1.0 - pixelPos.y * _NprScreenSize.w * 2.0;

                o.posCS = float4(ndc, 0, 1);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                return half4(1, 0, 0, 0.6);
            }
            ENDHLSL
        }
    }
}