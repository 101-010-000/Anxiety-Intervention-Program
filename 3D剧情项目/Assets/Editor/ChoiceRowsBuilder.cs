// 给剧情主场景的选择题面板预置「选项行」（最多 3 行），让用户能在 Scene 视图直接手调：
//   · 以前选项行是 ChoicePanel.Open() 运行时 new 出来、收档 Destroy——场景里看不见、没法手调
//   · 现在行节点预置在 ChoicePanel/Panel/List/View/Content 下（选项_0/1/2），Open() 只填文字/复位状态
//   · 行的贴图/字号/颜色/高度都可以在场景里改，运行时原样使用（★不换贴图：选中变灰=Button 的
//     ColorTint disabled 态，想调灰度就在 Inspector 改每行 Button 的 Disabled Color）
//
// ★ 行是【纯按钮】（头 + 标题，没有「解释」子节点）：选中后的解释改由底部对话框播独白
//   （ChoicePanel 调 DialogueUI.PlayLine，mon 样式），行里不再需要解释区。
//
// 用法（★ 必须先打开 Game.unity，工具不替你开场景、也不会存别的场景）：
//   · Tools/干预项目/生成选择题按钮行（预置3行）  —— 日常用这个：只补缺、只接线，已有的行一个数值都不改
//   · Tools/干预项目/生成选择题按钮行（强制重建） —— 删掉 选项_* 按默认样式重建（场景里的手改会丢，弹确认框）
//   · Tools/干预项目/选择题行去掉解释块           —— 旧版行里带过「解释」子节点，一键扫掉（幂等，不碰行的其它数值）
// 输出：Game.unity + 报告 Assets/assets/_报告/_选择题按钮行.txt
//
// 行结构（参数照搬旧版 ChoicePanel 运行时生成参数，去掉了解释块）：
//   选项_i (VerticalLayoutGroup + ContentSizeFitter)
//   └─ 头   LayoutElement(62) + Image(rowNormal, Sliced) + Button(ColorTint) + 标题(Text 28 Bold)
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class ChoiceRowsBuilder
{
    const string OUT_SCENE = "Assets/Scenes/Game.unity";
    const string REPORT    = "Assets/assets/_报告/_选择题按钮行.txt";
    const string FONT_PATH = "Assets/assets/05_UI/字体_Font/中文_Deng.ttf";
    const int    MAX_ROWS  = 3;

    static readonly string[] ROW_NAMES       = { "选项_0", "选项_1", "选项_2" };
    static readonly string[] HEAD_PLACEHOLDER = { "选项标题一", "选项标题二", "选项标题三" };

    // ------------------------------------------------------------------ 菜单
    [MenuItem("Tools/干预项目/生成选择题按钮行（预置3行）", false, 50)]
    public static void FillMissing() => Run(false);

    [MenuItem("Tools/干预项目/生成选择题按钮行（强制重建）", false, 51)]
    public static void ForceRebuild() => Run(true);

    [MenuItem("Tools/干预项目/选择题行去掉解释块", false, 52)]
    public static void RemoveBodyBlocks() => RunRemoveBodies();

    static void Run(bool rebuild)
    {
        var log = new List<string>();
        log.Add("选择题按钮行报告  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("模式：" + (rebuild ? "强制重建（删掉 选项_* 重建）" : "补缺+接线（已有的行一个数值都不改）"));
        log.Add("");

        // ★ 只处理当前打开的场景：不是 Game.unity 就停，不替用户开场景/存场景
        var scene = SceneManager.GetActiveScene();
        if (scene.path != OUT_SCENE)
        {
            Debug.LogError("[ChoiceRowsBuilder] 请先打开 Game.unity（当前场景：" +
                           (string.IsNullOrEmpty(scene.path) ? "未保存" : scene.path) + "）");
            EditorUtility.DisplayDialog("生成选择题按钮行", "请先打开 Game.unity 再跑本工具。", "知道了");
            log.Add("★ 当前打开的不是 Game.unity，什么都没动。");
            Write(log); return;
        }

        var cp = Object.FindObjectOfType<ChoicePanel>(true);
        if (cp == null)
        {
            Debug.LogError("[ChoiceRowsBuilder] 场景里没有 ChoicePanel 组件（先确认 ChoicePanel 面板还在）");
            EditorUtility.DisplayDialog("生成选择题按钮行", "场景里没有 ChoicePanel 组件。", "知道了");
            log.Add("★ 场景里没有 ChoicePanel 组件。");
            Write(log); return;
        }
        if (cp.rowsParent == null)
        {
            Debug.LogError("[ChoiceRowsBuilder] ChoicePanel.rowsParent 没接线（应指向 List/View/Content）");
            log.Add("★ ChoicePanel.rowsParent 没接线。");
            Write(log); return;
        }
        var content = cp.rowsParent;

        // 防呆：ChoicePanel 脚本要是还没重新编译，SerializedObject 里就不会有 rows 字段
        var so = new SerializedObject(cp);
        var rowsProp = so.FindProperty("rows");
        if (rowsProp == null)
        {
            Debug.LogError("[ChoiceRowsBuilder] ChoicePanel 上找不到 rows 字段——脚本改动还没编译？等 Unity 编译完再跑一次");
            log.Add("★ ChoicePanel 上没有 rows 字段（脚本未重新编译？）。");
            Write(log); return;
        }

        // 素材：优先用组件里已接好的引用（场景现状说了算）；组件上没有就按名字找，找不到留空并警告
        var rowNormal = cp.rowNormal;
        if (rowNormal == null)
        {
            rowNormal = MainMenuAssets.Sprite("卡片_普通");
            if (rowNormal != null) log.Add("组件 rowNormal 为空，按名字补用：" + rowNormal.name);
            else log.Add("★ rowNormal 为空也找不到 卡片_普通，头的 Image 先留空（可在场景里手动指定）");
        }
        var font = cp.font;
        if (font == null)
        {
            font = AssetDatabase.LoadAssetAtPath<Font>(FONT_PATH);
            if (font != null) log.Add("组件 font 为空，按路径补用：" + FONT_PATH);
            else log.Add("★ font 为空也找不到中文字体，标题/正文 Text 先没字体（可在场景里手动指定）");
        }
        var headColor = cp.headColor;

        // 强制重建：先弹确认框，再删掉 Content 下所有 选项_* （手改会丢，这是用户明确要求的才会走到这）
        if (rebuild)
        {
            bool yes = EditorUtility.DisplayDialog("强制重建选项行",
                "Content 下现有的 选项_* 会被删掉并按默认样式重建 3 行。\n你在场景里对这些行的手改（贴图/字号/颜色/布局）会全部丢失。继续吗？",
                "重建", "取消");
            if (!yes) { log.Add("已取消（什么都没动）。"); Write(log); return; }

            int removed = 0;
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                var child = content.GetChild(i);
                if (child.name.StartsWith("选项")) { Object.DestroyImmediate(child.gameObject); removed++; }
            }
            log.Add("强制重建：删掉旧行 " + removed + " 个。");
        }
        else
        {
            // 补缺模式：提醒用户 Content 下有工具不认识的东西（不碰，只报出来）
            var others = new List<string>();
            for (int i = 0; i < content.childCount; i++)
            {
                var n = content.GetChild(i).name;
                if (!System.Array.Exists(ROW_NAMES, x => x == n) && !n.StartsWith("选项")) others.Add(n);
            }
            if (others.Count > 0) log.Add("（Content 下还有这些非选项行节点，工具不碰：" + string.Join("、", others.ToArray()) + "）");
        }

        // 补缺：缺哪行建哪行；已有的行（包括用户手调过的）一个数值都不改
        for (int i = 0; i < MAX_ROWS; i++)
        {
            var exist = content.Find(ROW_NAMES[i]);
            if (exist != null) { log.Add(ROW_NAMES[i] + "：已存在，原样保留，只重新接线。"); continue; }
            BuildRow(content, i, rowNormal, font, headColor);
            log.Add(ROW_NAMES[i] + "：新建（纯按钮行，占位文字可在场景里直接改）。");
        }

        // 超出 3 行的选项_*：工具不管（最多 3 行），只提醒
        for (int i = 0; i < content.childCount; i++)
        {
            var n = content.GetChild(i).name;
            if (n.StartsWith("选项") && !System.Array.Exists(ROW_NAMES, x => x == n))
                log.Add("★ " + n + " 不在 选项_0/1/2 之列，不会接线（最多 3 行）；要留着自己看，要删请手动删。");
        }

        // 接线：把三行按 ChoicePanel.ChoiceRow 结构写回组件（只写 rows 字段，别的引用不碰）
        rowsProp.ClearArray();
        for (int i = 0; i < MAX_ROWS; i++)
        {
            var t = content.Find(ROW_NAMES[i]);
            rowsProp.InsertArrayElementAtIndex(i);
            WireRow(rowsProp.GetArrayElementAtIndex(i), t, log);
        }
        so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(scene);
        bool ok = EditorSceneManager.SaveScene(scene);
        log.Add("");
        log.Add("保存场景：" + OUT_SCENE + "  " + (ok ? "成功" : "★失败"));
        Write(log);
        Debug.Log("[ChoiceRowsBuilder] 完成，报告：" + REPORT);
    }

    // ------------------------------------------------------------------ 去解释块（解释改走底部对话框独白）
    // 扫 Content 下已有 选项_* 里名为「解释」的子节点删掉；rows 里指向它们的旧引用一并清掉。
    // 幂等、可重复跑；【不碰行的其它任何数值】（用户手调的贴图/字号/颜色/布局一律原样保留）。
    static void RunRemoveBodies()
    {
        var log = new List<string>();
        log.Add("选择题按钮行报告  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("模式：选择题行去掉解释块（解释改由底部对话框播独白，行里不再要解释区）");
        log.Add("");

        var scene = SceneManager.GetActiveScene();
        if (scene.path != OUT_SCENE)
        {
            Debug.LogError("[ChoiceRowsBuilder] 请先打开 Game.unity（当前场景：" +
                           (string.IsNullOrEmpty(scene.path) ? "未保存" : scene.path) + "）");
            EditorUtility.DisplayDialog("选择题行去掉解释块", "请先打开 Game.unity 再跑本工具。", "知道了");
            log.Add("★ 当前打开的不是 Game.unity，什么都没动。");
            Write(log); return;
        }

        var cp = Object.FindObjectOfType<ChoicePanel>(true);
        if (cp == null)
        {
            Debug.LogError("[ChoiceRowsBuilder] 场景里没有 ChoicePanel 组件");
            EditorUtility.DisplayDialog("选择题行去掉解释块", "场景里没有 ChoicePanel 组件。", "知道了");
            log.Add("★ 场景里没有 ChoicePanel 组件。");
            Write(log); return;
        }
        if (cp.rowsParent == null)
        {
            Debug.LogError("[ChoiceRowsBuilder] ChoicePanel.rowsParent 没接线（应指向 List/View/Content）");
            log.Add("★ ChoicePanel.rowsParent 没接线。");
            Write(log); return;
        }
        var content = cp.rowsParent;

        // 走 SerializedObject：只有脚本还没重编时才找得到旧字段，找得到就把指向「解释」的引用清成 null
        var so = new SerializedObject(cp);
        var rowsProp = so.FindProperty("rows");

        // 1) 删掉每行下的「解释」子节点（只删这一个名字，行的其它子节点/数值一律不动）
        int removed = 0;
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            var row = content.GetChild(i);
            if (!row.name.StartsWith("选项")) continue;
            var body = row.Find("解释");
            if (body == null) continue;
            log.Add(row.name + "：删掉「解释」子节点。");
            Object.DestroyImmediate(body.gameObject);
            removed++;
        }
        if (removed == 0) log.Add("（各行都没有「解释」子节点，本来就是纯按钮行。）");

        // 2) rows 数组元素里指向解释区的旧引用清掉（新版 ChoiceRow 已没有 body/bodyText/bodyLayout
        //    字段——重编译后这些键会被 Unity 静默丢弃；这里防的是"场景数据还带着旧键"的过渡态）
        int cleared = 0;
        if (rowsProp != null)
        {
            for (int i = 0; i < rowsProp.arraySize; i++)
            {
                var el = rowsProp.GetArrayElementAtIndex(i);
                cleared += NullLegacyRef(el, "body");
                cleared += NullLegacyRef(el, "bodyText");
                cleared += NullLegacyRef(el, "bodyLayout");
            }
        }
        so.ApplyModifiedProperties();
        log.Add(cleared > 0 ? "rows 里清掉旧解释引用 " + cleared + " 处。"
                            : "rows 里没有旧解释引用要清（ChoiceRow 已是纯按钮结构）。");
        if (rowsProp == null) log.Add("★ ChoicePanel 上没有 rows 字段（脚本未重新编译？旧引用这次没清，重编后再跑一次即可）。");

        EditorSceneManager.MarkSceneDirty(scene);
        bool ok = EditorSceneManager.SaveScene(scene);
        log.Add("");
        log.Add("保存场景：" + OUT_SCENE + "  " + (ok ? "成功" : "★失败"));
        Write(log);
        Debug.Log("[ChoiceRowsBuilder] 去解释块完成（删 " + removed + " 个），报告：" + REPORT);
    }

    /// 旧字段还序列化在场景里时把它清成 null；字段已不存在（FindPropertyRelative 返回 null）则跳过
    static int NullLegacyRef(SerializedProperty el, string field)
    {
        var p = el != null ? el.FindPropertyRelative(field) : null;
        if (p == null || p.objectReferenceValue == null) return 0;
        p.objectReferenceValue = null;
        return 1;
    }

    // ------------------------------------------------------------------ 建行（纯按钮：头+标题，解释改走底部对话框，不再生成解释块）
    static void BuildRow(Transform content, int i, Sprite rowNormal, Font font, Color headColor)
    {
        var rowGO = new GameObject(ROW_NAMES[i], typeof(RectTransform));
        rowGO.transform.SetParent(content, false);
        var vg = rowGO.AddComponent<VerticalLayoutGroup>();
        vg.spacing = 8f;
        vg.childForceExpandWidth = true; vg.childForceExpandHeight = false;
        vg.childAlignment = TextAnchor.UpperCenter;
        var fitter = rowGO.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // ---- 头（选项标题按钮）
        var headGO = new GameObject("头", typeof(RectTransform));
        headGO.transform.SetParent(rowGO.transform, false);
        var hle = headGO.AddComponent<LayoutElement>();
        hle.minHeight = 62f; hle.preferredHeight = 62f;
        var himg = headGO.AddComponent<Image>();
        himg.sprite = rowNormal; himg.type = Image.Type.Sliced;
        himg.raycastTarget = true;
        var hbtn = headGO.AddComponent<Button>();
        hbtn.targetGraphic = himg;
        hbtn.transition = Selectable.Transition.ColorTint;
        hbtn.colors = Tint();
        var head = MakeText(headGO.transform, "标题", HEAD_PLACEHOLDER[i], 28, headColor, TextAnchor.MiddleLeft, font);
        head.fontStyle = FontStyle.Bold;
        Stretch(head, 26f, 8f);
    }

    /// 把一行的各部件接进 rows 数组的第 idx 项（缺什么接 null 并警告，运行时都有 null 判断）
    static void WireRow(SerializedProperty el, Transform t, List<string> log)
    {
        if (t == null) { log.Add("★ 接线失败：找不到行节点。"); return; }
        var head = t.Find("头");
        var headText = head != null && head.Find("标题") != null ? head.Find("标题").GetComponent<Text>() : null;
        if (head == null) log.Add("  ★ " + t.name + " 下没有「头」，该行标题/点击不可用");
        else
        {
            if (head.GetComponent<Image>() == null) log.Add("  ★ " + t.name + "/头 上没有 Image");
            if (head.GetComponent<Button>() == null) log.Add("  ★ " + t.name + "/头 上没有 Button");
            if (headText == null) log.Add("  ★ " + t.name + "/头 下没有「标题」Text");
        }
        // 行是纯按钮：解释不再接线（旧场景数据里的 body/bodyText/bodyLayout 键 Unity 会静默忽略）

        el.FindPropertyRelative("root").objectReferenceValue = t.gameObject;
        el.FindPropertyRelative("headImage").objectReferenceValue = head != null ? head.GetComponent<Image>() : null;
        el.FindPropertyRelative("headButton").objectReferenceValue = head != null ? head.GetComponent<Button>() : null;
        el.FindPropertyRelative("headText").objectReferenceValue = headText;
    }

    static Text MakeText(Transform parent, string name, string content, int size, Color color, TextAnchor align, Font font)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.font = font; t.text = content; t.fontSize = size; t.color = color; t.alignment = align;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    static void Stretch(Text t, float inset, float insetY)
    {
        var rt = (RectTransform)t.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, insetY); rt.offsetMax = new Vector2(-inset, -insetY);
    }

    static ColorBlock Tint()   // 照抄旧版 ChoicePanel.Tint()
    {
        var c = ColorBlock.defaultColorBlock;
        c.normalColor = new Color(0.96f, 0.97f, 0.98f);
        c.highlightedColor = Color.white;
        c.pressedColor = new Color(0.88f, 0.92f, 0.96f);
        c.selectedColor = c.normalColor;
        c.fadeDuration = 0.08f;
        return c;
    }

    // ------------------------------------------------------------------ 报告
    static void Write(List<string> log)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(REPORT).Replace('/', Path.DirectorySeparatorChar));
        log.Add("");
        log.Add("———————————————————————————— 怎么改 ————————————————————————————");
        log.Add("· 三行节点在 Game.unity 的 UI_门口交互/ChoicePanel/Panel/List/View/Content 下（选项_0/1/2）");
        log.Add("· 外观（贴图/字号/颜色/间距/高度）直接在 Scene 视图改，运行时原样使用，不会被覆盖");
        log.Add("· 标题的【文字】每道题来自 剧情 json（Assets/数据/剧情/第1章.json），运行时写进去；场景里写的只是占位");
        log.Add("· ★ 行是纯按钮：选中后的解释不在行里展开，改由底部对话框播独白（mon 样式，DialogueUI 现有逻辑，对话框零改动）");
        log.Add("· 行数最多 3：剧本选项超过 3 会在 Console 报错，只显示前 3 行（第 1 章最多 3 个选项，够用）");
        log.Add("· 旧版行里带过「解释」子节点：跑 Tools/干预项目/选择题行去掉解释块 一键扫掉（幂等，不碰行的其它数值）");
        log.Add("· 改坏了想恢复默认：跑 Tools/干预项目/生成选择题按钮行（强制重建）（手改会丢，会弹确认框）");
        log.Add("· 运行时不会盖掉你的手调：选中变灰走 Button 的 disabled 色调（interactable=false），贴图/文字一律不换；");
        log.Add("  想调「灰」的深浅，在 Inspector 改每行 头/Button 的 Disabled Color 即可");
        File.WriteAllText(REPORT, string.Join("\n", log.ToArray()));
    }
}
