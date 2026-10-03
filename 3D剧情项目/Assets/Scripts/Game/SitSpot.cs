// 座位点 / NPC 坐姿（配合 Assets/Editor/SitSetup.cs 生成的 <角色>_Sit 剪辑 + 控制器 Sit 状态）
//
// 两种模式（SitSpot.mode）：
//   · 按F坐下      —— 玩家靠近（radius 内）按 F 坐下，再按 F / 按 WASD / 走开 起身（用户 2026-09-29 定稿）
//                     （现在没启用这种点：剧情只用「锁住自动坐」，黑底提示也关了）
//   · 剧情锁住自动坐 —— 站到座位上 + 剧情开始对话（locked=true）自动坐下（给 第3章_落座 等剧情锚点用）
//
// ★ 坐下的表现（2026-09-30 用户定稿）：【换模型】，不是给玩家动画器切坐姿状态 ——
//   坐下时把玩家的【站立模型】（<角色>_已绑定）整个藏掉，把场景里预先摆好、默认隐藏的
//   【坐姿模型】（`seatedModel`，一般挂在 Player_<角色> 下、名字带 _坐姿）显出来，
//   看上去就是“坐在了座位上”；按 WASD / 走开 / 再按 F → 切回站立模型。
//   `seatedModel` 留空时才是老路子（给玩家的 Animator 置 Sitting 布尔）。
//   ★ 坐姿的位置/朝向默认按【坐姿模型在场景里摆好的位置/朝向】来（useSeatedModelPose，2026-10-01）：
//     用户把坐姿模型摆到椅子上，坐姿就坐那儿；座位标记点只当 F 交互点用。
//   ★ 取消坐下（起身）时**回到坐下前站的位置**（用户 2026-10-01）——不是原地站在椅子/桌子里；
//     被剧情传走 / 自己走开起身时不往回传（会跟剧情传送打架）。
//   ★ 黑底「按 F 坐下/起身」小提示默认关（`showPrompt=false`，用户 2026-10-01）：
//     交互提示用游戏原有的蓝色那套（UI交互/交互提示）。
//
// 朝向：**跟板凳一样** —— `seat` 指到板凳/椅子（留空则自动找最近的），坐下时角色朝向 = 板凳的朝向。
// 高度：坐姿剪辑是「脚在地面、屁股在椅面」的基准（Hips≈0.55m），所以角色根必须在【地面】：
//       开 snapToGround 会从标记点向下打射线找地面，标记点放高放低都不会陷进地里。
//
// NPC：坐在位置上的实例挂 SitHere（Start 把 Animator 的 Sitting 置 true）即可。

using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public enum SitMode { 按F坐下, 剧情锁住自动坐下 }

public class SitSpot : MonoBehaviour
{
    [Header("模式")]
    public SitMode mode = SitMode.按F坐下;
    [Tooltip("（按F坐下模式）玩家离多近算在座位旁（米）")]
    public float radius = 1.5f;
    [Tooltip("（按F坐下模式）按哪个键坐下/起身")]
    public KeyCode key = KeyCode.F;

    [Header("朝向 / 高度")]
    [Tooltip("板凳或椅子（坐姿朝向跟它一致）。留空 = 自动找最近的板凳/椅子/沙发")]
    public Transform seat;
    [Tooltip("坐下时把 Y 对齐到地面（从标记点向下打射线）。标记点放哪都不会陷进地里")]
    public bool snapToGround = true;
    [Tooltip("贴地后额外抬高/压低（一般不用）")]
    public float groundOffset = 0f;
    [Tooltip("起身判定：离座位多远就算走开了（剧情把玩家传走时也会起身）")]
    public float standDistance = 1.8f;

    [Header("限定章节（留空 = 不限）")]
    [Tooltip("只在这些章允许坐。例：宿舍那个可坐点填 2、4 → 第5章就坐不下（用户 2026-09-29）")]
    public int[] onlyChapters = new int[0];

