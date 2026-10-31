// a tile out past the whole ones (TileStreamer.FarRadius): its own texture and colour, lit evenly with a little of the
// sun's side, and the world's fog where it meets the whole tiles, thinning out past them so the city goes on into the
// haze. the textures have no mipmaps, so where a texel is smaller than a pixel it's the texture's average colour (the
// windows shimmered as the view moved). a tile just built fades in, dithered. the fog's numbers are globals, set once
// a frame by TileStreamer
Shader "Hidden/FarTile"
{
	Properties
	{
		_BaseMap ("Texture", 2D) = "white" {}
		_BaseColor ("Colour", Color) = (1, 1, 1, 1)
		_Average ("The texture's average times the colour", Color) = (0.6, 0.6, 0.6, 1)
		_Born ("When the tile was built (_Time.y)", Float) = -100
	}
	SubShader
	{
		Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
		Pass
		{
			Tags { "LightMode" = "UniversalForward" }
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

			TEXTURE2D(_BaseMap);
			SAMPLER(sampler_BaseMap);

			CBUFFER_START(UnityPerMaterial)
			float4 _BaseMap_ST;
			float4 _BaseMap_TexelSize;
			float4 _BaseColor;
			float4 _Average;
			float _Born;
			CBUFFER_END

			float4 _FarHide;
			float _FarNear;
			float _FarEase;
			float _FarShare;
			float4 _FarFogColor;
			float _FarDensity;

			struct Attributes
			{
				float4 position : POSITION;
				float3 normal : NORMAL;
				float2 uv : TEXCOORD0;
			};

			struct Varyings
			{
				float4 position : SV_POSITION;
				float3 world : TEXCOORD0;
				float3 normal : TEXCOORD1;
				float2 uv : TEXCOORD2;
			};

			Varyings vert(Attributes input)
			{
				Varyings output;
				output.world = TransformObjectToWorld(input.position.xyz);
				output.position = TransformWorldToHClip(output.world);
				output.normal = TransformObjectToWorldNormal(input.normal);
				output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
				return output;
			}

			// 4x4 ordered dither, 0..1
			float Dither(float2 pixel)
			{
				uint2 p = uint2(pixel) % 4;
				const float pattern[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
				return (pattern[p.y * 4 + p.x] + 0.5) / 16.0;
			}

			half4 frag(Varyings input) : SV_Target
			{
				clip(saturate((_Time.y - _Born) / 0.8) - Dither(input.position.xy));
				float2 texels = input.uv * _BaseMap_TexelSize.zw;
				float footprint = max(length(ddx(texels)), length(ddy(texels)));
				float3 albedo = lerp(_Average.rgb, SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb, saturate(1.5 - footprint));
				Light sun = GetMainLight();
				float3 sky = SampleSH(normalize(input.normal));
				float facing = saturate(dot(normalize(input.normal), sun.direction));
				float3 colour = albedo * (sky + sun.color * (0.25 + 0.5 * facing));
				float2 a = input.world.xz - _FarHide.xy;
				float2 b = input.world.xz - _FarHide.zw;
				float near = sqrt(min(dot(a, a), dot(b, b)));
				float seen = length(input.world - _WorldSpaceCameraPos);
				float density = _FarDensity * lerp(1, _FarShare, smoothstep(_FarNear, _FarNear + _FarEase, near));
				return half4(lerp(_FarFogColor.rgb, colour, exp(-density * seen)), 1);
			}
			ENDHLSL
		}
	}
}
