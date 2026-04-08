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
            Blend Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct InstanceData
            {
                float4 rect; 
            };

            StructuredBuffer<InstanceData> _InstanceData;
            float4 _NprScreenSize; 

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
                Varyings output;

                float2 uv = GetQuadUV(input.vertexID);
                float4 rect = _InstanceData[input.instanceID].rect;

                // map local quad UV to pixel coords within bbox
                float2 pixelPos = rect.xy + uv * rect.zw;

                // convert to clip space
                float2 ndc;
                ndc.x = pixelPos.x * _NprScreenSize.z * 2.0 - 1.0;
                ndc.y = 1.0 - pixelPos.y * _NprScreenSize.w * 2.0; // flip y for Unity's screen space

                output.posCS = float4(ndc, 0.0, 1.0);
                output.screenUV = pixelPos * _NprScreenSize.zw;

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return half4(1, 0, 1, 1);
            }
            ENDHLSL
        }
    }
}