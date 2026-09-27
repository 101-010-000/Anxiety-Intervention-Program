// 手机聊天 UI 搭建（第1章微信段展示层）。
// 用法：菜单 Tools/干预项目/搭建手机聊天UI（或渲染手机聊天UI预览），或丢 Assets/_phonechat_trigger.txt
//
// 做什么：
//   1) 把 assets/05_UI/手机_Phone/ 的 8 张图设成 Sprite(2D/UI) 导入（只改导入方式，像素/样式零改动；
//      两张气泡按测得的尾巴位置写 spriteBorder，保证 9-slice 永不变形）；
//   2) 在用户画布「UI交互」下建/重建唯一节点 手机聊天（遮罩之上、对话/选择题之下，默认禁用）；
//   3) 接线 PhoneChatUI 素材引用 + StoryRunner.phoneChat；
//   4) 写报告 assets/_报告/_手机聊天UI.txt + 渲染预览 预览/场景/手机聊天UI_预览.png。
// 不碰什么：对话/对话框/名字/对话内容/遮罩/交互提示/ChoicePanel 等一切既有节点只读（层级上仅新增一个兄弟节点）。
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class PhoneChatBuilder
{
    const string NODE = "手机聊天";
    const string GAME_SCENE = "Assets/Scenes/Game.unity";
    const string REPORT = "Assets/assets/_报告/_手机聊天UI.txt";
    const string PREVIEW = "Assets/assets/_报告/预览/场景/手机聊天UI_预览.png";
    static readonly string[] CANVAS_NAMES = { "UI交互", "UI_门口交互" };

    // 素材（文件名 → 用途）。气泡边框 = Vector4(左, 下, 右, 上)，按像素测量值写：
    //   图层 5（274×83）尾巴在最左 0..8 列、竖直居中 → 左 30 / 右 30 / 上 57 / 下 25（尾巴整体进"上"角块，永不拉伸）
    //   图层 7（319×87）尾巴在最右 303..313 列 → 左 30 / 右 44 / 上 58 / 下 28
    static readonly (string file, string role, Vector4 border)[] ASSETS =
    {
        ("图层 2", "frame",  Vector4.zero),
        ("图层 5", "bubbleL", new Vector4(30f, 25f, 30f, 57f)),
        ("图层 7", "bubbleR", new Vector4(30f, 28f, 44f, 58f)),
        ("图层 6", "avatarL", Vector4.zero),
        ("图层 8", "avatarR", Vector4.zero),
        ("1",      "sticker0", Vector4.zero),
        ("2",      "sticker1", Vector4.zero),
        ("3",      "sticker2", Vector4.zero),
    };

    // 机身按等比缩放落到 1080 高的画布上（0.9 → 518×1030，屏幕正中，比例不变；用户 2026-09-27 定稿）
    const float ROOT_SCALE = 0.9f;

    [MenuItem("Tools/干预项目/搭建手机聊天UI")]
    public static void Run()
    {
        var log = new List<string>();
        log.Add("手机聊天 UI 搭建   " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("素材：assets/05_UI/手机_Phone/（用户设计稿，样式/比例不改；机身等比缩放 ×" + ROOT_SCALE + "）");
        log.Add("");

        // 0) 新加的运行时脚本先强制过一遍导入（避免"刚拷进来的 .cs 挂组件变缺脚本"的老坑）
        AssetDatabase.ImportAsset("Assets/Scripts/Story/PhoneChatUI.cs", ImportAssetOptions.ForceUpdate);

        // 1) 素材 → Sprite
        string dir = AssetLocator.Dir("手机_Phone");
        if (dir == null) { Debug.LogError("[PhoneChatBuilder] 找不到 手机_Phone 素材目录"); return; }
        var sp = new Dictionary<string, Sprite>();
        foreach (var a in ASSETS)
        {
            string path = dir + "/" + a.file + ".png";
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) { Debug.LogError("[PhoneChatBuilder] 找不到贴图 " + path); return; }
            bool dirty = ti.textureType != TextureImporterType.Sprite
                         || ti.spriteImportMode != SpriteImportMode.Single
                         || ti.mipmapEnabled
                         || ti.spriteBorder != a.border;
            if (dirty)
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.mipmapEnabled = false;
                ti.alphaIsTransparency = true;
                ti.spriteBorder = a.border;
                ti.SaveAndReimport();
            }
            var s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (s == null) { Debug.LogError("[PhoneChatBuilder] 贴图导入失败 " + path); return; }
            sp[a.role] = s;
            log.Add("  贴图 " + a.file + ".png → " + a.role +
                    (a.border == Vector4.zero ? "" : string.Format("（9-slice 边框 左{0}/下{1}/右{2}/上{3}）", a.border.x, a.border.y, a.border.z, a.border.w)));
        }

        // 2) 确保 Game.unity 打开（照门口传送工具的规矩：工具自己负责开场景、改完存盘）
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.IsValid() && scene.isDirty && !Application.isBatchMode
            && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (scene.path != GAME_SCENE) scene = EditorSceneManager.OpenScene(GAME_SCENE, OpenSceneMode.Single);

        // 3) 画布（复用用户的，不新建、不动 sortingOrder/CanvasScaler）
        Transform canvasT = null;
        foreach (var n in CANVAS_NAMES)
        {
            var c = GameObject.Find(n);
            if (c != null) { canvasT = c.transform; break; }
        }
        if (canvasT == null) { Debug.LogError("[PhoneChatBuilder] Game.unity 里没有 UI交互 画布"); return; }
        log.Add("画布：复用现有 " + canvasT.name + "（sortingOrder/缩放原样不动）");

        // 4) 重建自己的节点（只动 手机聊天，其余节点一律不碰）
        var old = canvasT.Find(NODE);
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var root = new GameObject(NODE, typeof(RectTransform), typeof(CanvasGroup), typeof(PhoneChatUI));
        var rt = (RectTransform)root.transform;
        rt.SetParent(canvasT, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);  // 屏幕正中（用户 2026-09-27 定稿）
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(576f, 1145f);
        rt.localScale = Vector3.one * ROOT_SCALE;
        rt.SetSiblingIndex(1);                                  // 遮罩(0) 之上，其余 UI 之下
        var group = root.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;                           // 点击穿透：推进剧情的点击不打折
        group.interactable = false;

        var body = NewImage("机身", rt, sp["frame"]);
        var brt = (RectTransform)body.transform;
        brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
        brt.anchoredPosition = Vector2.zero;
        brt.sizeDelta = new Vector2(576f, 1145f);

        var title = NewText("标题", rt, Color.white, 34);
        var trt = (RectTransform)title.transform;
        trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
        trt.anchoredPosition = new Vector2(0f, 427.5f);         // 顶部标题栏中线（自顶 145px）
        trt.sizeDelta = new Vector2(400f, 60f);
        title.text = "林溪";
        title.alignment = TextAnchor.MiddleCenter;

        var area = new GameObject("消息区", typeof(RectTransform), typeof(RectMask2D));
        var art = (RectTransform)area.transform;
        art.SetParent(rt, false);
        art.anchorMin = art.anchorMax = new Vector2(0f, 0f);
        art.pivot = new Vector2(0f, 0f);
        art.anchoredPosition = new Vector2(36f, 145f);          // 底部让开输入栏，顶部让开标题栏
        art.sizeDelta = new Vector2(504f, 810f);

        var content = new GameObject("内容", typeof(RectTransform));
        var crt = (RectTransform)content.transform;
        crt.SetParent(art, false);
        crt.anchorMin = new Vector2(0f, 1f); crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(0.5f, 1f);
        crt.anchoredPosition = Vector2.zero;
        crt.sizeDelta = new Vector2(0f, 0f);

        root.SetActive(false);                                  // 同 对话/交互提示 约定：进游戏由运行时启用
        SetLayerRecursively(root, LayerMask.NameToLayer("UI")); // 预览相机只画 UI 层（Overlay 模式下渲染不受层影响）

        // 5) 接线
        var ui = root.GetComponent<PhoneChatUI>();
        ui.frameSprite = sp["frame"];
        ui.bubbleLeft = sp["bubbleL"];
        ui.bubbleRight = sp["bubbleR"];
        ui.avatarLeft = sp["avatarL"];
        ui.avatarRight = sp["avatarR"];
        ui.stickerTags = new[] { "[比心]", "[鲜花]", "[加油]" };
        ui.stickerSprites = new[] { sp["sticker0"], sp["sticker1"], sp["sticker2"] };
        ui.chatFont = AssetDatabase.LoadAssetAtPath<Font>(AssetLocator.FileNamed("中文_Deng.ttf"));
        if (ui.chatFont == null) Debug.LogError("[PhoneChatBuilder] 没找到中文字体 中文_Deng.ttf（聊天文字会空白）");

        var runner = Object.FindObjectOfType<StoryRunner>();
        if (runner != null)
        {
            runner.phoneChat = ui;
            EditorUtility.SetDirty(runner);
            log.Add("接线：StoryRunner.phoneChat ✓");
        }
        else log.Add("★ 场景里没有 StoryRunner —— 运行时不会显示手机（先跑第一章剧情搭建）");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        log.Add("");
        log.Add("自检：");
        log.Add("  · " + NODE + " 在画布下 siblingIndex=1（遮罩之上，对话/选择题之下）✓");
        log.Add("  · 默认 SetActive(false)，运行时由 Show()/Hide() 接管 ✓");
        log.Add("  · CanvasGroup.blocksRaycasts=false，点击穿透不打断剧情推进 ✓");
        log.Add("  · 既有节点（对话/对话框/名字/对话内容/遮罩/交互提示/ChoicePanel）一个值都没动 ✓");
        Directory.CreateDirectory("Assets/assets/_报告");
        File.WriteAllText(REPORT, string.Join("\n", log.ToArray()));
        AssetDatabase.ImportAsset(REPORT);
        Debug.Log("[PhoneChatBuilder] 搭建完成，报告：" + REPORT);
    }

    // ================================================================== 预览
    [MenuItem("Tools/干预项目/渲染手机聊天UI预览")]
    public static void RenderPreview()
    {
        var canvasT = GameObject.Find("UI交互");
        if (canvasT == null) canvasT = GameObject.Find("UI_门口交互");
        if (canvasT == null) { Debug.LogError("[PhoneChatBuilder] 场景里没有 UI交互 画布"); return; }
        var root = canvasT.transform.Find(NODE);
        var ui = root != null ? root.GetComponent<PhoneChatUI>() : null;
        if (ui == null) { Debug.LogError("[PhoneChatBuilder] 先跑『搭建手机聊天UI』"); return; }
        var canvas = canvasT.GetComponent<Canvas>();
        var scaler = canvasT.GetComponent<CanvasScaler>();
        var cam = Camera.main;
        if (cam == null) cam = Object.FindObjectOfType<Camera>();
        if (cam == null) { Debug.LogError("[PhoneChatBuilder] 场景里没有相机"); return; }

        // 临时收起其它剧情 UI，只看手机（只切 active，渲染完原样恢复）
        var hidden = new List<(GameObject go, bool was)>();
        foreach (Transform c in canvasT.transform)
        {
            if (c.name == NODE || !c.gameObject.activeSelf) continue;
            c.gameObject.SetActive(false);
            hidden.Add((c.gameObject, true));
        }

        // 摆一段真实第1章聊天（含 [比心] 贴纸），滚动停最新一条
        ui.gameObject.SetActive(true);
        ui.GetComponent<CanvasGroup>().alpha = 1f;      // 编辑模式不走 Show() 的动画，直接给不透明
        ui.Clear();
        ui.Append("徐夏（微信）", "溪溪，我快愁死了");
        ui.Append("徐夏（微信）", "老师分的小组作业，组长居然让我做汇报，我本来就上台紧张，肯定要搞砸的");
        ui.Append("林溪（微信）", "夏夏，你别慌，我知道你怕搞砸，换我我也紧张");
        ui.Append("林溪（微信）", "你忘了上次你做的课堂展示？老师都夸你逻辑清晰、说话有条理，哪里差啦");
        ui.Append("徐夏（微信）", "那不一样啊，上次是小展示，这次是小组作业，影响大家分数的！！");
        ui.Append("林溪（微信）", "夏夏呀！");
        ui.Append("林溪（微信）", "[比心]");
        ui.Append("林溪（微信）", "怕就多准备嘛，先写个汇报大纲，每天练几分钟呢？");

        // 1:1 屏幕空间相机渲染（照抄主界面预览的套路；渲染完全部还原）
        var oldRender = canvas.renderMode; var oldCamRef = canvas.worldCamera; var oldPlane = canvas.planeDistance;
        var oldScaleMode = scaler.uiScaleMode; var oldFactor = scaler.scaleFactor;
        var oldOrtho = cam.orthographic; var oldSize = cam.orthographicSize;
        var oldClear = cam.clearFlags; var oldBg = cam.backgroundColor;
        var oldPos = cam.transform.position; var oldRot = cam.transform.rotation; var oldFar = cam.farClipPlane;
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = 10f;
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = 1f;
        cam.orthographic = true;
        cam.orthographicSize = 540f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color32(0xC9, 0xDD, 0xEC, 0xFF);
        // 相机挪进虚空 + 近远裁剪：正交 540m 若留在原地，会把场景近处物体放大成一团黑
        // （UI 是屏幕空间渲染，跟相机位置无关，照常出图）
        cam.transform.position = new Vector3(0f, -50000f, 0f);
        cam.transform.rotation = Quaternion.identity;
        cam.farClipPlane = 10f;

        var rt = new RenderTexture(1920, 1080, 24);
        cam.targetTexture = rt;
        Canvas.ForceUpdateCanvases();
        cam.Render();
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
        tex.Apply();
        RenderTexture.active = null;

        Directory.CreateDirectory("Assets/assets/_报告/预览/场景");
        File.WriteAllBytes(PREVIEW, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        cam.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(rt);
        canvas.renderMode = oldRender; canvas.worldCamera = oldCamRef; canvas.planeDistance = oldPlane;
        scaler.uiScaleMode = oldScaleMode; scaler.scaleFactor = oldFactor;
        cam.orthographic = oldOrtho; cam.orthographicSize = oldSize;
        cam.clearFlags = oldClear; cam.backgroundColor = oldBg;
        cam.transform.position = oldPos; cam.transform.rotation = oldRot; cam.farClipPlane = oldFar;

        ui.Clear();
        ui.GetComponent<CanvasGroup>().alpha = 0f;      // 恢复搭建默认（运行时 Show 会自己置 1）
        ui.gameObject.SetActive(false);
        foreach (var h in hidden) if (h.go != null) h.go.SetActive(true);

        Debug.Log("[PhoneChatBuilder] 预览已写 " + PREVIEW);
    }

    // ================================================================== 小工具
    static void SetLayerRecursively(GameObject go, int layer)
    {
        if (layer < 0) return;                                  // 工程里没有 UI 层就不管
        go.layer = layer;
        foreach (Transform c in go.transform) SetLayerRecursively(c.gameObject, layer);
    }

    static Image NewImage(string name, RectTransform parent, Sprite sp)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var t = (RectTransform)go.transform;
        t.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = sp;
        img.raycastTarget = false;
        return img;
    }

    static Text NewText(string name, RectTransform parent, Color col, int size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        var t = (RectTransform)go.transform;
        t.SetParent(parent, false);
        var txt = go.GetComponent<Text>();
        txt.color = col;
        txt.fontSize = size;
        txt.font = AssetDatabase.LoadAssetAtPath<Font>(AssetLocator.FileNamed("中文_Deng.ttf"));
        txt.raycastTarget = false;
        return txt;
    }
}

// 工程里存在 Assets/_phonechat_trigger.txt 时，编辑器下次刷新/重编译后自动跑一次（搭 + 渲染预览）。
[InitializeOnLoad]
public static class PhoneChatBuilderTrigger
{
    const string Trigger = "Assets/_phonechat_trigger.txt";
    const string ErrFile = "../额外文件/错误_手机聊天.txt";

    static PhoneChatBuilderTrigger()
    {
        if (File.Exists(Trigger))
        {
            EditorApplication.delayCall += () =>
            {
                try
                {
                    if (File.Exists(Trigger)) File.Delete(Trigger);
                    if (File.Exists(Trigger + ".meta")) File.Delete(Trigger + ".meta");
                    PhoneChatBuilder.Run();
                    PhoneChatBuilder.RenderPreview();
                }
                catch (System.Exception e)
                {
                    Directory.CreateDirectory("../额外文件");
                    File.WriteAllText(ErrFile, e.ToString());
                    throw;
                }
            };
        }
    }
}
