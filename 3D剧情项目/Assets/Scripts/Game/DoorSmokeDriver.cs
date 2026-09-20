// 门口传送的 Play 模式自检驱动（只在编辑器工具要求时才跑）。
//
// 为什么要有这个：编辑器脚本的 EditorApplication.update 的 tick 和游戏帧不是一回事，
// 用"等 N 个 tick"做等待会在游戏还没跑几帧时就断言，结果时好时坏。
// 放进 Play 模式的协程里、用 yield return null 等真游戏帧，时序就稳了。
//
// 用法：GameDoorBuilder.SmokeTest() 会把 Requested 置 true 并进 Play；
//       本类在场景加载后自动建一个临时节点跑完，把结果写进静态 Lines，然后 Finished = true；
//       编辑器那边轮询到 Finished 就写报告并退出 Play。
// 平时 Requested 是 false，什么都不做。
using System.Collections;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class DoorSmokeDriver : MonoBehaviour
{
    public static bool Requested;
    public static bool Finished;
    public static readonly List<string> Lines = new List<string>();
    public static readonly List<string> Errors = new List<string>();

    static bool _hooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (!Requested) return;
        var go = new GameObject("~DoorSmokeDriver");
        DontDestroyOnLoad(go);
        go.AddComponent<DoorSmokeDriver>();
    }

    void Awake()
    {
        if (!_hooked)
        {
            Application.logMessageReceived += OnLog;
            _hooked = true;
        }
        StartCoroutine(Run());
    }

    static void OnLog(string msg, string stack, LogType type)
    {
        if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
            Errors.Add(type + "：" + msg);
    }

    void Check(string what, bool ok) => Lines.Add((ok ? "  ✓ " : "  ★ ") + what);
    void Note(string s) => Lines.Add("    " + s);

    static void Teleport(FirstPersonController player, Vector3 pos)
    {
        var cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        player.transform.position = pos;
        if (cc != null) cc.enabled = true;
    }

    static Button FindButton(DoorTravelSystem sys, string locationId) => sys.GetDestinationButton(locationId);

    /// 站到门触发盒的正中（XZ 用盒中心、Y 保持玩家当前高度，不然会悬空/陷地）
    static Vector3 InsideDoor(DoorInteractable door, Vector3 keepY)
    {
        if (door != null && door.trigger != null)
        {
            var c = door.trigger.bounds.center;
            return new Vector3(c.x, keepY.y, c.z);
        }
        return door != null ? door.transform.position : keepY;
    }

    IEnumerator Run()
    {
        Finished = false;
        Lines.Clear(); Errors.Clear();

        // 等 Awake/Start 全部跑完
        for (int i = 0; i < 3; i++) yield return null;

        var sys = DoorTravelSystem.Instance;
        if (sys == null) { Note("★ 找不到 DoorTravelSystem.Instance"); Finish(); yield break; }
        var player = FirstPersonController.Instance;
        if (player == null) { Note("★ 找不到 FirstPersonController.Instance"); Finish(); yield break; }

        Note("进入 Play 后第 " + Time.frameCount + " 帧");

        Check("DoorTravelSystem 已就位", true);
        Check("玩家已就位", true);
        Check("门触发盒 " + sys.doors.Count(d => d != null) + " 个 / 去重后目标地点 " + sys.Destinations.Count + " 个",
            sys.doors.Count(d => d != null) >= 1 && sys.Destinations.Count >= 1);
        Check("UI 引用齐全（提示/面板/列表/关闭按钮）",
            sys.promptRoot != null && sys.panel != null && sys.listRoot != null && sys.closeButton != null);
        Check("场景里有 EventSystem（没有它按钮点不动）", Object.FindObjectOfType<EventSystem>() != null);
        Note("出生点" + (sys.promptRoot != null && sys.promptRoot.activeSelf ? "就在门口范围内 → 提示直接可见" : "不在门口 → 提示未显示"));

        // ---- 1) 走到房间中间，提示应该消失
        var room = GameObject.Find("GameRoot/Locations/Loc_教室");
        var door = sys.Destinations.FirstOrDefault(d => d != null && d.locationId == "Loc_教室");
        if (room == null || door == null) { Note("★ 找不到 Loc_教室 或它的门"); Finish(); yield break; }

        var from = player.transform.position;
        Teleport(player, room.transform.position);
        for (int i = 0; i < 4; i++) yield return null;
        {
            var pb = sys.ProbePoint();
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < sys.doors.Count; i++)
                if (sys.doors[i] != null)
                    sb.Append(sys.doors[i].locationId.Replace("Loc_", "") + (sys.doors[i].Contains(pb) ? "=在内 " : "=在外 "));
            Note("探针点（胸口）在房间中 (" + pb.x.ToString("0.00") + "," + pb.y.ToString("0.00") + "," + pb.z.ToString("0.00") + ")，各门判定：" + sb);
        }
        Check("离开门口 → 提示自动收起", sys.promptRoot == null || !sys.promptRoot.activeSelf);
        Check("离开门口 → 当前门变成 null", sys.CurrentDoor == null);

        // ---- 2) 走回门里，提示应该出现
        Teleport(player, InsideDoor(door, player.transform.position));
        for (int i = 0; i < 4; i++) yield return null;
        {
            var pb = sys.ProbePoint();
            string tb = door.trigger != null
                ? string.Format("盒中心({0:0.00},{1:0.00},{2:0.00}) 盒尺寸({3:0.00},{4:0.00},{5:0.00})",
                    door.trigger.bounds.center.x, door.trigger.bounds.center.y, door.trigger.bounds.center.z,
                    door.trigger.bounds.size.x, door.trigger.bounds.size.y, door.trigger.bounds.size.z)
                : "★ trigger 没接上";
            Note("传送目标门 = " + door.name + "（" + door.locationId + "）；探针点 (" +
                 pb.x.ToString("0.00") + "," + pb.y.ToString("0.00") + "," + pb.z.ToString("0.00") + ")；" + tb +
                 "；Contains=" + door.Contains(pb));
        }
        Check("走到「" + door.title + "」门内侧 → 弹出「按 F 开门」提示",
            sys.promptRoot != null && sys.promptRoot.activeSelf);
        Check("提示文字带地点名（\"" + (sys.promptLabel != null ? sys.promptLabel.text : "?") + "\"）",
            sys.promptLabel != null && sys.promptLabel.text.Contains(door.title));
        Check("当前门被识别为 教室", sys.CurrentDoor != null && sys.CurrentDoor.locationId == "Loc_教室");

        // ---- 3) 开面板（等价于按 F）
        sys.OpenPanel();
        yield return null;
        Check("按 F → 选择面板展开", sys.panel != null && sys.panel.IsOpen);
        Check("面板打开时玩家被锁住", player.locked);
        Check("面板打开时鼠标解锁（否则点不到按钮）", Cursor.lockState == CursorLockMode.None);
        {
            int shown = 0;
            for (int i = 0; i < sys.listRoot.childCount; i++)
                if (sys.listRoot.GetChild(i).gameObject.activeSelf) shown++;
            Check("目标按钮显示 " + shown + " 个（= 去重后的地点数 " + sys.Destinations.Count + "）", shown == sys.Destinations.Count);
        }
        {
            var hereBtn = FindButton(sys, "Loc_教室");
            Check("「当前所在地」按钮被禁用（不能传到自己）", hereBtn != null && !hereBtn.interactable);
        }

        // ---- 4) 选「图书馆」
        var target = sys.Destinations.FirstOrDefault(d => d != null && d.locationId == "Loc_图书馆");
        if (target == null) { Note("★ 没有 Loc_图书馆 的门（是不是没给它放触发盒？）"); Finish(); yield break; }
        var btn = FindButton(sys, "Loc_图书馆");
        if (btn == null) { Note("★ 列表里找不到图书馆按钮"); Finish(); yield break; }
        btn.onClick.Invoke();
        for (int i = 0; i < 4; i++) yield return null;
        {
            var arrive = target.arrivePoint;
            float dist = arrive != null ? Vector3.Distance(player.transform.position, arrive.position) : -1f;
            Check("点「图书馆」→ 玩家被传到落点（距离 " + (dist < 0f ? "？" : dist.ToString("0.00") + "m") + "）",
                dist >= 0f && dist < 0.3f);
            Check("落点高度正常（没掉进地下）", player.transform.position.y > -1f);
            Check("传送后离开原地点 > 100m", Vector3.Distance(player.transform.position, from) > 100f);
            Check("传送后面板自动收起", sys.panel == null || !sys.panel.IsOpen);
            Check("传送后玩家解锁", !player.locked);
        }

        Finish();
    }

    void Finish()
    {
        Lines.Add("");
        Lines.Add("（游戏帧数走到 " + Time.frameCount + "）");
        Finished = true;
    }
}
