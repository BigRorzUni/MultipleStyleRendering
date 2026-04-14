Shader "Custom/Normals"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "Normals"
            Tags { "LightMode"="UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings output;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                output.positionHCS = TransformWorldToHClip(posWS);

                output.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return output;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                return half4(n * 0.5 + 0.5, 1); // world-space normals mapped to 0..1
            }
            ENDHLSL
        }
    }
}