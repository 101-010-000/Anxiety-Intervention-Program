// NPC 剧情入场驱动（第2章陆宣雨"从门走进来"，2026-09-28 v5 简化版）：
//   由 StoryRunner 的 enter 步骤挂载：从门口锚点走到玩家面前（播行走动画），到位后面向玩家。
//   ★ 虚实渐变（幽灵层/透明材质）已按用户要求整体移除（2026-09-28）——全程真实形象。
//   开场隐藏 + 剧情到点入场由 StoryRunner 负责（CollectAndHideEntranceNpcs / State.Enter），
//   镜头平滑转向门口 = FirstPersonController.LookTowardRoutine（StoryRunner 启动）。
//   Skip() 供自检快进（瞬间终态，同一套收尾）。
//   v6（2026-09-29）：修 _anim 选取——NPC 实例根上有 GameCharSwap 停用的旧 Animator，
//   原先 GetComponentInChildren 拿到的是它 → Speed 写进死组件，走位全程播待机（"飘进来"）。
//   现在过滤 enabled + controller 非空，拿子物体模型上真正驱动身体的那个；
//   参数缺失时报警一次，不再静默兜底。
//   v7（2026-09-29）：Run 重构为 RunPath 分段走位（经由点）——enter/leave 可带 via 锚点列表
//   绕开桌椅（StoryRunner 把 step.via 解析成 pts）；Run 保留签名改薄包装，现有调用零变化。
//   v8（2026-09-29）：走位观感重构——每段【先原地转身到位再起步】（用户看了录屏反馈
//   「过拐点后斜着走」：旧版边走边转 + 段尾/到位两处朝向瞬切）；到位面向目标也改原地平滑转身。
using System.Collections;
using System.Linq;
using UnityEngine;

public class NpcEntrance : MonoBehaviour
{
    const float SPEED = 1.35f;          // 入场步速（偏缓，演出感）
    const float TURN_SPEED = 480f;      // 原地转身角速度（度/秒）
    const float TURN_EPS = 10f;         // 起步前转身到位的容差（度）——剩余小角直接补齐，肉眼不可见

    Collider _collider;
    bool _hadCollider;
    Animator _anim;
    bool _prepared, _restored, _warned;

    public bool Done { get; private set; }
    /// 自检/调试：置 true 后协程逐帧直达终态（等价 Skip）
    public bool fast;

    void Prepare()
    {
        if (_prepared) return;
        _prepared = true;
        _collider = GetComponent<Collider>();
        _hadCollider = _collider != null && _collider.enabled;
        // ★ 挑"活着的"那个：GameCharSwap 在 NPC 实例根上留了个停用的旧 Animator（controller=null），
        //   GetComponentInChildren 从根深度优先会先拿到它 → 写 Speed 写进空参数的死组件，
        //   走位全程播待机（试玩"待机漂移"的根因）。真正驱动身体的是子物体模型上的新 Animator。
        _anim = GetComponentsInChildren<Animator>(true)
            .FirstOrDefault(a => a.enabled && a.runtimeAnimatorController != null);
    }

    // ------------------------------------------------------------------ 演出（由 runner 协程 yield 驱动）
    /// 多段走位（v8）：逐段直线匀速；★ 每段【先原地转身到位、再起步】——段内朝向锁死=段方向，
    /// 不再"边走边转"（那是斜走感的根源）；到位面向目标（玩家）也是原地平滑转身，不再瞬切。
    /// fast==true（Skip 快进）时转身/位移同帧直达终态，与旧版语义一致。
    public IEnumerator RunPath(Vector3[] pts, Transform faceTarget)
    {
        // 二次入场防护（trellis-check 2026-09-28）：同会话重新 Begin 再入场时，
        // _restored/fast/Done 不重置会导致碰撞不恢复、快进残留
        _restored = false; fast = false; Done = false;
        if (pts == null || pts.Length < 2) { Done = true; yield break; }   // 防御：凑不够一段
        if (!_prepared) Prepare();

        transform.position = pts[0];
        if (_collider != null) _collider.enabled = false;   // 走位中不挡人（到位恢复）
        SetWalk(true);

        for (int i = 1; i < pts.Length; i++)
        {
            Vector3 from = pts[i - 1], to = pts[i];
            if ((to - from).sqrMagnitude < 0.0001f) continue;   // 近零长度段（重复经由点）直接跳过

            Vector3 dir = to - from; dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
            {
                // 原地转身到段方向（480°/s，像停下来迈两步转身的观感）；快进时直接到位。
                // 转完把朝向补齐到 face：≤10° 的瞬时修正肉眼不可见，但保证段内身体=位移方向。
                Quaternion face = Quaternion.LookRotation(dir.normalized);
                while (!fast && Quaternion.Angle(transform.rotation, face) > TURN_EPS)
                {
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, face, TURN_SPEED * Time.deltaTime);
                    yield return null;
                }
                transform.rotation = face;
            }

            float dur = Mathf.Max(0.6f, Vector3.Distance(from, to) / SPEED);
            float t = 0f;
            while (t < 1f && !fast)
            {
                t = Mathf.Clamp01(t + Time.deltaTime / dur);
                transform.position = Vector3.Lerp(from, to, t);            // 匀速直线
                yield return null;
            }
            transform.position = to;
        }

        // 到位后面向目标（enter=玩家；leave/hide 无）：原地平滑转身，不再瞬切
        if (faceTarget != null)
        {
            Vector3 d = faceTarget.position - transform.position; d.y = 0f;
            if (d.sqrMagnitude > 0.0001f)
            {
                Quaternion face = Quaternion.LookRotation(d.normalized);
                while (!fast && Quaternion.Angle(transform.rotation, face) > 1f)
                {
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, face, TURN_SPEED * Time.deltaTime);
                    yield return null;
                }
                transform.rotation = face;
            }
        }
        SetWalk(false);
        Restore();
        Done = true;
    }

    /// 两点直线（旧签名，薄包装）：现有 enter/leave/自检调用零变化
    public IEnumerator Run(Vector3 from, Vector3 to, Transform faceTarget)
    {
        return RunPath(new[] { from, to }, faceTarget);
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
        // 0.8：起步姿态更明确（0.65 贴着阈值，Idle↔Walk 混合过渡期姿态发飘——用户录屏反馈的斜走感之一）
        for (int i = 0; i < _anim.parameterCount; i++)
        {
            var p = _anim.GetParameter(i);
            if (p.type == AnimatorControllerParameterType.Float && p.name == "Speed")
            { _anim.SetFloat("Speed", on ? 0.8f : 0f); return; }
            if (p.type == AnimatorControllerParameterType.Bool && p.name == "Walk")
            { _anim.SetBool("Walk", on); return; }
        }
        // 没有可用参数：报警一次（别静默——之前就是静默兜底把"没播走路"藏了两轮试玩）
        if (!_warned)
        {
            _warned = true;
            Debug.LogWarning("[NpcEntrance] " + name + "：Animator 上没有可用的 Speed/Walk 参数——走位期间保持当前动画", this);
        }
    }
}
