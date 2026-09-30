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

    [Tooltip("道具联动（可空）：场景里的道具 GameObject 名（如 手机_淡蓝）。" +
             "① 交互判定中心 = 道具位置（玩家要走近道具才能交互，按名解析，手挪道具自动跟随）；" +
             "② Fire 后道具整棵隐藏（「拿起」的可见反馈）；③ Revive/重新武装时道具重新出现（下次交互前）。")]
    public string propObjectName = "";
    [Tooltip("任意座位模式：玩家【碰到】本地点（Loc_*）里任意一把凳子/椅子就算到位" +
             "（第5章「找个凳子坐下」；radius = 允许离凳子表面的间隙，0.4 ≈ 贴着凳子；判定只看本地点内）")]
    public bool anySeat;
    [Tooltip("任意座位模式的限定（可空）：只认名字等于它的那一把凳子（如「凳子2 (24)」）——" +
             "剧情角色坐在固定桌旁，要在【那把】凳子边才触发（第5章食堂）。留空 = 本地点内任意凳子。" +
             "判定仍按凳子表面距离算，与节点原点无关（这批凳子节点原点能偏 3m）。")]
    public string seatObjectName = "";

    /// 是否允许触发/显示（StoryRunner 只在进入对应的等待步骤时置 true）。
    /// ★ 不加这个开关：开场旁白段（自由走动）路过组长时按一下 F 就会把交互点消费掉，
    ///   等到真正该交互时提示还在、按 F 却没反应。
    public bool armed = true;

    /// 触发时回调（StoryRunner 运行期接上）
    public System.Action<StoryInteractable> onTriggered;

    public bool PlayerInRange { get; private set; }
    public bool Consumed { get; private set; }

    FirstPersonController _player;
    GameObject _prop;          // 按名解析后缓存（隐藏后是 inactive，GameObject.Find 找不到，必须缓存）
    bool _propMissing;         // 找过没找到：不再每帧 Find（ShowProp 时会再给一次机会）

    /// 交互判定中心：联动道具的位置，没配道具就用自身（水平距离判定会把 y 清零）
    public Vector3 PromptCenter
    {
        get { var p = ResolveProp(); return p != null ? p.transform.position : transform.position; }
    }

    GameObject ResolveProp()
    {
        if (string.IsNullOrEmpty(propObjectName)) return null;
        if (_prop != null) return _prop;
        if (_propMissing) return null;
        _prop = GameObject.Find(propObjectName);
        if (_prop == null)
        {
            _propMissing = true;
            Debug.LogWarning("[StoryInteractable] 没找到联动道具「" + propObjectName + "」（交互中心回退到自身位置，隐藏/重现不生效）", this);
        }
        return _prop;
    }

    /// 重新武装时让联动道具回到场景（StoryRunner 进入 interact 步骤时调；Revive 里也会调）
    public void ShowProp()
    {
        if (_prop == null) _propMissing = false;   // 每次武装都再试一次，别让一次 Find 失败永久失效
        var p = ResolveProp();
        if (p != null && !p.activeSelf) p.SetActive(true);
    }

    void HideProp()
    {
        var p = ResolveProp();
        if (p != null) p.SetActive(false);
    }

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

    /// 判定用的目标点：普通模式就是本节点；任意座位模式 = 玩家碰到的那把凳子
    /// （用凳子自身碰撞体/渲染包围盒算【表面】距离，不看节点原点 —— 原点可能在模型角落）。
    Vector3 AnchorFor(Vector3 playerPos)
    {
        if (!anySeat) return PromptCenter;   // 普通模式：道具联动（手机）时判定中心跟道具走，否则本节点
        var seats = SeatsHere();
        Transform best = null;
        Vector3 bestPoint = Vector3.zero;
        float bestD = radius;                       // 只有“碰得到的距离”才算
        for (int i = 0; i < seats.Count; i++)
        {
            var s = seats[i];
            if (s == null || !s.gameObject.activeInHierarchy) continue;
            if (!string.IsNullOrEmpty(seatObjectName) && s.name != seatObjectName) continue;
            Vector3 q = SurfacePoint(s, playerPos);
            float d = new Vector2(q.x - playerPos.x, q.z - playerPos.z).magnitude;
            if (d <= bestD) { bestD = d; best = s; bestPoint = q; }
        }
        if (best == null && !string.IsNullOrEmpty(seatObjectName) && !_seatWarned)
        {
            _seatWarned = true;              // 限定凳子不存在（改过名/删了）→ 提示一次，之后按旧逻辑回退（不会触发）
            Debug.LogWarning("[StoryInteractable] 限定的凳子「" + seatObjectName + "」在本地点里没找到，交互永远到不了位", this);
        }
        // 身边没碰到凳子 → 回退到本节点自身位置（radius 很小，基本等于不触发）
        return best != null ? bestPoint : transform.position;
    }

    bool _seatWarned;

    /// 凳子上离 point 最近的一点（优先碰撞体，其次渲染包围盒，都没有才回退节点原点）
    static Vector3 SurfacePoint(Transform seat, Vector3 point)
    {
        var col = seat.GetComponentInChildren<Collider>();
        if (col != null && col.enabled && col.gameObject.activeInHierarchy)
            return col.ClosestPoint(point);
        var r = seat.GetComponentInChildren<Renderer>();
        if (r != null && r.enabled && r.gameObject.activeInHierarchy)
            return r.bounds.ClosestPoint(point);
        return seat.position;
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
        HideProp();      // 联动道具整棵隐藏 = 「被拿起」的可见反馈（没配道具时 no-op）
    }

    /// 消费后复用（2026-09-28）：同一章多次用同一个点（第2章两次"拿起手机"）。
    /// ⚠ 不能叫 Reset()——那会撞 Unity 编辑器给 MonoBehaviour 的 Reset 消息（组件被重置时被编辑器调用）。
    public void Revive()
    {
        Consumed = false;
        PlayerInRange = false;
        var col = GetComponent<Collider>();
        if (col != null) col.enabled = true;
        ShowProp();      // 下一次交互快要开始时，道具先回到桌上（第2章第二次拿手机）
    }
}
