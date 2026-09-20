// 门口交互点：贴在门前的触发盒上，只负责"报名字 + 给落点"。
// 真正干活的是 DoorTravelSystem（单例）—— 它统一处理提示、F 键、选择面板、传送。
//
// 挂法：Tools/干预项目/搭建门口传送 会自动在每间房的门前建一个
//       Door_<地点> 节点（BoxCollider isTrigger + 本脚本），并接好 arrivePoint。
using UnityEngine;

public class DoorInteractable : MonoBehaviour
{
    [Tooltip("这个门属于哪个地点（场景里 Loc_XXX 的名字，用来做唯一标识）")]
    public string locationId;

    [Tooltip("显示名，如 大学教室")]
    public string title;

    [Tooltip("章节，如 第1章")]
    public string chapter;

    [Tooltip("传到这个门时，玩家落地的位置（朝门内、往前挪一点，别落在触发盒里）")]
    public Transform arrivePoint;

    [Tooltip("门前触发区。留空就取自己身上的 Collider")]
    public Collider trigger;

    void Reset() { trigger = GetComponent<Collider>(); }
    void OnValidate() { if (trigger == null) trigger = GetComponent<Collider>(); }

    /// 判定点是不是在门前。
    /// 用 ClosestPoint 判而不是 OnTriggerEnter —— 免得"传送落地正好落在触发盒里"这类边界情况漏掉。
    /// ★ 传进来的应该是【胸口高度】的探针点，不是脚底 —— 门触发盒底面往往离地几厘米，
    ///   拿脚底去比会差之毫厘判成"在外"（踩过：盒底 y=0.05、脚底 y=0.03）。
    public bool Contains(Vector3 worldPos)
    {
        if (trigger == null) return false;
        var p = trigger.ClosestPoint(worldPos);
        return (p - worldPos).sqrMagnitude < 0.0025f;     // 5cm 容差，避免边界反复闪
    }

    void OnDrawGizmosSelected()
    {
        if (trigger != null)
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.55f);
            var b = trigger.bounds;
            Gizmos.DrawWireCube(b.center, b.size);
        }
        if (arrivePoint != null)
        {
            Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.9f);
            Gizmos.DrawWireSphere(arrivePoint.position, 0.25f);
            Gizmos.DrawRay(arrivePoint.position, arrivePoint.forward * 0.8f);
        }
    }
}
