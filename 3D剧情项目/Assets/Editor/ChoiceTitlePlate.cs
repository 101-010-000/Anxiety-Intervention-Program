// 选择题面板「题干标题」清晰化 v3（用户 2026-09-30 定稿方案 A）：
//   题干没有底板、直接压在半透明遮罩+场景画面上 → 场景亮部一干扰就糊（选项行有云朵气泡底图所以清晰）。
//   而且题干不是短标题，是 15~91 字的说明段（20 道题里最长 91 字 ≈ 4 行）→ 底板必须高度自适应。
//
// ★ v3 结构（前两版的教训：
//   v1 底板做成 Title 的【子节点】→ uGUI 父 Graphic 先画、子 Graphic 后画，底板把文字盖住了；
//   v2 想把 Image 加到 Title 节点本体 → 实测该节点上 AddComponent<Image>() 恒返回 null（怪癖，不纠缠）。
//   v3 全部用【构造器建新节点】（v1 的子节点就是这么建的，可靠），Title/接线/字号/颜色零改动：
//     Panel
//     └─ 题干带（新容器：RectTransform + VerticalLayoutGroup + ContentSizeFitter(垂自适应)，
//        接管 Title 原来的锚点/位置/宽度；内边距 左右30/上下16 = 底板比字大出一圈）
//        ├─ 题干底板（新：Image(面板_标题条, Sliced) + LayoutElement(ignoreLayout)，stretch+外扩，垫底）
//        └─ Title（原节点原样挪进来：Text + 原 fitter → 高度=文字行数；ChoicePanel.titleLabel 不变）
//   → 文字排几行「题干带」就长到几行，底板 stretch 跟随；底板 sibling 在前 → 文字永远在上面。
//   样式（仅首次接线写入）：字号 38→40（91 字题干 4 行内放下）、颜色改墨蓝 #1B2C42（中蓝底板上
//   深字：暗场景 ~4:1、亮场景 ~7:1 对比，白字在亮场景会随底板变亮失效）、对齐 MiddleCenter。
//
// 幂等：Title 的父节点是「题干带」= 已接线 → 只核对底板贴图/组件，用户手调一律不覆盖；
//       v1 遗留（Title 下有「题干底板」子节点，会盖字）→ 拆除。
//
// 用法：菜单 Tools/干预项目/选择题：题干标题加底板（清晰化）  或丢 Assets/_choicetitleplate_trigger.txt
//       渲染预览：Tools/干预项目/选择题：题干标题加底板——渲染预览（真实场景背景 + 亮背景各一张）
// 报告：Assets/assets/_报告/_选择题题干清晰化.txt
// 预览：Assets/assets/_报告/预览/场景/选择题题干_修后.png、选择题题干_修后_亮背景.png
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class ChoiceTitlePlate
{
    const string MENU       = "Tools/干预项目/";
    const string GAME_SCENE = "Assets/Scenes/Game.unity";
    const string REPORT     = "Assets/assets/_报告/_选择题题干清晰化.txt";
    const string SPRITE     = "Assets/assets/05_UI/界面_Panel/面板_标题条.png";
    const string PREVIEW_DIR = "Assets/assets/_报告/预览/场景";

    const string BAND_NAME  = "题干带";     // v3 容器
    const string PLATE_NAME = "题干底板";   // v3 底板（题干带的子节点）；v1 遗留同名子节点见到即拆
    const string PLATE_V1   = "题干底板";
    const int    FONT_SIZE  = 40;
    const int    PAD_X = 30, PAD_Y = 16;

    /// 墨蓝（压中蓝底板：暗场景 ~4:1、亮场景 ~7:1 对比）
    public static readonly Color INK = new Color32(27, 44, 66, 255);   // #1B2C42

    static readonly StringBuilder _log = new StringBuilder();

    [MenuItem(MENU + "选择题：题干标题加底板（清晰化）", false, 54)]
    public static void Run()
    {
        _log.Clear();
        _log.AppendLine("选择题题干清晰化（v3 结构）  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        _log.AppendLine("底板贴图：" + Path.GetFileName(SPRITE) + "（192×48 中蓝胶囊条 #77B9F1，导入自带左右 32px 九宫格，素材自带半透明软边）");
        _log.AppendLine();
        try { Core(); }
        catch (System.Exception e)
        {
            _log.AppendLine();
            _log.AppendLine("★ 异常中止：" + e);
            Flush();
            DumpError(e);
        }
    }

    [MenuItem(MENU + "选择题：题干标题加底板——渲染预览", false, 55)]
    public static void RunPreview()
    {
        _log.AppendLine();
        _log.AppendLine("—— 渲染预览 ——");
        try { Preview(); }
        catch (System.Exception e)
        {
            _log.AppendLine("★ 预览异常中止：" + e);
            Flush();
            DumpError(e);
        }
    }

    static void Core()
    {
        if (EditorApplication.isPlaying)
        {
            _log.AppendLine("★ 正在 Play 模式，先停下再跑（运行时改的场景不会存盘）");
            Flush();
            return;
        }

        var scene = OpenGameScene();
        var cp = Object.FindObjectOfType<ChoicePanel>(true);
        if (cp == null) { _log.AppendLine("★ 场景里没有 ChoicePanel"); Flush(); return; }
        if (cp.titleLabel == null) { _log.AppendLine("★ ChoicePanel.titleLabel 没接线（题干标题）"); Flush(); return; }

        var titleRT = (RectTransform)cp.titleLabel.transform;
        _log.AppendLine("题干节点：" + PathOf(titleRT));

        bool wired = titleRT.parent != null && titleRT.parent.name == BAND_NAME;
        if (wired)
        {
            _log.AppendLine("  已接线（父节点=题干带）→ 只核对、不覆盖手调");
        }
        else
        {
            // ---- 迁移/新建 v3 结构
            var v1 = titleRT.Find(PLATE_V1);
            if (v1 != null)
            {
                Object.DestroyImmediate(v1.gameObject);
                _log.AppendLine("  拆除 v1 遗留「" + PLATE_V1 + "」子节点（v1 结构会盖住文字）");
            }

            var text = cp.titleLabel;
            var oldParent = titleRT.parent;
            int oldSibling = titleRT.GetSiblingIndex();
            var oldAnchMin = titleRT.anchorMin;
            var oldAnchMax = titleRT.anchorMax;
            var oldPos = titleRT.anchoredPosition;
            var oldPivot = titleRT.pivot;
            var oldWidth = titleRT.sizeDelta.x;
            if (text == null || oldParent == null) { _log.AppendLine("★ 题干引用/父节点空了，中止"); Flush(); return; }

            // ① 首次接线才写样式值（重跑绝不覆盖手调）
            text.fontSize = Mathf.Max(text.fontSize, FONT_SIZE);
            text.color = INK;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            EditorUtility.SetDirty(text);
            _log.AppendLine(string.Format("  样式：字号 {0} / 颜色 {1} / 对齐 {2}", text.fontSize, ColorText(text.color), text.alignment));

            // ② 新容器「题干带」接管 Title 原来的锚点/位置/宽度（全构造器建节点，绕开 AddComponent 怪癖）
            var bandGO = new GameObject(BAND_NAME, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var band = (RectTransform)bandGO.transform;
            band.SetParent(oldParent, false);
            band.SetSiblingIndex(oldSibling);          // 占 Title 原来的位次
            band.anchorMin = oldAnchMin; band.anchorMax = oldAnchMax;
            band.pivot = oldPivot;
            band.anchoredPosition = oldPos;
            band.sizeDelta = new Vector2(oldWidth, 0f); // 高度交给 fitter

            var vlg = bandGO.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(PAD_X, PAD_X, PAD_Y, PAD_Y);
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var bfit = bandGO.GetComponent<ContentSizeFitter>();
            bfit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            bfit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // ③ 底板：题干带的第一个子节点（垫底），stretch + 外扩一圈
            var plateGO = new GameObject(PLATE_NAME, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            var prt = (RectTransform)plateGO.transform;
            prt.SetParent(band, false);
            prt.SetSiblingIndex(0);
            prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.offsetMin = new Vector2(-PAD_X, -PAD_Y);
            prt.offsetMax = new Vector2(PAD_X, PAD_Y);
            var pimg = plateGO.GetComponent<Image>();
            var sp = AssetDatabase.LoadAssetAtPath<Sprite>(SPRITE);
            if (sp == null) { _log.AppendLine("★ 找不到底板贴图：" + SPRITE); Flush(); return; }
            pimg.sprite = sp;
            pimg.type = Image.Type.Sliced;
            pimg.raycastTarget = false;
            pimg.color = Color.white;
            plateGO.GetComponent<LayoutElement>().ignoreLayout = true;   // 底板不吃布局，纯跟随容器
            _log.AppendLine("  底板「" + PLATE_NAME + "」挂 " + Path.GetFileName(SPRITE) + "（Sliced，border " + sp.border + "，不接射线，垫底）");

            // ④ Title 原样挪进容器（Text/引用/原有 fitter 全都不动）
            titleRT.SetParent(band, false);
            titleRT.anchorMin = new Vector2(0.5f, 0.5f);
            titleRT.anchorMax = new Vector2(0.5f, 0.5f);
            titleRT.pivot = new Vector2(0.5f, 0.5f);
            _log.AppendLine("  Title 挪进「" + BAND_NAME + "」，ChoicePanel.titleLabel 接线不变");
        }

        // ---- 核对（已接线重跑也可能缺东西）
        var band2 = titleRT.parent;
        if (band2 == null || band2.name != BAND_NAME)
            _log.AppendLine("  ★ Title 不在「题干带」下——结构不对，重跑一次");
        else
        {
            var plateImg = band2.Find(PLATE_NAME);
            if (plateImg == null) _log.AppendLine("  ★ 题干带下没有「" + PLATE_NAME + "」");
            else
            {
                var pi = plateImg.GetComponent<Image>();
                var sp2 = AssetDatabase.LoadAssetAtPath<Sprite>(SPRITE);
                if (pi == null || pi.sprite != sp2)
                    _log.AppendLine("  ★ 底板贴图不对（缺 Image 或 sprite 不符）");
            }
            if (band2.GetComponent<VerticalLayoutGroup>() == null || band2.GetComponent<ContentSizeFitter>() == null)
                _log.AppendLine("  ★ 题干带缺 VerticalLayoutGroup/ContentSizeFitter");
        }

        // ---- 布局立刻算一遍，让存盘的 sizeDelta 就是贴合文字的值
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(titleRT);
        if (titleRT.parent != null) LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)titleRT.parent);

        // ---- 材质覆盖核查（按序列化值，不按运行时属性）
        var so = new SerializedObject(cp.titleLabel);
        var matProp = so.FindProperty("m_Material");
        _log.AppendLine("  材质覆盖核查：题干 Text 序列化 m_Material = " +
                        (matProp != null && matProp.objectReferenceValue != null
                            ? matProp.objectReferenceValue.name
                            : "空 → 无覆盖（上次报告的提示是 uGUI 属性回落默认材质的假象）"));

        var t = cp.titleLabel;
        var bandRT = (RectTransform)t.transform.parent;
        _log.AppendLine(string.Format("  改后：字号 {0} / 颜色 {1} / 对齐 {2} / 题干带 {3:0}×{4:0} @ {5}（编辑器里文字空 → 高度=内边距 32，运行时按行数撑高）",
            t.fontSize, ColorText(t.color), t.alignment, bandRT.rect.width, bandRT.rect.height, bandRT.anchoredPosition));
        _log.AppendLine("  布局：选项区顶边 +200；最长题干（91 字 ≈4 行）底边 ≈ +235 → 余 ~35px 不相压");
        _log.AppendLine("  未触碰：选项行（云朵贴图/字号/颜色）、确认按钮、对话框、Dim —— 一个数值都没动");

        EditorSceneManager.MarkSceneDirty(scene);
        bool ok = EditorSceneManager.SaveScene(scene);
        _log.AppendLine();
        _log.AppendLine("保存场景 " + GAME_SCENE + "：" + (ok ? "成功 ✓" : "★失败"));
        Flush();
        Debug.Log("[ChoiceTitlePlate] 完成，报告：" + REPORT);
    }

    // ================================================================== 预览
    // 照 MainMenuBuilder 预览套路：临时切 ScreenSpaceCamera + 正交相机 → RT 1:1 渲染 → 还原。
    // 两张：① 真实场景背景（相机当前视角）② 纯亮背景（可读性下限测试）。
    // 样例题 = 全剧本最长的 91 字题干（第2章），能顶出底板最大高度。
    const string SAMPLE_TITLE =
        "请帮徐夏完成未来自我视角创作，如果是未来的自己，会对现在这个面对不熟悉的人的邀约时、感到焦虑局促的自己，说些什么？亲爱的自己，现在你正在为拒绝同学邀约而焦虑，但1年后的我想告诉你——";

    static void Preview()
    {
        if (EditorApplication.isPlaying) { _log.AppendLine("★ 正在 Play 模式，先停下再渲预览"); Flush(); return; }
        var scene = OpenGameScene();

        var cp = Object.FindObjectOfType<ChoicePanel>(true);
        if (cp == null || cp.panel == null || cp.titleLabel == null)
        { _log.AppendLine("★ ChoicePanel / panel / titleLabel 缺接线"); Flush(); return; }

        var title = cp.titleLabel;
        var canvas = title.GetComponentInParent<Canvas>(true);
        if (canvas == null) { _log.AppendLine("★ 题干不在任何 Canvas 下"); Flush(); return; }
        var scaler = canvas.GetComponent<CanvasScaler>();

        var cam = Camera.main;
        if (cam == null) cam = Object.FindObjectOfType<Camera>();
        if (cam == null) { _log.AppendLine("★ 场景里没有相机"); Flush(); return; }

        // ---- 记录将被临时改动的状态
        var oldRenderMode = canvas.renderMode; var oldWorldCam = canvas.worldCamera; var oldPlane = canvas.planeDistance;
        var oldScaleMode = scaler != null ? scaler.uiScaleMode : (CanvasScaler.ScaleMode?)null;
        var oldFactor = scaler != null ? scaler.scaleFactor : 0f;
        var oldOrtho = cam.orthographic; var oldSize = cam.orthographicSize;
        var oldClear = cam.clearFlags; var oldBg = cam.backgroundColor;
        var oldWasOpen = cp.panel.IsOpen;

        // ---- 记录将被填充的内容
        var oldTitle = title.text;
        var oldConfirm = cp.confirmLabel != null ? cp.confirmLabel.text : null;
        var oldRows = new (bool active, string text)[cp.rows.Count];
        for (int i = 0; i < cp.rows.Count; i++)
        {
            var r = cp.rows[i];
            oldRows[i] = (r != null && r.root != null && r.root.activeSelf,
                          r != null && r.headText != null ? r.headText.text : null);
        }

        var rtex = new RenderTexture(1920, 1080, 24);
        try
        {
            // ---- 填样例（最长题干 + 2 行样例选项）
            title.text = SAMPLE_TITLE;
            if (cp.confirmLabel != null) cp.confirmLabel.text = "已记录 0/2";
            for (int i = 0; i < cp.rows.Count; i++)
            {
                var r = cp.rows[i];
                if (r == null || r.root == null) continue;
                r.root.SetActive(i < 2);
                if (r.headText != null)
                    r.headText.text = i == 0 ? "告诉他我要马上回去" : "告诉他之后撤离不再进宿舍";
            }
            cp.panel.Show(true);
            Canvas.ForceUpdateCanvases();

            // ---- 临时切相机渲染
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 10f;
            if (scaler != null) { scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; scaler.scaleFactor = 1f; }
            cam.orthographic = true;
            cam.orthographicSize = 540f;      // 1080/2 → 1 UI 单位 = 1 像素
            cam.targetTexture = rtex;

            Directory.CreateDirectory(PREVIEW_DIR);

            // ① 真实场景背景
            Shoot(rtex, cam, PREVIEW_DIR + "/选择题题干_修后.png");
            // ② 亮背景（可读性下限）
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.72f, 0.76f, 0.82f);
            Shoot(rtex, cam, PREVIEW_DIR + "/选择题题干_修后_亮背景.png");

            DumpDarkGraphics(cp);
        }
        finally
        {
            // ---- 还原一切
            cam.targetTexture = null;
            cam.clearFlags = oldClear; cam.backgroundColor = oldBg;
            cam.orthographic = oldOrtho; cam.orthographicSize = oldSize;
            canvas.renderMode = oldRenderMode; canvas.worldCamera = oldWorldCam; canvas.planeDistance = oldPlane;
            if (scaler != null && oldScaleMode.HasValue) { scaler.uiScaleMode = oldScaleMode.Value; scaler.scaleFactor = oldFactor; }

            title.text = oldTitle;
            if (cp.confirmLabel != null && oldConfirm != null) cp.confirmLabel.text = oldConfirm;
            for (int i = 0; i < cp.rows.Count && i < oldRows.Length; i++)
            {
                var r = cp.rows[i];
                if (r == null || r.root == null) continue;
                r.root.SetActive(oldRows[i].active);
                if (r.headText != null && oldRows[i].text != null) r.headText.text = oldRows[i].text;
            }
            if (oldWasOpen) cp.panel.Show(true); else cp.panel.Hide(true);
            Canvas.ForceUpdateCanvases();

            // 场景先被 Core 存过，预览的临时改动已全部还原 → 存一次 sheds 脏标记（内容与磁盘一致）
            EditorSceneManager.SaveScene(scene);
            Object.DestroyImmediate(rtex);
        }
        Flush();
        Debug.Log("[ChoiceTitlePlate] 预览已写 " + PREVIEW_DIR);
    }

    static void Shoot(RenderTexture rt, Camera cam, string path)
    {
        Canvas.ForceUpdateCanvases();
        cam.Render();
        cam.Render();   // 两遍：第一遍让布局/材质懒初始化稳定

        RenderTexture.active = rt;
        var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
        tex.Apply();
        RenderTexture.active = null;

        var png = tex.EncodeToPNG();
        File.WriteAllBytes(path, png);
        Object.DestroyImmediate(tex);
        _log.AppendLine(string.Format("  预览 {0}   {1:0} KB", path, png.Length / 1024f));
    }

    /// 预览排障：列出画布里所有【深色且未被剔除】的 Graphic——题干是墨蓝，谁渲染歪了一目了然
    static void DumpDarkGraphics(ChoicePanel cp)
    {
        var canvas = cp.titleLabel.GetComponentInParent<Canvas>(true);
        _log.AppendLine("  深色 Graphic 排查（色量和 < 1.35 且未剔除）：");
        foreach (var g in canvas.GetComponentsInChildren<Graphic>(true))
        {
            if (!g.gameObject.activeInHierarchy) continue;
            if (g.canvasRenderer == null || g.canvasRenderer.cull) continue;
            var c = g.color;
            if (c.r + c.g + c.b >= 1.35f) continue;
            var rt = (RectTransform)g.transform;
            _log.AppendLine(string.Format("    {0}  中心 {1}  尺寸 {2:0}×{3:0}  颜色 {4}",
                PathOf(g.transform), rt.anchoredPosition, rt.rect.width, rt.rect.height,
                ColorText(c)));
        }
        var t = cp.titleLabel;
        _log.AppendLine(string.Format("    [题干] 文字 {0} 字 / 对齐 {1} / cull {2} / rect {3:0}×{4:0} @ {5}",
            t.text.Length, t.alignment,
            t.canvasRenderer != null && t.canvasRenderer.cull,
            ((RectTransform)t.transform).rect.width, ((RectTransform)t.transform).rect.height,
            ((RectTransform)t.transform).anchoredPosition));
    }

    // ================================================================== 公共
    /// 不是 Game 场景就先存再开；是且脏就先存（沿用 ChoiceTitleColor 的约定）
    static Scene OpenGameScene()
    {
        var cur = SceneManager.GetActiveScene();
        if (cur.IsValid() && cur.path != GAME_SCENE)
        {
            EditorSceneManager.SaveOpenScenes();
            return EditorSceneManager.OpenScene(GAME_SCENE, OpenSceneMode.Single);
        }
        if (cur.IsValid() && cur.isDirty)
        {
            EditorSceneManager.SaveOpenScenes();
            _log.AppendLine("（先把当前场景的未保存改动存了）");
        }
        return cur;
    }

    static string ColorText(Color c)
    {
        return string.Format("#{0}", ColorUtility.ToHtmlStringRGB(c));
    }

    static string PathOf(Transform t)
    {
        var s = t.name;
        for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
        return s;
    }

    static void Flush()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(REPORT).Replace('/', Path.DirectorySeparatorChar));
        File.WriteAllText(REPORT, _log.ToString());
    }

    static void DumpError(System.Exception e)
    {
        try
        {
            Directory.CreateDirectory("../额外文件");
            File.WriteAllText("../额外文件/错误_选择题题干.txt", e.ToString());
        }
        catch { }
        Debug.LogError("[ChoiceTitlePlate] " + e);
    }
}

// 丢 Assets/_choicetitleplate_trigger.txt → 下次刷新/重编译后自动跑：接线 + 预览一次完成
[InitializeOnLoad]
public static class ChoiceTitlePlateTrigger
{
    const string Trigger = "Assets/_choicetitleplate_trigger.txt";

    static ChoiceTitlePlateTrigger()
    {
        if (!File.Exists(Trigger)) return;
        EditorApplication.delayCall += () =>
        {
            try
            {
                if (File.Exists(Trigger)) File.Delete(Trigger);
                if (File.Exists(Trigger + ".meta")) File.Delete(Trigger + ".meta");
                ChoiceTitlePlate.Run();
                ChoiceTitlePlate.RunPreview();
            }
            catch (System.Exception e)
            {
                Directory.CreateDirectory("../额外文件");
                File.WriteAllText("../额外文件/错误_选择题题干.txt", e.ToString());
                Debug.LogError("[ChoiceTitlePlate] " + e);
            }
        };
    }
}
