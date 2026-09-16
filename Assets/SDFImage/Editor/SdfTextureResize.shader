Shader "Hidden/SDFUI/TextureResize"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        Texture2D _MainTex;
        SamplerState sampler_PointClamp;
        float4 _MainTex_TexelSize;
        float4 _SourceRect;
        float4 _Filter;

        float4 SamplePixel(float2 pixel)
        {
            pixel = clamp(pixel, _SourceRect.xy + 0.5, _SourceRect.xy + _SourceRect.zw - 0.5);
            return _MainTex.SampleLevel(sampler_PointClamp, pixel * abs(_MainTex_TexelSize.xy), 0);
        }

        float4 Bilinear(v2f_img input) : SV_Target
        {
            float2 pixel = _SourceRect.xy + input.uv * _SourceRect.zw - 0.5;
            float2 basePixel = floor(pixel) + 0.5;
            float2 blend = frac(pixel);
            return lerp(lerp(SamplePixel(basePixel), SamplePixel(basePixel + float2(1, 0)), blend.x),
                lerp(SamplePixel(basePixel + float2(0, 1)), SamplePixel(basePixel + 1), blend.x), blend.y);
        }

        float MitchellWeight(float x)
        {
            x = abs(x);
            if (x < 1) return (7 * x * x * x - 12 * x * x + 16.0 / 3.0) / 6;
            if (x < 2) return (-7.0 / 3.0 * x * x * x + 12 * x * x - 20 * x + 32.0 / 3.0) / 6;
            return 0;
        }

        float4 Mitchell(v2f_img input) : SV_Target
        {
            float2 center = _SourceRect.xy + input.uv * _SourceRect.zw;
            float position = dot(center, _Filter.xy);
            float radius = 2 * _Filter.z;
            float4 result = 0;
            float total = 0;
            int last = (int)ceil(position + radius - 0.5);
            [loop] for (int pixel = (int)floor(position - radius - 0.5); pixel <= last; pixel++)
            {
                float weight = MitchellWeight((pixel + 0.5 - position) / _Filter.z);
                result += SamplePixel(center + _Filter.xy * (pixel + 0.5 - position)) * weight;
                total += weight;
            }
            return saturate(result / total);
        }
        ENDCG
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment Bilinear
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment Mitchell
            ENDCG
        }
    }
}
