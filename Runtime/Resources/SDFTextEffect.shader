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
        _ScaleX ("Font Scale X", Float) = 1
        _ScaleY ("Font Scale Y", Float) = 1
        _Sharpness ("Font Sharpness", Float) = 0
        _PerspectiveFilter ("Font Perspective Filter", Float) = 0.875
        _VertexOffsetX ("Font Vertex Offset X", Float) = 0
        _VertexOffsetY ("Font Vertex Offset Y", Float) = 0
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
                float3 normal : NORMAL;
                float4 texcoord : TEXCOORD0;
                float4 glyphBounds : TEXCOORD1;
                float4 style : TEXCOORD2;
                fixed4 effectColor : TEXCOORD3;
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
                float4 glyphBounds : TEXCOORD3;
                float faceScale : TEXCOORD4;
                float4 style : TEXCOORD5;
                float2 effectOffset : TEXCOORD6;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize, _ClipRect;
            fixed4 _FaceColor;
            float _FaceDilate, _WeightNormal, _WeightBold, _ScaleRatioA, _GradientScale;
            float _VertexOffsetX, _VertexOffsetY;
            float _UIMaskSoftnessX, _UIMaskSoftnessY;
            float _ScaleX, _ScaleY, _Sharpness, _PerspectiveFilter;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                v.vertex.xy += float2(_VertexOffsetX, _VertexOffsetY);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.localPosition = v.vertex.xy;
                o.color = v.effectColor;
                o.color.a *= v.color.a * _FaceColor.a;
                o.glyphBounds = v.glyphBounds;
                o.style = float4(v.style.xy, v.texcoord.z, 0);
                o.effectOffset = v.style.zw;

                // TMP in Unity 6 stores the bold sign in UV0.w, including fallback meshes.
                float bold = step(v.texcoord.w, 0.0);
                float weight = (lerp(_WeightNormal, _WeightBold, bold) * 0.25 + _FaceDilate) * _ScaleRatioA * 0.5;
                o.atlas = float3(v.texcoord.xy, 0.5 - weight);

                float2 pixelSize = o.vertex.w / abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                // Match TMP's native face antialiasing so the mask cannot extend beyond
                // its visible edge on rotated, italic or nonuniformly scaled labels.
                float2 facePixelSize = pixelSize / float2(_ScaleX, _ScaleY);
                o.faceScale = rsqrt(dot(facePixelSize, facePixelSize)) * abs(v.texcoord.w) * _GradientScale * (_Sharpness + 1);
                if (UNITY_MATRIX_P[3][3] == 0)
                    o.faceScale = lerp(abs(o.faceScale) * (1 - _PerspectiveFilter), o.faceScale,
                        abs(dot(UnityObjectToWorldNormal(v.normal), normalize(WorldSpaceViewDir(v.vertex)))));
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

            float2 OffsetAtlas(float2 uv, float2 local, float2 offset)
            {
                float2 dx = ddx(local), dy = ddy(local);
                float det = dx.x * dy.y - dx.y * dy.x;
                float inverseDet = rcp(abs(det) > 1e-10 ? det : (det < 0 ? -1e-10 : 1e-10));
                float2 du = (ddx(uv) * dy.y - ddy(uv) * dx.y) * inverseDet;
                float2 dv = (ddy(uv) * dx.x - ddx(uv) * dy.x) * inverseDet;
                return uv - du * offset.x - dv * offset.y;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float originalAlpha = tex2D(_MainTex, i.atlas.xy).a;
                float atlasAlpha = originalAlpha;
                float domain = 1;
                float position = i.style.z;
                bool innerUnderlay = position > 3.5;
                bool inner = (position > 0.5 && position < 1.5) || innerUnderlay;
                if (inner && any(i.effectOffset != 0))
                {
                    float2 shiftedUv = OffsetAtlas(i.atlas.xy, i.localPosition, i.effectOffset);
                    atlasAlpha = tex2D(_MainTex, shiftedUv).a;
                    float2 inside = step(i.glyphBounds.xy, shiftedUv) * step(shiftedUv, i.glyphBounds.zw);
                    domain = inside.x * inside.y;
                }
                float2 distanceRates = LocalDistanceRates(atlasAlpha, i.localPosition, i.atlas.xy);
                float rate = distanceRates.x;
                const float atlasStep = 1.0 / 255.0;
                float threshold = clamp(i.atlas.z, atlasStep * 2, 1 - atlasStep);
                float aa = min(max(distanceRates.y, atlasStep), threshold - atlasStep);
                float width = i.style.x * rate;
                float softness = max(0, i.style.y) * rate * 0.5;

                // An atlas has a finite distance range. Fit expansion and blur into it so
                // transparent atlas texels stay transparent even with very large settings.
                float available = max(0, threshold - aa - atlasStep);
                float distance = atlasAlpha - threshold;
                float coverage;
                if (position > 2.5)
                {
                    float fit = min(1, available / max(max(0, width) + softness, 1e-7));
                    float edge = aa + softness * fit;
                    // Negative spread contracts a shadow and does not consume exterior atlas range.
                    float shift = min(0, width) + max(0, width) * fit;
                    coverage = smoothstep(-edge, edge, distance + shift);
                    // An inner shadow is the part of the original face not covered by the
                    // shifted silhouette. Samples outside this glyph count as empty, not a neighbor.
                    if (innerUnderlay)
                        coverage = (1 - coverage * domain) * saturate((originalAlpha - i.atlas.z) * i.faceScale + 0.5);
                }
                else
                {
                    width = max(0, width);
                    float outerWidth = position < 0.5 ? width : (position > 1.5 ? width * 0.5 : 0);
                    float innerWidth = position > 1.5 ? width * 0.5 : (position > 0.5 ? width : 0);
                    float fit = min(1, available / max(outerWidth + softness, 1e-7));
                    float edge = aa + softness * fit;
                    float expanded = smoothstep(-edge, edge, distance + outerWidth * fit);
                    float contracted = smoothstep(-edge, edge, distance - innerWidth);
                    coverage = saturate(expanded - contracted);
                    // Mask against the unshifted glyph, including counters/holes. The border
                    // may move with Offset, but its visible result stays inside the original text.
                    if (inner)
                        coverage = min(coverage, saturate((originalAlpha - i.atlas.z) * i.faceScale + 0.5)) * domain;
                }
                float alpha = coverage * i.color.a;
                fixed4 result = fixed4(i.color.rgb * alpha, alpha);

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