    [Header("剧情门控（用户 2026-10-02）")]
    [Tooltip("非空 = 该名字的交互点被【按 F 消费之后】(Consumed) 才允许「锁住自动坐下」。\n" +
             "只认 Consumed 不认 armed（armed 有场景默认值 true，不可靠）。\n" +
             "防止同地点更早的对话把玩家提前按到座位上（第4章：班群段人还在书桌边就被宿舍座位 auto 坐，\n" +
             "到「坐下，翻看复习资料」的 F 点才该坐——宿舍两个座位点都填自己节点上的交互点名）")]
    public string requireInteract = "";

    [Header("提示")]
    [Tooltip("靠近时显示【黑底小提示「按 F 坐下/起身」】（运行时自建小画布）。★默认关（用户 2026-10-01）：" +
             "不要这个黑底提示，交互提示用游戏原有的蓝色那套（UI交互/交互提示）；只有以后真要自由坐下的点再开")]
    public bool showPrompt = false;

    [Header("坐姿模型（换模型，用户 2026-09-30）")]
    [Tooltip("坐下时显示的【坐姿模型】（场景里预先摆好、默认隐藏）。留空 = 老路子：给玩家动画器置 Sitting")]
    public GameObject seatedModel;
    [Tooltip("坐下时把玩家的站立模型整个藏掉（默认开）；起身时恢复")]
    public bool hideStandingModel = true;
    [Tooltip("坐姿位置/朝向按【场景里摆好的坐姿模型】来（用户 2026-10-01 定稿：模型摆哪就坐哪）；" +
             "关掉 = 老行为「坐到座位标记点 / 最近凳子的朝向」")]
    public bool useSeatedModelPose = true;

    [Header("任意凳子模式（第5章图书馆「找个凳子坐下」）")]
    [Tooltip("判定不看本节点位置：玩家【贴着本地点里任意一把凳子】就算到位（同 StoryInteractable.anySeat）")]
    public bool anySeat = false;
    [Tooltip("坐下位置 = 玩家此刻站的位置（朝向 = 最近凳子的朝向）；任意凳子模式用。默认关 = 坐本节点位置")]
    public bool sitAtPlayer = false;

    static readonly int SittingHash = Animator.StringToHash("Sitting");
    static readonly int SitStateHash = Animator.StringToHash("Sit");   // SitSetup 生成的状态名

    FirstPersonController _fpc;
    bool _seated;
    StoryInteractable _gateSI;      // requireInteract 的解析缓存（实例字段——别用 static，编辑器关域重载会跨会话残留）
    bool _gateLooked;
    static bool _suppressSit;          // 明确起身过（对话还锁着）→ 解锁前【所有座位】都别再自动坐回来
    public static bool Suppressed;     // 剧情演出总闸（stage 藏玩家模型期间）：Seat/Stand 一律不跑，
                                       // 防"被传走→起身"把站立模型亮回来（第3章老师视角段，2026-10-01）
                                       //   （★ 同一地点有好几个重叠座位时，只压自己那个会被隔壁座位重新坐下——探针踩过）
    // ★ 坐下前站的位置（用户 2026-10-01：起身要回到这儿）——【所有座位共享】：
    //   宿舍第4/5章两个座位点重叠，坐下时两个组件会接连 Seat()，共享记录才不会把
    //   「已经被隔壁座位挪到椅子上」的位置当成原站位（探针踩过）；同一次坐下只记一次。
    static Vector3 _standPos;
    static Quaternion _standRot;
    static bool _hasStandPos;
    // ★ 坐下那一刻定下来的坐姿（位置/朝向）：坐着期间用它，避免「坐姿跟着玩家跑」
    //   （任意凳子模式坐下点=玩家位置，每帧重算的话剧情把人传走都测不出来）
    Vector3 _sitPos;
    float _sitYaw;
    bool _hasSit;

    public bool Seated { get { return _seated; } }

