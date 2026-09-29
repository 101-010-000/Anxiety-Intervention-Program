// 存档卡定妆照：给存读档页的「有档」卡片换上每章的游戏场景定妆照。
//   痛点（2026-09-28 用户试玩反馈）：旧卡片白实底外露 + 运行时截图直角无裁切 + 章节文字压图。
//   方案：每章预渲染一张干净场景图（机位照 ScenePostFx 后期预览，开后期、无 UI 入镜），
//         圆角 + 底部压暗带直接烘进 PNG；存档卡整卡铺图，章名/时间压在暗带上，选中线框压在图上。
// 用法：菜单 Tools/干预项目/存档卡定妆照/…，或丢 Assets/_savecard_trigger.txt（依次①②③，跑完自删触发器）
// 输出：Assets/assets/05_UI/存档插图/ch1..ch5.png
//      + 报告 Assets/assets/_报告/_存档卡定妆照.txt + 预览 assets/_报告/预览/存档卡/
// 幂等：②只写本工具约定的槽位子树节点值，重复跑结果一致；不 AddComponent、不碰其他页。

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

public static class SaveCardArt
{
    // ------------------------------------------------------------------ 常量（都在这里调）
    const int   ART_W      = 720;     // 定妆照分辨率（卡片 360×200 的整 2 倍，比例正好 1.8:1）
    const int   ART_H      = 400;
    const float RADIUS     = 24f;     // 圆角半径（图上像素）
    const float CORNER_AA  = 2f;      // 圆角边缘的线性过渡宽度（抗锯齿）
    const int   BAND_H     = 120;     // 底部压暗带：渐变区高度
    const int   BAND_SOLID = 40;      //   最底部恒定不透明度的高度
    const float BAND_ALPHA = 0.72f;   //   压暗带最深处的不透明度
    static readonly Color BAND_COLOR = new Color32(0x16, 0x20, 0x2E, 0xFF);   // #16202E 深蓝黑

    const string GAME_SCENE  = "Assets/Scenes/Game.unity";
    const string MENU_SCENE  = "Assets/Scenes/MainMenu.unity";
    const string ART_DIR     = "Assets/assets/05_UI/存档插图";
    const string REPORT      = "Assets/assets/_报告/_存档卡定妆照.txt";
    const string PREVIEW_DIR = "Assets/assets/_报告/预览/存档卡";

    /// 章 → 定妆照内容（地点 + 机位水平偏转）。ch5 与 ch2 同一间宿舍，转 180° 区分。
    class ChapterDef { public int Ch; public string Loc; public float YawOff; }
    static readonly ChapterDef[] Chapters =
    {
        new ChapterDef { Ch = 1, Loc = "Loc_教室",   YawOff =   0f },
        new ChapterDef { Ch = 2, Loc = "Loc_宿舍",   YawOff =   0f },
        new ChapterDef { Ch = 3, Loc = "Loc_办公室", YawOff =   0f },
        new ChapterDef { Ch = 4, Loc = "Loc_图书馆", YawOff =   0f },
        new ChapterDef { Ch = 5, Loc = "Loc_宿舍",   YawOff = 180f },
    };

    // ------------------------------------------------------------------ 菜单
    [MenuItem("Tools/干预项目/存档卡定妆照/① 渲染5章定妆照（Game.unity）", false, 20)]
    public static void RenderArts()
    {
        var log = NewLog();
        DoRender(log);
        WriteReport(log);
    }

    [MenuItem("Tools/干预项目/存档卡定妆照/② 接线存读档槽位（MainMenu.unity）", false, 21)]
    public static void WireSlots()
    {
        var log = NewLog();
        DoWire(log);
        WriteReport(log);
    }

    [MenuItem("Tools/干预项目/存档卡定妆照/③ 渲染槽位预览", false, 22)]
    public static void RenderSlotPreview()
    {
        var log = NewLog();
        DoPreview(log);
        WriteReport(log);
    }

    /// 触发器入口：依次 ①②③（一段失败就停），最后写一份完整报告
    public static void RunAllFromTrigger()
    {
        var log = NewLog();
        if (DoRender(log))
            if (DoWire(log))
                DoPreview(log);
        WriteReport(log);
    }

