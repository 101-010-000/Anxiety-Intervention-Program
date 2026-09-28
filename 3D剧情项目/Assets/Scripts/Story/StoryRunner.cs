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
        NarFree, WaitInteract, WaitWalk, Choice, Fade, Enter, EndCard, Done
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
    int _lastLineLen;
    Coroutine _cardRt;
    Coroutine _fadeRt;
    Coroutine _enterRt;
    Coroutine _walkHintRt;
    NpcEntrance _entrance;                                  // 当前入场演出（DebugAdvance 快进用）
    readonly Dictionary<string, GameObject> _entranceNpcs = new Dictionary<string, GameObject>();

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

    void OnDestroy() { if (Instance == this) Instance = null; }

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
        CollectAndHideEntranceNpcs();   // enter 步骤的角色开场先禁用（第2章陆宣雨：她不在宿舍）
        if (phoneChat != null) phoneChat.HideImmediate();   // 万一上次没收干净
        if (blackFade != null) blackFade.canvasRenderer.SetAlpha(1f);   // 进场：从黑淡入
        Next();
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

    // json 是唯一事实源：Begin 时扫本章所有 enter 步骤的 who → 预禁用这些角色（记录引用，
    // enter 时再启用——FindObjectsOfType 找不到禁用对象，所以必须先存）。
    void CollectAndHideEntranceNpcs()
    {
        _entranceNpcs.Clear();
        foreach (var step in _ch.steps)
        {
            if (step.t != "enter" || string.IsNullOrEmpty(step.who) || _entranceNpcs.ContainsKey(step.who)) continue;
            var t = FindCharacterTransform(step.who);
            if (t == null) { Debug.LogWarning("[StoryRunner] enter 角色没找到：" + step.who + "（enter 时会再找一次）"); continue; }
            _entranceNpcs[step.who] = t.gameObject;
            if (t.gameObject.activeSelf) t.gameObject.SetActive(false);
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
            case "enter": DoEnter(step); break;

            case "walk":
                _currentTouch = FindFree(StoryInteractable.Mode.Touch);
                if (_currentTouch == null) { Debug.LogWarning("[StoryRunner] 没有 Touch 触发盒，跳过走动段"); Next(); return; }
                _currentTouch.armed = true;
                ShowWalkHint(step.x);
                SetPerms(State.WaitWalk);
                break;

            case "interact":
                _currentF = FindFree(StoryInteractable.Mode.InteractF);
                if (_currentF == null) { Debug.LogWarning("[StoryRunner] 没有 F 交互点，跳过"); Next(); return; }
                _currentF.armed = true;
                ShowWalkHint(step.x);
                SetPerms(State.WaitInteract);
                break;

            default: PlayText(step); break;      // nar / dlg / mon
        }
    }

    void PlayText(StoryStep step)
    {
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
            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / 0.6f;
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
            while (t < 1f) { t += Time.unscaledDeltaTime / 0.5f; blackFade.canvasRenderer.SetAlpha(Mathf.Clamp01(t)); yield return null; }
        }
        if (cardTitle != null) cardTitle.text = (_ch != null ? _ch.chapter : "第一章") + "  完";
        if (cardSubtitle != null)
        {
            cardSubtitle.text = END_HINT;
            cardSubtitle.gameObject.SetActive(true);
        }
        if (chapterCardGroup != null)
        {
            chapterCardGroup.gameObject.SetActive(true);
            float t = 0f;
            while (t < 1f) { t += Time.unscaledDeltaTime / 0.6f; chapterCardGroup.alpha = Mathf.Clamp01(t); yield return null; }
            chapterCardGroup.alpha = 1f;
        }
    }

    void ToDone()
    {
        CurrState = State.Done;
        if (_player != null) { _player.allowEscToUnlock = _savedEsc; _player.SetLocked(false); _player.moveLocked = false; }
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
            while (t < 1f) { t += Time.unscaledDeltaTime / 0.4f; blackFade.canvasRenderer.SetAlpha(Mathf.Clamp01(t)); yield return null; }
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
        }
        else if (!string.IsNullOrEmpty(step.to))
            Debug.LogWarning("[StoryRunner] fade 找不到锚点「" + step.to + "」——检查 fadeAnchors 接线 / 锚点命名");

        yield return null;                             // 让传送落地一帧再开淡出

        if (blackFade != null)
        {
            float t = 0f;
            while (t < 1f) { t += Time.unscaledDeltaTime / 0.4f; blackFade.canvasRenderer.SetAlpha(1f - Mathf.Clamp01(t)); yield return null; }
            blackFade.canvasRenderer.SetAlpha(0f);
        }
        _fadeRt = null;
        Next();
    }

    Transform FindFadeAnchor(string name)
    {
        if (fadeAnchors == null || string.IsNullOrEmpty(name)) return null;
        foreach (var a in fadeAnchors)
            if (a != null && a.name == name) return a;
        return null;
    }

    // ------------------------------------------------------------------ NPC 入场演出（enter 步骤，2026-09-27）
    // who 从预禁用表启用 → 虚影渐显 + 从 from 锚点走到玩家面前（缺省落点；to 显式锚点可覆盖）
    // → 落定（换回真实材质/恢复描边碰撞/面向玩家）→ 接对话。演出细节见 NpcEntrance。
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
        _entrance = go.GetComponent<NpcEntrance>();
        if (_entrance == null) _entrance = go.AddComponent<NpcEntrance>();
        yield return _entrance.Run(from.position, target, _player != null ? _player.transform : null);

        _entrance = null;
        _enterRt = null;
        Next();
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
        AutoSave("章节通关", force: true);   // 章末兜底存档不受"选择后自动存档"开关控制
        ToDone();
        if (debugStayInScene) return;
        SceneManager.LoadScene("MainMenu");
    }

    // ------------------------------------------------------------------ 自动存档（2026-09-27 接线）
    // 时机：每道干预题交卷后（受设置"选择后自动存档"开关控制）+ 章节通关（始终存，进度兜底）。
    // 槽位：最近使用的手动槽，从没存过 → 1 号槽。存到章级（读档 = 从该章第 1 步重播；
    // nodeId/step 先留档，章内续播以后要再做）。自检（StorySmokeDriver 在场）不写档，防污染真实存档。
    void AutoSave(string reason, bool force = false)
    {
        if (!force && !GameSettings.AutoSave) return;
        if (FindObjectOfType<StorySmokeDriver>() != null) return;
        int slot = SaveSystem.LatestSlot();
        if (slot < 0) slot = 0;
        var d = new SaveData
        {
            chapter = chapterIndex,
            step    = StepIndex,
            nodeId  = "step" + StepIndex,
        };
        SaveSystem.Write(slot, d);
        Debug.Log("[StoryRunner] 自动存档（" + reason + "）→ 槽 " + (slot + 1) + " · 第" + chapterIndex + "章 step " + StepIndex);
    }

    // ------------------------------------------------------------------ 干预题
    void DoChoice(StoryStep step)
    {
        int idx = _choiceCounter++;
        SetPerms(State.Choice);
        if (phoneChat != null && phoneChat.IsShown) phoneChat.Hide();   // 干预面板与手机不同屏（用户 2026-09-27）
        if (choicePanel != null) choicePanel.chapter = chapterIndex;    // 记录键按章隔离 story.choice.ch<N>.<idx>
        choicePanel.Open(step, idx, order =>
        {
            AutoSave("干预题交卷");
            // ②在微信段（收框状态下面板出）：面板收档后停一拍再继续，给"替林溪把话说完"留节奏
            if (order != null && order.Count > 0) { _pendingNextAt = Time.time + 0.4f; SetPerms(State.Gap); _gapTimer = 0f; return; }
            Next();
        });
    }

    // ------------------------------------------------------------------ 交互触发
    // 多章并存：只认本章（chapterTag）且未消费的交互点，防止第2章等待时抓走第3章的触发盒
    StoryInteractable FindFree(StoryInteractable.Mode m)
    {
        var all = FindObjectsOfType<StoryInteractable>();
        StoryInteractable best = null;
        foreach (var si in all)
        {
            if (si == null || si.mode != m || si.Consumed) continue;
            if (si.chapterTag != chapterIndex) continue;
            if (best == null) best = si;
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
        switch (CurrState)
        {
            case State.EndCard:
                if (AdvancePressed()) ToMainMenu();
                break;

            case State.NarFree:      // 开场旁白：自由走动，定时/点击都能进下一句
                if (AdvancePressed()) { Next(); break; }
                _gapTimer += Time.deltaTime;
                if (_gapTimer >= 1.2f + _lastLineLen * 0.055f) Next();
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
                    if (_gapTimer >= GameSettings.AutoDelaySeconds + _lastLineLen * 0.02f + 0.6f) Next();
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
                _player.SetLocked(false);
                _player.moveLocked = false;
                _player.SetCursorLocked(true);
                break;
            default:                                      // Card/Typing/Fade/Enter/EndCard/Idle：全锁
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
