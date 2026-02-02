Shader "Custom/Toon"
{
    Properties
    {
        _StylisedMask ("Style Mask", float) = 0

        // take in unity's base properties
        _BaseTex ("mainLight Texture", 2D) = "white" {}
        _BaseColor ("Colour", Color) = (1,1,1,1)

        [HDR]
        _SpecColor("Specular Colour", Color) = (1, 1, 1, 1)
        _Smoothness("Smoothness", Float) = 0.5


    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }

        Pass
        {
            Name "Toon"
            Tags { "LightMode"="UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag 

            #pragma multi_compile _ _mainLight_LIGHT_SHADOWS
            #pragma multi_compile _ _mainLight_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            TEXTURE2D(_BaseTex);
            SAMPLER(sampler_BaseTex);

            CBUFFER_START(UnityPerMaterial)
                float  _StylisedMask;
                float4 _BaseColor;
                float4 _SpecColor;
                float _Smoothness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : NORMAL;
            };

            struct Varyings
            {
                float4 posCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 posWS : TEXCOORD2;
                float4 shadowCoord : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;

                o.posCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.worldNormal = TransformObjectToWorldNormal(v.normal);
                o.posWS = TransformObjectToWorld(v.positionOS.xyz);

                VertexPositionInputs posInputs = GetVertexPositionInputs(v.positionOS.xyz);
                o.shadowCoord = GetShadowCoord(posInputs);

                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                uint mask = (uint)round(_StylisedMask);
                const uint TOON_BIT = 1u << 1;
                if ((mask & TOON_BIT) == 0u)
                    discard;

                float3 normal = normalize(i.worldNormal);

                Light mainLight = GetMainLight();
                float3 lightDir = normalize(mainLight.direction);

                float shadow = mainLight.shadowAttenuation;

                float3 ambient = 0.1 * mainLight.color.rgb;
                
                float NdotL = saturate(dot(lightDir, normal));
                //float lightIntensity = (NdotL > 0.66) ? 1.0 : (NdotL > 0.33 ? 0.5 : 0.01);
                // float b1 = smoothstep(0.005, 0.01, NdotL);
                // float b2 = smoothstep(0.66, 0.67, NdotL);
                float lightIntensity = smoothstep(0.05, 0.1, NdotL * shadow);
                float3 diffuse = lightIntensity * mainLight.color.rgb;

                float3 viewDir = normalize(GetWorldSpaceViewDir(i.posWS));
                float3 halfVector = normalize(lightDir + viewDir);
                float NdotH = saturate(dot(normal, halfVector));
                float specularIntensity = pow(NdotH, 64 * _Smoothness);
                float3 spec = specularIntensity * lightIntensity * _SpecColor.rgb * mainLight.color.rgb;

                float rim = 1 - dot(viewDir, normal);
                rim *= pow(diffuse, 0.1);

                float4 tex = SAMPLE_TEXTURE2D(_BaseTex, sampler_BaseTex, i.uv);

                //return float4(NdotL.xxx, 1);
                //return float4(specularIntensity.xxx, 1);

                return tex * _BaseColor  * float4((ambient + diffuse + max(spec, rim)), 1);
                //return tex * _BaseColor * (float4(ambient + diffuse,1) + specularIntensity);
            }
            ENDHLSL
        }
    }
}