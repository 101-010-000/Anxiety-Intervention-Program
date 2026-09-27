// NPC 入场虚影材质（第2章陆宣雨"从虚变实"演出，2026-09-27）：
// 入场期间临时替换角色所有 Renderer 的材质为本 shader 的实例（统一淡青白半透明），
// 由 NpcEntrance 驱动 _Alpha 从 0 → 0.75 渐显；到位后换回原材质"落定"。
// ★ 只做入场演出的"虚影"，不还原角色真实配色 —— 真实配色属于 CharacterLit（ShaderGraph，
//   9 角色共享，AGENTS 第三节历史事故区，不能动）。简单法线明暗给一点体积感。
// URP unlit transparent；_Alpha 独立属性方便全局驱动。
Shader "Custom/CharacterGhost"
{
    Properties
    {
        _Color ("虚影颜色", Color) = (0.75, 0.85, 0.92, 0.75)
        _Alpha ("显形度", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "GhostUnlit"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Alpha;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half3  normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half  shade       : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHclip(IN.positionOS.xyz);
                half3 n = normalize(IN.normalOS);
                OUT.shade = 0.72 + 0.28 * saturate(n.y * 0.5h + 0.5h);   // 顶面亮底面暗，一点体积感
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return half4(_Color.rgb * IN.shade, _Color.a * saturate(_Alpha));
            }
            ENDHLSL
        }
    }
}
