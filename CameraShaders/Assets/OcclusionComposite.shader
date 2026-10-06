Shader "Hidden/Apocaplayer/OcclusionComposite"
{
    Properties { _MainTex ("Original", 2D) = "white" {} _CleanTex ("Clear view", 2D) = "white" {} _CleanDepth ("Clear depth", 2D) = "white" {} _FrontTex ("Protected front view", 2D) = "white" {} _FrontDepth ("Protected front depth", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex, _CleanTex, _CleanDepth, _FrontTex, _FrontDepth;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            float4 _MainTex_TexelSize, _Ends;
            float _Radius, _Aspect, _Strength, _FocusDepth;
            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
                float2 depthUV = uv;
                #if UNITY_UV_STARTS_AT_TOP
                if (_MainTex_TexelSize.y < 0) depthUV.y = 1 - depthUV.y;
                #endif
                float mainDepth = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, depthUV));
                float clearDepth = tex2D(_CleanDepth, uv).r;
                float frontDepth = tex2D(_FrontDepth, uv).r;
                float useFront = step(frontDepth + .002, clearDepth);
                clearDepth = min(clearDepth, frontDepth);
                float2 scale = float2(_Aspect, 1);
                float2 a = _Ends.xy * scale, b = _Ends.zw * scale, p = uv * scale;
                float2 ab = b - a;
                float t = saturate(dot(p - a, ab) / max(dot(ab, ab), 0.00001));
                float distance = length(p - a - ab * t);
                float window = 1 - smoothstep(_Radius * .70, _Radius, distance);
                // The cutaway ends strictly before the character. The protected pass
                // also retains front walls belonging to the same mesh as a rear blocker.
                float blocker = step(mainDepth + .025, clearDepth) * step(mainDepth, _FocusDepth);
                fixed4 original = tex2D(_MainTex, uv);
                fixed4 clearView = lerp(tex2D(_CleanTex, uv), tex2D(_FrontTex, uv), useFront);
                return lerp(original, clearView, saturate(window * blocker * _Strength));
            }
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment fragDepth
            #pragma target 3.0
            #include "UnityCG.cginc"
            UNITY_DECLARE_DEPTH_TEXTURE(_MainTex);
            float4 fragDepth(v2f_img i) : SV_Target
            { return LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_MainTex, i.uv)); }
            ENDCG
        }
    }
}
