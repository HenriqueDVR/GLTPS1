// haze wherever nothing else drew, stands in for the scene's own skybox mesh which cut the far stuff off
Shader "Hidden/DistantSky"
{
	Properties
	{
		_FogColor ("Fog", Color) = (0.7, 0.72, 0.75, 1)
	}
	SubShader
	{
		Tags { "Queue" = "Transparent-100" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
		Pass
		{
			Tags { "LightMode" = "UniversalForward" }
			ZWrite Off
			Cull Front
			Blend SrcAlpha OneMinusSrcAlpha
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			CBUFFER_START(UnityPerMaterial)
			float4 _FogColor;
			CBUFFER_END

			struct Varyings
			{
				float4 position : SV_POSITION;
				float3 world : TEXCOORD0;
			};

			Varyings vert(float4 position : POSITION)
			{
				Varyings output;
				output.world = TransformObjectToWorld(position.xyz);
				output.position = TransformWorldToHClip(output.world);
				// on the far plane, only fills what nothing else drew
				#if UNITY_REVERSED_Z
				output.position.z = 1.0e-6 * output.position.w;
				#else
				output.position.z = (1 - 1.0e-6) * output.position.w;
				#endif
				return output;
			}

			half4 frag(Varyings input) : SV_Target
			{
				return half4(_FogColor.rgb, 1);
			}
			ENDHLSL
		}
	}
}
