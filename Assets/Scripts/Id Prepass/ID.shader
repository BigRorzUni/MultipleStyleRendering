Shader "Custom/ID"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            Name "ID"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back // lets effects work when viewed from past a backface
            ZWrite On
            ZTest LEqual
            Blend Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _DEBUG_ID_COLOUR
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _StylisedMask;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionHCS = TransformWorldToHClip(positionWS);
                return output;
            }

            // hashing colours for debugging
            float3 HashColour(float x) // x ~ 0..255
            {
                // Cheap stable hash -> RGB in [0,1)
                float3 p = frac(float3(0.1031, 0.11369, 0.13787) * x);
                p += dot(p, p.yzx + 19.19);
                return frac((p.xxy + p.yzz) * p.zyx);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float id8 = round(clamp(_StylisedMask, 0.0, 255.0));

                // NORMAL: encode ID into 0..1 so it can be decoded latur
                #ifndef _DEBUG_ID_COLOUR
                    float idNorm = id8 / 255.0;
                    return half4(idNorm, 0, 0, 1);
                #else
                // DEBUG: false colour display
                    if (id8 < 0.5) return half4(0,0,0,1);
                    float3 c = HashColour(id8);
                    return half4(c, 1);
                #endif
            }
            ENDHLSL
        }
    }
}