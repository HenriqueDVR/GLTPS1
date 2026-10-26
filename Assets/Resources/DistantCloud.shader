// cloud layer under the next level, thick toward the horizon
Shader "Hidden/DistantCloud"
{
	Properties
	{
		_FogColor ("Fog", Color) = (0.7, 0.72, 0.75, 1)
		_Cover ("How much of it is cloud", Range(0, 1)) = 0.5
		_Scale ("Patch size", Float) = 450
		_Drift ("Drift", Vector) = (4, 0, 2, 0)
		_FarFade ("Far clip", Float) = 7000
		_Origin ("Pattern centre", Vector) = (0, 0, 0, 0)
	}
	SubShader
	{
		// after the ceiling, before the towers, so a tower never gets a cloud line across it
		Tags { "Queue" = "Geometry+1" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
		Pass
		{
			Tags { "LightMode" = "UniversalForward" }
			ZWrite Off
			Cull Off
			Blend SrcAlpha OneMinusSrcAlpha
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			CBUFFER_START(UnityPerMaterial)
			float4 _FogColor;
			float _Cover;
			float _Scale;
			float4 _Drift;
			float _FarFade;
			float4 _Origin;
			CBUFFER_END

			// round each tower: x, z, from, to. arrays can't go in the material buffer
			float4 _Gaps[16];
			float _GapCount;

			struct Varyings
			{
				float4 position : SV_POSITION;
				float3 world : TEXCOORD0;
			};

			float Hash(float2 p)
			{
				return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
			}

			float Noise(float2 p)
			{
				float2 cell = floor(p);
				float2 f = frac(p);
				f = f * f * (3 - 2 * f);
				return lerp(lerp(Hash(cell), Hash(cell + float2(1, 0)), f.x), lerp(Hash(cell + float2(0, 1)), Hash(cell + float2(1, 1)), f.x), f.y);
			}

			Varyings vert(float4 position : POSITION)
			{
				Varyings output;
				output.world = TransformObjectToWorld(position.xyz);
				output.position = TransformWorldToHClip(output.world);
				return output;
			}

			half4 frag(Varyings input) : SV_Target
			{
				float2 p = (input.world.xz - _Origin.xy + _Drift.xz * _Time.y) / _Scale;
				float cloud = Noise(p) * 0.55 + Noise(p * 2.3 + 17) * 0.3 + Noise(p * 5.1 - 31) * 0.15;
				float alpha = smoothstep(1 - _Cover, 1 - _Cover + 0.3, cloud) * 0.85;
				float3 view = input.world - _WorldSpaceCameraPos;
				float distance = length(view);
				float rise = abs(view.y) / max(distance, 1);
				alpha = max(alpha, (1 - smoothstep(0.02, 0.3, rise)) * 0.95);
				alpha *= 1 - smoothstep(_FarFade * 0.75, _FarFade * 0.95, distance);
				// no hard line where the plane cuts a tower, ragged so it isn't a circle
				float ragged = Noise(p * 3.7 + 11) - 0.5;
				for (int i = 0; i < 16; i++)
				{
					if (i >= _GapCount)
					{
						break;
					}
					float4 gap = _Gaps[i];
					alpha *= smoothstep(gap.z, gap.w, length(input.world.xz - gap.xy) + ragged * (gap.w - gap.z) * 1.2);
				}
				return half4(_FogColor.rgb * 1.02, alpha);
			}
			ENDHLSL
		}
	}
}
