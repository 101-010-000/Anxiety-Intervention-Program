// NPC 剧情入场驱动（第2章陆宣雨"从门走进来"，2026-09-28 v5 简化版）：
//   由 StoryRunner 的 enter 步骤挂载：从门口锚点走到玩家面前（播行走动画），到位后面向玩家。
//   ★ 虚实渐变（幽灵层/透明材质）已按用户要求整体移除（2026-09-28）——全程真实形象。
//   开场隐藏 + 剧情到点入场由 StoryRunner 负责（CollectAndHideEntranceNpcs / State.Enter），
//   镜头平滑转向门口 = FirstPersonController.LookTowardRoutine（StoryRunner 启动）。
//   Skip() 供自检快进（瞬间终态，同一套收尾）。
using System.Collections;
using UnityEngine;

public class NpcEntrance : MonoBehaviour
{
    const float SPEED = 1.35f;          // 入场步速（偏缓，演出感）

    Collider _collider;
    bool _hadCollider;
    Animator _anim;
    bool _prepared, _restored;

    public bool Done { get; private set; }
    /// 自检/调试：置 true 后协程逐帧直达终态（等价 Skip）
    public bool fast;

    void Prepare()
    {
        if (_prepared) return;
        _prepared = true;
        _collider = GetComponent<Collider>();
        _hadCollider = _collider != null && _collider.enabled;
        _anim = GetComponentInChildren<Animator>();
    }

    // ------------------------------------------------------------------ 演出（由 runner 协程 yield 驱动）
    public IEnumerator Run(Vector3 from, Vector3 to, Transform faceTarget)
    {
        // 二次入场防护（trellis-check 2026-09-28）：同会话重新 Begin 再入场时，
        // _restored/fast/Done 不重置会导致碰撞不恢复、快进残留
        _restored = false; fast = false; Done = false;
        if (!_prepared) Prepare();

        transform.position = from;
        Vector3 dir = to - from; dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(dir.normalized);

        if (_collider != null) _collider.enabled = false;   // 走位中不挡人（到位恢复）
        SetWalk(true);

        float dur = Mathf.Max(0.6f, Vector3.Distance(from, to) / SPEED);
        float t = 0f;
        while (t < 1f && !fast)
        {
            t = Mathf.Clamp01(t + Time.deltaTime / dur);
            transform.position = Vector3.Lerp(from, to, t);            // 匀速
            yield return null;
        }

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
    void Restore()
    {
        if (_restored) return;
        _restored = true;
        if (_collider != null && _hadCollider) _collider.enabled = true;
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
}
