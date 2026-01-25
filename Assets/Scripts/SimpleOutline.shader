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
 
            float _OutlineThickness;
            float4 _OutlineColor;
 
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };
 
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };
 
            Varyings vert(Attributes input)
            {
                Varyings output;
 
                // Expand vertex position along its normal in object space
                float3 expanded = input.positionOS.xyz +
                                  normalize(input.normalOS) * _OutlineThickness;
 
                float3 positionWS = TransformObjectToWorld(float4(expanded, 1.0));
                output.positionHCS = TransformWorldToHClip(positionWS);
 
                return output;
            }
 
            half4 frag(Varyings input) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
}
