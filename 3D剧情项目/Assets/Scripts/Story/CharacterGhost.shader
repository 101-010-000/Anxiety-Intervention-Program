// NPC 入场虚影材质（第2章陆宣雨"从虚变实"演出，2026-09-28 v2 全息透明版）：
// 入场期间临时替换角色所有 Renderer 的材质为本 shader 的实例，由 NpcEntrance 驱动 _Alpha 0→1；
// 到位后换回原材质"落定"。★ 只做演出虚影，不还原真实配色（真实配色属于 CharacterLit，
//   ShaderGraph 9 角色共享，不能动）。
//
// v2（用户反馈"黑白感，要透明感"）：v1 是平色+固定 alpha 的剪影，看起来像灰白纸片。
//   v2 改为全息虚影：菲涅尔（视角与表面法线夹角）驱动——
//     正对镜头的"身体中心"近乎全透（能直接看穿看到背景），
//     侧向轮廓边缘更实更亮 → 一眼就是"透明的人影"而不是变色的人。
Shader "Custom/CharacterGhost"
{
    Properties
    {
        _Color ("虚影颜色", Color) = (0.7, 0.87, 1.0, 1)
        _CenterAlpha ("正对时透明度", Range(0, 1)) = 0.14   // 身体中心：几乎全透（看穿见背景）
        _RimAlpha ("边缘不透明度", Range(0, 1)) = 0.75      // 轮廓边缘：可辨
        _RimPower ("边缘聚拢程度", Range(0.5, 8)) = 3.0     // 边缘收窄——大面积保持可看穿
        _Alpha ("显形进度", Range(0, 1)) = 0                // NpcEntrance 全局驱动
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
            Name "GhostHologram"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _CenterAlpha;
                half _RimAlpha;
                half _RimPower;
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
                float3 positionWS : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionWS = p.positionWS;
                OUT.positionCS = p.positionCS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half3 N = normalize(IN.normalWS);
                half3 V = normalize(_WorldSpaceCameraPos.xyz - IN.positionWS);
                half fres = pow(1.0h - saturate(dot(N, V)), _RimPower);

                half alpha = lerp(_CenterAlpha, _RimAlpha, fres) * saturate(_Alpha);
                half3 col = _Color.rgb * (0.85h + 0.45h * fres);   // 边缘微亮，中心更"虚"
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
