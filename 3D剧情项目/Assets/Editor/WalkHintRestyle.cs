// 走动段任务提示 WalkHint 的样式手术：从「顶部压扁横条」改成「左上角目标卡」。
// 只动 UI交互/WalkHint 子树（对话/对话框/名字/对话内容/交互提示/遮罩等冻结节点一律不碰）；
// 幂等可重跑：竖条 与 CanvasGroup 已存在就复用不重建。改完存盘、写报告，并渲一张 1920×1080 预览。
// 用法：菜单 Tools/干预项目/任务栏样式/改为左上角目标卡（常驻补丁工具，不提供自动触发器）。
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class WalkHintRestyle
{
    const string ScenePath   = "Assets/Scenes/Game.unity";
    const string ReportDir   = "Assets/assets/_报告";
    const string ReportPath  = ReportDir + "/_任务栏改造.txt";
    const string PreviewDir  = ReportDir + "/预览";
    const string PreviewPath = PreviewDir + "/任务栏_目标卡.png";

    const string GlassGuid = "b9f0de3fb0b91234abde7dd354db82a8";   // assets/05_UI/界面_Panel/面板_玻璃.png（border 24）

    // ---- 目标卡参数（1920×1080 参照分辨率）----
    const float PosX = 48f, PosY = -48f;             // 根节点：距左 48 / 距顶 48
    const float Width = 430f, Height = 72f;          // 根节点尺寸
    const float PanelAlpha = 0.82f;                  // 玻璃底白色透明度
    const float BarX = 22f, BarW = 5f, BarH = 36f;   // 竖条：贴左 22，5×36
    const float TextLeft = 44f, TextRight = 20f;     // 文字区左右留白（从竖条右侧起）
    const int   FontSize = 26;

    [MenuItem("Tools/干预项目/任务栏样式/改为左上角目标卡")]
    public static void Run()
    {
        var log = new List<string>();
        var ok = false;
        try
        {
            Surgery(log);
            ok = true;
        }
        catch (System.Exception e)
        {
            log.Add("★异常：" + e.Message);
            log.Add(e.ToString());
            throw;
        }
        finally
        {
            Directory.CreateDirectory(ReportDir);
            log.Insert(0, "任务栏改左上角目标卡（" + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "）结果：" + (ok ? "成功 ✓" : "未完成 ★"));
            File.WriteAllText(ReportPath, string.Join("\n", log.ToArray()) + "\n");
            Debug.Log("[WalkHintRestyle] 报告 → " + ReportPath);
        }
    }

    static void Surgery(List<string> log)
    {
        // 当前打开的不是 Game.unity 就先打开（有未保存修改先问一声，不悄悄丢用户改动）
        var sc = EditorSceneManager.GetActiveScene();
        if (!sc.IsValid() || sc.path != ScenePath)
        {
            if (sc.IsValid() && sc.isDirty && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                log.Add("用户取消：当前场景有未保存修改，未打开 " + ScenePath);
                return;
            }
            sc = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }
        log.Add("场景：" + sc.path);

        var canvasGO = GameObject.Find("UI交互");
        if (canvasGO == null) throw new System.Exception("场景里没有画布 UI交互");
        var wh = canvasGO.transform.Find("WalkHint");
        if (wh == null) throw new System.Exception("UI交互 下按名字找不到 WalkHint（不代建，请放回后再跑）");

        // 贴图先校验再动手：素材丢了就整体不跑，别留一个改了一半的脏场景
        var glass = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(GlassGuid));
        if (glass == null) throw new System.Exception("找不到 面板_玻璃.png（guid " + GlassGuid + "），检查 assets/05_UI/界面_Panel/");

        // 1) 根节点：左上角 430×72 玻璃卡
        var rt = (RectTransform)wh;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(PosX, PosY);
        rt.sizeDelta = new Vector2(Width, Height);

        var img = wh.GetComponent<Image>();
        if (img == null) img = wh.gameObject.AddComponent<Image>();
        img.sprite = glass;                       // 换掉被压扁变形的 F_交互 键帽整图
        img.type = Image.Type.Sliced;             // 九宫格拉伸（border 24 已在导入设置里）
        img.color = new Color(1f, 1f, 1f, PanelAlpha);
        img.raycastTarget = false;
        log.Add("根节点：锚 (0,1)-(0,1)，pivot (0,1)，位置 (" + PosX + "," + PosY + ")，尺寸 " + Width + "×" + Height);
        log.Add("背景图：面板_玻璃.png（Sliced 九宫格），颜色 #FFFFFF × " + PanelAlpha);

        // 2) CanvasGroup：初始 alpha=0（节点保持默认 SetActive(false)，运行时由 StoryRunner 淡入）
        var group = wh.GetComponent<CanvasGroup>();
        if (group == null) { group = wh.gameObject.AddComponent<CanvasGroup>(); log.Add("CanvasGroup：新建"); }
        else log.Add("CanvasGroup：复用已有");
        group.alpha = 0f;

        // 3) 竖条（幂等：有就复用，始终排第一个子节点）
        var barT = wh.Find("竖条");
        Image bar;
        if (barT == null)
        {
            var barGO = new GameObject("竖条", typeof(Image));
            bar = barGO.GetComponent<Image>();
            barT = barGO.transform;
            barT.SetParent(wh, false);
            log.Add("竖条：新建");
        }
        else
        {
            bar = barT.GetComponent<Image>();
            if (bar == null) bar = barT.gameObject.AddComponent<Image>();
            log.Add("竖条：复用已有");
        }
        barT.SetAsFirstSibling();                 // 排在 Text 前面
        var brt = (RectTransform)barT;
        brt.anchorMin = new Vector2(0f, 0.5f);
        brt.anchorMax = new Vector2(0f, 0.5f);
        brt.pivot = new Vector2(0.5f, 0.5f);
        brt.anchoredPosition = new Vector2(BarX, 0f);
        brt.sizeDelta = new Vector2(BarW, BarH);
        bar.sprite = null;                        // 无 sprite = 纯白方块上色
        bar.color = MainMenuAssets.ACCENT;        // 主界面调色板主蓝 #4C9FE8（不自造颜色）
        bar.raycastTarget = false;
        log.Add("竖条参数：锚 (0,0.5)，x=" + BarX + "，尺寸 " + BarW + "×" + BarH + "，颜色 #" + ColorUtility.ToHtmlStringRGBA(MainMenuAssets.ACCENT));

        // 4) Text：铺满卡片、从竖条右侧起、左对齐 26 号
        var txtT = wh.Find("Text");
        if (txtT == null) throw new System.Exception("WalkHint 下没有 Text（场景结构和预期不符）");
        var trt = (RectTransform)txtT;
        trt.anchorMin = new Vector2(0f, 0f);
        trt.anchorMax = new Vector2(1f, 1f);
        trt.offsetMin = new Vector2(TextLeft, 0f);    // 左：竖条右侧
        trt.offsetMax = new Vector2(-TextRight, 0f);  // 右：留 20
        var txt = txtT.GetComponent<Text>();
        if (txt == null) throw new System.Exception("WalkHint/Text 上没有 Text 组件");
        txt.alignment = TextAnchor.MiddleLeft;
        txt.fontSize = FontSize;
        txt.color = new Color(0.16f, 0.2f, 0.26f, 1f);   // 原字色写回防漂移；字体不动
        log.Add("Text：锚拉伸铺满，offsetMin (" + TextLeft + ",0) / offsetMax (" + (-TextRight) + ",0)，"
              + FontSize + " 号 MiddleLeft，颜色 #" + ColorUtility.ToHtmlStringRGBA(txt.color)
              + "，字体 " + (txt.font != null ? txt.font.name : "（空）") + "（不改动）");

        // 5) 收起 + 存盘
        wh.gameObject.SetActive(false);           // 场景默认隐藏（原本就是 false，写回保证）
        EditorSceneManager.MarkSceneDirty(sc);
        if (!EditorSceneManager.SaveScene(sc)) throw new System.Exception("场景保存失败");
        log.Add("节点保持 SetActive(false)（运行时由 StoryRunner 按需亮起）");
        log.Add("场景已保存 ✓");
        log.Add("运行时配合：StoryRunner.ShowWalkHint 已去掉 \"→  \" 前缀、改为 CanvasGroup 淡入-常驻-淡出（见 Scripts/Story/StoryRunner.cs）");
        log.Add("冻结约定：对话/对话框/名字/对话内容/交互提示/遮罩 等节点未做任何写回");

        // 6) 预览（失败不影响改造结果，报告里注明）
        try { RenderPreview(log); }
        catch (System.Exception e)
        {
            log.Add("★预览渲染失败（改造本身已完成并保存）：" + e.Message);
            log.Add(e.ToString());
            log.Add("未渲预览");
        }
    }

    // 预览：照 MainMenuBuilder.RenderPreviews 的现成做法（ScreenSpaceCamera + ConstantPixelSize，
    // 1 UI 单位 = 1 像素渲 1920×1080；相机是 CharPreview 式临时相机，拉到高空只拍 UI 不卷进场景几何）。
    // 渲完恢复所有临时状态（兄弟节点开关 / 文案 / 相机 / 画布设置），并复存一次场景清脏。
    static void RenderPreview(List<string> log)
    {
        var canvasGO = GameObject.Find("UI交互");
        var canvas = canvasGO.GetComponent<Canvas>();
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        var wh = canvasGO.transform.Find("WalkHint");
        var group = wh.GetComponent<CanvasGroup>();
        var txt = wh.Find("Text").GetComponent<Text>();

        // ---- 临时状态记录（渲完逐一恢复）----
        var oldText = txt.text;
        txt.text = "走到教室门口";                 // 代表性文案（场景里 Text 默认是空串）
        wh.gameObject.SetActive(true);
        group.alpha = 1f;
        var hidden = new List<Transform>();
        foreach (Transform sib in canvasGO.transform)   // 常驻激活的兄弟 UI（交互提示/ChoicePanel）临时收起
            if (sib != wh && sib.gameObject.activeSelf) { sib.gameObject.SetActive(false); hidden.Add(sib); }
        var hidNames = new List<string>();
        foreach (var t in hidden) hidNames.Add(t.name);
        if (hidNames.Count > 0) log.Add("预览临时收起兄弟节点：" + string.Join("、", hidNames.ToArray()) + "（渲完恢复原状）");

        var oldMode = canvas.renderMode; var oldCam = canvas.worldCamera; var oldPlane = canvas.planeDistance;
        var oldScaleMode = scaler.uiScaleMode; var oldFactor = scaler.scaleFactor;

        var camGO = new GameObject("WalkHint预览相机");
        camGO.hideFlags = HideFlags.HideAndDontSave;
        var cam = camGO.AddComponent<Camera>();
        try
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 10f;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            cam.transform.position = new Vector3(0f, 2000f, 0f);   // 高空，far 范围内没有场景几何
            cam.orthographic = true;
            cam.orthographicSize = 540f;                           // 1080 / 2 → 1 UI 单位 = 1 像素
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 100f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = MainMenuAssets.MASK_NORMAL;      // 深蓝底示意游戏画面，衬托玻璃卡

            var rtex = new RenderTexture(1920, 1080, 24);
            cam.targetTexture = rtex;
            Canvas.ForceUpdateCanvases();
            cam.Render();
            cam.Render();                                          // 第二遍防首帧丢内容
            RenderTexture.active = rtex;
            var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            Directory.CreateDirectory(PreviewDir);
            File.WriteAllBytes(PreviewPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rtex);
            log.Add("预览已渲染 ✓ → " + PreviewPath + "（1920×1080，深蓝底示意）");
        }
        finally
        {
            // ---- 全部恢复（与已存盘状态一致）----
            cam.targetTexture = null;
            canvas.renderMode = oldMode; canvas.worldCamera = oldCam; canvas.planeDistance = oldPlane;
            scaler.uiScaleMode = oldScaleMode; scaler.scaleFactor = oldFactor;
            foreach (var t in hidden) if (t != null) t.gameObject.SetActive(true);
            wh.gameObject.SetActive(false);
            group.alpha = 0f;
            txt.text = oldText;
            Object.DestroyImmediate(camGO);
            EditorSceneManager.SaveScene(canvasGO.scene);          // 临时开关把场景标脏了，复存清掉
        }
    }
}
