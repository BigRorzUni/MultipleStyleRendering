Shader "Custom/bboxOcclusion"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            Name "bboxOcclusion"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite Off
            ZTest LEqual
            Blend Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

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

            // if renderer survives depth testing then it is visible and output white
            half4 frag(Varyings input) : SV_Target
            {
                return half4(1, 1, 1, 1);
            }
            ENDHLSL
        }
    }
}