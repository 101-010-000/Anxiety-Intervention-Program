// 角色/道具轮廓描边（Renderer Feature）—— 反向外壳，不跟随摄像机。
// 由菜单 Tools/干预项目/场景后期效果/① 自动挂到 URP_Renderer.asset 上。
//
// 【参数改哪儿】★ 只改 Assets/URP/后期/描边.mat 这一个地方 ★
//   Feature 在运行期【不会写】这个材质，而是直接把它当 overrideMaterial 用，
//   所以在 Inspector 里拖 _OutlineWidth / _OutlineColor / _FadeStart / _FadeEnd
//   是立刻生效的（Play 模式里也能实时拖）。
//   （以前的版本在运行期 new Material(...) 克隆一份、每帧再用 settings 覆盖参数，
//     所以调材质完全没反应 —— 已改掉。）
//   跑一次 ① 会把 ScenePostFx.cs 顶部的 OUTLINE_* 常量写回这个材质，重置成设计值。
//
// 【怎么做到“只描角色和道具”】
//   工具会自动建一个叫 Outline 的层，把每个 Loc_*/Content（道具）与 Loc_*/第X章角色（角色）
//   整棵子树放进去；这个 Feature 只画这一层。Shell*（地板/墙/天花板）不动。
//
// 【渲染时机】AfterRenderingOpaques —— 和普通不透明物体同阶段，
//   走正常深度测试（被墙挡住自动不描），后面还有雾/调色/景深，黑边会一起被处理。

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[DisallowMultipleRendererFeature("Scene Outline")]
public class OutlineFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Params
    {
        [Tooltip("总开关：关掉就完全不占开销")]
        public bool active = true;

        [Tooltip("要描边的层。工具会把角色和道具放到 Outline 层")]
        public LayerMask layers = 0;
    }

    public Params settings = new Params();

    [Tooltip("描边材质（Assets/URP/后期/描边.mat）。参数就改这个材质；留空会退化成 Shader.Find")]
    public Material material;

    OutlinePass m_Pass;
    Material m_Fallback;

    public override void Create()
    {
        m_Pass = null;
        CoreUtils.Destroy(m_Fallback);
        m_Fallback = null;

        var mat = material;
        if (mat == null)
        {
            var sh = Shader.Find("Hidden/SceneOutline");
            if (sh == null) return;
            m_Fallback = CoreUtils.CreateEngineMaterial(sh);
            mat = m_Fallback;
        }

        // ★ 直接把材质交出去，不在运行期改它 —— 所以 Inspector 里调参数是实时的。
        m_Pass = new OutlinePass(this, mat) { renderPassEvent = RenderPassEvent.AfterRenderingOpaques };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (!settings.active || m_Pass == null) return;
        if (settings.layers.value == 0) return;                 // 没设层 = 什么都不描

        var type = renderingData.cameraData.cameraType;
        if (type == CameraType.Preview || type == CameraType.Reflection) return;

        renderer.EnqueuePass(m_Pass);
    }

    protected override void Dispose(bool disposing)
    {
        m_Pass = null;
        CoreUtils.Destroy(m_Fallback);
        m_Fallback = null;
    }

    // ------------------------------------------------------------------ 渲染通道
    class OutlinePass : ScriptableRenderPass
    {
        static readonly ShaderTagId kForward     = new ShaderTagId("UniversalForward");
        static readonly ShaderTagId kForwardOnly = new ShaderTagId("UniversalForwardOnly");
        static readonly ShaderTagId kUnlit       = new ShaderTagId("SRPDefaultUnlit");
        static readonly ShaderTagId kLegacy      = new ShaderTagId("LightweightForward");

        readonly OutlineFeature m_Feature;
        readonly Material m_Material;

        public OutlinePass(OutlineFeature feature, Material mat)
        {
            m_Feature = feature;
            m_Material = mat;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (m_Material == null || m_Feature == null) return;
            var mask = m_Feature.settings.layers;
            if (mask.value == 0) return;

            // 用 overrideMaterial 把这一层的物体用描边 shader 再画一遍。
            // 用它们自己的几何体（含蒙皮后的），所以角色动起来描边也跟着动。
            var sorting = new SortingSettings(renderingData.cameraData.camera)
            {
                criteria = SortingCriteria.CommonOpaque
            };
            var draw = new DrawingSettings(kForward, sorting)
            {
                enableDynamicBatching = false,
                enableInstancing = false,
                overrideMaterial = m_Material,
                overrideMaterialPassIndex = 0,
            };
            draw.SetShaderPassName(1, kForwardOnly);
            draw.SetShaderPassName(2, kUnlit);
            draw.SetShaderPassName(3, kLegacy);

            var filter = new FilteringSettings(RenderQueueRange.all, mask);

            var cmd = CommandBufferPool.Get("Scene Outline");
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            context.DrawRenderers(renderingData.cullResults, ref draw, ref filter);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}
