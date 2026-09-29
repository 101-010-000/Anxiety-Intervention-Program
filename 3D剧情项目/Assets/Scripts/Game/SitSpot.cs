// 座位点 / NPC 坐姿（配合 Assets/Editor/SitSetup.cs 生成的 <角色>_Sit 剪辑 + 控制器 Sit 状态）
//
// 剧情里的用法（已经能对上现有数据，不用改 StoryRunner）：
//   · 交互锚点（第3章_落座 / 第4章_坐下看资料 / 第5章_回座位）上挂 SitSpot：
//     玩家按剧情走到座位上 → 剧情开始对话会把玩家【锁住】→ 检测到"站在座位上 + 被锁住"就坐下；
//     之后一按 WASD（或剧情把玩家传走）就自动起身。
//   · NPC：给坐在位置上的实例挂 SitHere，Start 时把 Animator 的 Sitting 置 true。
//
// 坐姿剪辑是"脚在地面、屁股在椅面"的基准，所以座位点的 Y 必须是【地面】(0)，朝向 = 面向桌子。

using UnityEngine;

public class SitSpot : MonoBehaviour
{
    [Tooltip("站得多近算“在座位上”（米）")]
    public float radius = 1.3f;
    [Tooltip("坐下后的朝向（yaw，度）。>= 999 表示用本物体自己的朝向")]
    public float yaw = 999f;
    [Tooltip("起身判定：离座位多远就算走开了（剧情把玩家传走时也会起身）")]
    public float standDistance = 1.8f;

    static readonly int SittingHash = Animator.StringToHash("Sitting");

    FirstPersonController _fpc;
    bool _seated;

    public bool Seated { get { return _seated; } }

    void Update()
    {
        if (_fpc == null) _fpc = FirstPersonController.Instance;
        if (_fpc == null || _fpc.animator == null) return;

        Vector3 p = _fpc.transform.position, a = transform.position;
        float d = new Vector2(p.x - a.x, p.z - a.z).magnitude;

        if (!_seated)
        {
            // 站在座位上 + 剧情把玩家锁住（= 正在对话）→ 坐下
            if (d <= radius && _fpc.locked) Seat();
        }
        else
        {
            bool wantMove = Input.GetAxisRaw("Horizontal") != 0f || Input.GetAxisRaw("Vertical") != 0f;
            if (wantMove || d > standDistance) Stand();
        }
    }

    void Seat()
    {
        var cc = _fpc.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;                  // 挪人必须关 CC（门口传送同款坑）
        _fpc.transform.position = transform.position;
        _fpc.transform.rotation = Quaternion.Euler(0f, yaw >= 999f ? transform.eulerAngles.y : yaw, 0f);
        if (cc != null) cc.enabled = true;

        _fpc.animator.SetBool(SittingHash, true);
        _fpc.SetSitting(true);                                // 镜头切坐姿档（支点压低）
        _seated = true;
        Debug.Log("[SitSpot] 坐下：" + name);
    }

    void Stand()
    {
        if (_fpc != null && _fpc.animator != null) _fpc.animator.SetBool(SittingHash, false);
        if (_fpc != null) _fpc.SetSitting(false);
        _seated = false;
        Debug.Log("[SitSpot] 起身：" + name);
    }

    void OnDisable()
    {
        if (_seated) Stand();
    }
}

/// <summary>NPC 坐姿：挂到坐在位置上的实例上，Start 就让它坐着（配合 SitSetup 生成的 Sit 状态）</summary>
public class SitHere : MonoBehaviour
{
    static readonly int SittingHash = Animator.StringToHash("Sitting");

    void Start()
    {
        var a = GetComponentInChildren<Animator>();
        if (a != null) a.SetBool(SittingHash, true);
        else Debug.LogWarning("[SitHere] " + name + " 下没有 Animator");
    }
}
