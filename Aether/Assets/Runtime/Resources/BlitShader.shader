Shader "Aether/ColorBlit"
{
        SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline"}
        LOD 100
        ZWrite Off Cull Off
        Pass
        {
            Name "ColorBlitPass"

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // The Blit.hlsl file provides the vertex shader (Vert),
            // input structure (Attributes) and output strucutre (Varyings)
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

            #pragma vertex Vert
            #pragma fragment frag

            TEXTURE2D_X(_CameraOpaqueTexture);
            SAMPLER(sampler_CameraOpaqueTexture);

            TEXTURE3D(_Volume);
            SAMPLER(sampler_Volume);

            float _fogFar;
            float _cameraFar;

            static const float4 curve4_0 = float4(0.0205f, 0.0205f, 0.0205f, 0.0f);
			static const float4 curve4_1 = float4(0.0855f, 0.0855f, 0.0855f, 0.0f);
			static const float4 curve4_2 = float4(0.232f, 0.232f, 0.232f, 0.0f);
			static const float4 curve4_3 = float4(0.324f, 0.324f, 0.324f, 1.0f);

            float4 GaussianDirection(float2 uv, float2 filterWidth, float depth)
            {
				float4 color = 0;
                color += SAMPLE_TEXTURE3D(_Volume, sampler_Volume, float3(uv - filterWidth * 3, saturate((depth * _cameraFar) / _fogFar))) * curve4_0;
				color += SAMPLE_TEXTURE3D(_Volume, sampler_Volume, float3(uv - filterWidth * 2, saturate((depth * _cameraFar) / _fogFar))) * curve4_1;
				color += SAMPLE_TEXTURE3D(_Volume, sampler_Volume, float3(uv - filterWidth, saturate((depth * _cameraFar) / _fogFar))) * curve4_2;
				color += SAMPLE_TEXTURE3D(_Volume, sampler_Volume, float3(uv, saturate((depth * _cameraFar) / _fogFar))) * curve4_3;
				color += SAMPLE_TEXTURE3D(_Volume, sampler_Volume, float3(uv - filterWidth, saturate((depth * _cameraFar) / _fogFar))) * curve4_2;
				color += SAMPLE_TEXTURE3D(_Volume, sampler_Volume, float3(uv - filterWidth * 2, saturate((depth * _cameraFar) / _fogFar))) * curve4_1;
				color += SAMPLE_TEXTURE3D(_Volume, sampler_Volume, float3(uv - filterWidth * 3, saturate((depth * _cameraFar) / _fogFar))) * curve4_0;

				return color;
			}

            half4 frag (Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float4 color = SAMPLE_TEXTURE2D_X(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, input.texcoord);

                float depth = Linear01Depth(SampleSceneDepth(input.texcoord), _ZBufferParams);
                float4 fog = GaussianDirection(input.texcoord, float2(-.02, .02), depth);

                fog = SAMPLE_TEXTURE3D(_Volume, sampler_Volume, float3(input.texcoord, saturate((depth * _cameraFar) / _fogFar)));
                
                return color + fog * fog.a;
            }
            ENDHLSL
        }
    }
}