// 角色/道具轮廓描边 —— 反向外壳（inverted hull）。
// 由 Assets/Scripts/Visual/OutlineFeature.cs 用 overrideMaterial 把这些物体再画一遍，
// 不是屏幕空间后处理，所以描边是「长在物体表面上」的，跟视角无关。
//
// 原理：
//   顶点沿法线往外推 _OutlineWidth 像素对应的世界距离 → 得到一圈"外壳"，
//   然后 Cull Front 只画背面。物体的正面已经在不透明阶段画过了，
//   壳上被物体挡住的像素 ZTest 会失败、只有露在轮廓外的那圈留下 → 就是描边。
//   因为走的是正常深度测试，被墙挡住的部分自然不会描（不需要额外判遮挡）。
//
// 线宽用屏幕像素算：屏幕 1 像素 ≈ 2 * 距离 * tan(fov/2) / 屏幕高 个世界单位，
// 所以离得远时壳自动变粗，看起来粗细稳定。
//
// 【为什么远处是"变淡"而不是"变细"】
//   试过让线宽乘淡出系数 —— 中远距离线宽掉到 1 像素以下就碎成一串虚点，看着全是噪点。
//   所以线宽恒定，只把 alpha 淡下去（Blend SrcAlpha OneMinusSrcAlpha），线才是连续的。
//
// 【Offset -1,-1】把外壳往相机方向偏一点点，避免它和物体自己的表面 z-fighting 起噪点。

Shader "Hidden/SceneOutline"
{
    Properties
    {
        _OutlineColor ("描边颜色", Color) = (0, 0, 0, 1)
        _OutlineWidth ("线宽（像素）", Range(0.5, 8)) = 1.8
        _FadeStart    ("线条淡出起点（米）", Float) = 8
        _FadeEnd      ("线条淡出终点（米）", Float) = 24
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 100

        Pass
        {
            Name "Outline"
            Cull Front          // ★ 只画外壳的背面，正面留给原物体
            ZWrite On
            ZTest LEqual
            Offset -1, -1       // 轻微深度偏移，压掉和物体表面的 z-fighting 噪点
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            half4 _OutlineColor;
            float _OutlineWidth;
            float _FadeStart;
            float _FadeEnd;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half   fade       : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);

                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 nrmWS = TransformObjectToWorldNormal(input.normalOS);

                // 相机方向的距离（米）
                float dist = max(-TransformWorldToView(posWS).z, 0.01);

                // 一像素对应多少世界单位
                float cotHalfFov    = unity_CameraProjection[1][1];      // = 1 / tan(fov/2)
                float worldPerPixel = 2.0 * dist / max(cotHalfFov * _ScreenParams.y, 1e-3);

                // 线宽恒定（不乘淡出），否则中远距离会碎成虚点
                posWS += nrmWS * (_OutlineWidth * worldPerPixel);

                // 远处淡出交给 alpha：跟雾/景深的朦胧感一致，又不会把线拉断
                output.fade = (half)saturate((_FadeEnd - dist) / max(_FadeEnd - _FadeStart, 1e-3));

                output.positionCS = TransformWorldToHClip(posWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return half4(_OutlineColor.rgb, _OutlineColor.a * input.fade);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
