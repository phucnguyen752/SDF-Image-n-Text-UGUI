Shader "UI/SDF Text Effect"
{
    Properties
    {
        [PerRendererData] _MainTex ("Font Atlas", 2D) = "white" {}
        _FaceColor ("Font Face Color", Color) = (1,1,1,1)
        _FaceDilate ("Font Face Dilate", Float) = 0
        _WeightNormal ("Font Normal Weight", Float) = 0
        _WeightBold ("Font Bold Weight", Float) = 0.5
        _ScaleRatioA ("Font Scale Ratio", Float) = 1
        _GradientScale ("Font Gradient Scale", Float) = 1
        _VertexOffsetX ("Font Vertex Offset X", Float) = 0
        _VertexOffsetY ("Font Vertex Offset Y", Float) = 0
        _EffectColor ("Effect Color", Color) = (0,0,0,1)
        _EffectWidth ("Effect Width", Float) = 0
        _EffectSoftness ("Effect Softness", Float) = 0
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "SDF Text Effect"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 texcoord : TEXCOORD0;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float3 atlas : TEXCOORD0;
                float2 localPosition : TEXCOORD1;
                half4 mask : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize, _ClipRect;
            fixed4 _FaceColor, _EffectColor;
            float _FaceDilate, _WeightNormal, _WeightBold, _ScaleRatioA, _GradientScale;
            float _VertexOffsetX, _VertexOffsetY, _EffectWidth, _EffectSoftness;
            float _UIMaskSoftnessX, _UIMaskSoftnessY;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                v.vertex.xy += float2(_VertexOffsetX, _VertexOffsetY);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.localPosition = v.vertex.xy;
                o.color = v.color;
                o.color.a *= _FaceColor.a;

                // TMP in Unity 6 stores the bold sign in UV0.w, including fallback meshes.
                float bold = step(v.texcoord.w, 0.0);
                float weight = (lerp(_WeightNormal, _WeightBold, bold) * 0.25 + _FaceDilate) * _ScaleRatioA * 0.5;
                o.atlas = float3(v.texcoord.xy, 0.5 - weight);

                float2 pixelSize = o.vertex.w / abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
                o.mask = half4(v.vertex.xy * 2 - clampedRect.xy - clampedRect.zw,
                    0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize)));
                return o;
            }

            // TMP stores one atlas-pixel distance in 1 / (2 * GradientScale) alpha units.
            // Bound the rate using the atlas mapping: sampled SDF gradients can vanish at
            // medial axes or fluctuate at corners, but the glyph's distance scale cannot.
            float2 LocalDistanceRates(float alpha, float2 local, float2 uv)
            {
                float2 dx = ddx(local), dy = ddy(local);
                float det = dx.x * dy.y - dx.y * dy.x;
                float inverseDet = rcp(abs(det) > 1e-10 ? det : (det < 0 ? -1e-10 : 1e-10));
                float2 atlasDx = ddx(uv) * _MainTex_TexelSize.zw;
                float2 atlasDy = ddy(uv) * _MainTex_TexelSize.zw;
                float2 atlasU = float2(atlasDx.x * dy.y - atlasDy.x * dx.y,
                    atlasDy.x * dx.x - atlasDx.x * dy.x) * inverseDet;
                float2 atlasV = float2(atlasDx.y * dy.y - atlasDy.y * dx.y,
                    atlasDy.y * dx.x - atlasDx.y * dy.x) * inverseDet;

                // The singular values bound every transformed unit-distance normal, including
                // nonuniform scaling and italic shear. Uniform glyphs have one constant rate.
                float uu = dot(atlasU, atlasU), vv = dot(atlasV, atlasV), uvDot = dot(atlasU, atlasV);
                float maximum = sqrt(max(0.5 * (uu + vv + sqrt((uu - vv) * (uu - vv) + 4 * uvDot * uvDot)), 1e-12));
                float minimum = abs(atlasU.x * atlasV.y - atlasU.y * atlasV.x) / maximum;
                float distanceRange = 2 * max(_GradientScale, 1);
                float minimumRate = max(minimum / distanceRange, 1e-7);
                float maximumRate = max(maximum / distanceRange, minimumRate);
                float2 gradient = float2(ddx(alpha) * dy.y - ddy(alpha) * dx.y,
                    ddy(alpha) * dx.x - ddx(alpha) * dy.x) * inverseDet;
                float rate = minimumRate >= maximumRate * 0.999
                    ? (minimumRate + maximumRate) * 0.5
                    : clamp(length(gradient), minimumRate, maximumRate);
                // Cover both axes of the pixel's atlas footprint so diagonal edges stay smooth.
                // Keep this independent of noisy sampled-alpha gradients and authored softness.
                float aa = 0.5 * sqrt(dot(atlasDx, atlasDx) + dot(atlasDy, atlasDy)) / distanceRange;
                return float2(rate, aa);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float atlasAlpha = tex2D(_MainTex, i.atlas.xy).a;
                float2 distanceRates = LocalDistanceRates(atlasAlpha, i.localPosition, i.atlas.xy);
                float rate = distanceRates.x;
                const float atlasStep = 1.0 / 255.0;
                float threshold = clamp(i.atlas.z, atlasStep * 2, 1 - atlasStep);
                float aa = min(max(distanceRates.y, atlasStep), threshold - atlasStep);
                float width = _EffectWidth * rate;
                float softness = max(0, _EffectSoftness) * rate * 0.5;

                // An atlas has a finite distance range. Fit expansion and blur into it so
                // transparent atlas texels stay transparent even with very large settings.
                float available = max(0, threshold - aa - atlasStep);
                float fit = min(1, available / max(max(0, width) + softness, 1e-7));
                float edge = aa + softness * fit;
                // Negative spread contracts a shadow and does not consume exterior atlas range.
                float shift = min(0, width) + max(0, width) * fit;
                float coverage = smoothstep(-edge, edge, atlasAlpha - threshold + shift);
                float alpha = coverage * _EffectColor.a * i.color.a;
                fixed4 result = fixed4(_EffectColor.rgb * alpha, alpha);

                #ifdef UNITY_UI_CLIP_RECT
                half2 mask = saturate((_ClipRect.zw - _ClipRect.xy - abs(i.mask.xy)) * i.mask.zw);
                result *= mask.x * mask.y;
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(result.a - 0.001);
                #endif
                return result;
            }
            ENDCG
        }
    }
}
