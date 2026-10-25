// inks the map picture: sepia washes, outlines, grain
Shader "Hidden/MapPaperStyle"
{
	Properties
	{
		_MainTex ("Map", 2D) = "white" {}
		_KeyColor ("Background key", Color) = (1, 0, 1, 1)
		_Paper ("Paper", Color) = (0.86, 0.8, 0.66, 1)
		_WashLight ("Light wash", Color) = (0.76, 0.69, 0.55, 1)
		_WashDark ("Dark wash", Color) = (0.55, 0.47, 0.36, 1)
		_WashTop ("Rooftop wash", Color) = (0.82, 0.76, 0.62, 1)
		_Ink ("Ink", Color) = (0.17, 0.13, 0.1, 1)
	}
	SubShader
	{
		Cull Off ZWrite Off ZTest Always
		Pass
		{
			CGPROGRAM
			#pragma vertex vert_img
			#pragma fragment frag
			#include "UnityCG.cginc"

			sampler2D _MainTex;
			float4 _MainTex_TexelSize;
			float4 _KeyColor;
			float4 _Paper;
			float4 _WashLight;
			float4 _WashDark;
			float4 _WashTop;
			float4 _Ink;

			float Hash(float2 p)
			{
				return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
			}

			// -1 where nothing was drawn
			float Tone(float2 uv)
			{
				float3 c = tex2D(_MainTex, uv).rgb;
				// cut-offs are in gamma
				#if !defined(UNITY_COLORSPACE_GAMMA)
				c = LinearToGammaSpace(c);
				#endif
				// key colour or the flat grey behind everything
				if (distance(c, _KeyColor.rgb) < 0.12 || distance(c, float3(0.8, 0.8, 0.8)) < 0.02)
				{
					return -1;
				}
				return dot(c, float3(0.3, 0.59, 0.11));
			}

			fixed4 frag(v2f_img i) : SV_Target
			{
				float2 px = _MainTex_TexelSize.xy;
				float centre = Tone(i.uv);
				// edges
				float edge = 0;
				float2 offsets[4] = { float2(px.x, 0), float2(-px.x, 0), float2(0, px.y), float2(0, -px.y) };
				for (int k = 0; k < 4; k++)
				{
					float other = Tone(i.uv + offsets[k]);
					bool background = (other < 0) != (centre < 0);
					edge = max(edge, background ? 1 : abs(other - centre) * 3.5);
				}
				float3 colour = _Paper.rgb;
				if (centre >= 0)
				{
					// ground ~0.36-0.4, darker walls, lighter roofs
					colour = centre < 0.3 ? _WashDark.rgb : centre < 0.47 ? _WashLight.rgb : _WashTop.rgb;
				}
				colour = lerp(colour, _Ink.rgb, saturate(edge * 1.4 - 0.35));
				// grain
				float grain = Hash(floor(i.uv * float2(480, 270))) * 0.06 + Hash(floor(i.uv * float2(60, 34))) * 0.04;
				colour *= 0.95 + grain;
				// folds and worn edge
				float fold = min(abs(i.uv.x - 0.333), abs(i.uv.x - 0.666));
				fold = min(fold, abs(i.uv.y - 0.5));
				colour *= 1 - 0.07 * saturate(1 - fold * 120);
				float2 fromEdge = min(i.uv, 1 - i.uv);
				colour *= lerp(0.72, 1, saturate(min(fromEdge.x, fromEdge.y) * 12));
				return fixed4(colour, 1);
			}
			ENDCG
		}
	}
}
