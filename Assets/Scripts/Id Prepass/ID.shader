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
            float3 HashColour(float x)
            {
                float3 p = frac(float3(0.1031, 0.11369, 0.13787) * x);
                p += dot(p, p.yzx + 19.19);
                return frac((p.xxy + p.yzz) * p.zyx);
            }

            half4 frag(Varyings input) : SV_Target
            {
                uint style = (uint)_ImageStyleID; 

                #ifndef _DEBUG_ID_COLOUR
                    float r = (float)style / 255.0; // the texture must be normalised
                    return half4(r, 0, 0, 1);
                #else
                    if (style == 0u) 
                        return half4(0,0,0,1);
                    float3 c = HashColour((float)style);
                    return half4(c, 1);
                #endif
            }
            ENDHLSL
        }
    }
}