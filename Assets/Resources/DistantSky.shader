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

			// AtmosphereStack: how much of the stars show. they only show where nothing else drew, through the deck's holes
			float _SkyStars;

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
				float3 view = normalize(input.world - _WorldSpaceCameraPos);
				// low down it's past the end of the drawn deck, not a hole in it
				float high = smoothstep(0.34, 0.5, view.y);
				view *= 170.0;
				float3 cell = floor(view);
				float luck = frac(sin(dot(cell, float3(12.9898, 78.233, 37.719))) * 43758.5453);
				float size = length(frac(view) - 0.5);
				float star = (luck > 0.985) ? smoothstep(0.5, 0.2, size) * (0.35 + (luck - 0.985) / 0.015 * 0.65) : 0.0;
				return half4(_FogColor.rgb + star * high * _SkyStars, 1);
			}
			ENDHLSL
		}
	}
}
