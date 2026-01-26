Shader "Custom/SimpleOutline"
{
	Properties
    {
        _OutlineColor ("Outline Color", Color) = (0,0,0,1)
        _OutlineThickness ("Outline Thickness", Float) = 0.03
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+1" }
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "UniversalForward" }
 
            Cull Front
            ZWrite On
            ZTest LEqual
 
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float  _StylisedMask; // per renderer
                float  _OutlineThickness;
                float4 _OutlineColor;
            CBUFFER_END
 
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };
 
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 screenPos : TEXCOORD0;
            };
 
            Varyings vert(Attributes input)
            {
                Varyings output;
 
                // Expand vertex position along its normal in object space
                float3 expandedOS = input.positionOS.xyz + normalize(input.normalOS) * _OutlineThickness;
 
                float3 positionWS = TransformObjectToWorld(expandedOS);
                output.positionHCS = TransformWorldToHClip(positionWS);
                output.screenPos = ComputeScreenPos(output.positionHCS);
 
                return output;
            }
 
            half4 frag(Varyings input) : SV_Target
            {
                uint mask = (uint)round(_StylisedMask);
                const uint OUTLINE_BIT = 1u << 0;

                if ((mask & OUTLINE_BIT) == 0u) discard;
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
}