    void Update()
    {
        if (_fpc == null) _fpc = FirstPersonController.Instance;
        if (_fpc == null || _fpc.animator == null) return;

        if (onlyChapters != null && onlyChapters.Length > 0 &&
            System.Array.IndexOf(onlyChapters, GameProgress.SelectedChapter) < 0)
        {
            if (_seated) Stand(false);                     // 本章不让坐了：就地起身，不往回传送
            if (showPrompt) SitPrompt.Hide(this);
            return;                                    // 这一章不允许坐
        }

        // ★ 判定半径一律从【本节点（剧情 F 交互点）】算，不用 SeatPos()：
        //   任意凳子/坐玩家位置这些模式下 SeatPos() 会跟着玩家跑，用它判距离会永远为 0（到处都触发，踩过）
        Vector3 p = _fpc.transform.position;
        Vector3 anchor = transform.position;
        float dAnchor = new Vector2(p.x - anchor.x, p.z - anchor.z).magnitude;
        float d = (_seated && _hasSit) ? new Vector2(p.x - _sitPos.x, p.z - _sitPos.z).magnitude : dAnchor;
        bool inRange = anySeat ? PlayerAtAnySeat() : dAnchor <= radius;
        if (!_fpc.locked) _suppressSit = false;            // 对话结束 → 恢复正常自动坐

        if (!_seated)
        {
            bool wantSit = (mode == SitMode.按F坐下)
                ? (inRange && !_fpc.locked && Input.GetKeyDown(key))
                : (inRange && _fpc.locked && InteractConsumed());
            if (wantSit && !_suppressSit) Seat();
        }
        else
        {
            // ★ 剧情把玩家锁住（正在对话）时：WASD / F 都不起身——坐的模型不许动，
            //   等对话结束（unlock）后按 WASD 才恢复站立模型（用户 2026-10-01）。
            //   走开/被传走（d > standDistance）不受锁影响，转场照样会起身。
            bool wantMove = !_fpc.locked && (Input.GetAxisRaw("Horizontal") != 0f || Input.GetAxisRaw("Vertical") != 0f);
            bool wantUp = !_fpc.locked && Input.GetKeyDown(key);
            bool movedAway = d > standDistance;           // 被剧情传走 / 自己走开：不能往回传送
            if (wantUp || wantMove || movedAway) Stand(!movedAway);
        }

        // 提示：剧情锁住（对话中）时 F 不起身，就别显示「按 F 起身」（用户 2026-10-01）
        if (showPrompt && !_fpc.locked) SitPrompt.Set(inRange, _seated, key);
        else SitPrompt.Hide(this);
    }

    /// <summary>requireInteract 门控：该交互点【已被消费】(Consumed，即玩家在那里按过 F) 才允许自动坐。
    /// 找不到指定交互点只警告一次并不门控（和 StoryRunner 缺锚点的兜底口径一致）。</summary>
    bool InteractConsumed()
    {
        if (string.IsNullOrEmpty(requireInteract)) return true;
        if (!_gateLooked)
        {
            _gateLooked = true;
            var own = GetComponent<StoryInteractable>();
            if (own != null && own.name == requireInteract) _gateSI = own;
            else
                foreach (var s in Resources.FindObjectsOfTypeAll<StoryInteractable>())
                    if (s != null && s.gameObject.scene.IsValid() && s.name == requireInteract) { _gateSI = s; break; }
            if (_gateSI == null) Debug.LogWarning("[SitSpot] requireInteract 找不到交互点（不门控）：" + requireInteract, this);
        }
        return _gateSI == null || _gateSI.Consumed;
    }

    // 进场（加载场景/组件启用）时清掉上次会话残留的压制标记：
    // 编辑器「关闭域重载」时 static 字段会跨 Play 会话保留，不干净的话下一次 Play 的第一个座位坐不下（踩过）
    void OnEnable()
    {
        _suppressSit = false;
        _seatCacheList = null;
    }

    void OnDisable()
    {
        if (_seated) Stand(false);
        SitPrompt.Hide(this);
    }

