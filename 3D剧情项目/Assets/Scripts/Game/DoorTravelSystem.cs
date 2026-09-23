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

    [Header("门口列表（工具自动填）")]
    public List<DoorInteractable> doors = new List<DoorInteractable>();

    /// 当前站着的那个门（null = 不在任何门前）
    public DoorInteractable CurrentDoor { get; private set; }
    public bool IsPanelOpen => panel != null && panel.IsOpen;

    /// 面板上的目标列表：**按地点去重**（一间房可能有好几个门，但目标只该有一个）
    public List<DoorInteractable> Destinations { get; } = new List<DoorInteractable>();

    readonly List<Text> _btnLabels = new List<Text>();
    readonly List<Button> _btnButtons = new List<Button>();
    bool _savedEsc;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        if (player == null) player = FirstPersonController.Instance;
        if (player == null) player = FindObjectOfType<FirstPersonController>();

        RebuildDestinations();
        BuildButtonLists();
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

        if (panelTitle != null) panelTitle.text = "去别的地点";
        if (panelHint != null)
            panelHint.text = CurrentDoor != null
                ? "当前站在「" + (string.IsNullOrEmpty(CurrentDoor.title) ? here : CurrentDoor.title) + "」的门口"
                : "选择要前往的地点";

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
            btn.gameObject.SetActive(true);

            if (lbl != null)
            {
                string s = d.title;
                if (!string.IsNullOrEmpty(d.chapter)) s += "    <size=22><color=#8A97A6>" + d.chapter + "</color></size>";
                if (isHere) s += "    <size=22><color=#8A97A6>（当前所在地）</color></size>";
                lbl.text = s;
            }

            btn.interactable = !isHere;
            var captured = d;
            btn.onClick.RemoveAllListeners();
            if (!isHere) btn.onClick.AddListener(() => TravelTo(captured));
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

        var dest = target.arrivePoint != null ? target.arrivePoint : target.transform;

        // CharacterController 开着的时候直接改 position 会被它拽回去，先关掉再挪
        var cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        player.transform.position = dest.position;
        player.transform.rotation = Quaternion.Euler(0f, dest.eulerAngles.y, 0f);

        if (cc != null) cc.enabled = true;

        ClosePanel();
    }
}
