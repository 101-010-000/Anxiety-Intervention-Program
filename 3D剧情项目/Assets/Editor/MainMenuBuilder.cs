// 搭建主界面（主菜单）UI 场景：背景 + 标题 + 6 个入口按钮 + 设置 / 存读档 / 章节选择 / 内容概览 / 大图 / 确认弹窗 / Toast。
// 用法：菜单 Tools/干预项目/搭建主界面UI场景、渲染主界面预览
// 输出：Assets/Scenes/MainMenu.unity + Assets/assets/_报告/_主界面搭建.txt（含层级树与自动检查）
//
// 设计要点：
//   · 全部用 uGUI（Canvas + Image + Text），不用 TextMeshPro（工程里没装 TMP 包）
//   · 贴图由 MainMenuAssets 程序化生成（清透治愈风：淡青绿 + 暖白 + 大圆角 + 细描边）
//   · 所有引用在搭场景时直接接好，运行时脚本里没有 FindObject
//   · 坐标一律用"卡片中心为原点"的锚点，1920×1080 参考分辨率
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class MainMenuBuilder
{
    public const string OUT_SCENE   = "Assets/Scenes/MainMenu.unity";
    public const string GAME_SCENE  = "Assets/Scenes/Game.unity";
    public const string REPORT      = "Assets/assets/_报告/_主界面搭建.txt";
    public const string REPORT_BTNHOVER = "Assets/assets/_报告/_按钮悬停改色.txt";
    public const string PREVIEW_DIR = "Assets/assets/_报告/预览/主界面";

    static Font _font;
    static readonly List<string> _warn = new List<string>();

    static readonly Color INK       = MainMenuAssets.INK;
    static readonly Color INK_SOFT  = MainMenuAssets.INK_SOFT;
    static readonly Color MUTED     = MainMenuAssets.MUTED;
    static readonly Color ACCENT    = MainMenuAssets.ACCENT;
    static readonly Color ACCENT_LT = MainMenuAssets.ACCENT_LIGHT;
    static readonly Color LINE      = MainMenuAssets.LINE;
    static readonly Color WARM      = MainMenuAssets.WARM;

    // 禁用态统一压灰半透明（主按钮 SpriteSwap 换"禁用"贴图；白底按钮 ColorTint 直接乘色）
    static readonly Color DisabledTint = new Color(0.80f, 0.85f, 0.89f, 0.6f);

    // 白底按钮（次按钮/图标钮/页签）的悬停配色：相对提亮。
    // 乘法 tint 提不动纯白，所以普通态先压暗一点，悬停回白、按下压蓝；Selected=normal，点过不常亮。
    internal static ColorBlock HoverTint()
    {
        var cb = ColorBlock.defaultColorBlock;
        cb.normalColor      = new Color(0.96f, 0.97f, 0.98f, 1f);
        cb.highlightedColor = Color.white;
        cb.pressedColor     = new Color(0.88f, 0.94f, 1f, 1f);
        cb.selectedColor    = cb.normalColor;
        cb.disabledColor    = DisabledTint;
        return cb;
    }

    // 收集起来的引用（搭完直接塞给 MainMenuUI）
    class Refs
    {
        public UIPanel menuLayer;
        public Button  btnStart, btnLoad, btnChapter, btnOverview, btnSettings, btnQuit;
        public Text    txtProgressHint, txtVersion;

        public UIPanel settingsPanel;
        public Slider  sldMaster, sldBgm, sldSfx, sldVoice, sldTextSpeed, sldAutoDelay;
        public Text    valMaster, valBgm, valSfx, valVoice, valTextSpeed, valAutoDelay;
        public UISwitch swFullscreen, swAutoPlay, swAutoSave;
        public Button  btnSettingsApply, btnSettingsReset, btnSettingsBack;

        public UIPanel     savePanel;
        public SaveSlotUI[] slots = new SaveSlotUI[SaveSystem.SlotCount];
        public Button      btnSaveRead, btnSaveDelete, btnSaveBack;
        public Text        txtSaveTip;

        public UIPanel        chapterPanel;
        public ChapterCardUI[] chapters = new ChapterCardUI[5];
        public Button         btnChapterBack;

        public UIPanel        overviewPanel;
        public Button[]       ovChapterTabs      = new Button[5];
        public Text[]         ovChapterTabLabels = new Text[5];
        public Button[]       ovViewTabs         = new Button[5];
        public Text[]         ovViewTabLabels    = new Text[5];
        public OverviewCardUI[] ovCards          = new OverviewCardUI[4];
        public Text   ovChapterTitle, ovChapterDesc, ovEmptyHint;
        public Button btnOverviewBack;

        public UIPanel imagePanel;
        public Image   bigImage;
        public Text    bigTitle, bigBody, bigCounter;
        public Button  btnBigClose, btnBigPrev, btnBigNext;

        public ConfirmDialog confirm;
        public ToastUI       toast;
    }

    // ================================================================== 主入口
    [MenuItem("Tools/干预项目/搭建主界面UI场景")]
    public static void Run()
    {
        _warn.Clear();
        var log = new List<string>();
        log.Add("主界面（MainMenu）搭建报告   " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("");

        // 1) 贴图 / 字体 / 收录图 / 概览数据
        log.Add("【一】UI 资源");
        MainMenuAssets.GenerateAll(false, log);
        _font = MainMenuAssets.EnsureFont(log);
        MainMenuAssets.CopyOverviewFrames(false, log);
        MainMenuAssets.BuildDatabase(log);
        MainMenuSlices.Run(log);
        PreloadScripts(log);
        log.Add("");

        // 2) 场景：搭 → 存 → 重开自检；如果 Unity 把脚本引用写成了"内联 MonoScript"（缺脚本）就重搭一次
        var sceneLog = new List<string>();
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            sceneLog.Clear();
            if (!BuildScene(sceneLog)) break;
            if (VerifyController(sceneLog)) break;
            if (attempt == 1) log.Add("    ★ 第 1 次保存后脚本引用不对，重搭一次…");
        }

        log.Add("【二】场景");
        log.AddRange(sceneLog);
        log.Add("    " + BuildSettings());
        log.Add("");

        var canvasGO = GameObject.Find("UI_Canvas");
        log.Add("【三】界面结构（层级树）");
        if (canvasGO != null) DumpHierarchy(canvasGO.transform, "", log);
        else log.Add("    ★ 重开场景后找不到 UI_Canvas");
        log.Add("");

        log.Add("【四】自动检查");
        if (canvasGO != null) CheckScene(canvasGO.GetComponent<Canvas>(), log);
        log.Add("");

        log.Add("【五】按钮 / 页面 对照表");
        log.Add("    开始游戏   有新档时先弹确认 → GameProgress.NewGame() → 加载 " + GAME_SCENE);
        log.Add("    读取存档   打开存档页（6 槽，空槽是虚线框）→ 选中 → 读取 → 写入待进入章节 → 加载 " + GAME_SCENE);
        log.Add("    章节选择   5 张章节卡（未解锁显示锁与灰底）→ 点击进入对应章节");
        log.Add("    内容概览   5 章 × 4 处选择；视角筛选 全部/自我/旁观/未来/其他；点卡片看原图（←/→ 翻页，ESC 关闭）");
        log.Add("    设置       总音量/音乐/音效/语音 + 文字速度 + 自动播放间隔 + 全屏/自动播放/自动存档 → PlayerPrefs");
        log.Add("    退出游戏   二次确认 → Application.Quit()");
        log.Add("    ESC        逐层关闭：确认弹窗 → 大图 → 概览 → 章节 → 存档 → 设置");
        log.Add("");

        log.Add("【六】还没做的（留给后续）");
        log.Add("    · 游戏内 UI（对话框/选项/手机界面/系统菜单）不在此场景，属于 Game 场景");
        log.Add("    · 存档缩略图：SaveData.thumbnail 字段留着，等游戏内自动截图写入");
        log.Add("    · 按钮音效：Assets/assets/04_音效_Audio 里只有开关机声，UI 点击音待补");
        log.Add("    · 内容概览的概述文案取自剧本原文，最终请和 20 张收录图逐一核对");

        Directory.CreateDirectory(Path.GetDirectoryName(REPORT).Replace('/', Path.DirectorySeparatorChar));
        File.WriteAllText(REPORT, string.Join("\n", log.ToArray()));
        Debug.Log("[MainMenuBuilder] 完成：" + OUT_SCENE + "，报告 " + REPORT);
    }

    /// 预热 UI 脚本的 MonoScript。
    /// ★ 坑：如果 .cs 是新导入/刚改过，MonoScript.GetClass() 可能是 null，
    ///   这时 AddComponent 出来的组件会被 Unity 写成"内联 MonoScript"（= 新会话里就是缺脚本，菜单全死）。
    ///   强制重导一次脚本就能修好，必须卡在搭场景之前做。
    static void PreloadScripts(List<string> log)
    {
        AssetDatabase.Refresh();
        var paths = new List<string>();
        if (Directory.Exists("Assets/Scripts/UI"))
            paths.AddRange(Directory.GetFiles("Assets/Scripts/UI", "*.cs", SearchOption.AllDirectories));
        paths.Add("Assets/Editor/MainMenuBuilder.cs");

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
            if (mono == null || mono.GetClass() == null) Warn("脚本类型还没编译进来：" + p);
            else ok++;
        }
        log.Add("脚本预热：" + ok + "/" + paths.Count + " 个 MonoScript 就绪" +
                (fixedCount > 0 ? "（其中 " + fixedCount + " 个强制重导过一次）" : ""));
    }

    /// 搭一次场景并保存（成功返回 true）
    static bool BuildScene(List<string> log)
    {
        if (!Application.isBatchMode)
        {
            var cur = EditorSceneManager.GetActiveScene();
            if (cur.IsValid() && cur.isDirty && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("[MainMenuBuilder] 用户取消，已中止");
                return false;
            }
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RenderSettings.ambientMode  = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.86f, 0.90f, 0.90f);

        BuildCamera();
        BuildEventSystem();
        var canvas = BuildCanvas();
        var R = BuildUI(canvas.transform);
        var ui = WireController(canvas.gameObject, R);
        // 概览数据一定要在"切场景之后"重新 Load：CreateAsset/Refresh/切场景都可能把内存实例变成 fake-null，
        // 那样序列化到场景里的就是空引用（运行时会一片空白）。
        ui.overviewDb = MainMenuAssets.LoadOverviewDb();
        if (ui.overviewDb == null) Warn("概览数据库没生成，内容概览会空着");

        // 弹层保存成展开状态：在编辑器里打开场景就能直接看到、直接手改每个子页。
        // 运行时不受影响——UIPanel.Awake 按 openOnStart（子页都是 false）重新收起。
        var pages = new[] { R.settingsPanel, R.savePanel, R.chapterPanel, R.overviewPanel, R.imagePanel, R.confirm == null ? null : R.confirm.panel };
        foreach (var p in pages)
        {
            if (p == null) continue;
            p.Show(true);   // Apply(1)：根 CanvasGroup 和遮罩一起置为不透明、可交互
        }

        // 让编辑器里也能直接看到真实内容（运行时会再刷一遍，幂等）
        ui.RefreshProgressHint();
        ui.RefreshSlots();
        ui.RefreshChapters();
        ui.RefreshOverview();
        ui.SyncSettingsUI();

        Directory.CreateDirectory("Assets/Scenes");
        AssetDatabase.Refresh();
        bool ok = EditorSceneManager.SaveScene(scene, OUT_SCENE);
        AssetDatabase.Refresh();
        log.Add("    保存：" + OUT_SCENE + "  " + (ok ? "成功" : "★失败"));
        return ok;
    }

    /// 重新打开刚存的场景，确认 MainMenuUI 真的能取到；
    /// 如果 Unity 把组件写成了"内联 MonoScript"（新会话里就是缺脚本），重开后再存一次就会改回 guid 引用。
    static bool VerifyController(List<string> log)
    {
        var scene = EditorSceneManager.OpenScene(OUT_SCENE, OpenSceneMode.Single);
        var go = GameObject.Find("UI_Canvas");
        if (go == null) { log.Add("    ★ 自检：重开场景后找不到 UI_Canvas"); return false; }
        var ui = go.GetComponent<MainMenuUI>();
        if (ui == null)
        {
            log.Add("    ★ 自检：MainMenuUI 引用丢失（Unity 写成了内联 MonoScript，运行时会缺脚本）");
            Warn("MainMenuUI 脚本引用丢失，已重搭");
            return false;
        }

        bool wired = ui.settingsPanel != null && ui.overviewPanel != null && ui.confirm != null;
        bool dbOk = ui.overviewDb != null;
        log.Add("    自检（重开场景后）：MainMenuUI ✓  页面接线 " + (wired ? "✓" : "★断") +
                "  概览数据 " + (dbOk ? "✓" : "★空"));

        // 再存一次，把脚本引用从"内联 MonoScript"落成正常的 guid 引用
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, OUT_SCENE);
        AssetDatabase.Refresh();
        log.Add("    二次保存（修脚本引用）：已存 " + OUT_SCENE);
        return wired && dbOk;
    }

    // ================================================================== 相机 / 事件系统 / 画布
    static void BuildCamera()
    {
        var go = new GameObject("Main Camera");
        go.tag = "MainCamera";
        go.transform.position = new Vector3(0f, 0f, -10f);
        var cam = go.AddComponent<Camera>();
        cam.clearFlags      = CameraClearFlags.SolidColor;
        cam.backgroundColor = MainMenuAssets.BG_MID;
        cam.orthographic    = true;
        cam.orthographicSize = 5.4f;
        cam.nearClipPlane   = 0.1f;
        cam.farClipPlane    = 100f;
        go.AddComponent<AudioListener>();
    }

    static void BuildEventSystem()
    {
        var go = new GameObject("EventSystem");
        go.AddComponent<UnityEngine.EventSystems.EventSystem>();
        go.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
    }

    static Canvas BuildCanvas()
    {
        var go = new GameObject("UI_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // 编辑器里把画布尺寸写成设计稿大小：Scene 视图能直接看到 1920×1080 的框，运行时会由 CanvasScaler 接管
        var crt = (RectTransform)go.transform;
        crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
        crt.pivot = new Vector2(0.5f, 0.5f);
        crt.anchoredPosition = Vector2.zero;
        crt.sizeDelta = new Vector2(1920f, 1080f);
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight  = 0.5f;
        scaler.referencePixelsPerUnit = 100f;
        return canvas;
    }

    // ================================================================== 整套 UI
    static Refs BuildUI(Transform canvas)
    {
        var R = new Refs();

        BuildBackground(canvas);
        R.menuLayer = BuildMenuLayer(canvas, R);
        R.settingsPanel = BuildSettingsPage(canvas, R);
        R.savePanel     = BuildSavePage(canvas, R);
        R.chapterPanel  = BuildChapterPage(canvas, R);
        R.overviewPanel = BuildOverviewPage(canvas, R);
        R.imagePanel    = BuildImageViewer(canvas, R);
        R.confirm       = BuildConfirm(canvas);
        R.toast         = BuildToast(canvas);

        return R;
    }

    // ------------------------------------------------------------------ 背景
    static void BuildBackground(Transform canvas)
    {
        var bg = Node("BG_背景", canvas);
        Stretch(bg);
        var img = bg.AddComponent<Image>();
        img.sprite = MainMenuAssets.Sprite(MainMenuAssets.Pick("稿_背景_主菜单", "bg_渐变"));
        img.color  = Color.white;
        img.raycastTarget = false;
        if (img.sprite == null) Warn("缺背景贴图 bg_渐变");

        var deco = Node("BG_装饰", canvas);
        Stretch(deco);

        bool artBg = MainMenuAssets.Sprite("稿_背景_主菜单") != null;
        if (artBg)
        {
            // 用《ui素材》的背景稿：稿子自带氛围，只补一块柔光把中间压亮，保证文字可读
            Img(deco.transform, "光斑_中央", "bg_光斑", new Vector2(0f, -40f), new Vector2(1500f, 1000f), new Color(1f, 1f, 1f, 0.30f));
            Img(deco.transform, "光斑_右下", "bg_光斑", new Vector2(700f, -360f), new Vector2(560f, 560f), new Color(1f, 1f, 1f, 0.35f));
        }
        else
        {
            Img(deco.transform, "光斑_左上", "bg_光斑", new Vector2(-620f, 380f), new Vector2(720f, 720f), new Color(1f, 1f, 1f, 0.75f));
            Img(deco.transform, "光斑_右下", "bg_光斑", new Vector2(760f, -300f), new Vector2(560f, 560f), new Color(1f, 1f, 1f, 0.55f));
            Img(deco.transform, "云_1", "bg_云", new Vector2(-560f, 120f), new Vector2(520f, 292f), new Color(1f, 1f, 1f, 0.85f));
            Img(deco.transform, "云_2", "bg_云", new Vector2(640f, 250f), new Vector2(360f, 202f), new Color(1f, 1f, 1f, 0.65f));
            Img(deco.transform, "云_3", "bg_云", new Vector2(280f, -430f), new Vector2(640f, 360f), new Color(1f, 1f, 1f, 0.55f));
            Img(deco.transform, "光点_1", "bg_光点", new Vector2(-780f, -180f), new Vector2(60f, 60f), new Color(1f, 1f, 1f, 0.9f));
            Img(deco.transform, "光点_2", "bg_光点", new Vector2(830f, 60f), new Vector2(44f, 44f), new Color(1f, 1f, 1f, 0.9f));
            Img(deco.transform, "光点_3", "bg_光点", new Vector2(-360f, 460f), new Vector2(36f, 36f), new Color(1f, 1f, 1f, 0.8f));
            Img(deco.transform, "光点_4", "bg_光点", new Vector2(500f, -180f), new Vector2(28f, 28f), new Color(1f, 1f, 1f, 0.8f));
        }
    }

    // ------------------------------------------------------------------ 主菜单层
    static UIPanel BuildMenuLayer(Transform canvas, Refs R)
    {
        var root = Node("Layer_主菜单", canvas);
        Stretch(root);
        var panel = root.AddComponent<UIPanel>();
        panel.openOnStart = true;
        panel.introFade   = true;
        panel.duration    = 0.45f;
        panel.fromScale   = 0.99f;
        panel.fromOffsetY = -18f;

        Img(root.transform, "Logo_徽标", "Logo_徽标", new Vector2(0f, 405f), new Vector2(132f, 132f));
        Label(root.transform, "Logo_标题", "干 预", new Vector2(0f, 270f), new Vector2(900f, 130f), 108,
              INK, TextAnchor.MiddleCenter, FontStyle.Bold);
        Label(root.transform, "Logo_英文", "A N X I E T Y   I N T E R V E N T I O N", new Vector2(0f, 185f), new Vector2(900f, 30f), 20,
              MUTED, TextAnchor.MiddleCenter);
        Label(root.transform, "Logo_标语", "一段关于焦虑、勇气与自我和解的旅程", new Vector2(0f, 140f), new Vector2(900f, 38f), 26,
              INK_SOFT, TextAnchor.MiddleCenter);
        Img(root.transform, "装饰_分隔线", "分隔线", new Vector2(0f, 96f), new Vector2(240f, 6f), new Color(1f, 1f, 1f, 0.9f));

        R.txtProgressHint = Label(root.transform, "提示_进度", "还没有存档 · 将从第 1 章开始",
            new Vector2(0f, 52f), new Vector2(900f, 28f), 20, MUTED, TextAnchor.MiddleCenter);

        // 六个入口
        R.btnStart = Btn(root.transform, "Btn_开始游戏", "开始游戏", new Vector2(0f, -20f), new Vector2(420f, 76f), true, out _, 32, "图标_播放");
        float y = -104f;
        R.btnLoad     = Btn(root.transform, "Btn_读取存档", "读取存档", new Vector2(0f, y),       new Vector2(420f, 64f), false, out _, 28, "图标_存档"); y -= 70f;
        R.btnChapter  = Btn(root.transform, "Btn_章节选择", "章节选择", new Vector2(0f, y),       new Vector2(420f, 64f), false, out _, 28, "图标_章节"); y -= 70f;
        R.btnOverview = Btn(root.transform, "Btn_内容概览", "内容概览", new Vector2(0f, y),       new Vector2(420f, 64f), false, out _, 28, "图标_概览"); y -= 70f;
        R.btnSettings = Btn(root.transform, "Btn_设置",     "设置",     new Vector2(0f, y),       new Vector2(420f, 64f), false, out _, 28, "图标_设置"); y -= 70f;
        R.btnQuit     = Btn(root.transform, "Btn_退出游戏", "退出游戏", new Vector2(0f, y),       new Vector2(420f, 64f), false, out _, 28, "图标_退出");

        R.txtVersion = Label(root.transform, "底部_版本", "v0.1 · Demo 版", Vector2.zero, new Vector2(400f, 26f), 18, MUTED, TextAnchor.MiddleLeft);
        Corner(R.txtVersion.gameObject, new Vector2(52f, 38f), new Vector2(400f, 26f), new Vector2(0f, 0f), new Vector2(0f, 0f));

        var hint = Label(root.transform, "底部_提示", "点击「开始游戏」进入第 1 章 · 做出选择后会自动存档",
            new Vector2(0f, 38f), new Vector2(900f, 26f), 18, MUTED, TextAnchor.MiddleCenter);
        Corner(hint.gameObject, new Vector2(0f, 38f), new Vector2(900f, 26f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));

        var copy = Label(root.transform, "底部_版权", "焦虑干预 · 剧情 Demo · 2026",
            Vector2.zero, new Vector2(500f, 26f), 18, MUTED, TextAnchor.MiddleRight);
        Corner(copy.gameObject, new Vector2(-52f, 38f), new Vector2(500f, 26f), new Vector2(1f, 0f), new Vector2(1f, 0f));

        return panel;
    }

    // ------------------------------------------------------------------ 设置页
    static UIPanel BuildSettingsPage(Transform canvas, Refs R)
    {
        RectTransform card; Button close;
        var panel = Page(canvas, "Page_设置", new Vector2(1000f, 780f), "设置", true, out card, out close);

        string[] names = { "总音量", "背景音乐", "音效", "台词语音" };
        string[] icons = { "图标_音量", "图标_音乐", "图标_音效", "图标_语音" };
        float[]  ys    = { 250f, 190f, 130f, 70f };
        var sliders = new Slider[4]; var values = new Text[4];
        for (int i = 0; i < 4; i++)
        {
            Img(card, "图标_" + names[i], icons[i], new Vector2(-430f, ys[i]), new Vector2(30f, 30f), ACCENT);
            Label(card, "名称_" + names[i], names[i], new Vector2(-305f, ys[i]), new Vector2(200f, 30f), 22, INK, TextAnchor.MiddleLeft);
            sliders[i] = Sld(card, "滑条_" + names[i], new Vector2(40f, ys[i]), new Vector2(420f, 30f));
            values[i]  = Label(card, "值_" + names[i], "80%", new Vector2(320f, ys[i]), new Vector2(80f, 30f), 20, ACCENT, TextAnchor.MiddleRight);
        }
        R.sldMaster = sliders[0]; R.sldBgm = sliders[1]; R.sldSfx = sliders[2]; R.sldVoice = sliders[3];
        R.valMaster = values[0];  R.valBgm = values[1];  R.valSfx = values[2];  R.valVoice = values[3];

        Img(card, "分隔线_1", "分隔线", new Vector2(0f, 25f), new Vector2(880f, 6f), new Color(1f, 1f, 1f, 0.85f));

        Img(card, "图标_文字速度", "图标_消息", new Vector2(-430f, -25f), new Vector2(30f, 30f), ACCENT);
        Label(card, "名称_文字速度", "文字速度", new Vector2(-305f, -25f), new Vector2(200f, 30f), 22, INK, TextAnchor.MiddleLeft);
        R.sldTextSpeed = Sld(card, "滑条_文字速度", new Vector2(40f, -25f), new Vector2(420f, 30f));
        R.valTextSpeed = Label(card, "值_文字速度", "55%", new Vector2(320f, -25f), new Vector2(80f, 30f), 20, ACCENT, TextAnchor.MiddleRight);

        Img(card, "图标_自动间隔", "图标_自动", new Vector2(-430f, -85f), new Vector2(30f, 30f), ACCENT);
        Label(card, "名称_自动间隔", "自动播放间隔", new Vector2(-305f, -85f), new Vector2(200f, 30f), 22, INK, TextAnchor.MiddleLeft);
        R.sldAutoDelay = Sld(card, "滑条_自动间隔", new Vector2(40f, -85f), new Vector2(420f, 30f));
        R.valAutoDelay = Label(card, "值_自动间隔", "50%", new Vector2(320f, -85f), new Vector2(80f, 30f), 20, ACCENT, TextAnchor.MiddleRight);

        Img(card, "分隔线_2", "分隔线", new Vector2(0f, -128f), new Vector2(880f, 6f), new Color(1f, 1f, 1f, 0.85f));

        string[] swNames = { "全屏显示", "剧情自动播放", "选择后自动存档" };
        float[]  swY     = { -180f, -240f, -300f };
        var sws = new UISwitch[3];
        for (int i = 0; i < 3; i++)
        {
            Label(card, "名称_" + swNames[i], swNames[i], new Vector2(-305f, swY[i]), new Vector2(260f, 30f), 22, INK, TextAnchor.MiddleLeft);
            sws[i] = Switch(card, "开关_" + swNames[i], new Vector2(320f, swY[i]));
        }
        R.swFullscreen = sws[0]; R.swAutoPlay = sws[1]; R.swAutoSave = sws[2];

        R.btnSettingsReset = Btn(card, "Btn_恢复默认", "恢复默认", new Vector2(-250f, -352f), new Vector2(220f, 54f), false, out _, 24, "图标_刷新");
        R.btnSettingsApply = Btn(card, "Btn_应用",     "应用",     new Vector2(0f, -352f),    new Vector2(220f, 58f), true,  out _, 26, "图标_对勾");
        R.btnSettingsBack  = Btn(card, "Btn_返回",     "返回",     new Vector2(250f, -352f),  new Vector2(220f, 54f), false, out _, 24, "图标_返回");
        return panel;
    }

    // ------------------------------------------------------------------ 存读档页
    static UIPanel BuildSavePage(Transform canvas, Refs R)
    {
        RectTransform card; Button close;
        var panel = Page(canvas, "Page_存读档", new Vector2(1280f, 800f), "存档 / 读档", true, out card, out close);

        R.txtSaveTip = Label(card, "提示_存档", "还没有任何存档 · 游戏里做出选择后会自动存档",
            new Vector2(-290f, 300f), new Vector2(620f, 28f), 20, MUTED, TextAnchor.MiddleLeft);

        for (int i = 0; i < SaveSystem.SlotCount; i++)
        {
            int col = i % 3, row = i / 3;
            R.slots[i] = BuildSlot(card, i, new Vector2(-384f + col * 384f, 170f - row * 230f));
        }

        R.btnSaveRead   = Btn(card, "Btn_读取", "读取", new Vector2(-430f, -330f), new Vector2(200f, 60f), true,  out _, 26, "图标_存档");
        R.btnSaveDelete = Btn(card, "Btn_删除", "删除", new Vector2(-210f, -330f), new Vector2(200f, 56f), false, out _, 24, "图标_关闭");
        R.btnSaveBack   = Btn(card, "Btn_返回", "返回", new Vector2(510f, -330f),  new Vector2(200f, 56f), false, out _, 24, "图标_返回");

        Label(card, "提示_说明", "存档 6 个槽位；做出选择后自动存档会覆盖最近一次的进度",
            new Vector2(-290f, -330f), new Vector2(460f, 26f), 18, MUTED, TextAnchor.MiddleLeft);
        return panel;
    }

    static SaveSlotUI BuildSlot(Transform parent, int index, Vector2 pos)
    {
        var go = Node("槽位_" + (index + 1), parent);
        At(go, pos, new Vector2(360f, 200f));

        var empty  = Img(go.transform, "空框",   "槽位_空",   Vector2.zero, new Vector2(360f, 200f));
        var filled = Img(go.transform, "实底",   MainMenuAssets.Pick("稿_卡片_1", "卡片_普通"), Vector2.zero, new Vector2(360f, 200f));
        var sel    = Img(go.transform, "选中框", MainMenuAssets.Pick("稿_卡片_2", "卡片_选中"), Vector2.zero, new Vector2(360f, 200f), new Color(1f, 1f, 1f, 0.9f));
        sel.enabled = false;

        var thumb = Img(go.transform, "缩略图", MainMenuAssets.Pick("稿_卡片_2", "卡片_悬停"), new Vector2(0f, 22f), new Vector2(316f, 140f));
        thumb.enabled = false;

        var chapter = Label(go.transform, "章节", "", new Vector2(0f, -52f), new Vector2(320f, 30f), 22, INK, TextAnchor.MiddleCenter);
        var time    = Label(go.transform, "时间", "", new Vector2(0f, -80f), new Vector2(320f, 26f), 18, MUTED, TextAnchor.MiddleCenter);
        var emptyT  = Label(go.transform, "空存档", "空存档", Vector2.zero, new Vector2(300f, 40f), 24, MUTED, TextAnchor.MiddleCenter);

        var hit = Img(go.transform, "点击区", "遮罩_白", Vector2.zero, new Vector2(360f, 200f), new Color(1f, 1f, 1f, 0f));
        hit.raycastTarget = true;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = hit;
        btn.transition = Selectable.Transition.None;
        go.AddComponent<UIHoverScale>().hoverScale = 1.02f;

        var ui = go.AddComponent<SaveSlotUI>();
        ui.button = btn; ui.thumb = thumb; ui.frameEmpty = empty; ui.frameFilled = filled; ui.selectFrame = sel;
        ui.chapterText = chapter; ui.timeText = time; ui.emptyText = emptyT; ui.index = index;
        return ui;
    }

    // ------------------------------------------------------------------ 章节页
    static UIPanel BuildChapterPage(Transform canvas, Refs R)
    {
        RectTransform card; Button close;
        var panel = Page(canvas, "Page_章节选择", new Vector2(1520f, 660f), "章节选择", true, out card, out close);

        for (int i = 0; i < 5; i++)
            R.chapters[i] = BuildChapterCard(card, i, new Vector2(-584f + i * 292f, -20f));

        Label(card, "提示_章节", "未解锁的章节需要先通关上一章；已完成标 ✦", new Vector2(-420f, -276f), new Vector2(560f, 28f), 19, MUTED, TextAnchor.MiddleLeft);
        R.btnChapterBack = Btn(card, "Btn_返回", "返回", new Vector2(600f, -276f), new Vector2(200f, 56f), false, out _, 24, "图标_返回");
        return panel;
    }

    static ChapterCardUI BuildChapterCard(Transform parent, int index, Vector2 pos)
    {
        var go = Node("章节_" + (index + 1), parent);
        At(go, pos, new Vector2(276f, 430f));

        var bottom = Img(go.transform, "底", MainMenuAssets.Pick("稿_卡片_1", "卡片_普通"), Vector2.zero, new Vector2(276f, 430f));
        var lockOv = Img(go.transform, "锁定遮罩", MainMenuAssets.Pick("稿_卡片_1", "卡片_普通"), Vector2.zero, new Vector2(276f, 430f), new Color(0.86f, 0.90f, 0.94f, 0.74f));

        var num   = Label(go.transform, "编号", "01", new Vector2(0f, 145f), new Vector2(240f, 96f), 64, ACCENT_LT, TextAnchor.MiddleCenter, FontStyle.Bold);
        var title = Label(go.transform, "标题", "", new Vector2(0f, 56f), new Vector2(240f, 76f), 26, INK, TextAnchor.MiddleCenter);
        var desc  = Label(go.transform, "描述", "", new Vector2(0f, -14f), new Vector2(236f, 80f), 18, MUTED, TextAnchor.UpperCenter);
        var icon  = Img(go.transform, "状态图标", "图标_对勾", new Vector2(0f, -100f), new Vector2(38f, 38f), ACCENT);
        var state = Label(go.transform, "状态", "", new Vector2(0f, -145f), new Vector2(240f, 30f), 20, ACCENT, TextAnchor.MiddleCenter);

        var hit = Img(go.transform, "点击区", "遮罩_白", Vector2.zero, new Vector2(276f, 430f), new Color(1f, 1f, 1f, 0f));
        hit.raycastTarget = true;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = hit;
        btn.transition = Selectable.Transition.None;
        go.AddComponent<UIHoverScale>();

        var ui = go.AddComponent<ChapterCardUI>();
        ui.button = btn; ui.numText = num; ui.titleText = title; ui.descText = desc;
        ui.stateText = state; ui.stateIcon = icon; ui.lockOverlay = lockOv.gameObject; ui.cardImage = bottom;
        ui.chapter = index + 1;
        return ui;
    }

    // ------------------------------------------------------------------ 内容概览页
    static UIPanel BuildOverviewPage(Transform canvas, Refs R)
    {
        RectTransform card; Button close;
        var panel = Page(canvas, "Page_内容概览", new Vector2(1600f, 900f), "内容概览", false, out card, out close);

        R.ovChapterTitle = Label(card, "章节标题", "第1章 · 小组作业的汇报", new Vector2(-340f, 330f), new Vector2(760f, 40f), 30, INK, TextAnchor.MiddleLeft, FontStyle.Bold);
        R.ovChapterDesc  = Label(card, "章节描述", "", new Vector2(-340f, 292f), new Vector2(760f, 30f), 19, MUTED, TextAnchor.MiddleLeft);
        R.ovEmptyHint    = Label(card, "未玩到提示", "", new Vector2(440f, 296f), new Vector2(560f, 28f), 19, WARM, TextAnchor.MiddleRight);

        // 章页签
        string[] chNames = { "第1章", "第2章", "第3章", "第4章", "第5章" };
        for (int i = 0; i < 5; i++)
        {
            Text label;
            R.ovChapterTabs[i] = Tab(card, "章页签_" + (i + 1), chNames[i], new Vector2(-632f + i * 188f, 244f), new Vector2(176f, 52f), out label, 22);
            R.ovChapterTabLabels[i] = label;
        }
        // 视角页签
        string[] viewNames = OverviewDatabase.Views;
        for (int i = 0; i < viewNames.Length; i++)
        {
            Text label;
            R.ovViewTabs[i] = Tab(card, "视角页签_" + viewNames[i], viewNames[i], new Vector2(-660f + i * 128f, 186f), new Vector2(120f, 44f), out label, 20);
            R.ovViewTabLabels[i] = label;
        }

        // 4 张选择卡
        Vector2[] cardPos = { new Vector2(-380f, 10f), new Vector2(380f, 10f), new Vector2(-380f, -285f), new Vector2(380f, -285f) };
        for (int i = 0; i < 4; i++) R.ovCards[i] = BuildOverviewCard(card, i, cardPos[i]);

        R.btnOverviewBack = Btn(card, "Btn_返回", "返回", new Vector2(700f, 382f), new Vector2(150f, 50f), false, out _, 24, "图标_返回");
        return panel;
    }

    static OverviewCardUI BuildOverviewCard(Transform parent, int index, Vector2 pos)
    {
        var go = Node("选择_" + (index + 1), parent);
        At(go, pos, new Vector2(736f, 270f));

        var bottom = Img(go.transform, "底", MainMenuAssets.Pick("稿_卡片_1", "卡片_普通"), Vector2.zero, new Vector2(736f, 270f));
        var pic    = Img(go.transform, "配图", MainMenuAssets.Pick("稿_卡片_2", "卡片_悬停"), new Vector2(-158f, 8f), new Vector2(380f, 214f));
        pic.preserveAspect = false;

        var chip   = Img(go.transform, "视角底", MainMenuAssets.Pick("稿_列表_选中", "页签_选中"), new Vector2(95f, 88f), new Vector2(90f, 34f));
        var view   = Label(go.transform, "视角", "自我", new Vector2(95f, 88f), new Vector2(90f, 34f), 19, ACCENT, TextAnchor.MiddleCenter);
        var title  = Label(go.transform, "标题", "", new Vector2(200f, 40f), new Vector2(320f, 34f), 23, INK, TextAnchor.MiddleLeft);
        var body   = Label(go.transform, "概述", "", new Vector2(200f, -40f), new Vector2(320f, 130f), 18, INK_SOFT, TextAnchor.UpperLeft, FontStyle.Normal, 1.25f);

        var sel = Img(go.transform, "选中框", MainMenuAssets.Pick("稿_卡片_2", "卡片_选中"), Vector2.zero, new Vector2(736f, 270f), new Color(1f, 1f, 1f, 0.9f));
        sel.enabled = false;

        var hit = Img(go.transform, "点击区", "遮罩_白", Vector2.zero, new Vector2(736f, 270f), new Color(1f, 1f, 1f, 0f));
        hit.raycastTarget = true;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = hit;
        btn.transition = Selectable.Transition.None;
        go.AddComponent<UIHoverScale>().hoverScale = 1.012f;

        var ui = go.AddComponent<OverviewCardUI>();
        ui.button = btn; ui.image = pic; ui.titleText = title; ui.viewText = view; ui.bodyText = body; ui.selectFrame = sel;
        ui.index = index;
        return ui;
    }

    // ------------------------------------------------------------------ 大图 / 确认 / Toast
    static UIPanel BuildImageViewer(Transform canvas, Refs R)
    {
        var root = Node("Page_大图", canvas);
        Stretch(root);
        var panel = root.AddComponent<UIPanel>();
        panel.openOnStart = false;
        panel.duration    = 0.16f;

        var dim = Node("遮罩", root.transform);
        Stretch(dim);
        var dimImg = dim.AddComponent<Image>();
        dimImg.sprite = MainMenuAssets.Sprite("遮罩_白");
        var deep = MainMenuAssets.MASK_DEEP;                    // 颜色取自 ui-004「加深遮罩」
        dimImg.color  = new Color(deep.r, deep.g, deep.b, 0.92f);
        dimImg.raycastTarget = true;
        panel.dimmer = dim;

        R.bigTitle   = Label(root.transform, "标题", "", new Vector2(0f, 470f), new Vector2(1400f, 44f), 28, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
        R.bigImage   = Img(root.transform, "图", "遮罩_白", new Vector2(0f, 10f), new Vector2(1480f, 820f), Color.white);
        R.bigImage.preserveAspect = true;
        R.bigBody    = Label(root.transform, "说明", "", new Vector2(0f, -462f), new Vector2(1400f, 60f), 20, new Color(1f, 1f, 1f, 0.88f), TextAnchor.UpperCenter, FontStyle.Normal, 1.2f);
        R.bigCounter = Label(root.transform, "计数", "1 / 4", new Vector2(0f, -508f), new Vector2(300f, 26f), 18, new Color(1f, 1f, 1f, 0.7f));

        R.btnBigPrev  = IconBtn(root.transform, "Btn_上一张", "图标_回退", Vector2.zero, 64f);
        Corner(R.btnBigPrev.gameObject, new Vector2(104f, 10f), new Vector2(64f, 64f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f));
        R.btnBigNext  = IconBtn(root.transform, "Btn_下一张", "图标_播放", Vector2.zero, 64f);
        Corner(R.btnBigNext.gameObject, new Vector2(-104f, 10f), new Vector2(64f, 64f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f));
        R.btnBigClose = IconBtn(root.transform, "Btn_关闭",   "图标_关闭", Vector2.zero, 56f);
        Corner(R.btnBigClose.gameObject, new Vector2(-28f, -28f), new Vector2(56f, 56f), new Vector2(1f, 1f), new Vector2(1f, 1f));
        return panel;
    }

    static ConfirmDialog BuildConfirm(Transform canvas)
    {
        var root = Node("Popup_确认", canvas);
        Stretch(root);
        var panel = root.AddComponent<UIPanel>();
        panel.openOnStart = false;
        panel.duration    = 0.14f;

        var dim = Node("遮罩", root.transform);
        Stretch(dim);
        var dimImg = dim.AddComponent<Image>();
        dimImg.sprite = MainMenuAssets.Sprite("遮罩_白");
        var mc = MainMenuAssets.MASK_DEEP;
        dimImg.color  = new Color(mc.r, mc.g, mc.b, 0.78f);
        dimImg.raycastTarget = true;
        panel.dimmer = dim;

        var cardGO = Node("卡片", root.transform);
        At(cardGO, Vector2.zero, new Vector2(680f, 340f));
        var cardImg = cardGO.AddComponent<Image>();
        cardImg.sprite = MainMenuAssets.Sprite(MainMenuAssets.Pick("稿_面板_弹窗", "面板_亮"));
        cardImg.type = Image.Type.Sliced;
        cardImg.raycastTarget = true;

        var title = Label(cardGO.transform, "标题", "确认", new Vector2(0f, 100f), new Vector2(560f, 44f), 30, INK, TextAnchor.MiddleCenter, FontStyle.Bold);
        var body  = Label(cardGO.transform, "正文", "", new Vector2(0f, 10f), new Vector2(580f, 120f), 22, INK_SOFT, TextAnchor.MiddleCenter, FontStyle.Normal, 1.25f);

        Text cancelLabel, okLabel;
        var cancel = Btn(cardGO.transform, "Btn_取消", "取消", new Vector2(-120f, -110f), new Vector2(200f, 56f), false, out cancelLabel, 24);
        var ok     = Btn(cardGO.transform, "Btn_确定", "确定", new Vector2(120f, -110f),  new Vector2(200f, 60f), true,  out okLabel, 26);

        var cd = root.AddComponent<ConfirmDialog>();
        cd.panel = panel; cd.titleText = title; cd.bodyText = body;
        cd.okButton = ok; cd.okLabel = okLabel; cd.cancelButton = cancel; cd.cancelLabel = cancelLabel;
        return cd;
    }

    static ToastUI BuildToast(Transform canvas)
    {
        var root = Node("Toast", canvas);
        At(root, new Vector2(0f, 400f), new Vector2(640f, 80f));
        var cg = root.AddComponent<CanvasGroup>();
        cg.alpha = 0f;                 // 场景里默认是隐藏的（ToastUI.Awake 也会置 0，这里是为了编辑器里打开就能看对）
        cg.blocksRaycasts = false;
        cg.interactable   = false;

        var body = Img(root.transform, "内容", "面板_暗", Vector2.zero, new Vector2(640f, 68f));
        var icon = Img(root.transform, "图标", "图标_对勾", new Vector2(-282f, 0f), new Vector2(26f, 26f), new Color(1f, 1f, 1f, 0.92f));
        var text = Label(root.transform, "文字", "", new Vector2(20f, 0f), new Vector2(540f, 40f), 22, Color.white, TextAnchor.MiddleLeft);

        var toast = root.AddComponent<ToastUI>();
        toast.group = cg;
        toast.body  = (RectTransform)body.transform;
        toast.text  = text;
        toast.icon  = icon;
        return toast;
    }

    // ================================================================== 接线
    static MainMenuUI WireController(GameObject canvasGO, Refs R)
    {
        var ui = canvasGO.AddComponent<MainMenuUI>();
        ui.menuLayer     = R.menuLayer;
        ui.settingsPanel = R.settingsPanel;
        ui.savePanel     = R.savePanel;
        ui.chapterPanel  = R.chapterPanel;
        ui.overviewPanel = R.overviewPanel;
        ui.imagePanel    = R.imagePanel;
        ui.confirm       = R.confirm;
        ui.toast         = R.toast;

        ui.btnStart = R.btnStart; ui.btnLoad = R.btnLoad; ui.btnChapter = R.btnChapter;
        ui.btnOverview = R.btnOverview; ui.btnSettings = R.btnSettings; ui.btnQuit = R.btnQuit;
        ui.txtProgressHint = R.txtProgressHint; ui.txtVersion = R.txtVersion;

        ui.sldMaster = R.sldMaster; ui.sldBgm = R.sldBgm; ui.sldSfx = R.sldSfx; ui.sldVoice = R.sldVoice;
        ui.sldTextSpeed = R.sldTextSpeed; ui.sldAutoDelay = R.sldAutoDelay;
        ui.valMaster = R.valMaster; ui.valBgm = R.valBgm; ui.valSfx = R.valSfx; ui.valVoice = R.valVoice;
        ui.valTextSpeed = R.valTextSpeed; ui.valAutoDelay = R.valAutoDelay;
        ui.swFullscreen = R.swFullscreen; ui.swAutoPlay = R.swAutoPlay; ui.swAutoSave = R.swAutoSave;
        ui.btnSettingsApply = R.btnSettingsApply; ui.btnSettingsReset = R.btnSettingsReset; ui.btnSettingsBack = R.btnSettingsBack;

        ui.slots = R.slots; ui.btnSaveRead = R.btnSaveRead; ui.btnSaveDelete = R.btnSaveDelete;
        ui.btnSaveBack = R.btnSaveBack; ui.txtSaveTip = R.txtSaveTip;

        ui.chapters = R.chapters; ui.btnChapterBack = R.btnChapterBack;

        ui.overviewDb = MainMenuAssets.LoadOverviewDb();
        ui.ovChapterTabs = R.ovChapterTabs; ui.ovChapterTabLabels = R.ovChapterTabLabels;
        ui.ovViewTabs = R.ovViewTabs; ui.ovViewTabLabels = R.ovViewTabLabels;
        ui.ovCards = R.ovCards;
        ui.ovChapterTitle = R.ovChapterTitle; ui.ovChapterDesc = R.ovChapterDesc; ui.ovEmptyHint = R.ovEmptyHint;
        ui.btnOverviewBack = R.btnOverviewBack;
        ui.tabNormalSprite   = MainMenuAssets.Sprite("页签_普通");
        ui.tabSelectedSprite = MainMenuAssets.Sprite("页签_选中");

        ui.bigImage = R.bigImage; ui.bigTitle = R.bigTitle; ui.bigBody = R.bigBody; ui.bigCounter = R.bigCounter;
        ui.btnBigClose = R.btnBigClose; ui.btnBigPrev = R.btnBigPrev; ui.btnBigNext = R.btnBigNext;

        ui.iconCheck = MainMenuAssets.Sprite("图标_对勾");
        ui.iconLock  = MainMenuAssets.Sprite("图标_锁");

        if (ui.overviewDb == null) Warn("概览数据库引用为空");
        return ui;
    }

    // ================================================================== 小工具
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

    static void SetSliced(Image img)
    {
        if (img == null || img.sprite == null) return;
        img.type = img.sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
    }

    static Image Img(Transform parent, string name, string spriteName, Vector2 pos, Vector2 size, Color? tint = null)
    {
        var go = Node(name, parent);
        At(go, pos, size);
        var img = go.AddComponent<Image>();
        img.sprite = MainMenuAssets.Sprite(spriteName);
        if (img.sprite == null) Warn("缺贴图：" + spriteName + "（" + name + "）");
        SetSliced(img);
        img.color = tint ?? Color.white;
        img.raycastTarget = false;
        return img;
    }

    static Text Label(Transform parent, string name, string content, Vector2 pos, Vector2 size, int fontSize, Color color,
                      TextAnchor anchor = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Normal, float lineSpacing = 1.2f)
    {
        var go = Node(name, parent);
        At(go, pos, size);
        var t = go.AddComponent<Text>();
        t.font = _font;
        if (t.font == null) Warn("没有中文字体：" + name);
        t.text = content;
        t.fontSize = fontSize;
        t.fontStyle = style;
        t.color = color;
        t.alignment = anchor;
        t.lineSpacing = lineSpacing;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow   = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        t.supportRichText = true;
        return t;
    }

    static Button Btn(Transform parent, string name, string label, Vector2 pos, Vector2 size, bool primary, out Text labelText, int fontSize = 28, string iconName = null)
    {
        var go = Node(name, parent);
        At(go, pos, size);

        var img = go.AddComponent<Image>();
        img.sprite = MainMenuAssets.Sprite(ButtonSprite(primary, "普通"));
        if (img.sprite == null) Warn("缺按钮贴图：" + ButtonSprite(primary, "普通"));
        SetSliced(img);
        img.raycastTarget = true;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        if (primary)
        {
            // 主按钮保留换贴图：悬停=亮蓝发光；selectedSprite 留空 → 点过之后回普通态，不常亮
            btn.transition = Selectable.Transition.SpriteSwap;
            btn.spriteState = new SpriteState
            {
                highlightedSprite = MainMenuAssets.Sprite(ButtonSprite(primary, "悬停")),
                pressedSprite     = MainMenuAssets.Sprite(ButtonSprite(primary, "按下")),
                disabledSprite    = MainMenuAssets.Sprite(ButtonSprite(primary, "禁用")),
            };
            var pcb = ColorBlock.defaultColorBlock;
            pcb.disabledColor = DisabledTint;
            btn.colors = pcb;
        }
        else
        {
            // 白底按钮：相对提亮（普通压暗一点、悬停回白），不再换"选中"贴图
            btn.transition = Selectable.Transition.ColorTint;
            btn.colors = HoverTint();
        }
        go.AddComponent<UIHoverScale>();

        float textX = iconName == null ? 0f : 26f;
        Color labelColor = primary ? Color.white : ACCENT;
        labelText = Label(go.transform, "文字", label, new Vector2(textX, 0f), new Vector2(size.x - 44f, size.y - 8f),
                          fontSize, labelColor, TextAnchor.MiddleCenter,
                          primary ? FontStyle.Bold : FontStyle.Normal);
        if (iconName != null)
            Img(go.transform, "图标", iconName, new Vector2(-size.x * 0.5f + 38f, 0f), new Vector2(26f, 26f),
                primary ? Color.white : ACCENT);

        // 悬停手感：手型光标 + 文字加深一点（贴图态仍由 Button 的 SpriteSwap 负责）
        var polish = go.AddComponent<UIButtonPolish>();
        polish.label = labelText;
        polish.normalLabel = labelColor;
        polish.hoverLabel = primary ? Color.white : MainMenuAssets.ACCENT_DARK;
        polish.cursorTexture = CursorTexture();
        return btn;
    }

    static Button IconBtn(Transform parent, string name, string iconName, Vector2 pos, float size)
    {
        var go = Node(name, parent);
        At(go, pos, new Vector2(size, size));

        var img = go.AddComponent<Image>();
        img.sprite = MainMenuAssets.Sprite("按钮_图标_普通");
        SetSliced(img);
        img.raycastTarget = true;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        // 白底圆钮：与次按钮一致的相对提亮，不换贴图
        btn.transition = Selectable.Transition.ColorTint;
        btn.colors = HoverTint();
        go.AddComponent<UIHoverScale>();

        var icon = Img(go.transform, "图标", iconName, Vector2.zero, new Vector2(size * 0.5f, size * 0.5f), INK);
        icon.enabled = icon.sprite != null;
        var polish = go.AddComponent<UIButtonPolish>();
        polish.cursorTexture = CursorTexture();
        return btn;
    }

    static Button Tab(Transform parent, string name, string label, Vector2 pos, Vector2 size, out Text labelText, int fontSize)
    {
        var go = Node(name, parent);
        At(go, pos, size);

        var img = go.AddComponent<Image>();
        img.sprite = MainMenuAssets.Sprite(MainMenuAssets.Pick("稿_列表_默认", "页签_普通"));
        SetSliced(img);
        img.raycastTarget = true;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        // 页签：悬停相对提亮；"当前页签"仍由 MainMenuUI 运行时换 稿_列表_选中 贴图 + 字色 accent 表达
        btn.transition = Selectable.Transition.ColorTint;
        btn.colors = HoverTint();

        labelText = Label(go.transform, "文字", label, Vector2.zero, new Vector2(size.x - 12f, size.y - 6f), fontSize,
                          MUTED, TextAnchor.MiddleCenter);
        var polish = go.AddComponent<UIButtonPolish>();
        polish.label = labelText;
        polish.normalLabel = MUTED;
        polish.hoverLabel = MainMenuAssets.ACCENT_DARK;
        polish.cursorTexture = CursorTexture();
        return btn;
    }

    static Slider Sld(Transform parent, string name, Vector2 pos, Vector2 size)
    {
        var go = Node(name, parent);
        At(go, pos, size);

        var slider = go.AddComponent<Slider>();
        slider.transition = Selectable.Transition.None;
        slider.direction  = Slider.Direction.LeftToRight;
        slider.minValue = 0f; slider.maxValue = 1f; slider.value = 0.6f;

        // 轨道
        var track = Img(go.transform, "轨道", "滑条_轨道", Vector2.zero, new Vector2(size.x, 14f));
        var trackRT = (RectTransform)track.transform;
        trackRT.anchorMin = new Vector2(0f, 0.5f);
        trackRT.anchorMax = new Vector2(1f, 0.5f);
        trackRT.offsetMin = new Vector2(0f, -7f);
        trackRT.offsetMax = new Vector2(0f, 7f);
        track.raycastTarget = true;

        // 填充
        var fillArea = Node("填充区", go.transform);
        var faRT = (RectTransform)fillArea.transform;
        faRT.anchorMin = new Vector2(0f, 0.5f);
        faRT.anchorMax = new Vector2(1f, 0.5f);
        faRT.offsetMin = new Vector2(9f, -6f);
        faRT.offsetMax = new Vector2(-9f, 6f);
        var fill = Img(fillArea.transform, "填充", "滑条_填充", Vector2.zero, Vector2.zero);
        var fillRT = (RectTransform)fill.transform;
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = Vector2.one;
        fillRT.offsetMin = Vector2.zero;
        fillRT.offsetMax = Vector2.zero;

        // 把手
        var handleArea = Node("把手区", go.transform);
        var haRT = (RectTransform)handleArea.transform;
        haRT.anchorMin = new Vector2(0f, 0.5f);
        haRT.anchorMax = new Vector2(1f, 0.5f);
        haRT.offsetMin = new Vector2(13f, 0f);
        haRT.offsetMax = new Vector2(-13f, 0f);
        var handle = Img(handleArea.transform, "把手", "滑条_把手", Vector2.zero, new Vector2(26f, 26f));
        var handleRT = (RectTransform)handle.transform;

        slider.targetGraphic = track;
        slider.fillRect   = fillRT;
        slider.handleRect = handleRT;
        return slider;
    }

    static UISwitch Switch(Transform parent, string name, Vector2 pos)
    {
        var go = Node(name, parent);
        At(go, pos, new Vector2(96f, 44f));

        var track = go.AddComponent<Image>();
        track.sprite = MainMenuAssets.Sprite("开关_槽_开");
        SetSliced(track);
        track.raycastTarget = true;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = track;
        btn.transition = Selectable.Transition.None;

        var knobGO = Node("把手", go.transform);
        At(knobGO, new Vector2(26f, 0f), new Vector2(36f, 36f));
        var knob = knobGO.AddComponent<Image>();
        knob.sprite = MainMenuAssets.Sprite("开关_把手");
        knob.raycastTarget = false;

        var sw = go.AddComponent<UISwitch>();
        sw.knob = (RectTransform)knobGO.transform;
        sw.knob.anchoredPosition = new Vector2(26f, 0f);
        sw.track = track;
        sw.onSprite  = MainMenuAssets.Sprite("开关_槽_开");
        sw.offSprite = MainMenuAssets.Sprite("开关_槽_关");
        sw.onX = 26f; sw.offX = -26f; sw.isOn = true;
        return sw;
    }

    static UIPanel Page(Transform canvas, string name, Vector2 cardSize, string title, bool withClose, out RectTransform card, out Button closeBtn)
    {
        var root = Node(name, canvas);
        Stretch(root);
        var panel = root.AddComponent<UIPanel>();
        panel.openOnStart = false;
        panel.duration    = 0.18f;

        var dim = Node("遮罩", root.transform);
        Stretch(dim);
        var dimImg = dim.AddComponent<Image>();
        dimImg.sprite = MainMenuAssets.Sprite("遮罩_白");
        // 设置/存档/章节/概览这些子页按 ui-002 的样子做：深色底 + 中间一块浅色大圆角面板
        var deepBg = MainMenuAssets.MASK_DEEP;
        dimImg.color  = new Color(deepBg.r, deepBg.g, deepBg.b, 0.94f);
        dimImg.raycastTarget = true;
        panel.dimmer = dim;

        var cardGO = Node("卡片", root.transform);
        At(cardGO, Vector2.zero, cardSize);
        var cardImg = cardGO.AddComponent<Image>();
        cardImg.sprite = MainMenuAssets.Sprite(MainMenuAssets.Pick("稿_面板_弹窗", "面板_亮"));
        if (cardImg.sprite == null) Warn("缺面板贴图");
        SetSliced(cardImg);
        cardImg.raycastTarget = true;
        card = (RectTransform)cardGO.transform;

        var bar = Img(card, "装饰条", "面板_标题条", Vector2.zero, new Vector2(7f, 38f));
        Corner(bar.gameObject, new Vector2(40f, -54f), new Vector2(7f, 38f), new Vector2(0f, 1f), new Vector2(0f, 0.5f));

        var t = Label(card, "页面标题", title, Vector2.zero, new Vector2(620f, 48f), 34, INK, TextAnchor.MiddleLeft, FontStyle.Bold);
        Corner(t.gameObject, new Vector2(62f, -54f), new Vector2(620f, 48f), new Vector2(0f, 1f), new Vector2(0f, 0.5f));

        closeBtn = null;
        if (withClose)
        {
            closeBtn = IconBtn(card, "Btn_关闭", "图标_关闭", Vector2.zero, 50f);
            Corner(closeBtn.gameObject, new Vector2(-26f, -26f), new Vector2(50f, 50f), new Vector2(1f, 1f), new Vector2(1f, 1f));
            closeBtn.onClick.AddListener(delegate { panel.Hide(); });
        }
        return panel;
    }

    static Texture2D _cursor;
    static Texture2D CursorTexture()
    {
        if (_cursor == null)
        {
            var sp = MainMenuAssets.Sprite("光标_手");
            if (sp != null) _cursor = sp.texture;
        }
        return _cursor;
    }

    /// 按钮贴图：主按钮用《ui素材》切出来的四态；次按钮用稿子的列表行（默认/选中）
    static string ButtonSprite(bool primary, string state)
    {
        // 主按钮：设计稿里没有「文字按钮四态」这套（ui-008 是滚动条+状态标签），
        // 所以主按钮用程序化生成的那套（配色已对齐设计稿的蓝）
        if (primary) return "按钮_主_" + state;
        string p = (state == "普通" || state == "禁用") ? "稿_列表_默认" : "稿_列表_选中";
        return MainMenuAssets.Sprite(p) != null ? p : "按钮_次_" + state;
    }

    static void Warn(string msg)
    {
        if (!_warn.Contains(msg)) _warn.Add(msg);
    }

    // ================================================================== 报告：层级树 + 检查
    static void DumpHierarchy(Transform t, string indent, List<string> log)
    {
        foreach (Transform c in t)
        {
            var rt = c as RectTransform;
            var img = c.GetComponent<Image>();
            var txt = c.GetComponent<Text>();
            var parts = new List<string>();
            if (rt != null)
                parts.Add(string.Format("[{0:0.#},{1:0.#} {2:0.#}×{3:0.#}]",
                    rt.anchoredPosition.x, rt.anchoredPosition.y, rt.sizeDelta.x, rt.sizeDelta.y));
            if (img != null)
                parts.Add("图:" + (img.sprite == null ? "★无" : img.sprite.name) + (img.enabled ? "" : "(隐藏)"));
            if (txt != null)
                parts.Add("字:" + (string.IsNullOrEmpty(txt.text) ? "（空）" : txt.text) + " " + txt.fontSize + "px");
            if (c.GetComponent<Button>() != null) parts.Add("按钮");
            if (c.GetComponent<Slider>() != null) parts.Add("滑条");
            if (c.GetComponent<UISwitch>() != null) parts.Add("开关");
            if (c.GetComponent<UIPanel>() != null) parts.Add("面板");
            parts.Add(c.gameObject.activeSelf ? "" : "(未激活)");

            log.Add(indent + c.name + "  " + string.Join("  ", parts.Where(p => p != "").ToArray()));
            DumpHierarchy(c, indent + "    ", log);
        }
    }

    static void CheckScene(Canvas canvas, List<string> log)
    {
        var canvasRT = (RectTransform)canvas.transform;
        int images = 0, nullImages = 0, texts = 0, nullFonts = 0, buttons = 0, nullTargets = 0, outOfBounds = 0;

        foreach (var g in canvas.GetComponentsInChildren<Graphic>(true))
        {
            var img = g as Image;
            if (img != null)
            {
                images++;
                if (img.sprite == null) { nullImages++; Warn("Image 没有贴图：" + PathOfNode(g.transform)); }
            }
            var txt = g as Text;
            if (txt != null)
            {
                texts++;
                if (txt.font == null) { nullFonts++; Warn("Text 没有字体：" + PathOfNode(g.transform)); }
            }
            var rt = g.transform as RectTransform;
            if (rt != null && !AllowOutside(g.transform) && !Inside(canvasRT, rt))
            {
                outOfBounds++;
                Warn("超出画布：" + PathOfNode(g.transform));
            }
        }
        foreach (var b in canvas.GetComponentsInChildren<Button>(true))
        {
            buttons++;
            if (b.targetGraphic == null) { nullTargets++; Warn("按钮没有目标图：" + PathOfNode(b.transform)); }
        }

        log.Add(string.Format("    Image {0} 个（无贴图 {1}）／ Text {2} 个（无字体 {3}）／ Button {4} 个（无目标图 {5}）",
            images, nullImages, texts, nullFonts, buttons, nullTargets));
        log.Add(outOfBounds == 0 ? "    越界元素：无 ✓" : "    ★ 越界元素：" + outOfBounds + " 个");
        log.Add(string.Format("    画布：1920×1080 参考分辨率，CanvasScaler 匹配 0.5；文字字体 {0}",
            _font == null ? "★未设置" : _font.name + "（Unity 内置字体没有汉字）"));

        if (_warn.Count == 0) log.Add("    贴图/字体/引用检查：全部通过 ✓");
        else
        {
            log.Add("    ★ 需要注意 " + _warn.Count + " 条：");
            foreach (var w in _warn) log.Add("        · " + w);
        }
        log.Add("");
        log.Add("    （TextureImporter 里九宫格 border 已按图形设定：面板 30 / 按钮 30~34 / 页签 22 / 卡片 26 / 线 2）");
    }

    static string PathOfNode(Transform t)
    {
        var s = t.name;
        while (t.parent != null && t.parent.parent != null) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }

    /// 背景装饰（光斑/云）故意出血到画布外，不算越界
    static bool AllowOutside(Transform t)
    {
        var s = PathOfNode(t);
        return s.StartsWith("BG_装饰/");
    }

    static bool Inside(RectTransform canvasRT, RectTransform rt)
    {
        // 用画布实际尺寸；拿不到（batch 里可能是 0×0）就退回设计稿尺寸。
        // 注意不能写死 1920×1080：编辑器 Game 视图不是 16:9 时，全屏元素合法地会超出设计稿范围。
        var rect = canvasRT.rect;
        if (rect.width < 100f || rect.height < 100f) rect = new Rect(-960f, -540f, 1920f, 1080f);
        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        foreach (var c in corners)
        {
            var local = canvasRT.InverseTransformPoint(c);
            if (local.x < rect.xMin - 2f || local.x > rect.xMax + 2f ||
                local.y < rect.yMin - 2f || local.y > rect.yMax + 2f) return false;
        }
        return true;
    }

    static string BuildSettings()
    {
        var list = EditorBuildSettings.scenes.ToList();
        list.RemoveAll(s => s.path == OUT_SCENE);
        list.Insert(0, new EditorBuildSettingsScene(OUT_SCENE, true));

        int gi = list.FindIndex(s => s.path == GAME_SCENE);
        if (gi > 1)
        {
            var g = list[gi];
            list.RemoveAt(gi);
            list.Insert(1, g);
        }
        EditorBuildSettings.scenes = list.ToArray();

        var txt = new List<string>();
        for (int i = 0; i < list.Count; i++) txt.Add(i + "=" + Path.GetFileNameWithoutExtension(list[i].path));
        return "Build Settings：" + string.Join("，", txt.ToArray());
    }

    // ================================================================== 预览渲染
    [MenuItem("Tools/干预项目/渲染主界面预览")]
    public static void RenderPreviews()
    {
        if (!File.Exists(OUT_SCENE)) { Debug.LogError("[MainMenuBuilder] 先跑『搭建主界面UI场景』"); return; }
        if (!Application.isBatchMode)
        {
            var curScene = EditorSceneManager.GetActiveScene();
            if (curScene.IsValid() && curScene.isDirty && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        }
        EditorSceneManager.OpenScene(OUT_SCENE, OpenSceneMode.Single);

        var canvasGO = GameObject.Find("UI_Canvas");
        if (canvasGO == null) { Debug.LogError("[MainMenuBuilder] 场景里没有 UI_Canvas"); return; }
        var canvas = canvasGO.GetComponent<Canvas>();
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        var ui     = canvasGO.GetComponent<MainMenuUI>();
        if (ui == null) { Debug.LogError("[MainMenuBuilder] 场景里的 MainMenuUI 是缺脚本状态，先重跑『搭建主界面UI场景』"); return; }
        var cam    = Camera.main;
        if (cam == null) { Debug.LogError("[MainMenuBuilder] 场景里没有相机"); return; }

        Directory.CreateDirectory(PREVIEW_DIR);

        // 切成 1:1 屏幕空间相机渲染，保证预览和 1920×1080 设计稿完全一致
        var oldRenderMode = canvas.renderMode; var oldCam = canvas.worldCamera; var oldPlane = canvas.planeDistance;
        var oldScaleMode = scaler.uiScaleMode; var oldFactor = scaler.scaleFactor;
        var oldOrtho = cam.orthographic; var oldSize = cam.orthographicSize;
        var oldClear = cam.clearFlags; var oldBg = cam.backgroundColor; var oldFov = cam.fieldOfView;

        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = 10f;
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = 1f;
        cam.orthographic = true;
        cam.orthographicSize = 540f;       // 1080 / 2 → 1 UI 单位 = 1 像素
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = MainMenuAssets.BG_MID;

        var rt = new RenderTexture(1920, 1080, 24);
        cam.targetTexture = rt;
        cam.Render();

        var log = new List<string>();
        log.Add("主界面预览   " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("1920×1080 1:1 渲染，面板状态由脚本临时切换（不写回场景）");

        var states = new (string file, UIPanel panel)[]
        {
            ("01_主菜单",      null),
            ("02_设置",        ui != null ? ui.settingsPanel : null),
            ("03_存读档",      ui != null ? ui.savePanel : null),
            ("04_章节选择",    ui != null ? ui.chapterPanel : null),
            ("05_内容概览",    ui != null ? ui.overviewPanel : null),
            ("06_大图查看",    ui != null ? ui.imagePanel : null),
            ("07_确认弹窗",    ui != null && ui.confirm != null ? ui.confirm.panel : null),
        };

        var all = new List<UIPanel>();
        foreach (var s in states) if (s.panel != null && !all.Contains(s.panel)) all.Add(s.panel);

        foreach (var st in states)
        {
            foreach (var p in all) { if (p == st.panel) p.Show(true); else p.Hide(true); }
            if (st.file == "06_大图查看" && ui != null) FillBigPreview(ui);
            Canvas.ForceUpdateCanvases();
            cam.Render();
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            byte[] png = tex.EncodeToPNG();
            string path = PREVIEW_DIR + "/" + st.file + ".png";
            File.WriteAllBytes(path, png);
            log.Add(string.Format("    {0}   {1}   {2:0} KB   非背景像素 {3:P1}   颜色数 {4}",
                st.file, Path.GetFileName(path), png.Length / 1024f, NonBg(tex), Colors(tex)));
            UnityEngine.Object.DestroyImmediate(tex);
        }

        // 恢复
        foreach (var p in all) { p.Show(true); p.Hide(true); }
        cam.targetTexture = null;
        canvas.renderMode = oldRenderMode; canvas.worldCamera = oldCam; canvas.planeDistance = oldPlane;
        scaler.uiScaleMode = oldScaleMode; scaler.scaleFactor = oldFactor;
        cam.orthographic = oldOrtho; cam.orthographicSize = oldSize;
        cam.clearFlags = oldClear; cam.backgroundColor = oldBg; cam.fieldOfView = oldFov;
        UnityEngine.Object.DestroyImmediate(rt);

        File.WriteAllText(PREVIEW_DIR + "/_预览说明.txt", string.Join("\n", log.ToArray()));
        Debug.Log("[MainMenuBuilder] 预览已写 " + PREVIEW_DIR);
    }

    /// 大图查看页在编辑器里是空的（靠运行时填），预览时先塞一条进去，方便看图
    static void FillBigPreview(MainMenuUI ui)
    {
        var db = MainMenuAssets.LoadOverviewDb();
        if (db == null || db.entries.Count == 0) return;
        var e = db.entries[0];
        if (ui.bigImage != null) { ui.bigImage.sprite = e.frame; ui.bigImage.enabled = e.frame != null; }
        if (ui.bigTitle != null) ui.bigTitle.text = e.view + " ｜ " + e.title;
        if (ui.bigBody != null) ui.bigBody.text = e.body;
        if (ui.bigCounter != null) ui.bigCounter.text = "1 / 4";
    }

    /// 用来快速判断"画面是不是空的"：统计和背景色明显不同的像素比例
    static float NonBg(Texture2D tex)
    {
        var px = tex.GetPixels32();
        Color32 bg = MainMenuAssets.BG_MID;
        int n = 0;
        for (int i = 0; i < px.Length; i += 7)
        {
            int d = Mathf.Abs(px[i].r - bg.r) + Mathf.Abs(px[i].g - bg.g) + Mathf.Abs(px[i].b - bg.b);
            if (d > 12) n++;
        }
        return (float)n / (px.Length / 7f);
    }

    static int Colors(Texture2D tex)
    {
        var px = tex.GetPixels32();
        var set = new HashSet<int>();
        for (int i = 0; i < px.Length; i += 11) set.Add((px[i].r << 16) | (px[i].g << 8) | px[i].b);
        return set.Count;
    }

    // ================================================================== 运行自检（进 Play 模式点一遍）
    static bool _smokeActive;
    static bool _smokeHooked;
    static int  _smokeIdx;
    static int  _smokeFrames;
    static double _smokeStart;
    static readonly List<string> _smokeErrors = new List<string>();
    static readonly List<string> _smokeSteps  = new List<string>();
    static bool _smokeExpectedAutoPlay;
    static int  _smokeLockedChapter = -1;
    static bool _smokeOldOptionsEnabled;
    static EnterPlayModeOptions _smokeOldOptions;

    [MenuItem("Tools/干预项目/主界面运行自检")]
    public static void SmokeTest()
    {
        if (!File.Exists(OUT_SCENE)) { Debug.LogError("[MainMenuBuilder] 先跑『搭建主界面UI场景』"); return; }
        // 关掉域重载：不然进 Play 模式会把我们的 update 回调打断
        _smokeOldOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
        _smokeOldOptions = EditorSettings.enterPlayModeOptions;
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;

        EditorSceneManager.OpenScene(OUT_SCENE, OpenSceneMode.Single);
        _smokeErrors.Clear(); _smokeSteps.Clear();
        _smokeIdx = 0; _smokeFrames = 0;
        _smokeStart = EditorApplication.timeSinceStartup;
        _smokeActive = true;
        if (!_smokeHooked)
        {
            Application.logMessageReceived += SmokeLog;
            EditorApplication.update += SmokeUpdate;
            _smokeHooked = true;
        }
        Debug.Log("[MainMenuBuilder] 主界面运行自检开始");
    }

    static void SmokeLog(string msg, string stack, LogType type)
    {
        if (!_smokeActive) return;
        if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
            _smokeErrors.Add(type + "：" + msg + "  @" + (stack ?? "").Split('\n')[0]);
    }

    static void SmokeCheck(string what, bool ok)
    {
        _smokeSteps.Add((ok ? "  ✓ " : "  ★ ") + what);
    }

    static void SmokeUpdate()
    {
        if (!_smokeActive) return;
        if (EditorApplication.timeSinceStartup - _smokeStart > 240) { SmokeFinish("超时"); return; }
        if (!EditorApplication.isPlaying) { EditorApplication.isPlaying = true; return; }
        if (_smokeFrames++ < 4) return;      // 等 Awake/Start

        var go = GameObject.Find("UI_Canvas");
        var ui = go == null ? null : go.GetComponent<MainMenuUI>();
        if (ui == null)
        {
            // 换个找法 + 带诊断：场景对不对、组件能不能解出
            var found = UnityEngine.Object.FindObjectsOfType<MainMenuUI>(true);
            if (found != null && found.Length > 0) ui = found[0];
        }
        if (ui == null)
        {
            var sc = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            int roots = sc.IsValid() ? sc.GetRootGameObjects().Length : -1;
            SmokeFinish("找不到 MainMenuUI（当前场景 “" + sc.name + "”，根对象 " + roots +
                        " 个，UI_Canvas " + (go != null ? "在" : "不在") + "，组件数量 " +
                        UnityEngine.Object.FindObjectsOfType<MainMenuUI>(true).Length + "）");
            return;
        }

        switch (_smokeIdx++)
        {
            case 0:
                SmokeCheck("主菜单层展开", ui.menuLayer != null && ui.menuLayer.IsOpen);
                SmokeCheck("子页默认收起", ui.settingsPanel != null && !ui.settingsPanel.IsOpen
                                        && ui.savePanel != null && !ui.savePanel.IsOpen);
                SmokeCheck("Toast 默认透明", ui.toast == null || ui.toast.group == null || ui.toast.group.alpha <= 0.01f);
                SmokeCheck("存档槽 6 个", ui.slots != null && ui.slots.Length == 6);
                SmokeCheck("章节卡 5 张", ui.chapters != null && ui.chapters.Length == 5);
                SmokeCheck("概览数据已接（20 条）", ui.overviewDb != null && ui.overviewDb.entries.Count == 20);
                break;

            case 1:   // 设置页
                ui.btnSettings.onClick.Invoke();
                break;
            case 2:
                SmokeCheck("点设置 → 设置页展开", ui.settingsPanel.IsOpen);
                if (ui.sldMaster != null) ui.sldMaster.value = 0.33f;
                break;
            case 3:
                SmokeCheck("拖总音量滑条 → GameSettings.Master=0.33", Mathf.Abs(GameSettings.Master - 0.33f) < 0.02f);
                _smokeExpectedAutoPlay = !GameSettings.AutoPlay;
                ui.swAutoPlay.Toggle();
                break;
            case 4:
                SmokeCheck("点自动播放开关 → 状态翻转", GameSettings.AutoPlay == _smokeExpectedAutoPlay);
                ui.btnSettingsApply.onClick.Invoke();
                SmokeCheck("应用 → 写进 PlayerPrefs", Mathf.Abs(PlayerPrefs.GetFloat(GameSettings.K_MASTER, -1f) - 0.33f) < 0.02f);
                ui.btnSettingsReset.onClick.Invoke();
                break;
            case 5:
                SmokeCheck("恢复默认 → Master 回 0.80", Mathf.Abs(GameSettings.Master - 0.80f) < 0.02f);
                ui.btnSettingsBack.onClick.Invoke();
                break;
            case 6:
                SmokeCheck("返回 → 设置页收起", !ui.settingsPanel.IsOpen);
                ui.btnLoad.onClick.Invoke();
                break;
            case 7:
                SmokeCheck("点读取存档 → 存档页展开", ui.savePanel.IsOpen);
                ui.slots[0].button.onClick.Invoke();
                break;
            case 8:
                SmokeCheck("点槽位 1 → 选中框亮", ui.slots[0].selectFrame != null && ui.slots[0].selectFrame.enabled);
                ui.btnSaveRead.onClick.Invoke();      // 空档：应只弹 toast，不应崩
                break;
            case 9:
                SmokeCheck("读空档不崩（toast 提示或日志）", true);
                ui.btnSaveDelete.onClick.Invoke();
                break;
            case 10:
                SmokeCheck("删除空档 → 无二次确认/不误删", ui.confirm.panel == null || !ui.confirm.panel.IsOpen);
                ui.btnSaveBack.onClick.Invoke();
                break;
            case 11:
                SmokeCheck("返回 → 存档页收起", !ui.savePanel.IsOpen);
                ui.btnChapter.onClick.Invoke();
                break;
            case 12:
                SmokeCheck("点章节选择 → 章节页展开", ui.chapterPanel.IsOpen);
                _smokeLockedChapter = -1;
                for (int i = 5; i >= 2; i--) if (!GameProgress.IsUnlocked(i)) { _smokeLockedChapter = i; break; }
                if (_smokeLockedChapter > 0) ui.chapters[_smokeLockedChapter - 1].button.onClick.Invoke();
                break;
            case 13:
                if (_smokeLockedChapter > 0)
                    SmokeCheck("点未解锁的第 " + _smokeLockedChapter + " 章：不换场景（只提示）",
                        UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenu");
                SmokeCheck("已解锁章节数：" + GameProgress.Unlocked + "（自检不会点它，避免真换场景）", true);
                ui.btnChapterBack.onClick.Invoke();
                break;
            case 14:
                SmokeCheck("返回 → 章节页收起", !ui.chapterPanel.IsOpen);
                ui.btnOverview.onClick.Invoke();
                break;
            case 15:
                SmokeCheck("点内容概览 → 概览页展开", ui.overviewPanel.IsOpen);
                ui.ovChapterTabs[2].onClick.Invoke();       // 第 3 章
                break;
            case 16:
                SmokeCheck("切到第 3 章", ui.ovChapterTitle != null && ui.ovChapterTitle.text.Contains("第3章"));
                SmokeCheck("第 3 章 4 张卡片都有图", CountEnabledImages(ui) == 4);
                ui.ovViewTabs[3].onClick.Invoke();           // 未来视角
                break;
            case 17:
                SmokeCheck("筛选“未来”视角：卡片变暗但仍在", ui.ovCards[0] != null && ui.ovCards[0].gameObject.activeSelf);
                ui.ovViewTabs[0].onClick.Invoke();            // 回到全部
                break;
            case 18:
                SmokeCheck("回到“全部”：卡片恢复正常", ui.ovCards[0].GetComponent<CanvasGroup>() == null
                        || ui.ovCards[0].GetComponent<CanvasGroup>().alpha > 0.9f);
                ui.ovCards[0].button.onClick.Invoke();
                break;
            case 19:
                SmokeCheck("点卡片 → 大图打开且有图", ui.imagePanel.IsOpen && ui.bigImage != null && ui.bigImage.sprite != null);
                ui.btnBigNext.onClick.Invoke();
                break;
            case 20:
                SmokeCheck("下一张 → 计数变化", ui.bigCounter != null && ui.bigCounter.text.StartsWith("2"));
                ui.btnBigClose.onClick.Invoke();
                break;
            case 21:
                SmokeCheck("关闭 → 大图收起", !ui.imagePanel.IsOpen);
                ui.Back();
                break;
            case 22:
                SmokeCheck("ESC 返回 → 概览收起", !ui.overviewPanel.IsOpen);
                ui.btnQuit.onClick.Invoke();
                break;
            case 23:
                SmokeCheck("点退出 → 确认弹窗打开", ui.confirm != null && ui.confirm.panel.IsOpen);
                ui.confirm.cancelButton.onClick.Invoke();
                break;
            case 24:
                SmokeCheck("点取消 → 弹窗关闭", !ui.confirm.panel.IsOpen);
                SmokeCheck("场景里没有未接线的主菜单按钮",
                    ui.btnStart != null && ui.btnLoad != null && ui.btnChapter != null &&
                    ui.btnOverview != null && ui.btnSettings != null && ui.btnQuit != null);
                SmokeCheck("剧情场景在 Build Settings 里可加载",
                    Application.CanStreamedLevelBeLoaded(MainMenuUI.GAME_SCENE));
                GameSettings.ResetToDefault();
                break;
            default:
                SmokeFinish("跑完 25 步");
                break;
        }
    }

    static int CountEnabledImages(MainMenuUI ui)
    {
        int n = 0;
        foreach (var c in ui.ovCards)
            if (c != null && c.gameObject.activeSelf && c.image != null && c.image.sprite != null) n++;
        return n;
    }

    static void SmokeFinish(string note)
    {
        _smokeActive = false;
        EditorApplication.isPlaying = false;
        EditorSettings.enterPlayModeOptionsEnabled = _smokeOldOptionsEnabled;
        EditorSettings.enterPlayModeOptions = _smokeOldOptions;

        var log = new List<string>();
        log.Add("主界面运行自检（进 Play 模式真实点一遍）   " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("结果：" + note + "｜异常 " + _smokeErrors.Count + " 条｜检查项 " + _smokeSteps.Count + " 条");
        log.Add("");
        log.Add("【检查项】");
        log.AddRange(_smokeSteps);
        log.Add("");
        log.Add("【运行期异常 / 错误】");
        if (_smokeErrors.Count == 0) log.Add("  无 ✓");
        else foreach (var e in _smokeErrors) log.Add("  ★ " + e);
        log.Add("");
        log.Add("说明：开始游戏 / 章节进入 / 读取存档 的「跳场景」动作没有点（会真的加载 Game.unity），");
        log.Add("      只验证了 Game 场景在 Build Settings 里可加载；这三条属于换场景，建议在编辑器里手点一次确认。");

        Directory.CreateDirectory("Assets/assets/_报告");
        File.WriteAllText("Assets/assets/_报告/_主界面运行自检.txt", string.Join("\n", log.ToArray()));
        Debug.Log("[MainMenuBuilder] 运行自检完成：" + note + "，异常 " + _smokeErrors.Count + " 条 → Assets/assets/_报告/_主界面运行自检.txt");
        if (Application.isBatchMode) EditorApplication.Exit(_smokeErrors.Count == 0 ? 0 : 1);
    }

    // ================================================================== 定向补丁：按钮悬停改纯变色（只改现有场景的 Button，不重建场景）
    // 整场景重建会把场景里的手改冲掉（已删的 Logo 徽标、标签微调），所以对打开的场景逐个 Button 改过渡方案：
    //   · 主按钮（贴图 按钮_主_*）：保留 SpriteSwap，selectedSprite 清空（点过回普通态）
    //   · 白底按钮（按钮_图标_* / 按钮_次_* / 稿_列表_* / 页签_*）：改 ColorTint 相对提亮
    //   · 顺手补挂 UIButtonPolish（手型光标 + 悬停文字变化；此前因脚本 guid 过期被当缺脚本清掉了）
    // 用法：菜单 Tools/干预项目/按钮悬停改成纯变色
    [MenuItem("Tools/干预项目/按钮悬停改成纯变色")]
    public static void PatchBtnHover()
    {
        var log = new List<string>();
        log.Add("按钮悬停改纯变色（定向补丁）   " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("");

        var scene = EditorSceneManager.OpenScene(OUT_SCENE, OpenSceneMode.Single);
        int nPrimary = 0, nTint = 0, nPolish = 0, nGhost = 0;
        foreach (var btn in UnityEngine.Object.FindObjectsOfType<Button>(true))
        {
            // 先清掉按钮上的缺脚本空壳（老 UIButtonPolish 换 guid 后留下的尸体），不受下面类型判断影响
            nGhost += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(btn.gameObject);
            var img = btn.targetGraphic as Image;
            string sp = img != null && img.sprite != null ? img.sprite.name : "";
            if (sp.StartsWith("按钮_主_"))
            {
                btn.transition = Selectable.Transition.SpriteSwap;
                btn.spriteState = new SpriteState
                {
                    highlightedSprite = MainMenuAssets.Sprite("按钮_主_悬停"),
                    pressedSprite     = MainMenuAssets.Sprite("按钮_主_按下"),
                    disabledSprite    = MainMenuAssets.Sprite("按钮_主_禁用"),
                };
                var pcb = ColorBlock.defaultColorBlock;
                pcb.disabledColor = DisabledTint;
                btn.colors = pcb;
                nPrimary++;
            }
            else if (sp.StartsWith("按钮_图标_") || sp.StartsWith("按钮_次_") ||
                     sp.StartsWith("稿_列表_")  || sp.StartsWith("页签_"))
            {
                btn.transition = Selectable.Transition.ColorTint;
                btn.spriteState = new SpriteState();
                btn.colors = HoverTint();
                nTint++;
            }
            else continue;   // Transition=None 的控件（滑条/卡片/存档槽底）不动

            // 手型光标 + 悬停文字变化（与搭场景的配法一致）
            var polish = btn.GetComponent<UIButtonPolish>();
            if (polish == null) polish = btn.gameObject.AddComponent<UIButtonPolish>();
            polish.cursorTexture = CursorTexture();
            polish.cursorHotspot = new Vector2(13f, 3f);
            polish.handCursor = true;
            var labelT = btn.transform.Find("文字");
            var label = labelT != null ? labelT.GetComponent<Text>() : null;
            if (label != null)
            {
                polish.label = label;
                polish.normalLabel = label.color;
                polish.hoverLabel  = sp.StartsWith("按钮_主_") ? Color.white : MainMenuAssets.ACCENT_DARK;
            }
            nPolish++;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        bool ok = EditorSceneManager.SaveScene(scene, OUT_SCENE);
        log.Add(string.Format("    主按钮（保留换贴图）{0} 个；白底改 ColorTint {1} 个；UIButtonPolish 挂上 {2} 个；清掉缺脚本空壳 {3} 个；保存 {4}",
                              nPrimary, nTint, nPolish, nGhost, ok ? "✓" : "失败"));
        Directory.CreateDirectory("Assets/assets/_报告");
        File.WriteAllText(REPORT_BTNHOVER, string.Join("\n", log.ToArray()));
        Debug.Log("[MainMenuBuilder] 按钮悬停补丁完成：主按钮 " + nPrimary + " / 变色 " + nTint);
    }

    // ================================================================== 触发器
    // 把 MainMenu.unity 里的全部弹层（子页/大图/确认弹窗）改成“展开”并存盘，
    // 场景在 Scene 视图里直接可见可手改。只动 CanvasGroup，运行时行为不变
    // （UIPanel.Awake 按 openOnStart 重新收起，与场景里存的透明度无关）。
    public static void RevealPanels()
    {
        var active = EditorSceneManager.GetActiveScene();
        var scene = active.IsValid() && active.path == OUT_SCENE
            ? active
            : EditorSceneManager.OpenScene(OUT_SCENE, OpenSceneMode.Single);

        var canvasT = GameObject.Find("UI_Canvas");
        if (canvasT == null) throw new Exception("场景里没找到 UI_Canvas，先跑一次『搭建主界面UI场景』");

        string[] names = { "Page_设置", "Page_存读档", "Page_章节选择", "Page_内容概览", "Page_大图", "Popup_确认" };
        int done = 0;
        foreach (var n in names)
        {
            var t = canvasT.transform.Find(n);
            if (t == null) continue;
            var cg = t.GetComponent<CanvasGroup>();
            if (cg == null) cg = t.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = 1f; cg.interactable = true; cg.blocksRaycasts = true;

            var panel = t.GetComponent<UIPanel>();
            if (panel != null && panel.dimmer != null)
            {
                var dcg = panel.dimmer.GetComponent<CanvasGroup>();
                if (dcg == null) dcg = panel.dimmer.AddComponent<CanvasGroup>();
                dcg.alpha = 1f; dcg.interactable = true; dcg.blocksRaycasts = true;
            }
            done++;
        }

        bool ok = EditorSceneManager.SaveScene(scene, OUT_SCENE);
        Debug.Log("[MainMenuBuilder] 弹层展开 " + done + "/" + names.Length + " 个，保存 " + OUT_SCENE + " " + (ok ? "成功" : "★失败"));
    }
}

// 工程根存在 Assets/_menu_trigger.txt 时，编辑器下次刷新/重编译后自动跑一次主界面搭建。
[InitializeOnLoad]
public static class MainMenuTrigger
{
    const string TriggerFile = "Assets/_menu_trigger.txt";
    const string ErrFile     = "../额外文件/错误_主界面搭建.txt";

    static MainMenuTrigger()
    {
        if (!File.Exists(TriggerFile)) return;
        File.Delete(TriggerFile);
        EditorApplication.delayCall += delegate
        {
            try
            {
                MainMenuBuilder.Run();
                if (File.Exists(ErrFile)) File.Delete(ErrFile);
                Debug.Log("[MainMenuBuilder] 自动搭建完成");
            }
            catch (System.Exception e)
            {
                Directory.CreateDirectory("../额外文件");
                File.WriteAllText(ErrFile, e.ToString());
                Debug.LogError("[MainMenuBuilder] 自动搭建失败：" + e);
            }
        };
    }
}

// 工程根存在 Assets/_btnhover_trigger.txt 时，编辑器下次刷新/重编译后自动跑一次按钮悬停补丁。
[InitializeOnLoad]
public static class BtnHoverPatchTrigger
{
    const string TriggerFile = "Assets/_btnhover_trigger.txt";
    const string ErrFile     = "../额外文件/错误_按钮悬停补丁.txt";

    static BtnHoverPatchTrigger()
    {
        if (!File.Exists(TriggerFile)) return;
        File.Delete(TriggerFile);
        EditorApplication.delayCall += delegate
        {
            try
            {
                MainMenuBuilder.PatchBtnHover();
                if (File.Exists(ErrFile)) File.Delete(ErrFile);
                Debug.Log("[MainMenuBuilder] 按钮悬停补丁自动执行完成");
            }
            catch (System.Exception e)
            {
                Directory.CreateDirectory("../额外文件");
                File.WriteAllText(ErrFile, e.ToString());
                Debug.LogError("[MainMenuBuilder] 按钮悬停补丁失败：" + e);
            }
        };
    }
}

// 工程根存在 Assets/_panels_visible_trigger.txt 时，编辑器下次刷新/重编译后
// 自动把主界面场景里的全部弹层展开并存盘（方便在 Scene 视图里直接看到、直接手改）。
[InitializeOnLoad]
public static class MenuPanelsRevealTrigger
{
    const string TriggerFile = "Assets/_panels_visible_trigger.txt";
    const string ErrFile     = "../额外文件/错误_弹层展开.txt";

    static MenuPanelsRevealTrigger()
    {
        if (!File.Exists(TriggerFile)) return;
        File.Delete(TriggerFile);
        EditorApplication.delayCall += delegate
        {
            try
            {
                MainMenuBuilder.RevealPanels();
                if (File.Exists(ErrFile)) File.Delete(ErrFile);
                Debug.Log("[MainMenuBuilder] 弹层展开自动执行完成");
            }
            catch (System.Exception e)
            {
                Directory.CreateDirectory("../额外文件");
                File.WriteAllText(ErrFile, e.ToString());
                Debug.LogError("[MainMenuBuilder] 弹层展开失败：" + e);
            }
        };
    }
}