    /// <summary>坐姿点：贴地后的位置（坐姿剪辑要求根在地面）</summary>
    public Vector3 SeatPos()
    {
        // ★ 有坐姿模型时以【模型摆好的位置】为准（用户 2026-10-01）：座位标记点常常在书桌边，
        //   而用户把坐姿模型摆在了椅子上/想要的座位上——坐姿要跟模型走，不然人会坐在桌子边悬空。
        // ★ 任意凳子模式（sitAtPlayer）：坐玩家此刻站的位置（他正贴着自己挑的那把凳子）。
        Vector3 p;
        if (sitAtPlayer && _fpc != null) p = _fpc.transform.position;
        else if (useSeatedModelPose && seatedModel != null) p = seatedModel.transform.position;
        else p = transform.position;
        if (snapToGround)
        {
            // ★ 用【玩家此刻的脚底高度】当地面（用户 2026-09-30 实测踩到）：
            //   标记点常常摆在家具上方（可坐点是凳子的子物体、Y≈0.5 坐垫高度；书桌旁的锚点在桌边），
            //   而“从标记点向下打一条射线取第一个命中”会打到凳子/桌面 → 人坐在半空（Hips 被抬到 2m+）。
            //   玩家此刻正站在座位旁的地面上，他的 Y 就是地面。
            var fpc = FirstPersonController.Instance;
            float y = fpc != null ? fpc.transform.position.y : float.MaxValue;
            // 再拿“向下打到的所有面里最低的那个”（地板）兜一道：
            // 玩家万一正站在凳子/桌子上触发，光看他的 Y 会把家具高度当地面
            foreach (var h in Physics.RaycastAll(p + Vector3.up * 2f, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore))
                if (h.point.y < y) y = h.point.y;
            if (y < float.MaxValue) p.y = y;
        }
        return p + Vector3.up * groundOffset;
    }

    /// <summary>坐姿朝向：跟板凳一致（没有板凳则用座位点自身朝向）</summary>
    public float SeatYaw()
    {
        if (sitAtPlayer && _fpc != null)
        {
            var near = seat != null ? seat : NearestSeatTo(_fpc.transform.position);
            return near != null ? near.eulerAngles.y : _fpc.transform.eulerAngles.y;
        }
        if (useSeatedModelPose && seatedModel != null) return seatedModel.transform.eulerAngles.y;   // 用户摆好的朝向优先
        var s = seat != null ? seat : FindNearestSeat();
        return s != null ? s.eulerAngles.y : transform.eulerAngles.y;
    }

    /// <summary>任意凳子判定：玩家是否贴着【本地点】里的任意一把凳子（同 StoryInteractable.anySeat；
    /// radius = 允许离凳子表面的间隙）</summary>
    bool PlayerAtAnySeat()
    {
        var fpc = _fpc != null ? _fpc : FirstPersonController.Instance;
        if (fpc == null) return false;
        var loc = MyLoc();
        if (loc == null) return false;
        foreach (var t in SeatsIn(loc))
        {
            if (t == null || !t.gameObject.activeInHierarchy) continue;
            Vector3 q = SurfacePoint(t, fpc.transform.position);
            float d = new Vector2(q.x - fpc.transform.position.x, q.z - fpc.transform.position.z).magnitude;
            if (d <= radius) return true;
        }
        return false;
    }

    Transform MyLoc()
    {
        for (var t = transform; t != null; t = t.parent)
            if (t.name.StartsWith("Loc_")) return t;
        return null;
    }

    // 凳子名单缓存：★【实例字段】（不要用 static 字典——编辑器「关闭域重载」时静态缓存会跨 Play
    // 会话残留，Unity 复用 instanceID 时按旧场景的凳子判定 → 在宿舍也会「图书馆任意凳子」成立，踩过）
    Transform _seatCacheLoc;
    System.Collections.Generic.List<Transform> _seatCacheList;
    float _seatCacheAt = -99f;

