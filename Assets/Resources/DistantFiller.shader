// ending ride filler city. lit evenly like the far towers, rows of windows, the scene's fog so it sits in the haze
Shader "Hidden/DistantFiller"
{
	Properties
	{
		_SunShare ("Sun on it, the same all round", Range(0, 1)) = 0.35
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
			#pragma multi_compile_fog
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

			CBUFFER_START(UnityPerMaterial)
			float _SunShare;
			CBUFFER_END

			struct Attributes
			{
				float4 position : POSITION;
				float3 normal : NORMAL;
				float4 color : COLOR;
				float2 uv : TEXCOORD0;
			};

			struct Varyings
			{
				float4 position : SV_POSITION;
				float3 normal : TEXCOORD0;
				float4 color : TEXCOORD1;
				// across the face and height above the deck, y under zero has no windows
				float2 facade : TEXCOORD2;
				float fog : TEXCOORD3;
			};

			Varyings vert(Attributes input)
			{
				Varyings output;
				float3 world = TransformObjectToWorld(input.position.xyz);
				output.position = TransformWorldToHClip(world);
				output.normal = TransformObjectToWorldNormal(input.normal);
				output.color = input.color;
				output.facade = input.uv;
				output.fog = ComputeFogFactor(output.position.z);
				return output;
			}

			half4 frag(Varyings input) : SV_Target
			{
				float windows = input.facade.y >= 0 ? step(frac(input.facade.y / 3.5), 0.35) * step(frac(input.facade.x / 4), 0.55) : 0;
				float3 albedo = input.color.rgb * lerp(1, 0.55, windows);
				Light sun = GetMainLight();
				float3 sky = (SampleSH(half3(0, 1, 0)) + SampleSH(half3(0, -1, 0))) * 0.5;
				// roofs a touch lighter than walls, some form without the sun's direction
				float form = lerp(0.85, 1, saturate(normalize(input.normal).y));
				float3 colour = albedo * (sun.color * _SunShare + sky) * form;
				return half4(MixFog(colour, input.fog), 1);
			}
			ENDHLSL
		}
	}
}
