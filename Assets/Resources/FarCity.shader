// the city past the tiles: plain boxes, shaded by vertex colour, lit evenly like the far towers and in thinner fog
// than the world's, so the skyline goes on into the haze. cut away where the real tiles stand
Shader "Hidden/FarCity"
{
	Properties
	{
		_Albedo ("Wall colour", Color) = (0.6, 0.6, 0.62, 1)
		_FogColor ("Fog", Color) = (0.7, 0.72, 0.75, 1)
		_Density ("World fog density", Float) = 0.002
		_Share ("Share of the world's fog far out", Range(0, 1)) = 0.22
		_Hide ("Where the tiles are, two points xz", Vector) = (0, 0, 0, 0)
		_HideRadius ("Tiles' reach", Float) = 680
		_SunShare ("Sun on it, the same all round", Range(0, 1)) = 0.35
		_FarFade ("Far clip", Float) = 7000
	}
	SubShader
	{
		Tags { "RenderType" = "Opaque" "Queue" = "Geometry+1" "RenderPipeline" = "UniversalPipeline" }
		Pass
		{
			Tags { "LightMode" = "UniversalForward" }
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

			CBUFFER_START(UnityPerMaterial)
			float4 _Albedo;
			float4 _FogColor;
			float _Density;
			float _Share;
			float4 _Hide;
			float _HideRadius;
			float _SunShare;
			float _FarFade;
			CBUFFER_END

			struct Attributes
			{
				float4 position : POSITION;
				float4 colour : COLOR;
			};

			struct Varyings
			{
				float4 position : SV_POSITION;
				float3 world : TEXCOORD0;
				float4 colour : COLOR;
			};

			Varyings vert(Attributes input)
			{
				Varyings output;
				output.world = TransformObjectToWorld(input.position.xyz);
				output.position = TransformWorldToHClip(output.world);
				output.colour = input.colour;
				return output;
			}

			half4 frag(Varyings input) : SV_Target
			{
				float2 a = input.world.xz - _Hide.xy;
				float2 b = input.world.xz - _Hide.zw;
				float near = sqrt(min(dot(a, a), dot(b, b)));
				clip(near - _HideRadius);
				Light sun = GetMainLight();
				float3 sky = (SampleSH(half3(0, 1, 0)) + SampleSH(half3(0, -1, 0))) * 0.5;
				float3 lit = sun.color * _SunShare + sky;
				float3 building = _Albedo.rgb * input.colour.rgb * lit;
				float seen = length(input.world - _WorldSpaceCameraPos);
				// world fog where it meets the tiles, so there's no step, thinning out past them
				float thin = exp(-_Density * _Share * seen);
				float world = exp(-_Density * seen);
				float shows = lerp(world, thin, smoothstep(_HideRadius, _HideRadius + 900, near));
				shows *= 1 - smoothstep(_FarFade * 0.8, _FarFade * 0.97, seen);
				return half4(lerp(_FogColor.rgb, building, shows), 1);
			}
			ENDHLSL
		}
	}
}