    System.Collections.Generic.List<Transform> SeatsIn(Transform loc)
    {
        if (_seatCacheList != null && _seatCacheLoc == loc && Time.unscaledTime - _seatCacheAt < 1f) return _seatCacheList;
        var list = new System.Collections.Generic.List<Transform>();
        foreach (var t in loc.GetComponentsInChildren<Transform>(true))
            if (IsSeatName(t.name)) list.Add(t);
        _seatCacheLoc = loc; _seatCacheList = list; _seatCacheAt = Time.unscaledTime;
        return list;
    }

    static Vector3 SurfacePoint(Transform seat, Vector3 point)
    {
        // ⚠ 非凸 MeshCollider 的 ClosestPoint【不可靠：会把入参原地返回】——场景设施碰撞体全是非凸
        //   MeshCollider，直接用会让「任意凳子」到处都算贴身（在宿舍出生就触发图书馆的座位，踩过）
        var col = seat.GetComponentInChildren<Collider>();
        if (col != null && col.enabled && col.gameObject.activeInHierarchy)
        {
            var mc = col as MeshCollider;
            if (mc == null || mc.convex) return col.ClosestPoint(point);
        }
        var r = seat.GetComponentInChildren<Renderer>();
        if (r != null && r.enabled && r.gameObject.activeInHierarchy) return r.bounds.ClosestPoint(point);
        return seat.position;
    }

    /// <summary>诊断用：打印「任意凳子」判定里本地点找到的凳子（名字/位置/判定点/距离/包围盒）</summary>
    public string DebugSeats(Vector3 playerPos)
    {
        var sb = new System.Text.StringBuilder();
        var loc = MyLoc();
        if (loc == null) return "（不在任何 Loc_ 下）";
        var seats = SeatsIn(loc);
        sb.Append("loc=" + loc.name + "，本地点凳子 " + seats.Count + " 把，radius=" + radius.ToString("F2") + "\n");
        int i = 0;
        foreach (var t in seats)
        {
            if (t == null) continue;
            Vector3 q = SurfacePoint(t, playerPos);
            float d = new Vector2(q.x - playerPos.x, q.z - playerPos.z).magnitude;
            var col = t.GetComponentInChildren<Collider>();
            string ci = col != null ? col.GetType().Name + "(" + (col is MeshCollider && !((MeshCollider)col).convex ? "非凸" : "可用") + ")" : "无碰撞体";
            var r = t.GetComponentInChildren<Renderer>();
            string ri = r != null ? "renderBounds " + r.bounds.size.ToString("F1") + " @" + r.bounds.center.ToString("F1") : "无渲染器";
            if (d <= radius || i < 6)
                sb.Append("  " + t.name + " pos=" + t.position.ToString("F1") + " 判定点=" + q.ToString("F1") +
                          " 距离=" + d.ToString("F2") + (d <= radius ? " ★贴身" : "") + "  " + ci + "  " + ri + "\n");
            i++;
        }
        return sb.ToString();
    }

    static Transform NearestSeatTo(Vector3 pos)
    {
        Transform best = null; float bestD = 4f;
        foreach (var t in FindObjectsOfType<Transform>())
        {
            if (t == null || !IsSeatName(t.name)) continue;
            float d = Vector3.Distance(t.position, pos);
            if (d < bestD) { bestD = d; best = t; }
        }
        return best;
    }

    public Transform FindNearestSeatPublic() { return FindNearestSeat(); }

    Transform FindNearestSeat()
    {
        // ① 先看【父级链】：可坐点通常是板凳的子物体 → 直接拿它当朝向来源（最准）
        for (var t = transform.parent; t != null; t = t.parent)
            if (IsSeatName(t.name)) return t;
        // ② 再找最近的（4m 内）
        Transform best = null;
        float bestD = 4f;
        foreach (var t in FindObjectsOfType<Transform>())
        {
            if (t == null || !IsSeatName(t.name)) continue;
            float d = Vector3.Distance(t.position, transform.position);
            if (d < bestD) { bestD = d; best = t; }
        }
        return best;
    }

