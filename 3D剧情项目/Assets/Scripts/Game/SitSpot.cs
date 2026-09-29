// 座位点 / NPC 坐姿（配合 Assets/Editor/SitSetup.cs 生成的 <角色>_Sit 剪辑 + 控制器 Sit 状态）
//
// 两种模式（SitSpot.mode）：
//   · 按F坐下      —— 玩家靠近（radius 内）按 F 坐下，再按 F / 按 WASD / 走开 起身（用户 2026-09-29 定稿）
//   · 剧情锁住自动坐 —— 站到座位上 + 剧情开始对话（locked=true）自动坐下（给 第3章_落座 等剧情锚点用）
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

    static readonly int SittingHash = Animator.StringToHash("Sitting");

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
            bool wantMove = Input.GetAxisRaw("Horizontal") != 0f || Input.GetAxisRaw("Vertical") != 0f;
            if (Input.GetKeyDown(key) || wantMove || d > standDistance) Stand();
        }

        if (showPrompt) SitPrompt.Set(inRange, _seated, key);
    }

    void OnDisable()
    {
        if (_seated) Stand();
        SitPrompt.Hide(this);
    }

    /// <summary>坐姿点：贴地后的位置（坐姿剪辑要求根在地面）</summary>
    public Vector3 SeatPos()
    {
        Vector3 p = transform.position;
        if (snapToGround)
        {
            RaycastHit hit;
            // 从标记点上方 2m 往下打，找地面（忽略触发盒）
            if (Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out hit, 6f, ~0, QueryTriggerInteraction.Ignore))
                p.y = hit.point.y;
        }
        return p + Vector3.up * groundOffset;
    }

    /// <summary>坐姿朝向：跟板凳一致（没有板凳则用座位点自身朝向）</summary>
    public float SeatYaw()
    {
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
        if (cc != null) cc.enabled = true;

        _fpc.animator.SetBool(SittingHash, true);
        _fpc.SetSitting(true);                                // 镜头切坐姿档（支点压低）
        _seated = true;
        Debug.Log("[SitSpot] 坐下：" + name + "  朝向=" + SeatYaw().ToString("0.0") + "°");
    }

    void Stand()
    {
        if (_fpc != null && _fpc.animator != null) _fpc.animator.SetBool(SittingHash, false);
        if (_fpc != null) _fpc.SetSitting(false);
        _seated = false;
        SitPrompt.Hide(this);
        Debug.Log("[SitSpot] 起身：" + name);
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
