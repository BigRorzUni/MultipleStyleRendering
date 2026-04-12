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


            StructuredBuffer<float4> _InstanceData;
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

                float2 uv = GetQuadUV(input.vertexID);
                float4 rect = _InstanceData[input.instanceID];

                float2 pixelPos = rect.xy + uv * rect.zw;

                float2 ndc;
                ndc.x = pixelPos.x * _NprScreenSize.z * 2.0 - 1.0;
                ndc.y = 1.0 - pixelPos.y * _NprScreenSize.w * 2.0;

                o.posCS = float4(ndc, 0, 1);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                return half4(1, 0, 0, 0.5);
            }
            ENDHLSL
        }
    }
}