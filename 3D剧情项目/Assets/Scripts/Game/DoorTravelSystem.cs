// 门口传送系统：走出门前 → 弹「按 F 开门」→ 按 F 弹出地点选择面板 → 选一个就传送过去。
//
// 为什么是"传送"而不是 LoadScene：6 个剧情地点本来就在同一个 Game.unity 里，
// 按 40m 间距并排摆成片场（见 SceneBuilder.GAP）。所以"切换场景"在这里 = 传送到另一个地点，
// 不需要真的切 Unity 场景。以后要真的换 Unity 场景，改 TravelTo() 一行就行。
//
// 由 Tools/干预项目/搭建门口传送 自动建好 UI 并接好引用。
// 玩家被锁住的方式沿用 FirstPersonController.SetLocked（剧情对话也是这一套）。
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class DoorTravelSystem : MonoBehaviour
{
    public static DoorTravelSystem Instance;

    [Header("引用（工具自动接）")]
    [Tooltip("玩家。留空则运行时自动找 FirstPersonController.Instance")]
    public FirstPersonController player;
    [Tooltip("门口提示的根节点（「按 F 开门」那一块）")]
    public GameObject promptRoot;
    [Tooltip("提示里的地点名")]
    public Text promptLabel;
    [Tooltip("选择面板（UIPanel：淡入淡出 + 压暗）")]
    public UIPanel panel;
    [Tooltip("面板标题")]
    public Text panelTitle;
    [Tooltip("面板副标题/提示")]
    public Text panelHint;
    [Tooltip("目标按钮的容器（工具会预建 N 个按钮，运行时填名字）")]
    public RectTransform listRoot;
    [Tooltip("关闭按钮")]
    public Button closeButton;

    [Header("行为")]
    [Tooltip("交互键")]
    public KeyCode interactKey = KeyCode.F;
    [Tooltip("面板打开后是否允许按 Esc 关掉")]
    public bool escToClose = true;
    [Tooltip("场景刚打开时如果玩家正好站在门前，也照样弹提示（不然会一直没反应）")]
    public bool checkOnStart = true;
    [Tooltip("判定用的探针高度（米）。用脚底判会跟门触发盒的底面错开几厘米，所以抬到胸口高度")]
    public float probeHeight = 0.9f;

    [Header("剧情导流（StoryRunner 驱动；平时全空）")]
    [Tooltip("剧情正在等玩家去的地点（locationId）。非空时：面板里只有它能点，其余置灰")]
    public string storyTargetId;
    [Tooltip("剧情到达目标地点后的落点（剧情锚点）。留空 = 用门自己的 arrivePoint")]
    public Transform storyArrivePoint;
    [Tooltip("到达目标地点后的回调（StoryRunner 接：关掉本系统、继续剧情）")]
    public System.Action<string> onStoryArrived;

    [Header("门口列表（工具自动填）")]
    public List<DoorInteractable> doors = new List<DoorInteractable>();

    /// 当前站着的那个门（null = 不在任何门前）
    public DoorInteractable CurrentDoor { get; private set; }
    public bool IsPanelOpen => panel != null && panel.IsOpen;

    /// 是否处于「剧情导流」：只放行 storyTargetId，到了就回调（第4章「走到门口按 F 去图书馆」）
    public bool InStoryMode => !string.IsNullOrEmpty(storyTargetId);

    /// 面板上的目标列表：**按地点去重**（一间房可能有好几个门，但目标只该有一个）
    public List<DoorInteractable> Destinations { get; } = new List<DoorInteractable>();

    readonly List<Text> _btnLabels = new List<Text>();
    readonly List<Button> _btnButtons = new List<Button>();
    bool _savedEsc;

    void Awake()
    {
        Instance = this;
        // ★ 目标列表/按钮列表必须在 Awake 里建，不能放 Start：
        //   剧情 runner 的 Begin()（也在 Start 里）会把本系统 enabled=false，谁先跑不确定 ——
        //   一旦被先关掉，Start() 就永远不跑 → Destinations 空着 → 开门面板一个按钮都没有
        //   （2026-09-29 自检抳到过：门触发盒 8 个 / 去重后目标地点 0 个，时序时好时坏）。
        if (player == null) player = FirstPersonController.Instance;
        if (player == null) player = FindObjectOfType<FirstPersonController>();
        RebuildDestinations();
        BuildButtonLists();
    }

    void Start()
    {
        if (player == null) player = FirstPersonController.Instance;
        if (player == null) player = FindObjectOfType<FirstPersonController>();

        if (Destinations.Count == 0) RebuildDestinations();      // Awake 之后又被清掉/doors 运行时才填的兜底
        if (panel != null) panel.Hide(true);
        if (promptRoot != null) promptRoot.SetActive(false);

        if (checkOnStart) ScanDoors();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ------------------------------------------------------------------ 每帧
    void Update()
    {
        if (player == null)
        {
            player = FirstPersonController.Instance;
            if (player == null) return;
        }

        if (IsPanelOpen)
        {
            if (escToClose && Input.GetKeyDown(KeyCode.Escape)) ClosePanel();
            return;                                  // 面板开着时不再管提示/F
        }

        ScanDoors();

        if (CurrentDoor != null && Input.GetKeyDown(interactKey))
            OpenPanel();
    }

    /// 判定用的探针点：玩家脚下往上 probeHeight 米（胸口高度）
    public Vector3 ProbePoint()
    {
        if (player == null) return Vector3.zero;
        return player.transform.position + Vector3.up * probeHeight;
    }

    /// 用 ClosestPoint 判玩家在不在某个门的触发盒里，顺带挑最近的那个
    void ScanDoors()
    {
        Vector3 p = ProbePoint();

        DoorInteractable best = null;
        float bestSqr = float.MaxValue;
        for (int i = 0; i < doors.Count; i++)
        {
            var d = doors[i];
            if (d == null || !d.isActiveAndEnabled) continue;
            if (!d.Contains(p)) continue;
            float sq = (d.transform.position - p).sqrMagnitude;
            if (sq < bestSqr) { bestSqr = sq; best = d; }
        }

        if (best != CurrentDoor)
        {
            CurrentDoor = best;
            RefreshPrompt();
        }
    }

    void RefreshPrompt()
    {
        if (promptRoot == null) return;
        bool show = CurrentDoor != null && !IsPanelOpen;
        promptRoot.SetActive(show);
        if (show && promptLabel != null)
            promptLabel.text = string.IsNullOrEmpty(CurrentDoor.title) ? "开门" : ("开门 · " + CurrentDoor.title);
    }

    /// 按 locationId 去重：一间房多个门只出一个目标；顺序跟 doors 里第一次出现的顺序一致
    void RebuildDestinations()
    {
        Destinations.Clear();
        for (int i = 0; i < doors.Count; i++)
        {
            var d = doors[i];
            if (d == null) continue;
            if (string.IsNullOrEmpty(d.locationId)) { Destinations.Add(d); continue; }
            if (Destinations.Any(x => x != null && x.locationId == d.locationId)) continue;
            Destinations.Add(d);
        }
    }

    // ------------------------------------------------------------------ 剧情导流（StoryRunner 用）
    // 第4章「走到门口，按 F 前往图书馆」：剧情把本系统打开 + 设成【只放行图书馆】，
    // 玩家自己走到门前按 F 选地点 → 到了图书馆 → 回调 StoryRunner 继续剧情。
    // ★ 不限制目的地直接放行的话，玩家一点食堂/教室剧情就断了（或得写一堆补丁），
    //   所以这里只置灰其余地点，画面/交互跟平时开门面板完全一样。
    public void EnterStoryMode(string targetLocationId, Transform arriveOverride, System.Action<string> onArrived)
    {
        storyTargetId = targetLocationId;
        storyArrivePoint = arriveOverride;
        onStoryArrived = onArrived;
        enabled = true;
        if (IsPanelOpen) ClosePanel();          // 上一次面板还开着（自检快进时可能）：先收掉
        RefreshPrompt();
    }

    public void ExitStoryMode()
    {
        storyTargetId = null;
        storyArrivePoint = null;
        onStoryArrived = null;
    }

    /// 剧情目标地点的显示名（面板提示/自检报告用）
    public string StoryTargetTitle()
    {
        for (int i = 0; i < doors.Count; i++)
            if (doors[i] != null && doors[i].locationId == storyTargetId)
                return string.IsNullOrEmpty(doors[i].title) ? doors[i].locationId : doors[i].title;
        return storyTargetId;
    }

    /// 门口列表里有没有这个地点（剧情 door 步骤先问一句：没有就退回直接传送，别把玩家卡在门口）
    public bool HasDestination(string locationId)
    {
        if (string.IsNullOrEmpty(locationId)) return false;
        for (int i = 0; i < doors.Count; i++)
            if (doors[i] != null && doors[i].locationId == locationId) return true;
        return false;
    }

    /// 自检/调试用：不点面板按钮，直接走一遍「到达剧情目标地点」（与玩家路径共用 TravelTo）
    public bool DebugTravelToStoryGoal()
    {
        if (!InStoryMode) return false;
        for (int i = 0; i < doors.Count; i++)
            if (doors[i] != null && doors[i].locationId == storyTargetId) { TravelTo(doors[i]); return true; }
        Debug.LogWarning("[DoorTravelSystem] 剧情目标地点在门口列表里找不到：" + storyTargetId);
        return false;
    }

    /// 面板上某个地点的按钮（找不到返回 null）
    public Button GetDestinationButton(string locationId)
    {
        if (listRoot == null) return null;
        for (int i = 0; i < Destinations.Count && i < listRoot.childCount; i++)
        {
            var d = Destinations[i];
            if (d == null || d.locationId != locationId) continue;
            return listRoot.GetChild(i).GetComponent<Button>();
        }
        return null;
    }

    // ------------------------------------------------------------------ 面板
    void BuildButtonLists()
    {
        _btnLabels.Clear();
        _btnButtons.Clear();
        if (listRoot == null) return;

        for (int i = 0; i < listRoot.childCount; i++)
        {
            var child = listRoot.GetChild(i);
            var btn = child.GetComponent<Button>();
            var txt = child.GetComponentInChildren<Text>(true);
            _btnButtons.Add(btn);
            _btnLabels.Add(txt);
        }
    }

    public void OpenPanel()
    {
        if (panel == null) return;
        if (Destinations.Count == 0) RebuildDestinations();     // 兜底（Start 没跑过也不至于面板空白）
        panel.Show();

        // 锁玩家 + 放开鼠标。
        // ★ 还要关掉 allowEscToUnlock：不然鼠标解锁状态下点一下按钮，
        //   FirstPersonController 会立刻把鼠标重新锁回去，UI 就点不动了。
        if (player != null)
        {
            _savedEsc = player.allowEscToUnlock;
            player.allowEscToUnlock = false;
            player.SetLocked(true);
            player.SetCursorLocked(false);
        }

        RefreshPrompt();
        RefreshPanelTexts();
    }

    public void ClosePanel()
    {
        if (panel == null) return;
        panel.Hide();

        if (player != null)
        {
            player.allowEscToUnlock = _savedEsc;
            player.SetLocked(false);
            player.SetCursorLocked(true);
        }

        if (promptRoot != null) promptRoot.SetActive(false);
        CurrentDoor = null;                 // 下一帧 ScanDoors 会重新判断
        ScanDoors();
    }

    void RefreshPanelTexts()
    {
        string here = CurrentDoor != null ? CurrentDoor.locationId : null;
        bool story = InStoryMode;

        if (panelTitle != null) panelTitle.text = "去别的地点";
        if (panelHint != null)
            panelHint.text = story
                ? "剧情：现在要去「" + StoryTargetTitle() + "」（其它地点本章暂时不开放）"
                : (CurrentDoor != null
                    ? "当前站在「" + (string.IsNullOrEmpty(CurrentDoor.title) ? here : CurrentDoor.title) + "」的门口"
                    : "选择要前往的地点");

        for (int i = 0; i < _btnButtons.Count; i++)
        {
            var btn = _btnButtons[i];
            var lbl = i < _btnLabels.Count ? _btnLabels[i] : null;
            if (btn == null) continue;

            if (i >= Destinations.Count || Destinations[i] == null)
            {
                btn.gameObject.SetActive(false);
                continue;
            }

            var d = Destinations[i];
            bool isHere = !string.IsNullOrEmpty(here) && d.locationId == here;
            bool isGoal = story && d.locationId == storyTargetId;
            btn.gameObject.SetActive(true);

            if (lbl != null)
            {
                string s = d.title;
                if (!string.IsNullOrEmpty(d.chapter)) s += "    <size=22><color=#8A97A6>" + d.chapter + "</color></size>";
                if (isHere) s += "    <size=22><color=#8A97A6>（当前所在地）</color></size>";
                else if (isGoal) s += "    <size=22><color=#4C9FE8>（本章前往）</color></size>";
                else if (story) s += "    <size=22><color=#8A97A6>（本章不去）</color></size>";
                lbl.text = s;
            }

            // 剧情导流时：只有目标地点能点（其余置灰，跟"当前所在地"同一个视觉规则）
            btn.interactable = !isHere && (!story || isGoal);
            var captured = d;
            btn.onClick.RemoveAllListeners();
            if (btn.interactable) btn.onClick.AddListener(() => TravelTo(captured));
        }

        if (closeButton != null)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(ClosePanel);
        }
    }

    // ------------------------------------------------------------------ 传送
    public void TravelTo(DoorInteractable target)
    {
        if (target == null || player == null) { ClosePanel(); return; }

        bool isStoryGoal = InStoryMode && target.locationId == storyTargetId;
        // 剧情导流时落点改用剧情锚点（第4章：直接落到图书馆里、林溪旁边那个座位）
        var dest = (isStoryGoal && storyArrivePoint != null)
            ? storyArrivePoint
            : (target.arrivePoint != null ? target.arrivePoint : target.transform);
        string arrivedId = target.locationId;

        // CharacterController 开着的时候直接改 position 会被它拽回去，先关掉再挪
        var cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        player.transform.position = dest.position;
        player.transform.rotation = Quaternion.Euler(0f, dest.eulerAngles.y, 0f);

        if (cc != null) cc.enabled = true;

        // ★ 镜头摆到落点朝向背后（2026-10-01）：此前传送不摆镜头，镜头还朝上一个房间的方向，
        //   水平限位窗跟着旧朝向重定 → 每个房间「能转的范围」死角落哪全看运气（与 StoryRunner.TeleportPlayer 对齐）
        player.ResetCameraNow();

        ClosePanel();

        // 到了剧情要的地方 → 回调 StoryRunner（先清导流状态，回调里再关系统/继续剧情）
        if (isStoryGoal)
        {
            var cb = onStoryArrived;
            ExitStoryMode();
            if (cb != null) cb(arrivedId);
        }
    }
}
