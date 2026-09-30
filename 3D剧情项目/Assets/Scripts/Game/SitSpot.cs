// 座位点 / NPC 坐姿（配合 Assets/Editor/SitSetup.cs 生成的 <角色>_Sit 剪辑 + 控制器 Sit 状态）
//
// 两种模式（SitSpot.mode）：
//   · 按F坐下      —— 玩家靠近（radius 内）按 F 坐下，再按 F / 按 WASD / 走开 起身（用户 2026-09-29 定稿）
//   · 剧情锁住自动坐 —— 站到座位上 + 剧情开始对话（locked=true）自动坐下（给 第3章_落座 等剧情锚点用）
//
// ★ 坐下的表现（2026-09-30 用户定稿）：【换模型】，不是给玩家动画器切坐姿状态 ——
//   坐下时把玩家的【站立模型】（<角色>_已绑定）整个藏掉，把场景里预先摆好、默认隐藏的
//   【坐姿模型】（`seatedModel`，一般挂在 Player_<角色> 下、名字带 _坐姿）显出来，
//   看上去就是“坐在了座位上”；按 WASD / 走开 / 再按 F → 切回站立模型。
//   `seatedModel` 留空时才是老路子（给玩家的 Animator 置 Sitting 布尔）。
//   ★ 坐姿的位置/朝向默认按【坐姿模型在场景里摆好的位置/朝向】来（useSeatedModelPose，2026-10-01）：
//     用户把坐姿模型摆到椅子上，坐姿就坐那儿；座位标记点只当 F 交互点用。
//
// 朝向：**跟板凳一样** —— `seat` 指到板凳/椅子（留空则自动找最近的），坐下时角色朝向 = 板凳的朝向。
// 高度：坐姿剪辑是「脚在地面、屁股在椅面」的基准（Hips≈0.55m），所以角色根必须在【地面】：
//       开 snapToGround 会从标记点向下打射线找地面，标记点放高放低都不会陷进地里。
//
// NPC：坐在位置上的实例挂 SitHere（Start 把 Animator 的 Sitting 置 true）即可。

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

    [Header("提示")]
    [Tooltip("靠近时显示「按 F 坐下」提示（运行时自建小画布，不影响剧情/门口那两套 UI）")]
    public bool showPrompt = true;

    [Header("坐姿模型（换模型，用户 2026-09-30）")]
    [Tooltip("坐下时显示的【坐姿模型】（场景里预先摆好、默认隐藏）。留空 = 老路子：给玩家动画器置 Sitting")]
    public GameObject seatedModel;
    [Tooltip("坐下时把玩家的站立模型整个藏掉（默认开）；起身时恢复")]
    public bool hideStandingModel = true;
    [Tooltip("坐姿位置/朝向按【场景里摆好的坐姿模型】来（用户 2026-10-01 定稿：模型摆哪就坐哪）；" +
             "关掉 = 老行为「坐到座位标记点 / 最近凳子的朝向」")]
    public bool useSeatedModelPose = true;

    static readonly int SittingHash = Animator.StringToHash("Sitting");
    static readonly int SitStateHash = Animator.StringToHash("Sit");   // SitSetup 生成的状态名

    FirstPersonController _fpc;
    bool _seated;

    public bool Seated { get { return _seated; } }

    void Update()
    {
        if (_fpc == null) _fpc = FirstPersonController.Instance;
        if (_fpc == null || _fpc.animator == null) return;

        if (onlyChapters != null && onlyChapters.Length > 0 &&
            System.Array.IndexOf(onlyChapters, GameProgress.SelectedChapter) < 0)
        {
            if (_seated) Stand();
            if (showPrompt) SitPrompt.Hide(this);
            return;                                    // 这一章不允许坐
        }

        Vector3 a = SeatPos();
        Vector3 p = _fpc.transform.position;
        float d = new Vector2(p.x - a.x, p.z - a.z).magnitude;
        bool inRange = d <= radius;

        if (!_seated)
        {
            bool wantSit = (mode == SitMode.按F坐下)
                ? (inRange && !_fpc.locked && Input.GetKeyDown(key))
                : (inRange && _fpc.locked);
            if (wantSit) Seat();
        }
        else
        {
            // ★ 剧情把玩家锁住（正在对话）时：WASD / F 都不起身——坐的模型不许动，
            //   等对话结束（unlock）后按 WASD 才恢复站立模型（用户 2026-10-01）。
            //   走开/被传走（d > standDistance）不受锁影响，转场照样会起身。
            bool wantMove = !_fpc.locked && (Input.GetAxisRaw("Horizontal") != 0f || Input.GetAxisRaw("Vertical") != 0f);
            bool wantUp = !_fpc.locked && Input.GetKeyDown(key);
            if (wantUp || wantMove || d > standDistance) Stand();
        }

        // 提示：剧情锁住（对话中）时 F 不起身，就别显示「按 F 起身」（用户 2026-10-01）
        if (showPrompt && !_fpc.locked) SitPrompt.Set(inRange, _seated, key);
        else SitPrompt.Hide(this);
    }

    void OnDisable()
    {
        if (_seated) Stand();
        SitPrompt.Hide(this);
    }

    /// <summary>坐姿点：贴地后的位置（坐姿剪辑要求根在地面）</summary>
    public Vector3 SeatPos()
    {
        // ★ 有坐姿模型时以【模型摆好的位置】为准（用户 2026-10-01）：座位标记点常常在书桌边，
        //   而用户把坐姿模型摆在了椅子上/想要的座位上——坐姿要跟模型走，不然人会坐在桌子边悬空。
        Vector3 p = (useSeatedModelPose && seatedModel != null) ? seatedModel.transform.position : transform.position;
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
        if (useSeatedModelPose && seatedModel != null) return seatedModel.transform.eulerAngles.y;   // 用户摆好的朝向优先
        var s = seat != null ? seat : FindNearestSeat();
        return s != null ? s.eulerAngles.y : transform.eulerAngles.y;
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
        var cc = _fpc.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;                  // 挪人必须关 CC（门口传送同款坑）
        _fpc.transform.position = SeatPos();
        _fpc.transform.rotation = Quaternion.Euler(0f, SeatYaw(), 0f);
        // ★ 坐着期间【保持关闭】：座位点大多在凳子/桌子正中间，胶囊插在家具里会被顶到凳面上去
        //   （实测人浮在凳子上 0.5m）。起身时（Stand）再打开，顺便让 collide-and-slide 把人挪出家具。

        // ★ 换模型：藏站立模型 + 亮出坐姿模型（坐姿模型跟人站同一个位置/朝向）
        if (seatedModel != null)
        {
            seatedModel.transform.SetPositionAndRotation(SeatPos(), Quaternion.Euler(0f, SeatYaw(), 0f));
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
        Debug.Log("[SitSpot] 坐下：" + name + "  朝向=" + SeatYaw().ToString("0.0") + "°" +
                  (seatedModel != null ? "（换坐姿模型「" + seatedModel.name + "」）" : "（动画器 Sitting）"));
    }

    void Stand()
    {
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
            if (cc != null) cc.enabled = true;               // 起身：CC 回来（顺手把插在家具里的胶囊顶出来）
        }
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
        var a = GetComponentInChildren<Animator>();
        if (a != null) a.SetBool(SittingHash, true);
        else Debug.LogWarning("[SitHere] " + name + " 下没有 Animator");
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
