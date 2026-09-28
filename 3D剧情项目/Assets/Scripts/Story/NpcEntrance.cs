// NPC 剧情入场驱动（第2章陆宣雨"门口半透明渐显走近"，2026-09-28 v3 双层交叉淡化）：
//   由 StoryRunner 的 enter 步骤在目标角色根节点上懒挂本组件，负责一段入场演出。
//
// ★ v3 核心思路（用户定稿："虚化=透明，由半透明慢慢变到不透明，全程连续"）：
//   真身本体不透明（CharacterLit 冻结不能改），无法渐显 → 用【双层】实现连续凝实：
//     · 幽灵层 = 复制她的 SkinnedMeshRenderer（共享骨骼，动作同步）套全息透明材质；
//     · 真身本体先隐藏，演出中段在幽灵层最浓时接通（同轮廓 → 视觉零跳变）；
//     · 幽灵层最后 ~1s 平滑淡出 → 真人从虚影里"凝实"出来。
//   整体不透明度单调递增 0.2→0.65→1.0，无任何一帧突变。
//
// 曲线（用户反馈"快靠近时落差大"的对症设计）：
//   位移：匀速（不做 SmoothStep，避免终点速度骤停）；
//   幽灵浓度：前 60% 路程缓出升到 GHOST_PEAK 后【保持】——靠近阶段视觉稳定不剧变；
//   凝实：走过 REVEAL_START 后启动，持续 REVEAL_DUR 秒（覆盖到达前后），三次曲线平滑。
//
// 穿帮防护沿用：入场期摘描边（Layer 暂离 Outline）、禁碰撞；落定恢复。
// Skip() 供自检快进（瞬间终态，同一套收尾）。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NpcEntrance : MonoBehaviour
{
    // ---------------- 可调参数（观感微调只动这里） ----------------
    const float SPEED = 1.35f;          // 入场步速（偏缓，演出感）
    // ★ 透明度区间（用户 2026-09-28 定稿："从 75% 不透明度变到 100%"）：
    //   出现即 ~75% 可见（微透，能隐约看穿），凝实后 100%。
    //   幽灵层 _Alpha 全程恒定（GHOST_ALPHA），75→100 的变化全部由凝实段完成：
    //   真身接通 + 幽灵淡出的合成观感 = 实心度连续上升，无来回收缩。
    const float GHOST_ALPHA = 0.88f;    // 幽灵层浓度（配合 shader 中心0.75/边缘1.0 → 全身≈75%~88%）
    const float REVEAL_START = 0.62f;   // 走到 62% 开始凝实（真身接通 + 幽灵开始淡出）
    const float REVEAL_DUR = 1.05f;     // 凝实总时长（覆盖到达前后，越大越"慢慢变实"）
    // --------------------------------------------------------------

    static readonly int AlphaId = Shader.PropertyToID("_Alpha");

    Renderer[] _realRenderers;          // 真身（演出中隐藏，凝实启动时接通）
    readonly List<GameObject> _ghostClones = new List<GameObject>();
    Material _ghost;                    // 幽灵材质实例（所有克隆共享，全局改 _Alpha）
    int _origLayer;
    Collider _collider;
    bool _hadCollider;
    Animator _anim;
    bool _prepared, _restored, _realShown;

    public bool Done { get; private set; }
    /// 自检/调试：置 true 后协程逐帧直达终态（等价 Skip）
    public bool fast;

    // ------------------------------------------------------------------ 准备
    public void Prepare()
    {
        if (_prepared) return;
        _prepared = true;
        _restored = false;
        _realShown = false;
        Done = false;

        _realRenderers = GetComponentsInChildren<Renderer>(true);
        var shader = Shader.Find("Custom/CharacterGhost");
        if (shader != null) _ghost = new Material(shader);
        else Debug.LogWarning("[NpcEntrance] 找不到 Custom/CharacterGhost shader —— 入场将退化为直接出现");

        _origLayer = gameObject.layer;
        _collider = GetComponent<Collider>();
        _hadCollider = _collider != null && _collider.enabled;
        _anim = GetComponentInChildren<Animator>();
    }

    // ------------------------------------------------------------------ 演出（由 runner 协程 yield 驱动）
    public IEnumerator Run(Vector3 from, Vector3 to, Transform faceTarget)
    {
        if (!_prepared) Prepare();

        transform.position = from;
        Vector3 dir = to - from; dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(dir.normalized);

        // 摘描边 + 禁碰撞（虚影不挡人）；真身隐藏 + 造幽灵层
        gameObject.layer = LayerMask.NameToLayer("Default");
        if (_collider != null) _collider.enabled = false;
        foreach (var r in _realRenderers) if (r != null) r.enabled = false;
        BuildGhostLayer();

        SetWalk(true);

        float dist = Vector3.Distance(from, to);
        float dur = Mathf.Max(0.6f, dist / SPEED);
        Vector3 start = from;
        float t = 0f;          // 路程进度 0→1（匀速）
        float reveal = 0f;     // 凝实进度 0→1

        while (t < 1f && !fast)
        {
            float dt = Time.deltaTime;
            t = Mathf.Clamp01(t + dt / dur);
            transform.position = Vector3.Lerp(start, to, t);            // 匀速，无 SmoothStep 骤停

            if (t >= REVEAL_START) reveal = Mathf.Clamp01(reveal + dt / REVEAL_DUR);
            UpdateGhostAlpha(t, reveal);
            if (reveal > 0f) ShowReal();                                 // 凝实启动：真身在幽灵最浓时接通（零跳变）
            yield return null;
        }

        // 到达后把凝实播完（fast 时直接跳满）
        if (!_realShown) ShowReal();
        transform.position = to;
        while (reveal < 1f)
        {
            if (fast) reveal = 1f;
            else reveal = Mathf.Clamp01(reveal + Time.deltaTime / REVEAL_DUR);
            UpdateGhostAlpha(1f, reveal);
            yield return null;
        }

        if (faceTarget != null)
        {
            Vector3 d = faceTarget.position - transform.position; d.y = 0f;
            if (d.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(d.normalized);
        }
        SetWalk(false);
        Restore();
        Done = true;
    }

    /// 自检/调试：立即完成（与协程共享同一套收尾）
    public void Skip() { fast = true; }

    // ------------------------------------------------------------------ 内部
    /// 幽灵层浓度：全程恒定 GHOST_ALPHA（出现即 ~75% 可见），凝实段平滑淡出到 0（露出真身=100%）
    void UpdateGhostAlpha(float t, float reveal)
    {
        if (_ghost == null) return;
        float fade = reveal * reveal * (3f - 2f * reveal);          // 三次平滑
        _ghost.SetFloat(AlphaId, GHOST_ALPHA * (1f - fade));
    }

    /// 复制蒙皮网格做幽灵层（共享骨骼 → 动作与真身完全同步），套幽灵材质
    void BuildGhostLayer()
    {
        if (_ghost == null) return;
        foreach (var r in _realRenderers)
        {
            if (r == null || !(r is SkinnedMeshRenderer)) continue;
            var cloneGo = Instantiate(r.gameObject, r.transform.parent);
            cloneGo.name = "幽灵层_" + r.name;
            foreach (var col in cloneGo.GetComponentsInChildren<Collider>()) col.enabled = false;
            var smr = cloneGo.GetComponent<SkinnedMeshRenderer>();
            // ★ Instantiate 会连"禁用"状态一起复制（克隆时原身刚被 enabled=false）——
            //   不显式启用，幽灵层全程一个像素都不渲染，观感=她"突然出现"（2026-09-28 踩坑）。
            cloneGo.SetActive(true);
            smr.enabled = true;
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            smr.updateWhenOffscreen = true;
            var mats = new Material[smr.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = _ghost;
            smr.sharedMaterials = mats;
            _ghostClones.Add(cloneGo);
        }
        if (_ghostClones.Count == 0)
            Debug.LogWarning("[NpcEntrance] 没有可复制的 SkinnedMeshRenderer —— 虚影层为空，将直接显示真身");
        else
            Debug.Log("[NpcEntrance] 幽灵层就绪：" + _ghostClones.Count + " 个克隆（已启用，浓度 " + GHOST_ALPHA + "）");
    }

    void ShowReal()
    {
        if (_realShown) return;
        _realShown = true;
        foreach (var r in _realRenderers) if (r != null) r.enabled = true;
    }

    void Restore()
    {
        if (_restored) return;
        _restored = true;
        gameObject.layer = _origLayer;
        if (_collider != null && _hadCollider) _collider.enabled = true;
        foreach (var go in _ghostClones) if (go != null) Destroy(go);
        _ghostClones.Clear();
        ShowReal();          // 保险：任何路径（含 fast）都保证真身可见
        if (_ghost != null) _ghost.SetFloat(AlphaId, 1f);
    }

    void SetWalk(bool on)
    {
        if (_anim == null) return;
        // PC_徐夏_Walk.controller：Speed 混合树（0 待机 / 0.5+ 行走）
        for (int i = 0; i < _anim.parameterCount; i++)
        {
            var p = _anim.GetParameter(i);
            if (p.type == AnimatorControllerParameterType.Float && p.name == "Speed")
            { _anim.SetFloat("Speed", on ? 0.65f : 0f); return; }
            if (p.type == AnimatorControllerParameterType.Bool && p.name == "Walk")
            { _anim.SetBool("Walk", on); return; }
        }
        // 没有可用参数：纯位移（动画保持原样，不报错）
    }

    void OnDestroy()
    {
        foreach (var go in _ghostClones) if (go != null) Destroy(go);
        if (_ghost != null) Destroy(_ghost);
    }
}