    public static bool IsSeatName(string n)
    {
        n = n.ToLower();
        return n.Contains("chair") || n.Contains("stool") || n.Contains("bench") || n.Contains("armchair")
            || n.Contains("凳") || n.Contains("椅") || n.Contains("沙发");
    }

    void Seat()
    {
        if (Suppressed) return;                        // 剧情演出总闸（老师视角段玩家不可见，不需要坐）
#if UNITY_EDITOR
        StageDiag.Log("Seat 放行 " + name);
#endif
        // ★ 记住【坐下前站的位置】（用户 2026-10-01）：取消坐下（起身）时人要回到这儿，
        //   而不是站在椅子/桌子中间（座位点就在家具上，起身原地会卡在家具里）。
        //   同一次坐下（重叠座位接连 Seat）只记第一次，谁先坐记谁。
        string posBefore = _fpc.transform.position.ToString("F2");
        if (!_hasStandPos)
        {
            _standPos = _fpc.transform.position;
            _standRot = _fpc.transform.rotation;
            _hasStandPos = true;
        }

        _sitPos = SeatPos();                                 // ★ 这一刻定下来的坐姿（坐着期间不再变）
        _sitYaw = SeatYaw();
        _hasSit = true;
        var cc = _fpc.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;                  // 挪人必须关 CC（门口传送同款坑）
        _fpc.transform.position = _sitPos;
        _fpc.transform.rotation = Quaternion.Euler(0f, _sitYaw, 0f);
        // ★ 坐着期间【保持关闭】：座位点大多在凳子/桌子正中间，胶囊插在家具里会被顶到凳面上去
        //   （实测人浮在凳子上 0.5m）。起身时（Stand）再打开，顺便让 collide-and-slide 把人挪出家具。

        // ★ 换模型：藏站立模型 + 亮出坐姿模型（坐姿模型跟人站同一个位置/朝向）
        if (seatedModel != null)
        {
            seatedModel.transform.SetPositionAndRotation(_sitPos, Quaternion.Euler(0f, _sitYaw, 0f));
            seatedModel.SetActive(true);
            if (hideStandingModel) _fpc.SetStandingModelVisible(false);
            // 坐姿模型自己的 Animator 要【立刻】在 Sit 状态：置参数 + 直接 Play("Sit") + Update(0)
            // （不然刚亮出来的那一瞬间还停在默认站姿，会看到“站着的人突然坐下”的抽一下）
            var sa = seatedModel.GetComponentInChildren<Animator>(true);
            if (sa != null)
            {
                sa.SetBool(SittingHash, true);
                if (sa.HasState(0, SitStateHash)) sa.Play(SitStateHash, 0, 0f);
                sa.Update(0f);
            }
        }
        else
        {
            _fpc.animator.SetBool(SittingHash, true);        // 老路子：玩家的动画器切坐姿
        }
        _fpc.SetSitting(true);                                // 镜头切坐姿档（支点压低）
        _seated = true;
        Debug.Log("[SitSpot] 坐下：" + name + "  朝向=" + _sitYaw.ToString("0.0") + "°" +
                  (seatedModel != null ? "（换坐姿模型「" + seatedModel.name + "」）" : "（动画器 Sitting）") +
                  "  原站位记录 " + _standPos.ToString("F2") + "（坐下前玩家在 " + posBefore + "）");
    }

    /// <summary>起身（剧情/演出可以直接调）：默认回到【坐下前站的位置】</summary>
    public void StandUp() { if (_seated) Stand(true); }

