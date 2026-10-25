// far tower: thin fog, hazy at the foot and the top, faint streaks
Shader "Hidden/DistantSilhouette"
{
	Properties
	{
		_Tint ("Structure", Color) = (0.1, 0.115, 0.14, 1)
		_FogColor ("Fog", Color) = (0.7, 0.72, 0.75, 1)
		_Visibility ("How much shows through the fog", Range(0, 1)) = 0.5
		_Base ("World height of the foot", Float) = 0
		_Height ("Height", Float) = 570
		_Streaks ("Streaks round it", Float) = 14
	}
	SubShader
	{
		Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
		Pass
		{
			Tags { "LightMode" = "UniversalForward" }
			// real pillar wins where both draw
			Offset 2, 2
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

			CBUFFER_START(UnityPerMaterial)
			float4 _Tint;
			float4 _FogColor;
			float _Visibility;
			float _Base;
			float _Height;
			float _Streaks;
			CBUFFER_END

			struct Attributes
			{
				float4 position : POSITION;
				float3 normal : NORMAL;
			};

			struct Varyings
			{
				float4 position : SV_POSITION;
				float3 world : TEXCOORD0;
				float3 normal : TEXCOORD1;
				float around : TEXCOORD2;
			};

			float Hash(float p)
			{
				return frac(sin(p * 12.9898) * 43758.5453);
			}

			Varyings vert(Attributes input)
			{
				Varyings output;
				output.world = TransformObjectToWorld(input.position.xyz);
				output.position = TransformWorldToHClip(output.world);
				output.normal = TransformObjectToWorldNormal(input.normal);
				output.around = atan2(input.normal.z, input.normal.x) / (2 * PI) + 0.5;
				return output;
			}

			half4 frag(Varyings input) : SV_Target
			{
				float up = saturate((input.world.y - _Base) / _Height);
				// haze at the foot and up into the ceiling
				float haze = _Visibility * lerp(0.2, 0.85, smoothstep(0.03, 0.6, up)) * lerp(1, 0.12, smoothstep(0.76, 0.97, up));
				float lit = 0.96 + 0.06 * saturate(dot(normalize(input.normal), GetMainLight().direction));
				float column = floor(input.around * _Streaks);
				float streak = 1 - 0.12 * step(0.55, Hash(column)) * smoothstep(0.1, 0.5, frac(input.around * _Streaks));
				float shadow = lerp(1, 0.75, smoothstep(0.65, 0.85, up));
				float3 structure = _Tint.rgb * lit * streak * shadow;
				return half4(lerp(_FogColor.rgb, structure, haze), 1);
			}
			ENDHLSL
		}
	}
}
