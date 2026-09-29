// 在剧情主场景 Game.unity 里搭「门口传送」：
//   · 每间房门前一个触发盒（DoorInteractable）+ 一个落点（arrivePoint）
//   · 一块交互 UI（底部「按 F 开门」提示 + 地点选择面板）
//   · 一个 DoorTravelSystem 把这些接起来
//
// 用法：菜单 Tools/干预项目/搭建门口传送，或丢 Assets/_doors_trigger.txt
// 输出：Assets/Scenes/Game.unity + 报告 Assets/assets/_报告/_门口传送.txt
//
// 设计要点：
//   · 6 个地点在同一个 Game.unity 里（按 GAP=40m 并排），所以"切换场景" = 传送到另一个地点
//   · 门位优先取场景里真实的 门框墙；没有就退回"南墙正中"（SceneBuilder 里门就开在南墙）
//   · 落点放在触发盒【外面】朝屋内 2.2m —— 不然传送落地正好落进触发盒，提示会卡住
//   · 幂等：重复跑只是刷新，不会叠出第二套 UI

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public static class GameDoorBuilder
{
    // [rev1] 用来强制重编译（触发器等域重载才会跑），不影响逻辑
    public const string OUT_SCENE = "Assets/Scenes/Game.unity";
    const string REPORT    = "Assets/assets/_报告/_门口传送.txt";
    const string ROOT_NAME = "门口传送";
    const string UI_NAME   = "UI_门口交互";
    const string FONT_PATH = "Assets/assets/05_UI/字体_Font/中文_Deng.ttf";
    const string SMOKE_REPORT = "Assets/assets/_报告/_门口传送运行自检.txt";

    // 地点 → 显示名 / 章节（跟 SceneBuilder.BuildLocs() 的表一致）
    class Place { public string Room, Title, Chapter; }
    static readonly Place[] PLACES =
    {
        new Place { Room = "Loc_教室",   Title = "大学教室",      Chapter = "第1章" },
        new Place { Room = "Loc_走廊",   Title = "教室外走廊",    Chapter = "第1章" },
        new Place { Room = "Loc_宿舍",   Title = "女生宿舍",      Chapter = "第2 / 4 / 5章" },
        new Place { Room = "Loc_食堂",   Title = "食堂",          Chapter = "第3 / 5章" },
        new Place { Room = "Loc_办公室", Title = "咨询办公室",    Chapter = "第3章" },
        new Place { Room = "Loc_图书馆", Title = "图书馆 / 自习区", Chapter = "第4 / 5章" },
    };

    // 配色（跟主界面一套）
    static readonly Color ACCENT      = new Color(0.20f, 0.45f, 0.72f);
    static readonly Color TEXT_DARK   = new Color(0.16f, 0.20f, 0.26f);
    static readonly Color TEXT_SUB    = new Color(0.42f, 0.48f, 0.55f);
    static readonly Color DIM         = new Color(0.06f, 0.09f, 0.13f, 0.55f);
    static Font _font;

    // ------------------------------------------------------------------ 菜单
    [MenuItem("Tools/干预项目/搭建门口传送", false, 30)]
    public static void Run() => RunInternal(false);

    public static void RunFromTrigger() => RunInternal(true);

    static void RunInternal(bool fromTrigger)
    {
        var log = new List<string>();
        log.Add("门口传送搭建报告  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("");

        if (!File.Exists(OUT_SCENE)) { Debug.LogError("[GameDoorBuilder] 找不到 " + OUT_SCENE); return; }
        var cur = SceneManager.GetActiveScene();
        if (cur.path != OUT_SCENE)
        {
            if (fromTrigger) { Debug.Log("[GameDoorBuilder] 跳过：当前不是 Game.unity"); return; }
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(OUT_SCENE, OpenSceneMode.Single);
        }

        LoadFont(log);
        PreloadScripts(log);              // ★ 必须在 AddComponent 之前：见下面注释
        var scene = SceneManager.GetActiveScene();

        // 1) 清掉上一次的（幂等）
        var old = GameObject.Find(ROOT_NAME);
        if (old != null) Object.DestroyImmediate(old);
        var oldUI = GameObject.Find(UI_NAME);
        if (oldUI != null) Object.DestroyImmediate(oldUI);
        // 每个房间下的旧门节点
        var locsRoot = GameObject.Find("Locations");
        if (locsRoot == null) { log.Add("★ 场景里没有 Locations，先跑『搭建游戏场景』"); Write(log); return; }
        for (int i = 0; i < locsRoot.transform.childCount; i++)
        {
            var loc = locsRoot.transform.GetChild(i);
            for (int c = loc.childCount - 1; c >= 0; c--)
                if (loc.GetChild(c).name.StartsWith("Door_") || loc.GetChild(c).name.StartsWith("Arrive_"))
                    Object.DestroyImmediate(loc.GetChild(c).gameObject);
        }

        // 2) 找场景里【已经存在的】门触发盒（用户手动加的），给它们挂上 DoorInteractable
        //    ★ 这里不再自己凭空摆触发盒 —— 门在哪由场景说了算
        var doors = new List<DoorInteractable>();
        var triggers = FindSceneDoorTriggers();
        if (triggers.Count == 0)
        {
            log.Add("★ 场景里一个门触发盒都没找到。");
            log.Add("  需要在门的位置放 BoxCollider 并勾上 Is Trigger（或 Door_G_Frame 之类带触发盒的门板）。");
            Write(log); return;
        }

        log.Add("发现门触发盒 " + triggers.Count + " 个：");
        var arriveByRoom = new Dictionary<string, Transform>();
        foreach (var bc in triggers)
        {
            var room = FindRoom(bc.transform);
            if (room == null)
            {
                log.Add("  ★ 跳过（不在任何 Loc_* 下面）：" + PathOf(bc.transform));
                continue;
            }
            var place = PlaceOf(room.name);
            var di = bc.GetComponent<DoorInteractable>();
            bool isNew = di == null;
            if (isNew) di = bc.gameObject.AddComponent<DoorInteractable>();

            di.trigger    = bc;
            di.locationId = room.name;
            di.title      = place.Title;
            di.chapter    = place.Chapter;

            // 每间房一个落点：摆在触发盒前方、朝屋内（放到触发盒外面，免得落地就卡在提示区里）
            if (di.arrivePoint == null)
            {
                Transform ap;
                if (!arriveByRoom.TryGetValue(room.name, out ap) || ap == null)
                {
                    ap = MakeArrivePoint(room, bc, room.name.Replace("Loc_", ""));
                    arriveByRoom[room.name] = ap;
                }
                di.arrivePoint = ap;
            }
            else arriveByRoom[room.name] = di.arrivePoint;

            EditorUtility.SetDirty(di);
            doors.Add(di);

            log.Add(string.Format("  {0}  尺寸 {1:0.0}×{2:0.0}×{3:0.0}  →  {4}（{5}）  落点 {6}",
                PathOf(bc.transform), bc.size.x, bc.size.y, bc.size.z,
                place.Title, isNew ? "新挂" : "已挂过",
                di.arrivePoint != null ? di.arrivePoint.name : "★无"));
        }

        // 场景里没有门触发盒的地点，也报一下，免得漏
        for (int i = 0; i < locsRoot.transform.childCount; i++)
        {
            var loc = locsRoot.transform.GetChild(i);
            if (!loc.name.StartsWith("Loc_")) continue;
            if (arriveByRoom.ContainsKey(loc.name)) continue;
            log.Add("  ⚠ " + loc.name + " 没有任何门触发盒（这个地点就传不进去，也不会出现在目标列表里）");
        }
        log.Add("");

        // 4) 交互 UI
        var canvas = BuildUI(log);

        // 5) 总控
        var root = new GameObject(ROOT_NAME);
        var sys = root.AddComponent<DoorTravelSystem>();      // ★ 新脚本：AddComponent 前要先过一遍 ImportAsset
        sys.player    = Object.FindObjectOfType<FirstPersonController>();
        sys.promptRoot = canvas.promptRoot;
        sys.promptLabel = canvas.promptLabel;
        sys.panel     = canvas.panel;
        sys.panelTitle = canvas.panelTitle;
        sys.panelHint  = canvas.panelHint;
        sys.listRoot   = canvas.listRoot;
        sys.closeButton = canvas.closeButton;
        sys.doors      = doors;

        EditorSceneManager.MarkSceneDirty(scene);
        bool ok = EditorSceneManager.SaveScene(scene);
        log.Add("");
        log.Add("保存场景：" + OUT_SCENE + "  " + (ok ? "成功" : "★失败"));
        log.Add("");
        log.Add("怎么用：");
        log.Add("  · 走到任意一间房的门前 → 底部弹「按 F 开门」→ 按 F → 选地点 → 传送");
        log.Add("  · 6 个地点都在同一个 Game.unity 里，所以是传送不是 LoadScene");
        log.Add("    （以后要真换 Unity 场景，改 DoorTravelSystem.TravelTo 里那一行就行）");
        log.Add("  · 门触发盒是场景里【已有】的（你手动加的 Is Trigger BoxCollider），本工具只是给它挂上交互");
        log.Add("  · 想改门位/触发范围：直接改那个门自己的 BoxCollider；想改显示名/章节：改它上面的 DoorInteractable");
        Write(log);
        Debug.Log("[GameDoorBuilder] 完成，报告：" + REPORT);
    }

    /// ★ AGENTS.md 里记过的坑：刚写/刚拷进来的 .cs，MonoScript.GetClass() 可能是 null，
    ///   这时 AddComponent 出来的组件会被 Unity 写成"内联 MonoScript" —— 新会话里就是缺脚本，
    ///   整个系统静默失效（不高亮、不报错）。所以 AddComponent 之前先强制过一遍。
    static void PreloadScripts(List<string> log)
    {
        AssetDatabase.Refresh();
        var paths = new List<string>();
        if (Directory.Exists("Assets/Scripts/Game"))
            paths.AddRange(Directory.GetFiles("Assets/Scripts/Game", "*.cs", SearchOption.AllDirectories));
        paths.Add("Assets/Scripts/UI/UIPanel.cs");
        paths.Add("Assets/Scripts/UI/UIHoverScale.cs");
        paths.Add("Assets/Scripts/UI/UIButtonPolish.cs");
        paths.Add("Assets/Scripts/Player/FirstPersonController.cs");
        paths.Add("Assets/Scripts/Game/DoorSmokeDriver.cs");
        paths.Add("Assets/Editor/GameDoorBuilder.cs");

        int ok = 0, fixedCount = 0;
        foreach (var raw in paths)
        {
            string p = raw.Replace('\\', '/');
            var mono = AssetDatabase.LoadAssetAtPath<MonoScript>(p);
            if (mono == null || mono.GetClass() == null)
            {
                AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
                mono = AssetDatabase.LoadAssetAtPath<MonoScript>(p);
                fixedCount++;
            }
            if (mono == null || mono.GetClass() == null) log.Add("  ★ 脚本类型还没编译进来：" + p);
            else ok++;
        }
        log.Add("脚本预热：" + ok + "/" + paths.Count + " 个 MonoScript 就绪" +
                (fixedCount > 0 ? "（其中 " + fixedCount + " 个强制重导过一次）" : ""));
    }

    // ------------------------------------------------------------------ 门 / 落点

    /// 场景里所有"门触发盒"：勾了 Is Trigger 的 BoxCollider，且不是
    /// 后期氛围体积（带 Volume 组件的 Post_*）也不是本系统的节点。
    static List<BoxCollider> FindSceneDoorTriggers()
    {
        var res = new List<BoxCollider>();
        var all = Object.FindObjectsOfType<BoxCollider>(true);
        foreach (var bc in all)
        {
            if (bc == null || !bc.isTrigger) continue;
            if (bc.GetComponent<Volume>() != null) continue;                       // 后期氛围体积
            if (bc.GetComponent<DoorInteractable>() != null) { res.Add(bc); continue; }
            // ★ 剧情触发盒不是门（踩过 2026-09-29）：第5章_宿舍门口（走动段 Touch 盒）、
            //   教室 BumpPoint（张知远撞人）都勾了 Is Trigger 且在 Loc_* 下，
            //   无差别挂 DoorInteractable 会让玩家在剧情触发盒前弹「按 F 开门」。
            if (bc.GetComponent<StoryInteractable>() != null) continue;
            if (bc.name.StartsWith("Post_")) continue;
            var root = bc.transform.root;
            if (root != null && root.name == UI_NAME) continue;                    // UI 画布
            res.Add(bc);
        }
        res.Sort((a, b) => string.CompareOrdinal(PathOf(a.transform), PathOf(b.transform)));
        return res;
    }

    static Transform FindRoom(Transform t)
    {
        for (var p = t; p != null; p = p.parent)
            if (p.name.StartsWith("Loc_")) return p;
        return null;
    }

    static Place PlaceOf(string roomName)
    {
        var hit = PLACES.FirstOrDefault(p => p.Room == roomName);
        if (hit != null) return hit;
        return new Place { Room = roomName, Title = roomName.Replace("Loc_", ""), Chapter = "" };
    }

    static string PathOf(Transform t)
    {
        var sb = new System.Text.StringBuilder(t.name);
        for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
        return sb.ToString();
    }

    /// 在触发盒【外面、朝屋内】放一个落点。
    /// 放在盒外是为了传送落地后不卡在提示区里（不然提示会一直挂着）。
    static Transform MakeArrivePoint(Transform room, BoxCollider bc, string shortName)
    {
        var b = bc.bounds;
        Vector3 center = b.center;
        Vector3 roomCenter = room.position;

        // 朝屋内 = 从触发盒指向房间中心（水平方向）
        Vector3 inward = new Vector3(roomCenter.x - center.x, 0f, roomCenter.z - center.z);
        if (inward.sqrMagnitude < 1e-4f) inward = room.forward;
        inward = inward.normalized;

        // 触发盒在"朝屋内"这个方向上的半长，取世界 AABB 的近似
        Vector3 ext = b.extents;
        float halfInward = Mathf.Abs(ext.x * inward.x) + Mathf.Abs(ext.z * inward.z);

        var go = new GameObject("Arrive_" + shortName);
        go.transform.SetParent(room, false);
        Vector3 p = center + inward * (halfInward + 1.4f);
        p.y = room.position.y;
        go.transform.position = p;

        Vector3 look = new Vector3(roomCenter.x - p.x, 0f, roomCenter.z - p.z);
        go.transform.rotation = look.sqrMagnitude > 1e-4f
            ? Quaternion.LookRotation(look.normalized, Vector3.up)
            : Quaternion.LookRotation(inward, Vector3.up);
        return go.transform;
    }

    // ------------------------------------------------------------------ UI
    class UIRefs
    {
        public GameObject promptRoot; public Text promptLabel;
        public UIPanel panel; public Text panelTitle; public Text panelHint;
        public RectTransform listRoot; public Button closeButton;
    }

    static UIRefs BuildUI(List<string> log)
    {
        // EventSystem：Game.unity 里原来没有，没有它按钮点不动
        if (Object.FindObjectOfType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            log.Add("新建 EventSystem（原本没有，没有它 UI 按钮点不动）");
        }

        var canvasGO = new GameObject(UI_NAME, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        var R = new UIRefs();

        // ---- 门口提示：**直接复用剧情那套「交互提示」**（用户 2026-09-29 定稿：交互 UI 统一用已有的那套）
        // ⚠ 别自建白底提示：旧版的 提示_按F 用的贴图名（面板_暗 / 按钮_次_普通）在当前 MenuAssets 里
        //   已经不存在 → Sprite 取到 null → 画面里就是一个纯白方块，还跟剧情提示叠在同一个位置。
        var existing = FindExistingPrompt();
        if (existing.go != null)
        {
            R.promptRoot = existing.go;
            R.promptLabel = existing.label;
            log.Add("门口提示：复用已有节点 " + PathOf(existing.go.transform) + "（不再新建白底提示）");
        }
        else
        {
            // 兜底（场景里还没搭剧情 UI 时才走到这里）：自建一个，贴图用确实存在的那张
            var prompt = Node("提示_按F", canvasGO.transform);
            Corner(prompt, new Vector2(0f, 118f), new Vector2(520f, 92f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            var pbg = prompt.AddComponent<Image>();
            pbg.sprite = SlicedSprite("面板_玻璃");
            pbg.type = Image.Type.Sliced;
            pbg.color = new Color(1f, 1f, 1f, 0.92f);
            pbg.raycastTarget = false;
            R.promptRoot = prompt;

            var key = Node("键位", prompt.transform);
            At(key, new Vector2(-168f, 0f), new Vector2(64f, 64f));
            var kimg = key.AddComponent<Image>();
            kimg.sprite = SlicedSprite("页签_普通");
            kimg.type = Image.Type.Sliced;
            kimg.raycastTarget = false;
            Label(key.transform, "字", "F", Vector2.zero, new Vector2(64f, 64f), 34, TEXT_DARK, TextAnchor.MiddleCenter, FontStyle.Bold);

            R.promptLabel = Label(prompt.transform, "文字", "开门", new Vector2(24f, 0f), new Vector2(400f, 64f), 30,
                                  TEXT_DARK, TextAnchor.MiddleLeft);
            log.Add("★ 场景里没有 交互提示 节点 → 自建了一个兜底提示（建议先搭好剧情 UI）");
        }

        // ---- 选择面板
        var pageGO = Node("面板_选择地点", canvasGO.transform);
        Stretch(pageGO);
        var cg = pageGO.AddComponent<CanvasGroup>();
        var page = pageGO.AddComponent<UIPanel>();
        page.openOnStart = false;

        var dim = Node("遮罩", pageGO.transform);
        Stretch(dim);
        var dimImg = dim.AddComponent<Image>();
        dimImg.sprite = MainMenuAssets.Sprite("遮罩_白");
        dimImg.color = DIM;
        dimImg.raycastTarget = true;              // 点空白处也能关
        var dimBtn = dim.AddComponent<Button>();
        dimBtn.transition = Selectable.Transition.None;

        var card = Node("卡片", pageGO.transform);
        At(card, Vector2.zero, new Vector2(760f, 720f));
        var cardImg = card.AddComponent<Image>();
        cardImg.sprite = SlicedSprite("面板_亮");
        cardImg.type = Image.Type.Sliced;
        cardImg.raycastTarget = true;

        R.panel = page;
        R.panelTitle = Label(card.transform, "标题", "去别的地点", new Vector2(0f, 288f), new Vector2(640f, 56f), 40,
                             TEXT_DARK, TextAnchor.MiddleCenter, FontStyle.Bold);
        R.panelHint  = Label(card.transform, "副标题", "", new Vector2(0f, 238f), new Vector2(700f, 34f), 24,
                             TEXT_SUB, TextAnchor.MiddleCenter);

        // 关闭按钮
        var closeGO = Node("按钮_关闭", card.transform);
        Corner(closeGO, new Vector2(-18f, -18f), new Vector2(48f, 48f), new Vector2(1f, 1f), new Vector2(1f, 1f));
        var closeImg = closeGO.AddComponent<Image>();
        closeImg.sprite = SlicedSprite("按钮_图标_悬停");
        closeImg.type = Image.Type.Sliced;
        closeImg.raycastTarget = true;
        R.closeButton = closeGO.AddComponent<Button>();
        R.closeButton.targetGraphic = closeImg;
        R.closeButton.transition = Selectable.Transition.ColorTint;
        R.closeButton.colors = HoverTint();
        var ci = Node("图标", closeGO.transform);
        At(ci, Vector2.zero, new Vector2(24f, 24f));
        var cimg = ci.AddComponent<Image>();
        cimg.sprite = SlicedSprite("图标_关闭");
        cimg.raycastTarget = false;

        // 目标按钮列表（预建 8 个，运行时按 doors 数量显示/隐藏）
        var list = Node("列表", card.transform);
        At(list, new Vector2(0f, -30f), new Vector2(640f, 460f));
        R.listRoot = (RectTransform)list.transform;
        int slot = 8;
        for (int i = 0; i < slot; i++)
        {
            float y = 190f - i * 66f;
            var bgo = Node("目标_" + i, list.transform);
            At(bgo, new Vector2(0f, y), new Vector2(620f, 58f));
            var bimg = bgo.AddComponent<Image>();
            bimg.sprite = SlicedSprite("按钮_次_悬停");       // 用确实存在的贴图（旧版名字已失效 → 白块）
            bimg.type = Image.Type.Sliced;
            bimg.raycastTarget = true;
            var btn = bgo.AddComponent<Button>();
            btn.targetGraphic = bimg;
            btn.transition = Selectable.Transition.ColorTint;
            btn.colors = HoverTint();
            bgo.AddComponent<UIHoverScale>();
            var pol = bgo.AddComponent<UIButtonPolish>();
            pol.label = Label(bgo.transform, "文字", "", Vector2.zero, new Vector2(560f, 44f), 28,
                              ACCENT, TextAnchor.MiddleCenter);
            pol.normalLabel = ACCENT;
            pol.hoverLabel = MainMenuAssets.ACCENT_DARK;
        }

        log.Add("交互 UI：提示（底部居中）+ 选择面板 760×720（预建 8 个目标按钮）");
        log.Add("面板在场景里是收起状态；想手改就在 Hierarchy 里选中 " + UI_NAME + "/面板_选择地点 打开看看");
        return R;
    }

    // ------------------------------------------------------------------ 小工具（照 MainMenuBuilder 的写法）
    /// 找剧情那套已有提示（UI交互/交互提示 + 它的 文字 子节点）——门口提示直接复用它
    static (GameObject go, Text label) FindExistingPrompt()
    {
        foreach (var canvasName in new[] { "UI交互", "UI_门口交互" })
        {
            var canvas = GameObject.Find(canvasName);
            if (canvas == null) continue;
            var t = canvas.transform.Find("交互提示");
            if (t == null) continue;
            var label = t.Find("文字");
            return (t.gameObject, label != null ? label.GetComponent<Text>() : t.GetComponentInChildren<Text>(true));
        }
        return (null, null);
    }

    static void LoadFont(List<string> log)
    {
        _font = AssetDatabase.LoadAssetAtPath<Font>(FONT_PATH);
        if (_font == null) log.Add("★ 找不到中文字体：" + FONT_PATH + "（中文会显示成方块）");
        else log.Add("字体：" + FONT_PATH);
    }

    static Sprite SlicedSprite(string name)
    {
        var s = MainMenuAssets.Sprite(name);
        return s;
    }

    static GameObject Node(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    static RectTransform At(GameObject go, Vector2 pos, Vector2 size)
    {
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    static RectTransform Corner(GameObject go, Vector2 pos, Vector2 size, Vector2 anchor, Vector2 pivot)
    {
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    static RectTransform Stretch(GameObject go, float pad = 0f)
    {
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(pad, pad);
        rt.offsetMax = new Vector2(-pad, -pad);
        return rt;
    }

    static Text Label(Transform parent, string name, string content, Vector2 pos, Vector2 size, int fontSize, Color color,
                      TextAnchor anchor = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Normal)
    {
        var go = Node(name, parent);
        At(go, pos, size);
        var t = go.AddComponent<Text>();
        t.font = _font;
        t.text = content;
        t.fontSize = fontSize;
        t.fontStyle = style;
        t.color = color;
        t.alignment = anchor;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        t.supportRichText = true;
        return t;
    }

    static ColorBlock HoverTint()
    {
        var c = ColorBlock.defaultColorBlock;
        c.normalColor      = new Color(0.96f, 0.97f, 0.98f);
        c.highlightedColor = Color.white;
        c.pressedColor     = new Color(0.88f, 0.92f, 0.96f);
        c.selectedColor    = c.normalColor;
        c.disabledColor    = new Color(0.80f, 0.85f, 0.89f, 0.6f);
        c.fadeDuration     = 0.08f;
        return c;
    }

    static void Write(List<string> log)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(REPORT).Replace('/', Path.DirectorySeparatorChar));
        log.Add("");
        log.Add("———————————————————————————— 怎么改 ————————————————————————————");
        log.Add("· 门位/触发范围：在场景里直接改那个门自己的 BoxCollider（Is Trigger），一般就够用");
        log.Add("  工具每次会重新扫一遍，新加的门只要勾了 Is Trigger 就会被识别（必须挂在 Loc_* 下面）");
        log.Add("· 落点：Loc_*/Arrive_<地点>，工具自动摆在触发盒前方朝屋内 1.4m；嫌不好就直接拖");
        log.Add("· 显示名/章节：改 Door_<地点> 上 DoorInteractable 的 title / chapter");
        log.Add("· 交互键：门口传送/DoorTravelSystem 的 interactKey（默认 F）");
        log.Add("· 面板高亮色/尺寸：改 Assets/Editor/GameDoorBuilder.cs 的 BuildUI()，再跑一次");
        log.Add("· UI 在场景里默认收起；想手改就在 Hierarchy 里选中 UI_门口交互/面板_选择地点 打开 CanvasGroup");
        File.WriteAllText(REPORT, string.Join("\n", log.ToArray()));
    }

    // ================================================================== 运行自检
    // 具体步骤跑在 Play 模式里的协程中（Assets/Scripts/Game/DoorSmokeDriver.cs）：
    // EditorApplication.update 的 tick 和游戏帧不是一回事，用 tick 做等待会时序错乱。
    // 这里只负责：置标志 → 进 Play → 轮询 Finished → 写报告 → 退出 Play。
    static bool _smokeActive, _smokeHooked;
    static double _smokeStart;
    static bool _smokeOldOptEnabled;
    static EnterPlayModeOptions _smokeOldOpt;

    [MenuItem("Tools/干预项目/门口传送运行自检", false, 31)]
    public static void SmokeTest()
    {
        if (!File.Exists(OUT_SCENE)) { Debug.LogError("[GameDoorBuilder] 先跑『搭建门口传送』"); return; }

        _smokeOldOptEnabled = EditorSettings.enterPlayModeOptionsEnabled;
        _smokeOldOpt = EditorSettings.enterPlayModeOptions;
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;

        EditorSceneManager.OpenScene(OUT_SCENE, OpenSceneMode.Single);

        DoorSmokeDriver.Requested = true;             // 静态标志，域不会重载，所以能带进 Play
        DoorSmokeDriver.Finished = false;
        DoorSmokeDriver.Lines.Clear();
        DoorSmokeDriver.Errors.Clear();

        _smokeStart = EditorApplication.timeSinceStartup;
        _smokeActive = true;
        if (!_smokeHooked)
        {
            EditorApplication.update += SmokePoll;
            _smokeHooked = true;
        }
        Debug.Log("[GameDoorBuilder] 门口传送运行自检开始");
    }

    static void SmokePoll()
    {
        if (!_smokeActive) return;

        if (EditorApplication.timeSinceStartup - _smokeStart > 300) { SmokeFinish("超时（300 秒）"); return; }
        if (!EditorApplication.isPlaying) { EditorApplication.isPlaying = true; return; }
        if (!DoorSmokeDriver.Finished) return;        // 协程还没跑完

        SmokeFinish(null);
    }

    static void SmokeFinish(string fail)
    {
        _smokeActive = false;
        DoorSmokeDriver.Requested = false;

        EditorSettings.enterPlayModeOptionsEnabled = _smokeOldOptEnabled;
        EditorSettings.enterPlayModeOptions = _smokeOldOpt;

        var outLines = new List<string>();
        outLines.Add("门口传送运行自检（真进 Play 模式走一遍）  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        outLines.Add("");
        outLines.AddRange(DoorSmokeDriver.Lines);
        if (!string.IsNullOrEmpty(fail)) { outLines.Add(""); outLines.Add("★ 中止：" + fail); }
        if (DoorSmokeDriver.Errors.Count > 0)
        {
            outLines.Add("");
            outLines.Add("运行期报错/异常 " + DoorSmokeDriver.Errors.Count + " 条：");
            outLines.AddRange(DoorSmokeDriver.Errors);
        }
        else { outLines.Add(""); outLines.Add("运行期报错：无 ✓"); }
        outLines.Add("");
        outLines.Add("注：编辑器里没法伪造 Input.GetKeyDown，所以走的是 OpenPanel()，");
        outLines.Add("    和按 F 是同一个入口（Update 里 KeyDown 之后就是调它）。");

        Directory.CreateDirectory(Path.GetDirectoryName(SMOKE_REPORT).Replace('/', Path.DirectorySeparatorChar));
        File.WriteAllText(SMOKE_REPORT, string.Join("\n", outLines.ToArray()));

        EditorApplication.isPlaying = false;
        Debug.Log("[GameDoorBuilder] 门口传送运行自检完成，报告：" + SMOKE_REPORT);
    }

}

// 工程里存在 Assets/_doors_trigger.txt 时，编辑器下次刷新/重编译后自动跑一次。
[InitializeOnLoad]
public static class GameDoorBuilderTrigger
{
    const string Trigger = "Assets/_doors_trigger.txt";
    const string Smoke   = "Assets/_doorssmoke_trigger.txt";
    const string ErrFile = "../额外文件/错误_门口传送.txt";

    static GameDoorBuilderTrigger()
    {
        if (File.Exists(Smoke))
        {
            EditorApplication.delayCall += () =>
            {
                try
                {
                    if (File.Exists(Smoke)) File.Delete(Smoke);
                    if (File.Exists(Smoke + ".meta")) File.Delete(Smoke + ".meta");
                    GameDoorBuilder.SmokeTest();
                }
                catch (System.Exception e)
                {
                    Directory.CreateDirectory("../额外文件");
                    File.WriteAllText("../额外文件/错误_门口传送自检.txt", e.ToString());
                    Debug.LogError("[GameDoorBuilder] 自检失败: " + e);
                }
            };
            return;
        }

        if (!File.Exists(Trigger)) return;
        EditorApplication.delayCall += () =>
        {
            try
            {
                if (File.Exists(Trigger)) File.Delete(Trigger);
                if (File.Exists(Trigger + ".meta")) File.Delete(Trigger + ".meta");
                GameDoorBuilder.RunFromTrigger();
                if (File.Exists(ErrFile)) File.Delete(ErrFile);
                Debug.Log("[GameDoorBuilder] 自动搭建完成");
            }
            catch (System.Exception e)
            {
                Directory.CreateDirectory("../额外文件");
                File.WriteAllText(ErrFile, e.ToString());
                Debug.LogError("[GameDoorBuilder] 自动搭建失败: " + e);
            }
        };
    }
}
