// NPC 剧情入场驱动（第2章陆宣雨"门口虚影渐显走近"，2026-09-27；机制通用，第4章林溪可复用）：
//   由 StoryRunner 的 enter 步骤在目标角色根节点上懒挂本组件，负责一段入场演出：
//     1. Prepare：快照原材质/原 Layer/原碰撞状态 → 摘描边（Layer 暂离 Outline，防半透明人
//        挂着实心黑壳穿帮）→ 禁胶囊碰撞（虚影不挡人）→ 全部 Renderer 换虚影材质（_Alpha=0）；
//     2. Run（协程，由 runner yield 驱动）：从起点走向终点（面朝移动方向、播行走动画、
//        _Alpha 随进度渐升到上限）→ 到位切待机、转身面向玩家 → Restore（换回原材质、
//        恢复 Layer/碰撞）→ Done；
//     3. Skip：自检/快进用，立即终态（同一套收尾逻辑，幂等）。
//   落点不写死：StoryRunner 按玩家当前位置算"玩家面前 1.3m"传入（用户 2026-09-27 定）。
//
// 材质说明：虚影材质 = CharacterGhost.shader 的运行时实例（Shader.Find），只在内存中存在，
//   不落资产；还原用快照的 sharedMaterials 写回，零资产污染。
// 动画说明：复用 PC_徐夏_Walk.controller（Speed 驱动的待机/行走混合树，humanoid 跨角色
//   重定向）；兼容 Walk(Bool) 型；animator 缺失/无参数时退化为纯位移。
using System.Collections;
using UnityEngine;

public class NpcEntrance : MonoBehaviour
{
    const float SPEED = 1.35f;          // 入场步速（偏缓，演出感）
    // 显形驱动：_Alpha 0→1 随位移进度升（shader 内部再按菲涅尔分中心/边缘——
    // v2 全息版：中心透、边缘实，整体一直是"虚"的，直到落定换回真实材质才是"实"）

    static readonly int AlphaId = Shader.PropertyToID("_Alpha");

    Renderer[] _renderers;
    Material[][] _origMats;             // 每个 renderer 的 sharedMaterials 快照
    Material _ghost;                    // 虚影材质实例（所有 renderer 共享一个，全局改 _Alpha）
    int _origLayer;
    bool _hadCollider;
    Collider _collider;
    Animator _anim;
    bool _prepared, _restored;

    public bool Done { get; private set; }
    /// 自检/调试：置 true 后协程下一帧直达终态（等价 Skip）
    public bool fast;

    // ------------------------------------------------------------------ 准备
    public void Prepare()
    {
        if (_prepared) return;
        _prepared = true;
        _restored = false;
        Done = false;

        _renderers = GetComponentsInChildren<Renderer>(true);
        _origMats = new Material[_renderers.Length][];
        var shader = Shader.Find("Custom/CharacterGhost");
        if (shader != null) _ghost = new Material(shader);
        else Debug.LogWarning("[NpcEntrance] 找不到 Custom/CharacterGhost shader —— 虚影渐显退化为直接出现");

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

        HideVisualSide();               // 摘描边 + 禁碰撞 + 换虚影材质（_Alpha=0）
        SetWalk(true);

        float dist = Vector3.Distance(from, to);
        float dur = Mathf.Max(0.6f, dist / SPEED);
        Vector3 start = from;
        float t = 0f;
        while (t < 1f && !fast)
        {
            t += Time.deltaTime / dur;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            transform.position = Vector3.Lerp(start, to, k);
            if (_ghost != null) _ghost.SetFloat(AlphaId, Mathf.Clamp01(t));
            yield return null;
        }

        // 终态（正常走完或 fast/Skip 都到这）
        transform.position = to;
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
    void HideVisualSide()
    {
        // 描边：OutlineFeature 只画 Outline 层 —— 暂离该层即摘掉黑壳（落定恢复）
        gameObject.layer = LayerMask.NameToLayer("Default");
        if (_collider != null) _collider.enabled = false;

        for (int i = 0; i < _renderers.Length; i++)
        {
            _origMats[i] = _renderers[i].sharedMaterials;
            if (_ghost != null)
            {
                var ghostArr = new Material[_renderers[i].sharedMaterials.Length];
                for (int j = 0; j < ghostArr.Length; j++) ghostArr[j] = _ghost;
                _renderers[i].materials = ghostArr;      // 赋 materials 会实例化，这里全是同一个 _ghost 引用
            }
        }
    }

    void Restore()
    {
        if (_restored) return;
        _restored = true;
        gameObject.layer = _origLayer;
        if (_collider != null && _hadCollider) _collider.enabled = true;
        for (int i = 0; i < _renderers.Length; i++)
            if (_renderers[i] != null && _origMats[i] != null)
                _renderers[i].sharedMaterials = _origMats[i];   // 写回原引用，不产生材质实例
        if (_ghost != null) _ghost.SetFloat(AlphaId, 1f);       // 万一有残留引用也不再透明
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
        if (_ghost != null) Destroy(_ghost);
    }
}