    void Stand(bool restoreToStandPos = true)
    {
        if (Suppressed) return;                        // 剧情演出总闸：演出期间谁都不许把站立模型亮回来
#if UNITY_EDITOR
        StageDiag.Log("SitSpot.Stand 放行 " + name + "\n" + System.Environment.StackTrace);
#endif
        // ★ 对话还锁着时被叫起身（剧情调 StandUp / 被传走）：别再自动坐回来（用户 2026-10-01，探针踩过）
        if (_fpc != null && _fpc.locked) _suppressSit = true;
        if (seatedModel != null)
        {
            seatedModel.SetActive(false);                    // 收起坐姿模型
            if (hideStandingModel) _fpc.SetStandingModelVisible(true);   // 站立模型回来
        }
        else if (_fpc != null && _fpc.animator != null)
        {
            _fpc.animator.SetBool(SittingHash, false);
        }
        if (_fpc != null)
        {
            _fpc.SetSitting(false);
            var cc = _fpc.GetComponent<CharacterController>();
            if (restoreToStandPos && _hasStandPos)
            {
                // ★ 回到坐下前站的位置（用户 2026-10-01）：走开/被剧情传走时不回传（会打架）
                if (cc != null) cc.enabled = false;
                _fpc.transform.position = _standPos;
                _fpc.transform.rotation = _standRot;
                _hasStandPos = false;            // 同帧里另一个重叠座位再 Stand 就不重复传送了
            }
            if (cc != null) cc.enabled = true;               // 起身：CC 回来
        }
        _hasStandPos = false;                // 起身收工（含走开/被传走的路径）：这次坐下的记录作废
        _hasSit = false;
        _seated = false;
        SitPrompt.Hide(this);
        Debug.Log("[SitSpot] 起身：" + name);
    }
}

/// <summary>NPC 坐姿：挂到坐在位置上的实例上，Start 就让它坐着（配合 SitSetup 生成的 Sit 状态）</summary>
public class SitHere : MonoBehaviour
{
    static readonly int SittingHash = Animator.StringToHash("Sitting");
    static readonly int SitStateHash = Animator.StringToHash("Sit");   // SitSetup 生成的状态名

    void Start()
    {
        // 挑"活着的"那个：prefab 实例根上可能有 GameCharSwap 停用的旧 Animator（controller=null），
        // 盲取第一个会写到死组件上 → Sitting 永远到不了真控制器，Play 里人站着不坐（NpcEntrance v6 同款坑）
        var a = GetComponentsInChildren<Animator>(true)
            .FirstOrDefault(x => x.enabled && x.runtimeAnimatorController != null);
        if (a != null) a.SetBool(SittingHash, true);
        else Debug.LogWarning("[SitHere] " + name + " 下没有带控制器的活 Animator");
    }
}

/// <summary>「按 F 坐下 / 起身」提示：运行时自建的小画布（订单 90，压在剧情 UI(100) 下面）</summary>
static class SitPrompt
{
    static Canvas _canvas;
    static Text _text;
    static SitSpot _owner;

    public static void Set(bool inRange, bool seated, KeyCode key)
    {
        if (!inRange && !seated) { Hide(null); return; }
        Ensure();
        _canvas.gameObject.SetActive(true);
        _text.text = (seated ? "按 " + key + " 起身" : "按 " + key + " 坐下");
    }

    public static void Hide(SitSpot who)
    {
        if (who != null && _owner != who) return;
        if (_canvas != null) _canvas.gameObject.SetActive(false);
    }

    static void Ensure()
    {
        if (_canvas != null) return;
        var go = new GameObject("UI_坐下提示");
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 90;
        go.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        go.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920, 1080);

        var bg = new GameObject("底");
        bg.transform.SetParent(go.transform, false);
        var img = bg.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.45f);
        var rt = bg.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f); rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 220f);
        rt.sizeDelta = new Vector2(220f, 54f);

        var tx = new GameObject("文字");
        tx.transform.SetParent(bg.transform, false);
        _text = tx.AddComponent<Text>();
        // ⚠ Unity 2022.3 起内置字体叫 LegacyRuntime.ttf（Arial.ttf 会抛 ArgumentException，踩过）
        _text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (_text.font == null) _text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        _text.alignment = TextAnchor.MiddleCenter;
        _text.color = Color.white;
        _text.fontSize = 26;
        _text.text = "按 F 坐下";
        var trt = tx.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
    }
}
