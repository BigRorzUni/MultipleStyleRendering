Shader "Custom/ID"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            Name "ID"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual
            Blend Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _DEBUG_ID_COLOUR

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderVariablesFunctions.hlsl"

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
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }


            CBUFFER_START(UnityPerMaterial)
                uint _ImageStyleID;   // set via MaterialPropertyBlock from stylised tag
            CBUFFER_END

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

            float4 PackUIntToRGBA8(uint v)
            {
                // 32 bits, 8 per colour channel
                uint r = v & 0xFFu;
                uint g = (v >> 8) & 0xFFu;
                uint b = (v >> 16) & 0xFFu;
                uint a = (v >> 24) & 0xFFu;

                return float4(r / 255.0, g / 255.0, b / 255.0, a / 255.0);
            }

            half4 frag(Varyings input) : SV_Target
            {
                uint style = (uint)_ImageStyleID;

                #ifndef _DEBUG_ID_COLOUR
                    return PackUIntToRGBA8(style);
                #else
                    if (style == 0u)
                        return half4(0,0,0,1);
                    float3 c = HashColour(style); // hash gets unstable at higher indices
                    return half4(c, 1);
                #endif
            }
            ENDHLSL
        }
    }
}