// 剧情交互点：两种模式（挂在场景的触发节点上）
//   InteractF —— 玩家走近（半径内）出"按 F 交谈"提示，按 F 触发（如：组长）
//                 变体：anySeat=true = 「任意座位」——不按本节点自身位置判，
//                 而是「玩家 radius 米内有本地点里任意一把凳子/椅子」就算到位（第5章「找个凳子坐下」）。
//   Touch     —— 玩家走进触发盒即触发（如：教室门口撞张知远）
// 触发后回调 StoryRunner（运行期由 runner 统一接线），一次性触发后自禁用。
using System.Collections.Generic;
using UnityEngine;

public class StoryInteractable : MonoBehaviour
{
    public enum Mode { InteractF, Touch }

    public Mode mode = Mode.InteractF;
    public string displayName = "";
    public float radius = 2.2f;          // F 模式判定半径（水平距离）
    public KeyCode key = KeyCode.F;
    public bool oneShot = true;

    [Tooltip("属于第几章（1-5）。多章交互点并存时，StoryRunner 只取本章的（FindFree 过滤）。默认 1 兼容第一章既有节点。")]
    public int chapterTag = 1;

    [Tooltip("F 提示整句（如「拿起手机」）。留空 = 默认「与<displayName>交谈」。")]
    public string promptText = "";

    [Tooltip("任意座位模式：玩家 radius 米内有【本节点所在地点（Loc_*）】里的任意一把凳子/椅子，就算到位" +
             "（第5章「找个凳子坐下」；判定扫描只在本地点内，跑宿舍里坐不算）")]
    public bool anySeat;

    /// 是否允许触发/显示（StoryRunner 只在进入对应的等待步骤时置 true）。
    /// ★ 不加这个开关：开场旁白段（自由走动）路过组长时按一下 F 就会把交互点消费掉，
    ///   等到真正该交互时提示还在、按 F 却没反应。
    public bool armed = true;

    /// 触发时回调（StoryRunner 运行期接上）
    public System.Action<StoryInteractable> onTriggered;

    public bool PlayerInRange { get; private set; }
    public bool Consumed { get; private set; }

    FirstPersonController _player;

    FirstPersonController Player
    {
        get
        {
            if (_player == null) _player = FindObjectOfType<FirstPersonController>();
            return _player;
        }
    }

    void Update()
    {
        if (Consumed || onTriggered == null) return;
        if (!armed) { PlayerInRange = false; return; }

        if (mode == Mode.InteractF)
        {
            var p = Player;
            if (p == null) return;
            Vector3 a = AnchorFor(p.transform.position); a.y = 0f;
            Vector3 b = p.transform.position; b.y = 0f;
            bool inRange = (a - b).sqrMagnitude <= radius * radius;
            if (inRange != PlayerInRange) PlayerInRange = inRange;
            if (inRange && Input.GetKeyDown(key)) Fire();
        }
    }

    // ------------------------------------------------------------------ 任意座位模式
    // 场景里的凳子/椅子（名字含 凳/椅/chair/stool/bench/沙发）——按地点缓存，避免每帧扫全场景
    static readonly Dictionary<Transform, List<Transform>> _seatCache = new Dictionary<Transform, List<Transform>>();

    Transform MyLoc()
    {
        for (var t = transform; t != null; t = t.parent)
            if (t.name.StartsWith("Loc_")) return t;
        return transform.root;
    }

    List<Transform> SeatsHere()
    {
        var loc = MyLoc();
        List<Transform> list;
        if (_seatCache.TryGetValue(loc, out list) && list != null) return list;
        list = new List<Transform>();
        foreach (var t in loc.GetComponentsInChildren<Transform>(true))
            if (SitSpot.IsSeatName(t.name)) list.Add(t);
        _seatCache[loc] = list;
        return list;
    }

    /// 判定用的目标点：普通模式就是本节点；任意座位模式 = 玩家脚下最近的那把凳子
    /// （最近的那把也得在 radius 内 → 真的“走到凳子旁/碰到凳子”才算）。
    Vector3 AnchorFor(Vector3 playerPos)
    {
        if (!anySeat) return transform.position;
        Transform best = null;
        float bestSqr = radius * radius;
        var seats = SeatsHere();
        for (int i = 0; i < seats.Count; i++)
        {
            var s = seats[i];
            if (s == null || !s.gameObject.activeInHierarchy) continue;
            Vector3 q = s.position;
            float sq = (q.x - playerPos.x) * (q.x - playerPos.x) + (q.z - playerPos.z) * (q.z - playerPos.z);
            if (sq <= bestSqr) { bestSqr = sq; best = s; }
        }
        // 身边没有凳子 → 回退到本节点自身位置（半径很小，基本等于不触发）
        return best != null ? best.position : transform.position;
    }

    void OnTriggerEnter(Collider other)
    {
        if (Consumed || !armed || mode != Mode.Touch || onTriggered == null) return;
        if (other.GetComponentInParent<FirstPersonController>() == null) return;
        Fire();
    }

    /// 触发（oneShot 时自禁用）；自检/调试可直接调
    public void Fire()
    {
        if (Consumed) return;
        if (oneShot)
        {
            Consumed = true;
            PlayerInRange = false;
            var col = GetComponent<Collider>();
            if (col != null) col.enabled = false;
        }
        if (onTriggered != null) onTriggered(this);
    }

    /// 消费后复用（2026-09-28）：同一章多次用同一个点（第2章两次"拿起手机"）。
    /// ⚠ 不能叫 Reset()——那会撞 Unity 编辑器给 MonoBehaviour 的 Reset 消息（组件被重置时被编辑器调用）。
    public void Revive()
    {
        Consumed = false;
        PlayerInRange = false;
        var col = GetComponent<Collider>();
        if (col != null) col.enabled = true;
    }
}
