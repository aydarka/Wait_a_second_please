// The game's lettering: TextMeshPro's mobile distance-field shader (TMP_SDF-Mobile, Unity Companion License)
// reworked in two ways.
// - Ink first, faces second. The first pass draws every letter's black shadow and outline, the second every
//   white face, like a comic letterer inks before filling. In one pass a letter's thick outline would paint
//   over the face of the letter before it.
// - Captions never fade, they leave by hatching like the street fog. The vertex alpha is not opacity but the
//   share of each diagonal stripe still standing: 1 is the solid letter, 0.6 thick lines, 0 gone. A stripe
//   period is _HatchEm of the letter's own size; ink and face are hatched together. TMP's <alpha=#00> tag
//   therefore hides characters, which typing uses to keep the layout still.
// The outline sits fully outside the face (Face Dilate equals Outline Thickness) and the shadow is the outlined
// letter moved down and to the right; Tools > Lettering > Build Font Assets sets them in em.
Shader "WaitASecond/Lettering" {

Properties {
	_FaceColor          ("Face Color", Color) = (1,1,1,1)
	_FaceDilate			("Face Dilate", Range(-1,1)) = 0

	_OutlineColor	    ("Outline Color", Color) = (0,0,0,1)
	_OutlineWidth		("Outline Thickness", Range(0,1)) = 0
	_OutlineSoftness	("Outline Softness", Range(0,1)) = 0

	_UnderlayColor	    ("Border Color", Color) = (0,0,0,.5)
	_UnderlayOffsetX 	("Border OffsetX", Range(-1,1)) = 0
	_UnderlayOffsetY 	("Border OffsetY", Range(-1,1)) = 0
	_UnderlayDilate		("Border Dilate", Range(-1,1)) = 0
	_UnderlaySoftness 	("Border Softness", Range(0,1)) = 0

	_HatchEm			("Hatch Period (em)", Range(0.02, 1)) = 0.16
	_HatchSamplingSize	("Atlas Sampling Point Size", float) = 72

	_WeightNormal		("Weight Normal", float) = 0
	_WeightBold			("Weight Bold", float) = .5

	_ShaderFlags		("Flags", float) = 0
	_ScaleRatioA		("Scale RatioA", float) = 1
	_ScaleRatioB		("Scale RatioB", float) = 1
	_ScaleRatioC		("Scale RatioC", float) = 1

	_MainTex			("Font Atlas", 2D) = "white" {}
	_TextureWidth		("Texture Width", float) = 512
	_TextureHeight		("Texture Height", float) = 512
	_GradientScale		("Gradient Scale", float) = 5
	_ScaleX				("Scale X", float) = 1
	_ScaleY				("Scale Y", float) = 1
	_PerspectiveFilter	("Perspective Correction", Range(0, 1)) = 0.875
	_Sharpness			("Sharpness", Range(-1,1)) = 0

	_VertexOffsetX		("Vertex OffsetX", float) = 0
	_VertexOffsetY		("Vertex OffsetY", float) = 0

	_ClipRect			("Clip Rect", vector) = (-32767, -32767, 32767, 32767)
	_MaskSoftnessX		("Mask SoftnessX", float) = 0
	_MaskSoftnessY		("Mask SoftnessY", float) = 0

	_StencilComp		("Stencil Comparison", Float) = 8
	_Stencil			("Stencil ID", Float) = 0
	_StencilOp			("Stencil Operation", Float) = 0
	_StencilWriteMask	("Stencil Write Mask", Float) = 255
	_StencilReadMask	("Stencil Read Mask", Float) = 255

	_CullMode			("Cull Mode", Float) = 0
	_ColorMask			("Color Mask", Float) = 15
}

SubShader {
	Tags
	{
		"Queue"="Transparent"
		"IgnoreProjector"="True"
		"RenderType"="Transparent"
	}

	Stencil
	{
		Ref [_Stencil]
		Comp [_StencilComp]
		Pass [_StencilOp]
		ReadMask [_StencilReadMask]
		WriteMask [_StencilWriteMask]
	}

	Cull [_CullMode]
	ZWrite Off
	Lighting Off
	Fog { Mode Off }
	ZTest [unity_GUIZTestMode]
	Blend One OneMinusSrcAlpha
	ColorMask [_ColorMask]

	CGINCLUDE
	#include "UnityCG.cginc"
	#include "UnityUI.cginc"
	#include "Assets/TextMesh Pro/Shaders/TMPro_Properties.cginc"

	float _HatchEm;
	float _HatchSamplingSize;
	float _UIMaskSoftnessX;
	float _UIMaskSoftnessY;
	int _UIVertexColorAlwaysGammaSpace;

	struct vertex_t {
		UNITY_VERTEX_INPUT_INSTANCE_ID
		float4	vertex			: POSITION;
		float3	normal			: NORMAL;
		fixed4	color			: COLOR;
		float4	texcoord0		: TEXCOORD0;
		float2	texcoord1		: TEXCOORD1;
	};

	struct pixel_t {
		UNITY_VERTEX_INPUT_INSTANCE_ID
		UNITY_VERTEX_OUTPUT_STEREO
		float4	vertex			: SV_POSITION;
		fixed4	faceColor		: COLOR;
		fixed4	outlineColor	: COLOR1;
		float4	texcoord0		: TEXCOORD0;			// Texture UV, Mask UV
		half4	param			: TEXCOORD1;			// Scale(x), Outline outer edge(y), Face edge(z)
		half4	mask			: TEXCOORD2;			// Position in clip space(xy), Softness(zw)
		half	hatch			: TEXCOORD3;			// Share of each stripe still standing
		float4	texcoord1		: TEXCOORD4;			// Shadow UV
		half2	underlayParam	: TEXCOORD5;			// Shadow scale(x), bias(y)
	};

	pixel_t VertShader(vertex_t input)
	{
		pixel_t output;

		UNITY_INITIALIZE_OUTPUT(pixel_t, output);
		UNITY_SETUP_INSTANCE_ID(input);
		UNITY_TRANSFER_INSTANCE_ID(input, output);
		UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

		float bold = step(input.texcoord0.w, 0);

		float4 vert = input.vertex;
		vert.x += _VertexOffsetX;
		vert.y += _VertexOffsetY;
		float4 vPosition = UnityObjectToClipPos(vert);

		float2 pixelSize = vPosition.w;
		pixelSize /= float2(_ScaleX, _ScaleY) * abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));

		float scale = rsqrt(dot(pixelSize, pixelSize));
		scale *= abs(input.texcoord0.w) * _GradientScale * (_Sharpness + 1);
		if(UNITY_MATRIX_P[3][3] == 0) scale = lerp(abs(scale) * (1 - _PerspectiveFilter), scale, abs(dot(UnityObjectToWorldNormal(input.normal.xyz), normalize(WorldSpaceViewDir(vert)))));

		float weight = lerp(_WeightNormal, _WeightBold, bold) / 4.0;
		weight = (weight + _FaceDilate) * _ScaleRatioA * 0.5;

		float layerScale = scale;

		scale /= 1 + (_OutlineSoftness * _ScaleRatioA * scale);
		float bias = (0.5 - weight) * scale - 0.5;
		float outline = _OutlineWidth * _ScaleRatioA * 0.5 * scale;

		if (_UIVertexColorAlwaysGammaSpace && !IsGammaSpace())
		{
			input.color.rgb = UIGammaToLinear(input.color.rgb);
		}

		// The alpha hatches instead of fading, so every layer is drawn opaque.
		fixed4 faceColor = fixed4(input.color.rgb, 1) * _FaceColor;
		faceColor.rgb *= faceColor.a;

		fixed4 outlineColor = _OutlineColor;
		outlineColor.rgb *= outlineColor.a;

		layerScale /= 1 + ((_UnderlaySoftness * _ScaleRatioC) * layerScale);
		float layerBias = (.5 - weight) * layerScale - .5 - ((_UnderlayDilate * _ScaleRatioC) * .5 * layerScale);
		float2 layerOffset = float2(
			-(_UnderlayOffsetX * _ScaleRatioC) * _GradientScale / _TextureWidth,
			-(_UnderlayOffsetY * _ScaleRatioC) * _GradientScale / _TextureHeight);

		// Generate UV for the Masking Texture
		float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
		float2 maskUV = (vert.xy - clampedRect.xy) / (clampedRect.zw - clampedRect.xy);

		output.vertex = vPosition;
		output.faceColor = faceColor;
		output.outlineColor = outlineColor;
		output.texcoord0 = float4(input.texcoord0.x, input.texcoord0.y, maskUV.x, maskUV.y);
		output.param = half4(scale, bias - outline, bias + outline, bias);
		output.hatch = input.color.a;

		const half2 maskSoftness = half2(max(_UIMaskSoftnessX, _MaskSoftnessX), max(_UIMaskSoftnessY, _MaskSoftnessY));
		output.mask = half4(vert.xy * 2 - clampedRect.xy - clampedRect.zw, 0.25 / (0.25 * maskSoftness + pixelSize.xy));
		output.texcoord1 = float4(input.texcoord0.xy + layerOffset, 0, 0);
		output.underlayParam = half2(layerScale, layerBias);

		return output;
	}

	// Rect clipping and the hatch, shared by both passes. Derivatives stay outside any branch; atlas texels per
	// screen pixel give the letter's size on screen, so the stripes keep to the letter whatever its scale.
	half4 Finish(pixel_t input, half4 c)
	{
		#if UNITY_UI_CLIP_RECT
		half2 m = saturate((_ClipRect.zw - _ClipRect.xy - abs(input.mask.xy)) * input.mask.zw);
		c *= m.x * m.y;
		#endif

		float texelsPerPixel = length(fwidth(input.texcoord0.xy * float2(_TextureWidth, _TextureHeight))) * 0.7071;
		float period = max(2.0, _HatchEm * _HatchSamplingSize / max(texelsPerPixel, 1e-4));
		float stripe = frac((input.vertex.x + input.vertex.y) / period);
		c *= input.hatch >= 0.999 ? 1.0 : step(stripe, input.hatch - 0.001);

		#if UNITY_UI_ALPHACLIP
		clip(c.a - 0.001);
		#endif
		return c;
	}

	// Ink: the outline over the whole letter, the shadow under it.
	fixed4 InkShader(pixel_t input) : SV_Target
	{
		UNITY_SETUP_INSTANCE_ID(input);
		half d = tex2D(_MainTex, input.texcoord0.xy).a * input.param.x;
		half4 c = input.outlineColor * saturate(d - input.param.y);
		d = tex2D(_MainTex, input.texcoord1.xy).a * input.underlayParam.x;
		c += half4(_UnderlayColor.rgb * _UnderlayColor.a, _UnderlayColor.a) * saturate(d - input.underlayParam.y) * (1 - c.a);
		return Finish(input, c);
	}

	// Face: the white letter inside the outline.
	fixed4 FaceShader(pixel_t input) : SV_Target
	{
		UNITY_SETUP_INSTANCE_ID(input);
		half d = tex2D(_MainTex, input.texcoord0.xy).a * input.param.x;
		half4 c = input.faceColor * saturate(d - input.param.z);
		return Finish(input, c);
	}
	ENDCG

	Pass {
		Name "Ink"
		CGPROGRAM
		#pragma vertex VertShader
		#pragma fragment InkShader
		#pragma multi_compile __ UNITY_UI_CLIP_RECT
		#pragma multi_compile __ UNITY_UI_ALPHACLIP
		// Unused in code, declared so TMP sees them on the material: OUTLINE_ON and UNDERLAY_ON size the glyph
		// quads for the ink, RATIOS_OFF keeps the scale ratios at 1 because the ink is set in em.
		#pragma shader_feature __ OUTLINE_ON
		#pragma shader_feature __ UNDERLAY_ON
		#pragma shader_feature __ RATIOS_OFF
		ENDCG
	}

	Pass {
		Name "Face"
		CGPROGRAM
		#pragma vertex VertShader
		#pragma fragment FaceShader
		#pragma multi_compile __ UNITY_UI_CLIP_RECT
		#pragma multi_compile __ UNITY_UI_ALPHACLIP
		#pragma shader_feature __ OUTLINE_ON
		#pragma shader_feature __ UNDERLAY_ON
		#pragma shader_feature __ RATIOS_OFF
		ENDCG
	}
}

CustomEditor "TMPro.EditorUtilities.TMP_SDFShaderGUI"
}