    static List<string> NewLog()
    {
        return new List<string> { "存档卡定妆照  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"), "" };
    }

    static void WriteReport(List<string> log)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(REPORT));
        File.WriteAllText(REPORT, string.Join("\n", log.ToArray()));
        Debug.Log("[SaveCardArt] 报告：" + REPORT);
    }

    // ------------------------------------------------------------------ ① 渲染定妆照
    static bool DoRender(List<string> log)
    {
        log.Add("── ① 渲染 5 章定妆照 ──");
        log.Add(string.Format("  参数：{0}×{1}，圆角 {2}px（过渡 {3}px），压暗带 渐变{4}px/恒值{5}px/alpha {6}（#{7}）",
            ART_W, ART_H, RADIUS, CORNER_AA, BAND_H, BAND_SOLID, BAND_ALPHA, ColorUtility.ToHtmlStringRGB(BAND_COLOR)));
        log.Add("  机位与 ScenePostFx 后期预览一致（站房间中心、眼高 1.62m、开后期、无 UI 入镜）");
        log.Add("");

        var cur = SceneManager.GetActiveScene();
        if (CheckDirty(cur, log)) return false;
        if (!File.Exists(GAME_SCENE)) { log.Add("★中止：找不到 " + GAME_SCENE); return false; }
        if (!File.Exists(MENU_SCENE)) { log.Add("★中止：找不到 " + MENU_SCENE); return false; }

        if (cur.path != GAME_SCENE) EditorSceneManager.OpenScene(GAME_SCENE, OpenSceneMode.Single);
        var locsRoot = GameObject.Find("Locations");
        if (locsRoot == null) { log.Add("★中止：Game.unity 里没有 Locations 根节点"); return false; }

        // 相机参数照 ScenePostFx.RenderPreviewInternal（HDR + 后期 + SMAA，这样定妆照和游戏里看到的一致）
        var camGO = new GameObject("savecard_cam");
        var cam = camGO.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.fieldOfView = 62f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 600f;
        cam.allowHDR = true;
        var extra = cam.GetUniversalAdditionalCameraData();
        extra.renderPostProcessing = true;
        extra.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        extra.antialiasingQuality = AntialiasingQuality.High;
        extra.dithering = true;

        var rt = new RenderTexture(ART_W, ART_H, 24, RenderTextureFormat.DefaultHDR);
        cam.targetTexture = rt;
        cam.Render();                              // 预热 shader

        Directory.CreateDirectory(ART_DIR);
        int err = 0;
        foreach (var def in Chapters)
        {
            var loc = locsRoot.transform.Find(def.Loc);
            var floor = loc != null ? loc.Find("Shell/Shell_地板") : null;
            var rb = floor != null ? floor.GetComponent<Renderer>() : null;
            if (rb == null) { log.Add("  ★ch" + def.Ch + "：找不到 " + def.Loc + "/Shell/Shell_地板，跳过"); err++; continue; }

            // 机位算法照 ScenePostFx.RenderPreviewInternal：站房间中心、眼高 1.62，窄长房沿长边看
            float d = rb.bounds.size.z, w = rb.bounds.size.x;
            bool alongX = w > d * 1.8f;
            float len = alongX ? w : d;
            Vector3 eye = new Vector3(loc.position.x, 1.62f, loc.position.z);
            Vector3 focus = eye;
            if (alongX) { eye.x = loc.position.x - len * 0.20f; focus.x = loc.position.x + len * 0.45f; }
            else        { eye.z = loc.position.z - len * 0.10f; focus.z = loc.position.z + len * 0.45f; }
            focus.y = 1.30f;
            cam.transform.position = eye;
            cam.transform.LookAt(focus, Vector3.up);
            if (def.YawOff != 0f) cam.transform.RotateAround(focus, Vector3.up, def.YawOff);   // 同屋换角度

            cam.Render(); cam.Render();
            string path = ART_DIR + "/ch" + def.Ch + ".png";
            ShootProcessed(rt, path);
            ConfigureSpriteImport(path);
            if (AssetDatabase.LoadAssetAtPath<Sprite>(path) == null)
            {
                log.Add("  ★ch" + def.Ch + "：PNG 已写但 Sprite 导入失败");
                err++;
                continue;
            }
            log.Add(string.Format("  ch{0} ← {1}（{2:0.#}×{3:0.#}m，{4}，偏转 {5}°）→ 存档插图/ch{0}.png ✓",
                def.Ch, def.Loc, w, d, alongX ? "沿长边" : "朝北看", def.YawOff));
        }
        cam.targetTexture = null;              // 先解绑再销毁（还挂在相机上就释放会报 "Releasing render texture…"）
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(camGO);

        // 临时相机的增删会把场景弄脏：存一次盘（内容与打开时一致，不产生真实改动），再切回主界面
        var gameScene = SceneManager.GetActiveScene();
        if (gameScene.isDirty) EditorSceneManager.SaveScene(gameScene);
        if (SceneManager.GetActiveScene().path != MENU_SCENE)
            EditorSceneManager.OpenScene(MENU_SCENE, OpenSceneMode.Single);

        log.Add("");
        log.Add("  已切回 " + MENU_SCENE + "（可继续 ②③）。本段错误：" + err + " 条" + (err == 0 ? " ✓" : " ★"));
        return err == 0;
    }

    /// 渲好的 RT 读回来 → 圆角 + 底部压暗带（烘进像素）→ 落 PNG
    static void ShootProcessed(RenderTexture rt, string path)
    {
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;

        var px = tex.GetPixels();
        int w = tex.width, h = tex.height;
        for (int y = 0; y < h; y++)
        {
            int fromBottom = h - 1 - y;
            // 压暗带：BAND_H 高度里线性升到 BAND_ALPHA，最底 BAND_SOLID 恒定
            float band = fromBottom >= BAND_H ? 0f
                       : fromBottom <= BAND_SOLID ? BAND_ALPHA
                       : BAND_ALPHA * (fromBottom - BAND_SOLID) / (float)(BAND_H - BAND_SOLID);
            for (int x = 0; x < w; x++)
            {
                var c = px[y * w + x];

                // 圆角：只处理四个角象限内的像素，圆外全透明、2px 线性过渡（抗锯齿）
                float dx = Mathf.Min(x, w - 1 - x);
                float dy = Mathf.Min(y, h - 1 - y);
                if (dx < RADIUS && dy < RADIUS)
                {
                    float qx = dx - RADIUS, qy = dy - RADIUS;
                    float dist = Mathf.Sqrt(qx * qx + qy * qy);
                    float a = Mathf.Clamp01((RADIUS - dist) / CORNER_AA + 0.5f);
                    if (a <= 0f) { px[y * w + x] = Color.clear; continue; }
                    c.a = a;
                }
                float keepA = c.a;
                if (band > 0f) c = Color.Lerp(c, BAND_COLOR, band);
                c.a = keepA;   // 暗带只压颜色，不吃掉圆角边缘那 2px 的透明过渡（压暗带穿过底部两角）
                px[y * w + x] = c;
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }

    /// 定妆照导入设置：Sprite、单图（无九宫格）、关 mipmap、圆角要透明
    static void ConfigureSpriteImport(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) return;
        if (ti.textureType != TextureImporterType.Sprite || ti.mipmapEnabled || !ti.alphaIsTransparency)
        {
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.SaveAndReimport();
        }
    }

    // ------------------------------------------------------------------ ② 接线存读档槽位
    static bool DoWire(List<string> log)
    {
        log.Add("── ② 接线存读档槽位 ──");
        var cur = SceneManager.GetActiveScene();
        if (!RequireMenuScene(cur, log)) return false;
        if (CheckDirty(cur, log)) return false;

        var canvasGO = GameObject.Find("UI_Canvas");
        if (canvasGO == null) { log.Add("★中止：场景里没有 UI_Canvas"); return false; }
        var uiRoot = canvasGO.GetComponent<MainMenuUI>();
        if (uiRoot == null) { log.Add("★中止：MainMenuUI 是缺脚本状态（重跑『搭建主界面UI场景』或先解决编译错误）"); return false; }
        var page = canvasGO.transform.Find("Page_存读档");
        if (page == null) { log.Add("★中止：找不到 Page_存读档"); return false; }

        // 5 张定妆照 → MainMenuUI.chapterArt（运行时 SaveSlotUI 优先用它，缺图回退旧截图）
        var arts = new Sprite[5];
        int got = 0;
        for (int i = 0; i < 5; i++)
        {
            arts[i] = AssetDatabase.LoadAssetAtPath<Sprite>(ART_DIR + "/ch" + (i + 1) + ".png");
            if (arts[i] != null) got++;
            else log.Add("  ★缺定妆照 ch" + (i + 1) + "（先跑 ①；不阻塞接线，运行时会回退旧截图）");
        }
        uiRoot.chapterArt = arts;

        var slots = page.GetComponentsInChildren<SaveSlotUI>(true);
        System.Array.Sort(slots, (a, b) => a.index.CompareTo(b.index));
        if (slots.Length != 6) log.Add("  ★Page_存读档 下找到 " + slots.Length + " 个 SaveSlotUI（应为 6）");

        int err = 0, patched = 0;
        foreach (var s in slots) { PatchSlot(s, log, ref err); patched++; }

        EditorSceneManager.MarkSceneDirty(cur);
        bool saved = EditorSceneManager.SaveScene(cur);
        log.Add(string.Format("  接线 {0} 个槽位，chapterArt {1}/5 张；保存场景：{2}", patched, got, saved ? "成功" : "★失败"));
        log.Add("  自检：未 AddComponent；空框/空存档/点击区/选中框 及其他页节点一律没动。");
        log.Add("  本段错误：" + err + " 条" + (err == 0 ? " ✓" : " ★"));
        return saved && err == 0 && patched == 6;
    }

    /// 单个槽位的幂等补丁（数值来自定稿设计，只写这些，其余一概不碰）
    static void PatchSlot(SaveSlotUI ui, List<string> log, ref int err)
    {
        if (ui == null || ui.thumb == null)
        {
            log.Add("  ★" + (ui != null ? ui.name : "?") + "：缩略图引用缺失");
            err++;
            return;
        }

        // 缩略图 → 整卡铺定妆照（360×200 压满、关射线）
        var trt = (RectTransform)ui.thumb.transform;
        trt.sizeDelta = new Vector2(360f, 200f);
        trt.anchoredPosition = Vector2.zero;
        ui.thumb.raycastTarget = false;

        // 层级：选中指示物必须压在图上。当前场景没有「选中框」线框节点（选中态 = SlotSelectFx 的
        // 右上角「选中角标」，是槽位最后一个子节点，天然在图之上）；若以后接回 selectFrame，
        // 把图挪到它前面（图在下、线框在上）。
        if (ui.selectFrame != null && ui.thumb.transform.GetSiblingIndex() > ui.selectFrame.transform.GetSiblingIndex())
            ui.thumb.transform.SetSiblingIndex(ui.selectFrame.transform.GetSiblingIndex());

        // 白色实底整卡藏掉（定妆照自带底；节点保留，旧序列化引用不悬空）
        if (ui.frameFilled != null) ui.frameFilled.enabled = false;

        // 章名（白）/时间（浅灰）压进底部暗带，与图零重叠
        if (ui.chapterText != null)
        {
            var rt = (RectTransform)ui.chapterText.transform;
            rt.anchoredPosition = new Vector2(0f, -64f);
            rt.sizeDelta = new Vector2(320f, 28f);
            ui.chapterText.color = Color.white;                        // 字号保持 22 不动
        }
        if (ui.timeText != null)
        {
            var rt = (RectTransform)ui.timeText.transform;
            rt.anchoredPosition = new Vector2(0f, -88f);
            rt.sizeDelta = new Vector2(320f, 22f);
            ui.timeText.color = new Color(0.86f, 0.90f, 0.94f, 0.92f);
            ui.timeText.fontSize = 17;
        }
        log.Add("  " + ui.name + " ✓（缩略图整卡铺图、实底隐藏、章节/时间落暗带；选中指示在图上未被盖）");
    }

    // ------------------------------------------------------------------ ③ 渲染槽位预览
    static bool DoPreview(List<string> log)
    {
        log.Add("── ③ 渲染槽位预览 ──");
        var cur = SceneManager.GetActiveScene();
        if (!RequireMenuScene(cur, log)) return false;
        if (CheckDirty(cur, log)) return false;

        var canvasGO = GameObject.Find("UI_Canvas");
        if (canvasGO == null) { log.Add("★中止：场景里没有 UI_Canvas"); return false; }
        var canvas = canvasGO.GetComponent<Canvas>();
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        var ui = canvasGO.GetComponent<MainMenuUI>();
        if (canvas == null || ui == null) { log.Add("★中止：Canvas / MainMenuUI 缺失或缺脚本"); return false; }
        var cam = Camera.main;
        if (cam == null) { log.Add("★中止：场景里没有主相机"); return false; }
        if (ui.savePanel == null) { log.Add("★中止：MainMenuUI.savePanel 没接"); return false; }

        var page = canvasGO.transform.Find("Page_存读档");
        var slots = page != null ? page.GetComponentsInChildren<SaveSlotUI>(true) : new SaveSlotUI[0];
        System.Array.Sort(slots, (a, b) => a.index.CompareTo(b.index));
        if (slots.Length < 2 || slots[0] == null || slots[1] == null)
        { log.Add("★中止：Page_存读档 下没找到足够的槽位"); return false; }

        Directory.CreateDirectory(PREVIEW_DIR);

        // 切成 1:1 屏幕空间相机渲染（照 MainMenuBuilder.RenderPreviews 的做法：1920×1080 一像素一单位）
        var oldRenderMode = canvas.renderMode; var oldWorldCam = canvas.worldCamera; var oldPlane = canvas.planeDistance;
        var oldScaleMode = scaler != null ? scaler.uiScaleMode : CanvasScaler.ScaleMode.ConstantPixelSize;
        var oldFactor = scaler != null ? scaler.scaleFactor : 1f;
        var oldOrtho = cam.orthographic; var oldSize = cam.orthographicSize;
        var oldClear = cam.clearFlags; var oldBg = cam.backgroundColor;
        var oldCamPos = cam.transform.position; var oldCamRot = cam.transform.rotation;

        // 面板显示状态备份（编辑模式下 Show/Hide 直接改 CanvasGroup 和 RectTransform，跑完原样还原）
        var panels = CollectPanels(ui);
        var panSnap = new List<PanelSnap>();
        foreach (var p in panels) panSnap.Add(PanelSnap.Capture(p));

        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = 10f;
        if (scaler != null) { scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; scaler.scaleFactor = 1f; }
        cam.orthographic = true;
        cam.orthographicSize = 540f;       // 1080 / 2 → 1 UI 单位 = 1 像素
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = MainMenuAssets.BG_MID;
        cam.transform.position = Vector3.zero;
        cam.transform.rotation = Quaternion.identity;

        foreach (var p in panels) p.Hide(true);
        ui.savePanel.Show(true);

        // 摆三态：空档（场景原样）→ 有档（槽位1/2 假装存了 ch1/ch4）→ 选中（对勾角标亮起）
        var snap1 = SlotSnap.Capture(slots[0]);
        var snap2 = SlotSnap.Capture(slots[1]);
        var arts = new Sprite[5];
        int missing = 0;
        for (int i = 0; i < 5; i++)
        {
            arts[i] = AssetDatabase.LoadAssetAtPath<Sprite>(ART_DIR + "/ch" + (i + 1) + ".png");
            if (arts[i] == null) missing++;
        }
        if (missing > 0) log.Add("  ★缺 " + missing + " 张定妆照（先跑 ①）——预览里「有档」卡会退成空图");

        var states = new (string name, bool filled, bool selected)[]
        {
            ("空档", false, false),
            ("有档", true,  false),
            ("选中", true,  true),
        };

        var rt = new RenderTexture(1920, 1080, 24);
        cam.targetTexture = rt;
        cam.Render(); cam.Render();        // 预热 shader

        int err = missing;
        foreach (var st in states)
        {
            if (st.filled) { MockFill(slots[0], 1, arts); MockFill(slots[1], 4, arts); }
            else           { snap1.Apply(slots[0]);       snap2.Apply(slots[1]); }
            SetSelectedVisual(slots[0], st.selected);

            Canvas.ForceUpdateCanvases();
            cam.Render(); cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            string full = PREVIEW_DIR + "/整页_" + st.name + ".png";
            File.WriteAllBytes(full, tex.EncodeToPNG());
            SaveCrop(tex, SlotPixelRect((RectTransform)slots[0].transform), PREVIEW_DIR + "/特写_" + st.name + ".png");
            log.Add(string.Format("  整页_{0}.png + 特写_{0}.png   {1:0} KB", st.name, new FileInfo(full).Length / 1024f));
            Object.DestroyImmediate(tex);
        }

        // 还原（槽位 → 面板 → 画布/相机），内容与进入前一致，存一次盘清掉脏标记
        snap1.Apply(slots[0]); snap2.Apply(slots[1]);
        foreach (var s in panSnap) s.Apply();
        cam.targetTexture = null;
        canvas.renderMode = oldRenderMode; canvas.worldCamera = oldWorldCam; canvas.planeDistance = oldPlane;
        if (scaler != null) { scaler.uiScaleMode = oldScaleMode; scaler.scaleFactor = oldFactor; }
        cam.orthographic = oldOrtho; cam.orthographicSize = oldSize;
        cam.clearFlags = oldClear; cam.backgroundColor = oldBg;
        cam.transform.position = oldCamPos; cam.transform.rotation = oldCamRot;
        Object.DestroyImmediate(rt);
        Canvas.ForceUpdateCanvases();

        // 定妆照拷一份进预览目录，验收一处看全
        for (int i = 1; i <= 5; i++)
        {
            var src = ART_DIR + "/ch" + i + ".png";
            if (File.Exists(src)) File.Copy(src, PREVIEW_DIR + "/定妆照_ch" + i + ".png", true);
        }

        if (cur.isDirty) EditorSceneManager.SaveScene(cur);
        log.Add("  槽位/面板/画布已原样还原并保存场景；定妆照 5 张已拷入本目录。");
        log.Add("  本段错误：" + err + " 条" + (err == 0 ? " ✓" : " ★"));
        return err == 0;
    }

    /// 预览用：把一个槽位临时摆成「存了第 ch 章」的样子（只改内存值，跑完 SlotSnap 还原）
    static void MockFill(SaveSlotUI s, int ch, Sprite[] arts)
    {
        if (s.thumb != null)
        {
            s.thumb.sprite = (arts != null && ch >= 1 && ch <= arts.Length) ? arts[ch - 1] : null;
            s.thumb.enabled = s.thumb.sprite != null;
        }
        if (s.chapterText != null) { s.chapterText.text = GameProgress.ChapterTitle(ch); s.chapterText.gameObject.SetActive(true); }
        if (s.timeText != null)    { s.timeText.text = "2026-09-28 12:00";               s.timeText.gameObject.SetActive(true); }
        if (s.emptyText != null)   s.emptyText.gameObject.SetActive(false);
        if (s.frameEmpty != null)  s.frameEmpty.enabled = false;
    }

    /// 预览用：把选中指示摆到终态。当前场景的选中态 = SlotSelectFx 的对勾角标 + 空框变色
    ///（编辑器模式 Update 不跑，SetSelected 不会自己落到位），这里直接写成选中/未选中的稳定值。
    static void SetSelectedVisual(SaveSlotUI s, bool on)
    {
        if (s == null) return;
        if (s.selectFx != null && s.selectFx.badgeCg != null)
            s.selectFx.badgeCg.alpha = on ? 1f : 0f;
        if (s.selectFrame != null) s.selectFrame.enabled = on;   // 旧场景兜底（当前场景 selectFrame 未接线）
        if (s.selectFx != null && s.selectFx.emptyFrame != null)
            s.selectFx.emptyFrame.color = on ? s.selectFx.frameColorSelected : s.selectFx.frameColorNormal;
    }

    /// 槽位在 1920×1080 预览图上的像素范围（1:1 相机：世界坐标 = 像素 - 画布中心）
    static RectInt SlotPixelRect(RectTransform rt)
    {
        var c = new Vector3[4];
        rt.GetWorldCorners(c);
        int m = 14;    // 留一点边，看得出卡片轮廓
        int px = Mathf.Clamp(Mathf.FloorToInt(c[0].x) + 960 - m, 0, 1920);
        int py = Mathf.Clamp(Mathf.FloorToInt(c[0].y) + 540 - m, 0, 1080);
        int pw = Mathf.Clamp(Mathf.CeilToInt(c[2].x) + 960 + m, 0, 1920) - px;
        int ph = Mathf.Clamp(Mathf.CeilToInt(c[2].y) + 540 + m, 0, 1080) - py;
        return new RectInt(px, py, pw, ph);
    }

    static void SaveCrop(Texture2D full, RectInt r, string path)
    {
        if (r.width <= 0 || r.height <= 0) return;
        var t = new Texture2D(r.width, r.height, TextureFormat.RGB24, false);
        t.SetPixels(full.GetPixels(r.x, r.y, r.width, r.height));
        t.Apply();
        File.WriteAllBytes(path, t.EncodeToPNG());
        Object.DestroyImmediate(t);
    }

    static List<UIPanel> CollectPanels(MainMenuUI ui)
    {
        var l = new List<UIPanel>();
        foreach (var p in new[] { ui.menuLayer, ui.settingsPanel, ui.savePanel, ui.chapterPanel, ui.overviewPanel, ui.imagePanel })
            if (p != null && !l.Contains(p)) l.Add(p);
        if (ui.confirm != null && ui.confirm.panel != null && !l.Contains(ui.confirm.panel)) l.Add(ui.confirm.panel);
        return l;
    }

    // ------------------------------------------------------------------ 小工具
    /// 场景有未保存改动就中止（切场景会静默丢改动，也不许弹窗卡自动化）
    static bool CheckDirty(Scene cur, List<string> log)
    {
        if (!cur.isDirty) return false;
        log.Add("★中止：当前场景（" + SceneName(cur) + "）有未保存改动。");
        log.Add("  先 Ctrl+S 保存，再跑本工具。");
        return true;
    }

    static bool RequireMenuScene(Scene cur, List<string> log)
    {
        if (cur.path == MENU_SCENE) return true;
        log.Add("★中止：本步要求打开 " + MENU_SCENE + "，当前是「" + SceneName(cur) + "」。");
        log.Add("  打开主界面场景后再跑本入口（触发器流程会自己切过去）。");
        return false;
    }

    static string SceneName(Scene cur)
    {
        return cur.IsValid() && cur.path.Length > 0 ? cur.path : "未命名场景";
    }

    /// 面板显示状态的备份/还原（编辑模式下 UIPanel.Show/Hide 改的就是这几样）
    class PanelSnap
    {
        UIPanel p;
        float alpha; bool blocks, interact;
        Vector2 pos; Vector3 scale;
        CanvasGroup dimmerCg; float dimmerAlpha; bool dimmerBlocks;

        public static PanelSnap Capture(UIPanel panel)
        {
            var s = new PanelSnap { p = panel };
            if (panel == null) return s;
            var cg = panel.GetComponent<CanvasGroup>();
            if (cg != null) { s.alpha = cg.alpha; s.blocks = cg.blocksRaycasts; s.interact = cg.interactable; }
            var rt = panel.transform as RectTransform;
            if (rt != null) { s.pos = rt.anchoredPosition; s.scale = rt.localScale; }
            if (panel.dimmer != null)
            {
                s.dimmerCg = panel.dimmer.GetComponent<CanvasGroup>();
                if (s.dimmerCg != null) { s.dimmerAlpha = s.dimmerCg.alpha; s.dimmerBlocks = s.dimmerCg.blocksRaycasts; }
            }
            return s;
        }

        public void Apply()
        {
            if (p == null) return;
            var cg = p.GetComponent<CanvasGroup>();
            if (cg != null) { cg.alpha = alpha; cg.blocksRaycasts = blocks; cg.interactable = interact; }
            var rt = p.transform as RectTransform;
            if (rt != null) { rt.anchoredPosition = pos; rt.localScale = scale; }
            if (dimmerCg != null) { dimmerCg.alpha = dimmerAlpha; dimmerCg.blocksRaycasts = dimmerBlocks; }
        }
    }

    /// 槽位显示值的备份/还原（预览摆三态用，跑完恢复成场景原样）
    class SlotSnap
    {
        Sprite thumbSprite; bool thumbOn;
        string chapText; bool chapOn;
        string timeTxt; bool timeOn;
        bool emptyOn, frameEmptyOn, selOn;
        CanvasGroup badgeCg; float badgeA; bool hasBadge;
        Color emptyFrameColor; bool hasEmptyFrameColor;

        public static SlotSnap Capture(SaveSlotUI s)
        {
            var snap = new SlotSnap
            {
                thumbSprite = s.thumb != null ? s.thumb.sprite : null,
                thumbOn = s.thumb != null && s.thumb.enabled,
                chapText = s.chapterText != null ? s.chapterText.text : "",
                chapOn = s.chapterText != null && s.chapterText.gameObject.activeSelf,
                timeTxt = s.timeText != null ? s.timeText.text : "",
                timeOn = s.timeText != null && s.timeText.gameObject.activeSelf,
                emptyOn = s.emptyText == null || s.emptyText.gameObject.activeSelf,
                frameEmptyOn = s.frameEmpty == null || s.frameEmpty.enabled,
                selOn = s.selectFrame != null && s.selectFrame.enabled,
            };
            if (s.selectFx != null)
            {
                if (s.selectFx.badgeCg != null) { snap.badgeCg = s.selectFx.badgeCg; snap.badgeA = snap.badgeCg.alpha; snap.hasBadge = true; }
                if (s.selectFx.emptyFrame != null) { snap.emptyFrameColor = s.selectFx.emptyFrame.color; snap.hasEmptyFrameColor = true; }
            }
            return snap;
        }

        public void Apply(SaveSlotUI s)
        {
            if (s.thumb != null) { s.thumb.sprite = thumbSprite; s.thumb.enabled = thumbOn; }
            if (s.chapterText != null) { s.chapterText.text = chapText; s.chapterText.gameObject.SetActive(chapOn); }
            if (s.timeText != null) { s.timeText.text = timeTxt; s.timeText.gameObject.SetActive(timeOn); }
            if (s.emptyText != null) s.emptyText.gameObject.SetActive(emptyOn);
            if (s.frameEmpty != null) s.frameEmpty.enabled = frameEmptyOn;
            if (s.selectFrame != null) s.selectFrame.enabled = selOn;
            if (hasBadge && badgeCg != null) badgeCg.alpha = badgeA;
            if (hasEmptyFrameColor && s.selectFx != null && s.selectFx.emptyFrame != null)
                s.selectFx.emptyFrame.color = emptyFrameColor;
        }
    }
}

/// 触发器：Assets/_savecard_trigger.txt 存在时依次跑 ①②③，用完自删（照仓库既有触发器模式）
[InitializeOnLoad]
static class SaveCardArtTrigger
{
    const string T = "Assets/_savecard_trigger.txt";

    static SaveCardArtTrigger()
    {
        if (!File.Exists(T)) return;
        EditorApplication.delayCall += () =>
        {
            try
            {
                if (File.Exists(T)) File.Delete(T);
                DelMeta(T);
                SaveCardArt.RunAllFromTrigger();
            }
            catch (System.Exception e)
            {
                Debug.LogError("[SaveCardArt] " + e);
                Directory.CreateDirectory("../额外文件");
                File.WriteAllText("../额外文件/错误_存档卡定妆照.txt", e.ToString());
            }
        };
    }

    static void DelMeta(string p)
    {
        if (File.Exists(p + ".meta")) File.Delete(p + ".meta");
    }
}
