// underside of the next level, hazes by view angle too so it fades out toward the horizon
Shader "Hidden/DistantCeiling"
{
	Properties
	{
		_Tint ("Underside", Color) = (0.13, 0.15, 0.18, 1)
		_Rim ("Broken edges", Color) = (0.7, 0.73, 0.78, 1)
		_FogColor ("Fog", Color) = (0.7, 0.72, 0.75, 1)
		_Density ("Fog density", Float) = 0.0006
		_FarFade ("Far clip", Float) = 7000
		_Strength ("Most it ever shows", Range(0, 1)) = 0.7
		_Facade ("Buildings on the deck", Color) = (0.5, 0.52, 0.55, 1)
		_DetailNear ("Underside detail full until", Float) = 650
		_DetailFar ("Underside detail gone by", Float) = 1000
		_Detail ("How much the panels and beams show", Range(0, 1)) = 1
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
			float4 _Facade;
			float4 _FogColor;
			float _Density;
			float _FarFade;
			float _Strength;
			float _DetailNear;
			float _DetailFar;
			float _Detail;
			CBUFFER_END

			// noclip debug, F3: flat red, no fades
			float _DebugFlat;

			struct Attributes
			{
				float4 position : POSITION;
				float4 color : COLOR;
			};

			struct Varyings
			{
				float4 position : SV_POSITION;
				float3 world : TEXCOORD0;
				// rim, recess, truss, and alpha under 1 for building facades
				float4 marks : TEXCOORD1;
				// pattern in object space, the world one moves with the floating origin
				float3 local : TEXCOORD2;
			};

			float Hash(float2 p)
			{
				return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
			}

			// 1 on bands this wide every spacing, both ways
			float Lines(float2 p, float spacing, float width)
			{
				float2 d = abs(frac(p / spacing + 0.5) - 0.5) * spacing;
				return step(min(d.x, d.y), width * 0.5);
			}

			Varyings vert(Attributes input)
			{
				Varyings output;
				output.world = TransformObjectToWorld(input.position.xyz);
				output.position = TransformWorldToHClip(output.world);
				output.marks = input.color;
				output.local = input.position.xyz;
				return output;
			}

			half4 frag(Varyings input) : SV_Target
			{
				float3 view = input.world - _WorldSpaceCameraPos;
				float distance = length(view);
				// sin of elevation
				float rise = view.y / max(distance, 1);
				float visibility = exp(-_Density * distance) * smoothstep(0.04, 0.4, rise) * (1 - smoothstep(_FarFade * 0.8, _FarFade * 0.97, distance));
				float2 p = input.local.xz;
				float across = input.local.x + input.local.z;
				// steel truss under the broken edges: chords, posts and crossed diagonals, the rest cut out
				if (input.marks.b > 0.5)
				{
					float bay = frac(across / 16);
					float down = saturate(-input.local.y / 16);
					float member = step(down, 0.12) + step(0.88, down) + step(bay, 0.06) + step(abs(bay - down), 0.07) + step(abs(bay - (1 - down)), 0.07);
					clip(member - 0.5);
				}
				// patchy
				// cells divide the tile so its seams don't show
				float patches = Hash(floor(p / 70)) * 0.25 + Hash(floor(p / 28)) * 0.1;
				// beams every 100 m, ribs between, panels with some sunk in, grain. only fairly near overhead
				float near = (1 - smoothstep(_DetailNear, _DetailFar, distance)) * _Detail;
				float beams = Lines(p, 100, 8);
				float ribs = Lines(p, 25, 3) * (1 - beams);
				float panel = Hash(floor(p / 25) + 7.3);
				float sunk = step(panel, 0.25) * (1 - beams) * (1 - ribs);
				float grain = Hash(floor(p / 5) + 3.1) - 0.5;
				float detail = beams * 0.5 + ribs * 0.2 + (panel - 0.5) * 0.2 - sunk * 0.45 + grain * 0.12;
				// softer patches, same average brightness (they come to 0.175)
				float3 colour = _Tint.rgb * max(0.85 + lerp(0.175, patches, _Detail) + detail * near, 0.3);
				// storeys between the slabs, dark with columns
				float columns = step(abs(frac((p.x + p.y) / 20) - 0.5) * 20, 2.5);
				colour = lerp(colour, _Tint.rgb * lerp(0.5, 0.95, columns), input.marks.y);
				colour = lerp(colour, _Rim.rgb, input.marks.x);
				colour = lerp(colour, _Tint.rgb * 0.5, input.marks.b);
				// buildings on the deck: concrete with floors of windows
				if (input.marks.a < 0.75)
				{
					float windows = step(frac(input.local.y / 7), 0.35) * step(frac(across / 6), 0.55);
					colour = lerp(_Facade.rgb, _Tint.rgb * 0.6, windows);
				}
				if (_DebugFlat > 0.5)
				{
					return half4(lerp(float3(0.6, 0.05, 0.05), float3(1, 0.6, 0.6), input.marks.x), 1);
				}
				return half4(lerp(_FogColor.rgb, colour, visibility * _Strength), 1);
			}
			ENDHLSL
		}
	}
}
