// underside of the next level, hazes by view angle too so it fades out toward the horizon
Shader "Hidden/DistantCeiling"
{
	Properties
	{
		_Tint ("Underside", Color) = (0.13, 0.15, 0.18, 1)
		_Rim ("Broken edges", Color) = (0.7, 0.73, 0.78, 1)
		_FogColor ("Fog", Color) = (0.7, 0.72, 0.75, 1)
		_Density ("Fog density", Float) = 0.0006
		_FarFade ("Far clip", Float) = 2000
		_Strength ("Most it ever shows", Range(0, 1)) = 0.7
	}
	SubShader
	{
		Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
		Pass
		{
			Tags { "LightMode" = "UniversalForward" }
			Cull Off
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			CBUFFER_START(UnityPerMaterial)
			float4 _Tint;
			float4 _Rim;
			float4 _FogColor;
			float _Density;
			float _FarFade;
			float _Strength;
			CBUFFER_END

			struct Attributes
			{
				float4 position : POSITION;
				float4 color : COLOR;
			};

			struct Varyings
			{
				float4 position : SV_POSITION;
				float3 world : TEXCOORD0;
				float rim : TEXCOORD1;
			};

			float Hash(float2 p)
			{
				return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
			}

			Varyings vert(Attributes input)
			{
				Varyings output;
				output.world = TransformObjectToWorld(input.position.xyz);
				output.position = TransformWorldToHClip(output.world);
				output.rim = input.color.r;
				return output;
			}

			half4 frag(Varyings input) : SV_Target
			{
				float3 view = input.world - _WorldSpaceCameraPos;
				float distance = length(view);
				// sin of elevation
				float rise = view.y / max(distance, 1);
				float visibility = exp(-_Density * distance) * smoothstep(0.04, 0.4, rise) * (1 - smoothstep(_FarFade * 0.6, _FarFade * 0.9, distance));
				// patchy
				float patches = Hash(floor(input.world.xz / 70)) * 0.25 + Hash(floor(input.world.xz / 23)) * 0.1;
				float3 colour = lerp(_Tint.rgb * (0.85 + patches), _Rim.rgb, input.rim);
				return half4(lerp(_FogColor.rgb, colour, visibility * _Strength), 1);
			}
			ENDHLSL
		}
	}
}
