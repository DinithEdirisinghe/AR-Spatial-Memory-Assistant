Shader "SpatialSense/Home Route Occlusion"
{
    Properties
    {
        [MainColor] _BaseColor("Route color", Color) = (0.1,0.5,1,1)
        _EnvironmentDepthBias("Environment depth tolerance", Range(0,0.05)) = 0.015
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "HomeRoute"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ HARD_OCCLUSION SOFT_OCCLUSION
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.meta.xr.sdk.core/Shaders/EnvironmentDepth/URP/EnvironmentOcclusionURP.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                META_DEPTH_VERTEX_OUTPUT(0)
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _EnvironmentDepthBias;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                META_DEPTH_INITIALIZE_VERTEX_OUTPUT(output,input.positionOS);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 color = _BaseColor;
                #if defined(HARD_OCCLUSION) || defined(SOFT_OCCLUSION)
                    float4 depthSpace = mul(_EnvironmentDepthReprojectionMatrices[unity_StereoEyeIndex],float4(input.posWorld,1));
                    float2 uv = (depthSpace.xy / max(depthSpace.w,0.000001) + 1) * 0.5;
                    // Outside sensed depth coverage, retain the normal route
                    // rather than sampling a clamped, unrelated edge pixel.
                    if(depthSpace.w > 0 && all(uv >= 0) && all(uv <= 1))
                    {
                        META_DEPTH_OCCLUDE_OUTPUT_PREMULTIPLY(input,color,_EnvironmentDepthBias);
                    }
                #endif
                return color;
            }
            ENDHLSL
        }
    }
}
