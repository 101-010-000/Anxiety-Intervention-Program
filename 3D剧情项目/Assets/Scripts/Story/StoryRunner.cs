// 剧情总控：按 json 步骤驱动单章全流程（状态机 + 控制权限 + 推进输入）。
// 多章并存（第2-5章，2026-09-27）：场景 StorySystem 下每章一个 runner 子节点（chapterIndex 区分），
// 主菜单选章写 GameProgress.SelectedChapter → 只有匹配章的 runner 会 Begin，其余静默。
// UI 全部共享「UI交互」画布下同一套节点（对话/选择题/手机/WalkHint/结束卡/黑幕），多章零新建。
//
// 方案 v5（2026-09-27 用户定稿）：微信段「二选一」显示 ——
//   · （微信）台词：只落在手机聊天 UI（PhoneChatUI，居中放大），气泡即时报；对话框收着不出现，
//     打字机隐形跑维持节奏，onLineTyped 照常进间隙 → 点击推进；
//   · 旁白/独白：走用户的 Dialog 对话框，此时手机暂时收起；点完再遇微信台词 → 对话框让位、手机回屏。
//   · 干预面板：出现时手机也收起（选项与手机不同屏）；收档后照常回剧情（鼓励语选题时已借对话框读过）。
//   全部剧情 UI 挂用户手搭的「UI交互」画布下（没有独立剧情画布）。
//
// 控制权限（设计定稿）：
//   打字机播放中  = 锁视角 + 锁移动
//   间隙（一句说完）= 可转视角、不可移动（FirstPersonController.moveLocked）
//   开场旁白段    = 不锁移动，边走边听，定时/点击进下一句
//   走动段/F交互段 = 恢复移动（等触发盒）
//   干预面板       = 全锁 + 解锁鼠标（allowEscToUnlock 关掉，照抄门口面板的坑）
// 剧情期间 DoorTravelSystem 整个禁用，防止"按 F 开门"跟剧情交互打架。
// "说完话"的判定 = 该句打字机播完（DialogueUI.onLineTyped）→ 进入间隙。
// 打字中点击 = 先补全全句；再点才推进。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class StoryRunner : MonoBehaviour
{
    public static StoryRunner Instance;

    public const string END_HINT = "查看本章核心内容...请前往填写实验者发的问卷链接~";

    [Header("数据与引用（工具自动接）")]
    public TextAsset chapterJson;
    public DialogueUI dialogue;
    public ChoicePanel choicePanel;
    public GameObject promptRoot;          // "按 F 交谈"提示（挂在 UI_门口交互 下，样式照门口提示）
    public Text promptLabel;
    public GameObject walkHintRoot;        // 走动段 HUD 提示
    public Text walkHintLabel;
    public CanvasGroup chapterCardGroup;   // 结束卡
    public Text cardTitle;
    public Text cardSubtitle;
    public Image blackFade;                // 全屏黑幕
    public Transform startAnchor;          // 章节起点
    public PhoneChatUI phoneChat;          // 手机聊天 UI（微信段展示层；场景没接也能跑，全 null 保护）
    public Transform[] fadeAnchors;        // fade 步骤的传送锚点（元素名 = json 里 to 的值）

    [Header("行为")]
    public bool runOnStart = true;
    public int chapterIndex = 1;
    [Tooltip("自检用：结束卡不真跳回主菜单")]
    public bool debugStayInScene;

    public enum State
    {
        Idle, Card, Typing, Gap,
        NarFree, WaitInteract, WaitWalk, WaitDoor, Choice, Fade, Enter, EndCard, Done
    }

    public State CurrState { get; private set; }
    public int StepIndex { get; private set; }
    public int TotalSteps { get { return _ch != null ? _ch.steps.Count : 0; } }
    public bool Finished { get { return CurrState == State.Done; } }
    public StoryChapter Chapter { get { return _ch; } }

    StoryChapter _ch;
    FirstPersonController _player;
    DoorTravelSystem _doors;
    bool _savedEsc;
    bool _nodeOpen;
    bool _openingNar;        // 开场旁白段：不锁移动，边走边听（第一次遇到非旁白步骤即结束）
    float _gapTimer;
    float _pendingNextAt = -1f;
    StoryInteractable _currentF;
    StoryInteractable _currentTouch;
    int _choiceCounter;
    string _pendingThumb;   // 选择题全选时的抓屏文件名（存档缩略图；章末存档复用最后一张）
    readonly List<StoryStep> _resumeChat = new List<StoryStep>();   // 续播快进时收集的微信台词（FlushResumeChat 回放）
#if UNITY_EDITOR
    // ---- 调试「回退到上一句」（仅编辑器，正式构建不编译进来）----
    readonly List<int> _chatLog = new List<int>();   // 手机上每个聊天气泡对应的步骤索引（与屏幕内容同步）
    int _chatClearStep = -1;                          // 最近一次聊天流被清空（换段/重新亮屏）时的步骤索引 = 回退下限
#endif
    int _lastLineLen;
    Coroutine _cardRt;
    Coroutine _fadeRt;
    Coroutine _enterRt;
    Coroutine _leaveRt;
    Coroutine _walkHintRt;
    NpcEntrance _entrance;                                  // 当前入场演出（DebugAdvance 快进用）
    readonly Dictionary<string, GameObject> _entranceNpcs = new Dictionary<string, GameObject>();
    // leave 步骤的"回家位"：Begin 快照 enter/leave 角色的开场 pos/yaw（enter 会挪动她，退场走回这里）
    readonly Dictionary<string, Vector3> _npcHomePos = new Dictionary<string, Vector3>();
    readonly Dictionary<string, float> _npcHomeYaw = new Dictionary<string, float>();

    // 拿/放手机的 3D 动画钩子（第1章是逐字硬编码；多章后改关键词数组，旁白原文即可命中）：
    //   拿起 → animator TakePhone(Trigger) + Phone=true；放下（关键词或转入当面对话）→ Phone=false。
    //   微信台词本身也会自动补"拿手机"（防漏），所以这里只放剧本原文实际出现的说法。
    static readonly string[] PHONE_TAKE_KEYS = { "拿起手机", "拿过手机", "拿出手机", "把手机从桌角拿过来" };
    static readonly string[] PHONE_DOWN_KEYS = { "紧绷的肩膀也慢慢放松", "紧绷的肩膀彻底放松", "放下手机", "爬上了床" };

    void Awake() { Instance = this; }

    void Start()
    {
        GameSettings.EnsureLoaded();
        HideAuxUI();
        // 只在"从主菜单选了本章进游戏"时自动开跑（编辑器直接 Play 其它章节/场景不被劫持；
        // 想彻底不自动跑：取消勾选 剧情系统 上 StoryRunner 的 runOnStart）。
        // CurrState != Idle = 已被自检驱动 Begin 过 → 不重复开跑（防执行顺序导致的从头重放）
        if (runOnStart && chapterJson != null && CurrState == State.Idle)
        {
            if (GameProgress.SelectedChapter == chapterIndex) Begin();
            else Debug.Log("[StoryRunner] 跳过：当前选定第 " + GameProgress.SelectedChapter + " 章，本 runner 只管第 " + chapterIndex + " 章");
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
#if UNITY_EDITOR
        if (phoneChat != null) phoneChat.onCleared -= OnChatCleared;
#endif
    }

    // ------------------------------------------------------------------ 开始
    public void Begin()
    {
        _ch = StoryChapter.FromTextAsset(chapterJson);
        if (_ch == null || _ch.steps.Count == 0)
        {
            Debug.LogError("[StoryRunner] 章节数据为空：" + (chapterJson != null ? chapterJson.name : "json 未绑定"));
            CurrState = State.Done;
            return;
        }

        _player = FindObjectOfType<FirstPersonController>();
        if (_player == null) { Debug.LogError("[StoryRunner] 场景里没有 FirstPersonController"); CurrState = State.Done; return; }

        // 门系统抑制 + ESC 惯例保存
        _doors = DoorTravelSystem.Instance;
        if (_doors != null) _doors.enabled = false;
        _savedEsc = _player.allowEscToUnlock;
        _player.allowEscToUnlock = false;

        // ★ 先把黑幕拉满（下面会瞬移玩家）——开场本来就从黑淡入，先黑住就不会看到“玩家自己跳一下”
        //   ⚠ 场景里 BlackFade 可能被手动禁用了（踩过：它被禁用 → 黑幕全程不生效 →
        //     开场瞬移 + 镜头摆位全部露在画面里，看着就像“角色自己向右向前挪了几下”）。所以这里顺手启用。
        if (blackFade != null)
        {
            if (!blackFade.gameObject.activeSelf) blackFade.gameObject.SetActive(true);
            blackFade.canvasRenderer.SetAlpha(1f);
        }

        // 传送玩家到章节起点（CharacterController 关了再挪，不然会被拽回去）。
        // ★ 起点解析顺序（用户 2026-09-28 约定：自己手放的点优先）：
        //   ① 场景里名为「第N章起点」的物体（汉字章号，用户随手放、随手挪，无需接线）
        //   ② Inspector 里接线的 startAnchor（工具搭的 第N章_起点）
        //   ③ 场景里名为「第N章_起点」的物体（工具命名兜底）
        var spawn = FindSpawnAnchor();
        if (spawn != null)
        {
            var cc = _player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            _player.transform.position = spawn.position;
            _player.transform.rotation = Quaternion.Euler(0f, spawn.eulerAngles.y, 0f);
            if (cc != null) cc.enabled = true;
            Debug.Log("[StoryRunner] 第" + chapterIndex + "章起点 = " + spawn.name + " @ " + spawn.position.ToString("F1"));
            _player.ResetCameraNow();               // 镜头立刻跟到新位置（不然第一帧会甩一下）
        }

        // 接线本章剧情交互点（★ 初始一律 unarm：只有走到对应的等待步骤才允许触发）。
        // ★ 只接 chapterTag==本章 的点：多 runner 并存时（自检/选章），后 Begin 的 runner 若把
        //   全场景交互点都接到自己身上，会把别的章的交互点"抢走"——点燃后回调落在本 runner、
        //   状态机对不上 → 永远不推进（2026-09-27 自检第1/3/4/5章卡 WaitInteract 的根因）。
        foreach (var si in FindObjectsOfType<StoryInteractable>())
        {
            if (si.chapterTag != chapterIndex) continue;
            si.onTriggered = OnInteractableFired;
            si.armed = false;
        }

        _openingNar = true;
        StepIndex = 0;
        _choiceCounter = 0;
        ApplyChapterNpcVisibility();        // 角色容器按章显隐（第2章时宿舍里不该有第五章的舍友）
        PreHideDeferredChars();             // fade(showChars) 要用的容器先藏起来，黑屏落地时才点亮（第5章食堂）
        CollectAndHideEntranceNpcs();   // enter 步骤的角色开场先禁用（第2章陆宣雨：她不在宿舍）
        if (phoneChat != null) phoneChat.HideImmediate();   // 万一上次没收干净
#if UNITY_EDITOR
        _chatLog.Clear();               // 调试回退：聊天日志按屏幕内容重置
        _chatClearStep = -1;
        if (phoneChat != null) { phoneChat.onCleared -= OnChatCleared; phoneChat.onCleared += OnChatCleared; }
#endif

        // 章内续播（2026-09-28）：读档写入的 ResumeChapter/Step 在这里一次性消费——
        // 静默快进 0..resume-1（复刻世界副作用：传送/入场退场/交互消费/微信记录），再从 resume 步正常播。
        int resume = GameProgress.ResumeChapter == chapterIndex ? GameProgress.ResumeStep : 0;
        GameProgress.ClearResume();         // 无论是否命中都清掉，防残留泄漏到重开/别的章
        if (resume >= _ch.steps.Count) resume = 0;   // 章末档（step=总步数）：整章已通关 → 从头重播本章，
                                                      // 不钳到 end 步放"空壳结局"（用户反馈"一进去就跳游戏结束"，踩过）
        if (resume > 0)
        {
            Debug.Log("[StoryRunner] 续播第" + chapterIndex + "章：静默快进 0.." + (resume - 1) + "，从第 " + resume + " 步继续");
            for (int i = 0; i < resume; i++) SilentApply(_ch.steps[i]);
            StepIndex = resume;
            _openingNar = false;            // 续播点必在开场自由走动段之后（存档只在选择题/章末产生）
            // 先把「对话」节点激活（框仍收着）：微信台词的"隐形打字机"要在活节点上跑协程——
            // 正常游玩里开场旁白早把它激活了，静默快进跳过了这一步（续播落在微信段时报过
            // "Coroutine couldn't be started because '对话' is inactive" ×2，踩过）
            if (dialogue != null && !dialogue.gameObject.activeSelf)
            {
                dialogue.gameObject.SetActive(true);
                dialogue.HideNode();
            }
            FlushResumeChat(resume);        // 微信段历史回放（若续播点仍在微信段，手机带着记录亮屏）
            DoCard(null);                   // 黑幕淡入 → FadeInRoutine 末尾 Next() 正好执行续播步
            return;
        }
        Next();
    }

    // ------------------------------------------------------------------ 章内续播：静默快进
    // 只复刻"对后续剧情有影响的世界副作用"，跳过一切 UI 演出（对话打字/走位动画/黑屏/镜头转向）：
    //   fade/enter/leave/walk/interact = 传送与站位；interact 顺带消费交互点 + 隐藏联动道具（拿起手机）；
    //   choice = 题号计数对齐（选择记录在 PlayerPrefs，本来就持久）；微信台词进回放队列（FlushResumeChat）。
    // 拿/放手机动画不回放：续播后的微信台词/交互提示有"自动补拿"兜底（PlayText 里已处理）。
    // ⚠ StoryInteractable.Fire() 会触发 onTriggered → OnInteractableFired——它按 CurrState 分发，
    //   快进期间状态不是 WaitWalk/WaitInteract，天然 no-op，安全。
    void SilentApply(StoryStep step)
    {
        switch (step.t)
        {
            case "card":
            case "nar":
            case "dlg":
            case "mon":
            {
                bool wechat = step.t == "dlg" && !string.IsNullOrEmpty(step.s)
                              && (step.s.Contains("（微信）") || step.s.Contains("（通知）"));
                if (wechat) _resumeChat.Add(step);          // 微信历史排队；续播点仍在微信段时回放
                break;
            }

            case "choice":
                _choiceCounter++;                           // 题号对齐（交卷计数与缩略图命名都要连续）
                break;

            case "cut":
                CutawayCamera.Restore();                    // 快进不演切视角；兜底收掉（正常配对时本来就空）
                break;

            case "fade":
            {
                var target = FindFadeAnchor(step.to);
                if (target != null && _player != null) TeleportPlayer(target.position, target.eulerAngles.y);
                if (step.showChars) SetCharContainersAt(LocOf(target), true);   // 续播跨过这一幕时同样点亮（世界状态一致）
                break;
            }

            case "enter":
            {
                if (string.IsNullOrEmpty(step.who)) break;
                GameObject go = null;
                _entranceNpcs.TryGetValue(step.who, out go);
                if (go == null) { var t = FindCharacterTransform(step.who); go = t != null ? t.gameObject : null; }
                if (go == null) { Debug.LogWarning("[StoryRunner] 续播 enter 找不到角色：" + step.who); break; }
                Transform from = FindFadeAnchor(step.from);
                Transform toT  = FindFadeAnchor(step.to);
                Vector3 target;
                if (toT != null) target = toT.position;                          // 显式落点优先
                else if (from != null && _player != null)
                {
                    Vector3 p = _player.transform.position;                      // 缺省 = 玩家面前 1.3m（同 EnterRoutine 的算法）
                    Vector3 dir = p - from.position; dir.y = 0f;
                    target = dir.sqrMagnitude > 0.01f ? p - dir.normalized * 1.3f : p;
                }
                else if (_player != null) target = _player.transform.position;
                else break;
                if (!go.activeSelf) go.SetActive(true);
                go.transform.position = target;
                if (_player != null)                                               // 到位面向玩家（同 NpcEntrance 收尾）
                {
                    Vector3 f = _player.transform.position - target; f.y = 0f;
                    if (f.sqrMagnitude > 0.001f) go.transform.rotation = Quaternion.LookRotation(f.normalized);
                }
                break;
            }

            case "leave":
            {
                if (string.IsNullOrEmpty(step.who)) break;
                GameObject go = null;
                _entranceNpcs.TryGetValue(step.who, out go);
                if (go == null) { var t = FindCharacterTransform(step.who); go = t != null ? t.gameObject : null; }
                if (go == null) break;
                Transform toT = FindFadeAnchor(step.to);
                Vector3 target; float yaw;
                if (toT != null) { target = toT.position; yaw = toT.eulerAngles.y; }
                else if (_npcHomePos.TryGetValue(step.who, out target) && _npcHomeYaw.TryGetValue(step.who, out yaw)) { }
                else break;                                                       // 没落点信息，保持现状
                go.transform.position = target;
                if (step.hide) go.SetActive(false);                    // 续播同样处理「走出门即隐藏」
                else go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                break;
            }

            case "walk":
            case "interact":
            {
                var si = step.t == "walk"
                    ? FindFree(StoryInteractable.Mode.Touch)
                    : FindFree(StoryInteractable.Mode.InteractF, step.at);
                if (si == null) { Debug.LogWarning("[StoryRunner] 续播找不到交互点（" + step.t + " " + step.at + "）"); break; }
                si.Fire();                                // 消费 + 联动道具隐藏（拿起手机的可见状态）
                if (_player != null)
                {
                    Vector3 c = si.PromptCenter;          // 玩家站到交互点旁（y 沿用玩家脚高，锚点可能悬空/入地）
                    TeleportPlayer(new Vector3(c.x, _player.transform.position.y, c.z), _player.transform.eulerAngles.y);
                }
                break;
            }
        }
    }

    /// 续播点仍在微信段时回放聊天记录：先 Show（内部会 Clear）再补历史，随后 PlayText 往下追加当前句。
    void FlushResumeChat(int resume)
    {
        if (_resumeChat.Count == 0 || phoneChat == null) return;
        bool nextIsWechat = resume < _ch.steps.Count && _ch.steps[resume].t == "dlg"
                            && !string.IsNullOrEmpty(_ch.steps[resume].s)
                            && (_ch.steps[resume].s.Contains("（微信）") || _ch.steps[resume].s.Contains("（通知）"));
        if (!nextIsWechat) return;    // 此刻手机该收着：正常游玩里手机每次重新亮屏也会清空记录，行为一致
        phoneChat.Show();
        foreach (var st in _resumeChat)
        {
            if (!st.s.Contains("徐夏"))
                phoneChat.SetContact(st.s.Replace("（微信）", "").Replace("（通知）", ""));
            phoneChat.Append(st.s, st.x);
        }
        _resumeChat.Clear();
    }

    /// 传送玩家（照抄 FadeRoutine 的坑：CharacterController 不先关会被拽回去；镜头立刻跟过去）
    void TeleportPlayer(Vector3 pos, float yaw)
    {
        var cc = _player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        _player.transform.position = pos;
        _player.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        if (cc != null) cc.enabled = true;
        _player.ResetCameraNow();
    }

    // ------------------------------------------------------------------ 角色容器按章显隐 + NPC 入场
    // 「第X章角色」容器名兼容两种写法：汉字（第一章角色/第三章角色）与数字（第3章角色）。
    // 场景里同一 Loc 可能摆多章容器（宿舍有 第二章角色 + 第五章角色），不按章过滤会互相穿帮
    // （踩过：第2章开场宿舍里站着第五章的陆宣雨+舍友A/B）。
    static readonly System.Text.RegularExpressions.Regex ChapterContainerRx =
        new System.Text.RegularExpressions.Regex(@"^第([0-9一二三四五])章角色$");

    static int ParseChapterNum(string s)
    {
        if (s.Length != 1) return -1;
        switch (s[0])
        {
            case '一': return 1;
            case '二': return 2;
            case '三': return 3;
            case '四': return 4;
            case '五': return 5;
            default: return s[0] >= '0' && s[0] <= '9' ? s[0] - '0' : -1;
        }
    }

    void ApplyChapterNpcVisibility()
    {
        int shown = 0, hidden = 0;
        foreach (var t in FindObjectsOfType<Transform>(true))
        {
            var m = ChapterContainerRx.Match(t.name);
            if (!m.Success) continue;
            bool want = ParseChapterNum(m.Groups[1].Value) == chapterIndex;
            if (t.gameObject.activeSelf == want) continue;
            t.gameObject.SetActive(want);
            if (want) shown++; else hidden++;
        }
        Debug.Log("[StoryRunner] 第" + chapterIndex + "章：角色容器显隐 → 显示 " + shown + " / 隐藏 " + hidden +
                  "（其余容器状态本就正确）");
    }

    // fade 步骤的 "showChars": true —— 落地时点亮【落点所在 Loc】下的本章角色容器。
    // 场景：Loc_食堂/第五章角色（陆宣雨+舍友A/B 坐着）只在第5章到食堂那幕出现；
    // ApplyChapterNpcVisibility 是"全地点显示本章"，覆盖不了"剧中才出现" → Begin 先预藏、
    // 黑屏期间点亮（FadeRoutine 里已在全黑状态），淡出时人已在座，不穿帮。
    // 续播快进（SilentApply）走同一个点亮口；宿舍/图书馆的第五章容器不带此标记，不受影响。
    void PreHideDeferredChars()
    {
        foreach (var step in _ch.steps)
        {
            if (step.t != "fade" || !step.showChars) continue;
            var target = FindFadeAnchor(step.to);
            var loc = LocOf(target);
            if (loc != null) SetCharContainersAt(loc, false);
            else Debug.LogWarning("[StoryRunner] showChars fade 找不到锚点「" + step.to + "」，其角色容器不预藏（剧情落地时也点不亮）");
        }
    }

    static Transform LocOf(Transform t)
    {
        for (var p = t; p != null; p = p.parent)
            if (p.name.StartsWith("Loc_")) return p;
        return null;
    }

    void SetCharContainersAt(Transform loc, bool on)
    {
        if (loc == null) return;
        int n = 0;
        foreach (var t in loc.GetComponentsInChildren<Transform>(true))    // true：容器可能是隐藏的
        {
            var m = ChapterContainerRx.Match(t.name);
            if (!m.Success || ParseChapterNum(m.Groups[1].Value) != chapterIndex) continue;
            if (t.gameObject.activeSelf == on) continue;
            t.gameObject.SetActive(on);
            n++;
        }
        Debug.Log("[StoryRunner] " + (on ? "点亮" : "预藏") + " " + loc.name + " 下本章角色容器 ×" + n);
    }

    // json 是唯一事实源：Begin 时扫本章所有 enter/leave 步骤的 who → 记引用（FindObjectsOfType
    // 找不到禁用对象，所以必须先存）+ 快照【开场原位】（leave 退场走回这里）。enter 的 who 预禁用
    // （开场不在场）；leave-only 的角色开场在场，只快照不禁用（2026-09-28）。
    void CollectAndHideEntranceNpcs()
    {
        _entranceNpcs.Clear();
        _npcHomePos.Clear();
        _npcHomeYaw.Clear();
        foreach (var step in _ch.steps)
        {
            if ((step.t != "enter" && step.t != "leave") || string.IsNullOrEmpty(step.who)
                || _entranceNpcs.ContainsKey(step.who)) continue;
            var t = FindCharacterTransform(step.who);
            if (t == null) { Debug.LogWarning("[StoryRunner] enter/leave 角色没找到：" + step.who + "（步骤触发时会再找一次）"); continue; }
            _entranceNpcs[step.who] = t.gameObject;
            _npcHomePos[step.who] = t.position;                 // 此刻必是场景手摆原位（enter 还没挪过她）
            _npcHomeYaw[step.who] = t.eulerAngles.y;
            if (step.t == "enter" && t.gameObject.activeSelf) t.gameObject.SetActive(false);
        }
    }

    /// 按名找角色实例。★ 同名实例可能摆在多章容器下（宿舍有第二/第五章两个 陆宣雨_可动）：
    /// 优先返回【本章容器】子树里的那个（第二章 enter 的是第二章的陆宣雨，不是第五章的），
    /// 找不到本章的才退回第一个同名实例。
    Transform FindCharacterTransform(string name)
    {
        Transform fallback = null;
        foreach (var t in FindObjectsOfType<Transform>(true))     // true：含禁用对象
        {
            if (t.name != name) continue;
            if (fallback == null) fallback = t;
            for (var p = t.parent; p != null; p = p.parent)
            {
                var m = ChapterContainerRx.Match(p.name);
                if (m.Success && ParseChapterNum(m.Groups[1].Value) == chapterIndex) return t;
            }
        }
        return fallback;
    }

    // 章节起点解析（用户手放的「第N章起点」汉字命名优先；详见 Begin 里的注释）
    static readonly string[] CN_NUM = { "", "一", "二", "三", "四", "五" };

    Transform FindSpawnAnchor()
    {
        string cn = chapterIndex >= 1 && chapterIndex <= 5 ? CN_NUM[chapterIndex] : chapterIndex.ToString();
        Transform t = FindCharacterTransform("第" + cn + "章起点");        // ① 用户手放（汉字章号）
        if (t != null) return t;
        if (startAnchor != null) return startAnchor;                      // ② 工具接线
        return FindCharacterTransform("第" + chapterIndex + "章_起点");    // ③ 工具命名兜底
    }

    // ------------------------------------------------------------------ 步骤推进
    public void Next()
    {
        _pendingNextAt = -1f;
        if (DialogueVoicePlayer.Instance != null) DialogueVoicePlayer.Instance.Stop();   // 推进即切上一句语音（补全不切）
        if (_ch == null || StepIndex >= _ch.steps.Count) { ToDone(); return; }
        var step = _ch.steps[StepIndex++];
        bool isText = step.t == "nar" || step.t == "dlg" || step.t == "mon";
        // 开场旁白段结束（card 不算：每章都以 card 开头，它不能把"自由走动听旁白"提前掐掉）
        if (_openingNar && step.t != "nar" && step.t != "card") _openingNar = false;

        // 3D 文本节点：进入非文本步骤时收框
        if (!isText && _nodeOpen) { dialogue.HideNode(); _nodeOpen = false; }

        switch (step.t)
        {
            case "card": DoCard(step); break;
            case "end": DoEnd(); break;
            case "choice": DoChoice(step); break;
            case "fade": DoFade(step); break;
            case "door": DoDoor(step); break;
            case "enter": DoEnter(step); break;
            case "leave": DoLeave(step); break;
            case "cut": DoCut(step); break;

            case "walk":
                _currentTouch = FindFree(StoryInteractable.Mode.Touch);
                if (_currentTouch == null) { Debug.LogWarning("[StoryRunner] 没有 Touch 触发盒，跳过走动段"); Next(); return; }
                _currentTouch.armed = true;
                ShowWalkHint(step.x);
                SetPerms(State.WaitWalk);
                break;

            case "interact":
                _currentF = FindFree(StoryInteractable.Mode.InteractF, step.at);
                if (_currentF == null) { Debug.LogWarning("[StoryRunner] 没有 F 交互点，跳过"); Next(); return; }
                _currentF.armed = true;
                _currentF.ShowProp();   // 联动道具（手机）在交互步骤到达时先回到桌上（第2章第二次拿手机）
                ShowWalkHint(step.x);
                SetPerms(State.WaitInteract);
                break;

            default: PlayText(step); break;      // nar / dlg / mon
        }
    }

    void PlayText(StoryStep step)
    {
        // 台词语音（2026-09-29）：一句开始就播（缺片/自检/快进时内部静默跳过）；
        // 微信台词也播（发送方声线念出来，像语音消息）；班群（通知）生成期就没做片 = 静默。
        DialogueVoicePlayer.Ensure().PlayStep(chapterIndex, StepIndex - 1);

        // （微信）/（通知）台词 = 手机聊天段（第2-5章扩展：班群通知也走手机 UI）
        bool wechat = step.t == "dlg" && !string.IsNullOrEmpty(step.s)
                      && (step.s.Contains("（微信）") || step.s.Contains("（通知）"));

        // 旁白关键词 → 3D 拿/放手机动画（第1章逐字判定的多章通用版）
        if (!string.IsNullOrEmpty(step.x) && step.t == "nar" && _player != null && _player.animator != null)
        {
            if (HitsAny(step.x, PHONE_TAKE_KEYS) && !_player.animator.GetBool("Phone"))
            { _player.animator.SetTrigger("TakePhone"); _player.animator.SetBool("Phone", true); }
            else if (HitsAny(step.x, PHONE_DOWN_KEYS))
                _player.animator.SetBool("Phone", false);
        }
        // 转入当面对话（非微信的 dlg）= 手机收起来（第2章陆宣雨/第4章林溪进场都靠它）
        if (step.t == "dlg" && !wechat && _player != null && _player.animator != null
            && _player.animator.GetBool("Phone"))
            _player.animator.SetBool("Phone", false);

        // ★ 微信段定稿（用户 2026-09-27）：（微信）台词只落在手机聊天 UI，不进对话框；
        //   旁白/独白走对话框，此时手机暂时收起；点完再遇微信台词 → 对话框让位、手机回到屏幕。
        if (wechat)
        {
            if (_nodeOpen && dialogue != null) { dialogue.HideNode(); _nodeOpen = false; }
            if (phoneChat != null)
            {
                // 收到方决定聊天对象（徐夏发出不换段）：换段自动清空旧聊天、换标题
                if (!step.s.Contains("徐夏"))
                    phoneChat.SetContact(step.s.Replace("（微信）", "").Replace("（通知）", ""));
                if (!phoneChat.IsShown) phoneChat.Show();
            }
            // 微信台词出现 = 徐夏在看手机：动画兜底补拿（nar 关键词没命中也不穿帮）
            if (_player != null && _player.animator != null && !_player.animator.GetBool("Phone"))
            { _player.animator.SetTrigger("TakePhone"); _player.animator.SetBool("Phone", true); }

            SetPerms(State.Typing);
            _lastLineLen = step.x != null ? step.x.Length : 0;
            if (dialogue != null)
            {
                dialogue.PlayLine(step);     // 打字机照跑（框收着，玩家看不见），维持节奏与 onLineTyped
                dialogue.HideNode();         // 框与压暗层都不出现（PlayLine 会按 dlg 开遮罩，这里立刻关掉）
            }
            if (phoneChat != null) phoneChat.Append(step.s, step.x);   // 气泡立即落进聊天流
#if UNITY_EDITOR
            if (phoneChat != null) _chatLog.Add(StepIndex - 1);         // 调试回退：记录这条气泡对应的步骤
#endif
            return;
        }

        if (phoneChat != null && phoneChat.IsShown) phoneChat.Hide();   // 旁白/独白：手机让位给对话框

        // 开场旁白段：照常走对话框打字机，但不锁人——边走边听，到点自动下一句
        if (_openingNar && step.t == "nar")
        {
            if (!_nodeOpen && dialogue != null) { dialogue.ShowNode(); _nodeOpen = true; }
            _lastLineLen = step.x != null ? step.x.Length : 0;
            _gapTimer = 0f;
            dialogue.PlayLine(step);
            SetPerms(State.NarFree);
            return;
        }

        if (!_nodeOpen && dialogue != null) { dialogue.ShowNode(); _nodeOpen = true; }
        SetPerms(State.Typing);
        _lastLineLen = step.x != null ? step.x.Length : 0;
        dialogue.PlayLine(step);
    }

    // ------------------------------------------------------------------ 入场 / 结束卡
    // 用户反馈定稿：进场不出"第一章"标题黑屏，直接从黑淡入进场景。
    // "card" 步骤现在只做一件事：黑幕 1→0 淡出后立刻进入下一步。
    void DoCard(StoryStep step)
    {
        if (_cardRt != null) StopCoroutine(_cardRt);
        _cardRt = StartCoroutine(FadeInRoutine());
    }

    IEnumerator FadeInRoutine()
    {
        CurrState = State.Card;
        SetPerms(State.Card);
        if (blackFade != null)
        {
            // ★ 先黑一帧：保证"开场黑幕"一定被看到一次
            blackFade.canvasRenderer.SetAlpha(1f);
            yield return null;
            float t = 0f;
            while (t < 1f)
            {
                // ★ 每帧最多推进 1/30 秒：开局首帧经常很慢（域重载/场景加载/着色器预热），
                //   用真实 dt 会让整个淡入在一两帧里被跳过去 —— 那就“看不见黑幕”了（踩过）。
                t += Mathf.Min(Time.unscaledDeltaTime, 1f / 30f) / 0.8f;
                blackFade.canvasRenderer.SetAlpha(1f - Mathf.Clamp01(t));
                yield return null;
            }
            blackFade.canvasRenderer.SetAlpha(0f);
        }
        Next();
    }

    void DoEnd()
    {
        if (_cardRt != null) StopCoroutine(_cardRt);
        _cardRt = StartCoroutine(EndRoutine());
    }

    IEnumerator EndRoutine()
    {
        CurrState = State.EndCard;
        SetPerms(State.EndCard);
        if (blackFade != null)
        {
            float t = 0f;
            while (t < 1f)
            {
                t += Mathf.Min(Time.unscaledDeltaTime, 1f / 30f) / 0.5f;   // 同上：卡帧也不会把淡出跳过去
                blackFade.canvasRenderer.SetAlpha(Mathf.Clamp01(t));
                yield return null;
            }
        }
        if (cardTitle != null) cardTitle.text = (_ch != null ? _ch.chapter : "第一章") + "  完";
        if (cardSubtitle != null)
        {
            cardSubtitle.text = END_HINT;
            cardSubtitle.gameObject.SetActive(true);
        }
        if (chapterCardGroup != null)
        {
            // ★ 章末卡必须压在 BlackFade 之上（2026-09-28 修复"章末只有黑屏"）：BlackFade 是全 UI
            //   最上层的设计约定（转场黑幕）不能动，而章节卡排在它下面——上面的淡黑停在全黑后，
            //   卡片淡入得再好也被纯黑盖住。运行时置顶解决，不改场景文件；随后即转主菜单，无需还原。
            chapterCardGroup.transform.SetAsLastSibling();
            chapterCardGroup.gameObject.SetActive(true);
            float t = 0f;
            while (t < 1f) { t += Time.unscaledDeltaTime / 0.6f; chapterCardGroup.alpha = Mathf.Clamp01(t); yield return null; }
            chapterCardGroup.alpha = 1f;
        }
    }

    // ------------------------------------------------------------------ 切视角（cut 步骤，2026-09-29）
    // {"t":"cut","who":"李老师_可动"} = 镜头切成 TA 的第三人称跟拍视角（CutawayCamera），
    // 主角原地不动、输入暂停（对话本就锁行走）；{"t":"cut"}（who 空）= 切回主角相机。
    // cut 是瞬时状态翻转，紧跟的 dlg/nar 承担时长；进出场都靠台词节奏，无需黑幕。
    void DoCut(StoryStep step)
    {
        if (string.IsNullOrEmpty(step.who))
        {
            CutawayCamera.Restore();
            if (_player != null) _player.enabled = true;
            Next();
            return;
        }
        var t = FindCharacterTransform(step.who);
        if (t == null) { Debug.LogWarning("[StoryRunner] cut 找不到角色「" + step.who + "」，跳过切视角"); Next(); return; }
        if (_player != null) _player.enabled = false;    // 停输入 + 停相机控制（组件停用，方法调用不受影响）
        CutawayCamera.Show(t);
        Next();
    }

    void ToDone()
    {
        CurrState = State.Done;
        CutawayCamera.Restore();                          // 章末兜底：万一 cut 没配对收掉
        if (_player != null) { _player.enabled = true; _player.allowEscToUnlock = _savedEsc; _player.SetLocked(false); _player.moveLocked = false; }
        // 剧情跑完 → 把门口传送还给玩家（Begin 里整体关掉了，不恢复的话出了剧情也开不了门）
        if (_doors != null) { _doors.ExitStoryMode(); _doors.enabled = true; }
    }

    // ------------------------------------------------------------------ 黑屏转场（第2-5章"黑屏/刷新"跳转）
    // fade：黑幕淡入 → 传送玩家到 to 同名锚点 → 淡出 → 继续。
    // BlackFade 在 UI 最上层，黑屏期间对话框不可见 → 时间流逝旁白用独立的 nar 步骤（放 fade 前后皆可）。
    void DoFade(StoryStep step)
    {
        if (_fadeRt != null) StopCoroutine(_fadeRt);
        _fadeRt = StartCoroutine(FadeRoutine(step));
    }

    IEnumerator FadeRoutine(StoryStep step)
    {
        CurrState = State.Fade;
        SetPerms(State.Fade);                          // 全锁（同 Card）
        if (phoneChat != null && phoneChat.IsShown) phoneChat.HideImmediate();   // 转场手机不挂屏

        if (blackFade != null)
        {
            float t = 0f;
            while (t < 1f) { t += Mathf.Min(Time.unscaledDeltaTime, 1f / 30f) / 0.4f; blackFade.canvasRenderer.SetAlpha(Mathf.Clamp01(t)); yield return null; }
            blackFade.canvasRenderer.SetAlpha(1f);
        }

        Transform target = FindFadeAnchor(step.to);
        if (target != null && _player != null)
        {
            var cc = _player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;        // 照抄门口传送的坑：不关会被拽回去
            _player.transform.position = target.position;
            _player.transform.rotation = Quaternion.Euler(0f, target.eulerAngles.y, 0f);
            if (cc != null) cc.enabled = true;
            _player.ResetCameraNow();        // 第三人称：镜头立刻跟到新位置（此时屏幕是黑的）
        }
        else if (!string.IsNullOrEmpty(step.to))
            Debug.LogWarning("[StoryRunner] fade 找不到锚点「" + step.to + "」——检查 fadeAnchors 接线 / 锚点命名");

        if (step.showChars) SetCharContainersAt(LocOf(target), true);   // 黑屏期间点亮本幕角色（PreHideDeferredChars 预藏的）

        yield return null;                             // 让传送落地一帧再开淡出

        if (blackFade != null)
        {
            float t = 0f;
            while (t < 1f) { t += Mathf.Min(Time.unscaledDeltaTime, 1f / 30f) / 0.4f; blackFade.canvasRenderer.SetAlpha(1f - Mathf.Clamp01(t)); yield return null; }
            blackFade.canvasRenderer.SetAlpha(0f);
        }
        _fadeRt = null;
        Next();
    }

    // ------------------------------------------------------------------ 门口传送引导（第4章「走到门口，按 F 去图书馆」）
    // json：{ "t":"door", "to":"Loc_图书馆", "anchor":"第4章_图书馆座位", "x":"走到门口，按 F 前往图书馆" }
    // 与 fade 的区别：fade 是剧情自己把玩家瞬移过去（用户 2026-09-29 反馈「场景不应该自己直接跳转」）；
    // door 把玩家放回自己手里 —— 亮起目标卡 → 玩家走到任意一个门 → 按 F → 面板里只有目标地点可点
    // → 选中即传送（落点=anchor 剧情锚点）→ 回调本类 → 关掉门系统 → 继续剧情。
    // ★ 门系统在 Begin 里是整体关掉的（防跟剧情交互打架），只有 door 步骤临时打开；
    //   找不到门系统 / 没给 to 时退回旧行为（直接传送到 anchor），不让流程卡死。
    void DoDoor(StoryStep step)
    {
        var doors = DoorTravelSystem.Instance;
        var anchor = FindFadeAnchor(step.anchor);

        if (doors == null || string.IsNullOrEmpty(step.to) || !doors.HasDestination(step.to))
        {
            Debug.LogWarning("[StoryRunner] door 步骤缺门系统 / 没给 to / 门口列表里没有「" + step.to +
                             "」—— 退回直接传送" +
                             (anchor != null ? "（" + anchor.name + "）" : "（且没接 anchor！）"));
            if (anchor != null && _player != null)
            {
                var cc = _player.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                _player.transform.position = anchor.position;
                _player.transform.rotation = Quaternion.Euler(0f, anchor.eulerAngles.y, 0f);
                if (cc != null) cc.enabled = true;
                _player.ResetCameraNow();
            }
            Next();
            return;
        }

        _doors = doors;
        doors.EnterStoryMode(step.to, anchor, OnDoorArrived);
        Debug.Log("[StoryRunner] 第" + chapterIndex + "章：门口传送导流 → " + step.to +
                  "（落点 " + (anchor != null ? anchor.name : "门自己的落点") + "）");
        ShowWalkHint(step.x);
        SetPerms(State.WaitDoor);
    }

    /// 门口传送系统到位回调（DoorTravelSystem.TravelTo → 此处）
    void OnDoorArrived(string locationId)
    {
        if (CurrState != State.WaitDoor) return;
        if (_doors != null) { _doors.ExitStoryMode(); _doors.enabled = false; }   // 剧情期间门系统整体关掉（照 Begin 的惯例）
        HideWalkHint();
        Next();
    }

    Transform FindFadeAnchor(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (fadeAnchors != null)
            foreach (var a in fadeAnchors)
                if (a != null && a.name == name) return a;
        // 接线池里没有 → 全场景按名找（用户手放/挪动的同名锚点直接生效，同「第N章起点」约定：
        // 想让陆宣雨从真正的 门(1) 进来，把「第2章_陆宣雨门口」拖到门前即可，无需重跑工具）
        return FindCharacterTransform(name);
    }

    // ------------------------------------------------------------------ NPC 入场演出（enter 步骤，2026-09-27）
    // who 从预禁用表启用 → 从 from 锚点走到玩家面前（缺省落点；to 显式锚点可覆盖）
    // → 面向玩家 → 接对话。全程真实形象（虚实渐变已移除，2026-09-28）。细节见 NpcEntrance。
    void DoEnter(StoryStep step)
    {
        if (_enterRt != null) StopCoroutine(_enterRt);
        _enterRt = StartCoroutine(EnterRoutine(step));
    }

    IEnumerator EnterRoutine(StoryStep step)
    {
        CurrState = State.Enter;
        SetPerms(State.Enter);                          // 全锁（同 Fade）

        GameObject go = null;
        _entranceNpcs.TryGetValue(step.who, out go);
        if (go == null)
        {
            var t = FindCharacterTransform(step.who);   // 没预禁用过（如 json 后补的 enter）也能兜底
            go = t != null ? t.gameObject : null;
        }
        if (go == null)
        {
            Debug.LogWarning("[StoryRunner] enter 找不到角色「" + step.who + "」——跳过入场");
            Next(); yield break;
        }

        Transform from = FindFadeAnchor(step.from);
        if (from == null)
        {
            Debug.LogWarning("[StoryRunner] enter 找不到起点锚点「" + step.from + "」——检查锚点接线/命名");
            if (!go.activeSelf) go.SetActive(true);
            Next(); yield break;
        }

        // 落点：to 显式锚点优先；缺省 = 玩家面前 1.3m（沿"起点→玩家"来向退 1.3m，用户 2026-09-27 定：
        // 玩家此刻在交互点半径内但位置不精确，不能写死落点）
        Vector3 target;
        Transform toT = FindFadeAnchor(step.to);
        if (toT != null) target = toT.position;
        else if (_player != null)
        {
            Vector3 p = _player.transform.position;
            Vector3 dir = p - from.position; dir.y = 0f;
            target = dir.sqrMagnitude > 0.01f ? p - dir.normalized * 1.3f : p;
        }
        else target = from.position;

        if (!go.activeSelf) go.SetActive(true);

        // ★ 镜头平滑转向门口（v4）： yaw+pitch 一起动 0.3s（此前硬切且不管 pitch，
        //   玩家低头看桌面时进场会盯着自己的脚——视频评审 2026-09-28）。与她淡入同步。
        if (_player != null)
            StartCoroutine(_player.LookTowardRoutine(from.position));

        _entrance = go.GetComponent<NpcEntrance>();
        if (_entrance == null) _entrance = go.AddComponent<NpcEntrance>();
        // via 途经点：每项按 fade 锚点同名解析，缺锚点只警告并跳过该点；via 为空/全缺 = 原两点直线
        var pts = BuildPath(from.position, step.via, target);
        yield return _entrance.RunPath(pts, _player != null ? _player.transform : null);

        _entrance = null;
        _enterRt = null;
        Next();
    }

    // ------------------------------------------------------------------ NPC 退场（leave 步骤，2026-09-28）
    // 与 enter 对称：who 从当前位置走回【开场原位】（Begin 快照；to 显式锚点可覆盖，同名解析同 fade），
    // 到后面向原朝向、保持在场待机。第2章陆宣雨对话完"回到自己的座位上"，不再站桩在玩家旁边。
    // 状态复用 State.Enter（演出语义一致：能转不能走）；_entrance 字段同步指向退场演出，自检快进可用。
    void DoLeave(StoryStep step)
    {
        if (_leaveRt != null) StopCoroutine(_leaveRt);
        _leaveRt = StartCoroutine(LeaveRoutine(step));
    }

    IEnumerator LeaveRoutine(StoryStep step)
    {
        CurrState = State.Enter;
        SetPerms(State.Enter);                          // 同入场演出：能转不能走

        GameObject go = null;
        _entranceNpcs.TryGetValue(step.who, out go);
        if (go == null)
        {
            var t = FindCharacterTransform(step.who);   // 没进过预记录表（json 后补的 leave）也能兜底
            go = t != null ? t.gameObject : null;
        }
        if (go == null || !go.activeSelf)
        {
            Debug.LogWarning("[StoryRunner] leave 找不到在场的角色「" + step.who + "」——跳过退场");
            Next(); yield break;
        }

        Vector3 target;
        float yaw;
        Transform toT = FindFadeAnchor(step.to);
        if (toT != null) { target = toT.position; yaw = toT.eulerAngles.y; }
        else if (_npcHomePos.TryGetValue(step.who, out target) && _npcHomeYaw.TryGetValue(step.who, out yaw))
        {
            // 走回 Begin 快照的开场原位（enter 把她挪到了玩家旁边，"自己的座位"= 场景手摆原位）
        }
        else
        {
            Debug.LogWarning("[StoryRunner] leave 没有「" + step.who + "」的原位快照、也没给 to 锚点——跳过退场");
            Next(); yield break;
        }

        _entrance = go.GetComponent<NpcEntrance>();
        if (_entrance == null) _entrance = go.AddComponent<NpcEntrance>();
        Vector3 from = go.transform.position;
        // via 途经点（绕开桌椅）：每项按 fade 锚点同名解析，缺锚点只警告并跳过该点；
        // via 为空/全缺时 pts 就两点 = 原直线，行为不变
        var pts = BuildPath(from, step.via, target);
        yield return _entrance.RunPath(pts, null); // faceTarget=null：保持走向（面朝座位方向走回去）

        if (step.hide)
        {
            // 「走出门」：到位直接整棵隐藏——不转身、不在门口待机（用户 2026-09-29：第4章林溪与玩家一起去图书馆）
            go.SetActive(false);
            _entrance = null;
            _leaveRt = null;
            Next();
            yield break;
        }
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);   // 到位回原朝向（如面朝书桌）
        _entrance = null;
        _leaveRt = null;
        Next();
    }

    /// 组装 enter/leave 的途经点列：起点 + 各 via 锚点（同名解析同 fade 锚点，缺锚点只警告跳过）+ 终点；
    /// via 为空/全缺时返回 [起点, 终点] 两点 = 原直线行为（v7，配合 NpcEntrance.RunPath 分段走位）
    Vector3[] BuildPath(Vector3 start, List<string> via, Vector3 end)
    {
        var list = new List<Vector3> { start };
        if (via != null)
            foreach (var n in via)
            {
                var a = FindFadeAnchor(n);
                if (a != null) list.Add(a.position);
                else Debug.LogWarning("[StoryRunner] via 途经锚点「" + n + "」找不到——跳过该点（此段走直线）");
            }
        list.Add(end);
        return list.ToArray();
    }

    static bool HitsAny(string text, string[] keys)
    {
        if (keys == null) return false;
        foreach (var k in keys) if (!string.IsNullOrEmpty(k) && text.Contains(k)) return true;
        return false;
    }

    void ToMainMenu()
    {
        GameProgress.MarkCompleted(chapterIndex);
        AutoSave("章节通关", force: true, done: true);   // 章末兜底存档不受"选择后自动存档"开关控制；chapterDone=通关档标记
        ToDone();
        if (debugStayInScene) return;
        SceneManager.LoadScene("MainMenu");
    }

    // ------------------------------------------------------------------ 自动存档（2026-09-27 接线；2026-09-28 改每章一档 + 章内续播）
    // 时机：每道干预题交卷后（受设置"选择后自动存档"开关控制）+ 章节通关（始终存，进度兜底）。
    // 槽位：★每章一档——第 N 章写 N 号槽（重玩本章只覆盖本章自己的档，章与章互不冲掉；槽 6 留空）。
    // step 语义 = "下一个待执行步骤号"（Next() 先自增再执行，交卷时 StepIndex 已指向题目后一步）；
    // 读档时经 GameProgress.SetResume 带进 Game 场景 → Begin 静默快进到该步 = 章内续播。
    // 缩略图：选择题全选完毕、面板完整显示的那一刻抓屏；本章没抓到新图时保留槽里旧图。
    // 自检（StorySmokeDriver.Requested / StoryResumeDriver.Active）不写档，防污染真实存档。
    void AutoSave(string reason, bool force = false, bool done = false)
    {
        if (!force && !GameSettings.AutoSave) return;
        if (StorySmokeDriver.Requested || StoryResumeDriver.Active) return;   // ★只看"自检真的在跑"标志；场景里常驻的驱动组件不代表在自检（FindObjectOfType 会把真实游玩的存档也挡掉，踩过）
        int slot = Mathf.Clamp(chapterIndex, 1, SaveSystem.SlotCount) - 1;    // 第1章→槽1 … 第5章→槽5

        var old = SaveSystem.Info(slot);
        string oldThumb = old != null && old.data != null ? old.data.thumbnail : "";
        string newThumb = string.IsNullOrEmpty(_pendingThumb) ? oldThumb : _pendingThumb;   // 本章还没抓到新图 → 沿用旧图
        if (!string.IsNullOrEmpty(oldThumb) && oldThumb != newThumb)
        {
            try { System.IO.File.Delete(System.IO.Path.Combine(SaveSystem.Dir, oldThumb)); } catch { }
        }

        var d = new SaveData
        {
            chapter     = chapterIndex,
            step        = StepIndex,
            nodeId      = "step" + StepIndex,
            thumbnail   = newThumb,
            chapterDone = done,
        };
        SaveSystem.Write(slot, d);
        Debug.Log("[StoryRunner] 自动存档（" + reason + "）→ 槽 " + (slot + 1) + " · 第" + chapterIndex + "章 step " + StepIndex +
                  (string.IsNullOrEmpty(newThumb) ? "" : " · 缩略图 " + newThumb));
    }

    /// 选择题缩略图抓屏（ChoicePanel.onPanelComplete → 全选且面板完整显示的那一刻）
    void CaptureChoiceThumb()
    {
        if (StorySmokeDriver.Requested) return;
        string name = "ch" + chapterIndex + "_c" + _choiceCounter + "_" +
                      System.DateTime.Now.ToString("yyyyMMddHHmmssfff") + ".png";
        StartCoroutine(CaptureThumbCo(System.IO.Path.Combine(SaveSystem.Dir, name), name));
    }

    IEnumerator CaptureThumbCo(string absPath, string fileName)
    {
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(absPath);          // 帧末呈现时落盘（异步写文件）
        // 换新图前删掉上一题的临时截图（还没被存档引用过的）
        if (!string.IsNullOrEmpty(_pendingThumb) && _pendingThumb != fileName)
        {
            try { System.IO.File.Delete(System.IO.Path.Combine(SaveSystem.Dir, _pendingThumb)); } catch { }
        }
        _pendingThumb = fileName;
    }

    // ------------------------------------------------------------------ 干预题
    void DoChoice(StoryStep step)
    {
        int idx = _choiceCounter++;
        SetPerms(State.Choice);
        if (phoneChat != null && phoneChat.IsShown) phoneChat.Hide();   // 干预面板与手机不同屏（用户 2026-09-27）
        if (choicePanel != null) choicePanel.chapter = chapterIndex;    // 记录键按章隔离 story.choice.ch<N>.<idx>
        if (choicePanel != null) choicePanel.onPanelComplete = CaptureChoiceThumb;   // 全选完整显示时抓存档缩略图
        choicePanel.Open(step, idx, order =>
        {
            AutoSave("干预题交卷");
            // ②在微信段（收框状态下面板出）：面板收档后停一拍再继续，给"替林溪把话说完"留节奏
            if (order != null && order.Count > 0) { _pendingNextAt = Time.time + 0.4f; SetPerms(State.Gap); _gapTimer = 0f; return; }
            Next();
        });
    }

    // ------------------------------------------------------------------ 交互触发
    // 多章并存：只认本章（chapterTag）且未消费的交互点，防止第2章等待时抓走第3章的触发盒。
    // at（2026-09-28）：interact 步骤点名要哪个交互点（GameObject 名）。★ 必须点名——第3章一章
    // 3 个 F 点（点饭/落座/办公室门），按"第一个找到的"取会武装错点（试玩实测：点餐步骤武装了
    // 座位点，走到窗口没提示）。同名点已全部消费 → Revive 复用（第2章两次"拿起手机"）；
    // 点名没匹配上 → 警告后退回旧逻辑（第一章 json 不带 at，走的就是旧逻辑）。
    StoryInteractable FindFree(StoryInteractable.Mode m, string at = null)
    {
        var all = FindObjectsOfType<StoryInteractable>();
        StoryInteractable best = null;
        StoryInteractable named = null, namedUsed = null;
        foreach (var si in all)
        {
            if (si == null || si.mode != m) continue;
            if (si.chapterTag != chapterIndex) continue;
            if (!string.IsNullOrEmpty(at) && si.name == at)
            {
                if (!si.Consumed) named = si;
                else if (namedUsed == null) namedUsed = si;
            }
            if (!si.Consumed && best == null) best = si;
        }
        if (!string.IsNullOrEmpty(at))
        {
            if (named != null) return named;
            if (namedUsed != null) { namedUsed.Revive(); return namedUsed; }
            Debug.LogWarning("[StoryRunner] 没找到名为「" + at + "」的本章交互点——退回第一个可用点（检查 at 拼写/场景点名）");
        }
        return best;
    }

    void OnInteractableFired(StoryInteractable si)
    {
        if (si.mode == StoryInteractable.Mode.Touch && CurrState == State.WaitWalk)
        {
            si.armed = false;
            HideWalkHint();
            _currentTouch = null;
            Next();
        }
        else if (si.mode == StoryInteractable.Mode.InteractF && CurrState == State.WaitInteract)
        {
            // 「拿起手机」类提示 = 按 F 这一刻才拿（2026-09-28：此前 nar 命中关键词就提前低头，
            // 走过去的过程被清 Phone 修正为走路姿势，到达按下才真正拿起）
            if (_player != null && _player.animator != null && !string.IsNullOrEmpty(si.promptText)
                && HitsAny(si.promptText, PHONE_TAKE_KEYS))
            { _player.animator.SetTrigger("TakePhone"); _player.animator.SetBool("Phone", true); }
            si.armed = false;
            HideWalkHint();
            HidePrompt();
            _currentF = null;
            Next();
        }
    }

    // ------------------------------------------------------------------ 每帧
    void Update()
    {
#if UNITY_EDITOR
        if (Input.GetKeyDown(debugBackKey)) { DebugBack(); return; }   // 调试：回退到上一句
#endif
        switch (CurrState)
        {
            case State.EndCard:
                if (AdvancePressed()) ToMainMenu();
                break;

            case State.NarFree:      // 开场旁白：自由走动，纯点击推进（2026-09-29 用户定稿：去掉定时自动播，移动保留）
                if (AdvancePressed()) { Next(); break; }
                break;

            case State.Typing:
                if (AdvancePressed() && dialogue != null && dialogue.IsTyping) dialogue.SkipTyping();
                break;

            case State.Gap:
                if (_pendingNextAt > 0f && Time.time >= _pendingNextAt) { Next(); break; }
                if (AdvancePressed()) { Next(); break; }
                if (GameSettings.AutoPlay)
                {
                    _gapTimer += Time.deltaTime;
                    if (_gapTimer >= GameSettings.AutoDelaySeconds + _lastLineLen * 0.02f + 0.6f
                        && !DialogueVoicePlayer.IsPlaying) Next();   // 自动播放同样等语音播完
                }
                break;

            case State.WaitInteract:
                if (promptRoot != null && _currentF != null)
                {
                    bool show = _currentF.PlayerInRange;
                    if (promptRoot.activeSelf != show) promptRoot.SetActive(show);
                    if (show && promptLabel != null)
                        promptLabel.text = string.IsNullOrEmpty(_currentF.promptText)
                            ? "与" + (string.IsNullOrEmpty(_currentF.displayName) ? "他" : _currentF.displayName) + "交谈"
                            : _currentF.promptText;
                }
                break;
        }
    }

    static bool AdvancePressed()
    {
        return Input.GetMouseButtonDown(0)
            || Input.GetKeyDown(KeyCode.Space)
            || Input.GetKeyDown(KeyCode.Return);
    }

    // ------------------------------------------------------------------ 权限与 HUD
    void SetPerms(State s)
    {
        CurrState = s;
        if (_player == null) return;
        switch (s)
        {
            case State.Choice:
                _player.SetLocked(true);
                _player.moveLocked = false;
                _player.SetCursorLocked(false);          // 面板要鼠标（allowEscToUnlock 已在 Begin 关掉）
                break;
            case State.Gap:
                _player.SetLocked(false);
                _player.moveLocked = true;               // ★ 间隙：能转不能走
                _player.SetCursorLocked(true);
                break;
            case State.NarFree:                          // 开场旁白：能走能转
            case State.WaitInteract:
            case State.WaitWalk:
            case State.WaitDoor:                         // 走去门口：能走能转（自己走到门前按 F）
                _player.SetLocked(false);
                _player.moveLocked = false;
                _player.SetCursorLocked(true);
                break;
            case State.Enter:                            // 入场演出：能转视角（看她走过来），不能走
                _player.SetLocked(false);
                _player.moveLocked = true;
                _player.SetCursorLocked(true);
                break;
            default:                                      // Card/Typing/Fade/EndCard/Idle：全锁（Enter 有独立 case：能转不能走）
                _player.SetLocked(true);
                _player.moveLocked = false;
                _player.SetCursorLocked(true);
                break;
        }
        if (s != State.WaitInteract) HidePrompt();
    }

    // 走动段 HUD（左上角目标卡）：淡入 0→1（0.35s）→ 停 4s → 降到 0.55（0.6s）常驻；
    // 显示期间再次 Show = 停掉旧协程、alpha 重置回 1 重新计时；Hide = 淡出（0.3s）后收起。
    CanvasGroup WalkHintGroup()
    {
        return walkHintRoot != null ? walkHintRoot.GetComponent<CanvasGroup>() : null;
    }

    void ShowWalkHint(string text)
    {
        if (walkHintLabel != null) walkHintLabel.text = text;   // 目标卡自带竖条引导，不再拼 "→  " 前缀
        if (walkHintRoot == null) return;
        bool reshown = walkHintRoot.activeSelf;                 // 显示期间再次提示：从 1 重新计时，不重头淡入
        if (_walkHintRt != null) { StopCoroutine(_walkHintRt); _walkHintRt = null; }
        walkHintRoot.SetActive(true);
        _walkHintRt = StartCoroutine(WalkHintShowRoutine(reshown ? 1f : 0f));
    }

    IEnumerator WalkHintShowRoutine(float from)
    {
        var g = WalkHintGroup();
        if (g != null)
        {
            float t = 0f;
            while (t < 1f)
            {
                if (g == null) yield break;                     // 步骤跳跃/场景卸载时引用可能已被销毁
                t += Time.unscaledDeltaTime / 0.35f;
                g.alpha = Mathf.Lerp(from, 1f, Mathf.Clamp01(t));
                yield return null;
            }
            g.alpha = 1f;
        }
        yield return new WaitForSecondsRealtime(4f);
        if (g != null)
        {
            float t = 0f;
            while (t < 1f)
            {
                if (g == null) yield break;
                t += Time.unscaledDeltaTime / 0.6f;
                g.alpha = Mathf.Lerp(1f, 0.55f, Mathf.Clamp01(t));
                yield return null;
            }
            g.alpha = 0.55f;                                    // 半透明常驻，等 HideWalkHint 淡出
        }
        _walkHintRt = null;
    }

    void HideWalkHint()
    {
        if (walkHintRoot == null) return;
        if (_walkHintRt != null) { StopCoroutine(_walkHintRt); _walkHintRt = null; }
        if (WalkHintGroup() == null) { walkHintRoot.SetActive(false); return; }   // 旧场景没补 CanvasGroup：退化为直接收
        _walkHintRt = StartCoroutine(WalkHintHideRoutine());
    }

    IEnumerator WalkHintHideRoutine()
    {
        var g = WalkHintGroup();
        if (g != null)
        {
            float from = g.alpha;
            float t = 0f;
            while (t < 1f)
            {
                if (walkHintRoot == null || g == null) yield break;   // 同上：销毁保护，不得抛空引用
                t += Time.unscaledDeltaTime / 0.3f;
                g.alpha = Mathf.Lerp(from, 0f, Mathf.Clamp01(t));
                yield return null;
            }
        }
        if (walkHintRoot != null) walkHintRoot.SetActive(false);
        _walkHintRt = null;
    }

    void HidePrompt() { if (promptRoot != null) promptRoot.SetActive(false); }

    void HideAuxUI()
    {
        HideWalkHint(); HidePrompt();
        if (chapterCardGroup != null) { chapterCardGroup.alpha = 0f; chapterCardGroup.gameObject.SetActive(false); }
        if (blackFade != null) blackFade.canvasRenderer.SetAlpha(0f);
    }

    // ------------------------------------------------------------------ 自检接口
    /// 自检/调试用：替玩家做当前状态该做的事
#if UNITY_EDITOR
    // ------------------------------------------------------------------ 调试：回退到上一句
    [Header("调试（仅编辑器）")]
    [Tooltip("回退到上一句台词的按键；只在正停在一句话上时生效")]
    public KeyCode debugBackKey = KeyCode.Backspace;

    void OnChatCleared()
    {
        _chatLog.Clear();
        _chatClearStep = StepIndex - 1;      // 聊天流被清空（换段/重新亮屏）：回退不能跨过这里
    }

    /// 调试：回退到当前【连续文本段】内的上一句台词（nar/dlg/mon）并重播（含语音）。
    /// 只在 Typing/Gap/NarFree（正停在一句话上）生效；不跨 interact/walk/fade/enter/choice/card，
    /// 也不跨手机聊天流的清空点（换联系人/重新亮屏），避免与「正常玩到该句」的状态不一致。
    public void DebugBack()
    {
        if (CurrState != State.Typing && CurrState != State.Gap && CurrState != State.NarFree) return;
        if (_ch == null) return;
        int cur = StepIndex - 1;                                  // 当前正停的这句（Next 已自增过）
        if (cur < 0 || cur >= _ch.steps.Count) return;
        int target = cur - 1;
        if (target < 0 || !IsTextStep(_ch.steps[target]))
        {
            Debug.Log("[StoryRunner] 调试回退：已到本段第一句（上一句不是台词）");
            return;
        }
        if (target < _chatClearStep)
        {
            Debug.Log("[StoryRunner] 调试回退：上一句在手机聊天换段/重新亮屏之前，不能退（防聊天气泡对不上）");
            return;
        }
        TrimChatTo(target);
        _pendingNextAt = -1f;
        StepIndex = target;
        Debug.Log("[StoryRunner] 调试回退 → 第 " + target + " 步（" + _ch.steps[target].t + "）");
        Next();
    }

    static bool IsTextStep(StoryStep st)
    {
        return st != null && (st.t == "nar" || st.t == "dlg" || st.t == "mon");
    }

    /// 把手机上「步骤索引 >= target」的气泡摘掉（target 会被 Next 重播重新加回，避免重复一条）
    void TrimChatTo(int target)
    {
        if (phoneChat == null) return;
        while (_chatLog.Count > 0 && _chatLog[_chatLog.Count - 1] >= target)
        {
            _chatLog.RemoveAt(_chatLog.Count - 1);
            phoneChat.RemoveLast();
        }
    }
#endif

    public void DebugAdvance()
    {
        switch (CurrState)
        {
            case State.EndCard: ToMainMenu(); break;
            case State.Typing: if (dialogue != null) dialogue.SkipTyping(); break;
            case State.Gap:
            case State.NarFree: Next(); break;
            case State.WaitInteract: if (_currentF != null) _currentF.Fire(); break;
            case State.WaitWalk: if (_currentTouch != null) _currentTouch.Fire(); break;
            case State.WaitDoor:                                  // 自检不等玩家走：直接走一遍「到达剧情目标地点」
                if (_doors == null || !_doors.DebugTravelToStoryGoal())
                { Debug.LogWarning("[StoryRunner] 自检：door 步骤没能走到目标地点，直接继续"); HideWalkHint(); Next(); }
                break;
            case State.Enter: if (_entrance != null) _entrance.Skip(); break;   // 自检不等演出，直接终态
            case State.Choice:
                if (choicePanel != null)
                {
                    if (!choicePanel.AllSelected) choicePanel.DebugSelectNext();
                    else choicePanel.DebugConfirm();
                }
                break;
        }
    }

    /// 打字机播完回调（DialogueUI 接线）
    public void OnLineTyped()
    {
        if (CurrState != State.Typing) return;
        _gapTimer = 0f;
        SetPerms(State.Gap);
    }

    void OnEnable() { if (dialogue != null) dialogue.onLineTyped += OnLineTyped; }
    void OnDisable() { if (dialogue != null) dialogue.onLineTyped -= OnLineTyped; }
}
