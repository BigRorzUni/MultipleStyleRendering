Shader "Custom/Toon"
{
    Properties
    {
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

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            TEXTURE2D(_BaseTex);
            SAMPLER(sampler_BaseTex);

            CBUFFER_START(UnityPerMaterial)
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

                o.uv = v.uv;
                o.worldNormal = TransformObjectToWorldNormal(v.normal);
                
                VertexPositionInputs posInputs = GetVertexPositionInputs(v.positionOS.xyz);
                o.posCS = posInputs.positionCS;
                o.shadowCoord = GetShadowCoord(posInputs);
                o.posWS = posInputs.positionWS;

                return o;
            }

            float3 LightContribution(Light L, float3 normal, float3 worldPos)
            {
                float3 lightDir = normalize(L.direction);

                float attentuation = L.distanceAttenuation * L.shadowAttenuation;
                
                float NdotL = saturate(dot(lightDir, normal));
                float toon = smoothstep(0.05, 0.1, NdotL * attentuation);
                float3 diffuse = L.color.rgb * toon;

                float3 viewDir = normalize(GetWorldSpaceViewDir(worldPos));
                float3 halfVector = normalize(lightDir + viewDir);
                float NdotH = saturate(dot(normal, halfVector));
                float specularIntensity = pow(NdotH, 64 * _Smoothness);
                float3 spec = specularIntensity * _SpecColor.rgb * toon * L.color.rgb;

                float rim = 1 - dot(viewDir, normal);
                rim *= pow(diffuse, 0.1) * toon;

                return diffuse + max(spec, rim);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 normal = normalize(i.worldNormal);
                float3 worldPos = i.posWS;

                float3 lit = 0;

                Light mainLight = GetMainLight(i.shadowCoord);

                float3 ambient = 0.1 * mainLight.color.rgb;
                lit += ambient; 

                lit += LightContribution(mainLight, normal, worldPos);

                #ifdef _ADDITIONAL_LIGHTS
                    int lightCount = GetAdditionalLightsCount();
                    for(int li = 0; li < lightCount; li++)
                    {
                        Light l = GetAdditionalLight(li, worldPos);
                        lit += LightContribution(l, normal, worldPos);
                    }
                #endif

                float4 tex = SAMPLE_TEXTURE2D(_BaseTex, sampler_BaseTex, i.uv);

                return tex * _BaseColor * float4(lit, 1);
            }
            ENDHLSL
        }
    }
}