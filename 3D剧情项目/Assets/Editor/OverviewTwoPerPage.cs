// 一次性场景手术：把 MainMenu.unity 的内容概览页从“4 张选择卡”改成“每页 2 张大卡 + 翻页”。
// 只动 Page_内容概览/卡片 内的对象和 MainMenuUI 的概览引用，其他页面（用户手改布局）一律不碰；
// 贴图/字体等引用全部保持原样，只调整 RectTransform 与字号。改完存盘并写报告。
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class OverviewTwoPerPage
{
    const string ScenePath  = "Assets/Scenes/MainMenu.unity";
    const string ReportDir  = "Assets/assets/_报告";
    const string ReportPath = ReportDir + "/_概览页改造.txt";

    // 新布局（与 MainMenuBuilder.BuildOverviewPage/BuildOverviewCard 保持一致）
    // 此页"卡片"容器被手改放大 1.2808 倍。以下坐标 = 用户 2026-09-15 手调基线，勿随意改动
    static readonly Vector2   CardSize = new Vector2(600f, 410f);
    static readonly Vector2[] CardPos  = { new Vector2(-319f, -63f), new Vector2(305f, -63f) };
    static readonly float[]   ChipX    = { -200f, -173f };   // 视角标签 x：两张卡沿用用户手调的错位

    [MenuItem("Tools/干预项目/概览页改每页2卡")]
    public static void Run()
    {
        var log = new List<string>();
        var ok  = false;
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
            log.Insert(0, "概览页改每页2卡（" + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "）结果：" + (ok ? "成功 ✓" : "失败 ★"));
            File.WriteAllText(ReportPath, string.Join("\n", log.ToArray()) + "\n");
            Debug.Log("[OverviewTwoPerPage] 报告 → " + ReportPath);
        }
    }

    static void Surgery(List<string> log)
    {
        MainMenuBuilder.PreloadScripts(log);

        // 概览文案（MainMenuAssets.OV_ROWS）可能被更新，重建概览数据资产保持同步
        MainMenuAssets.BuildDatabase(log);

        var sc = EditorSceneManager.GetActiveScene();
        if (!sc.IsValid() || sc.path != ScenePath)
            sc = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        log.Add("场景：" + sc.path);

        var canvasGO = GameObject.Find("UI_Canvas");
        if (canvasGO == null) throw new System.Exception("场景里没有 UI_Canvas");
        var ui = canvasGO.GetComponent<MainMenuUI>();
        if (ui == null) throw new System.Exception("UI_Canvas 上没有 MainMenuUI");

        Transform page = ui.overviewPanel != null ? ui.overviewPanel.transform : null;
        if (page == null)
        {
            var found = GameObject.Find("Page_内容概览");
            if (found != null) page = found.transform;
        }
        if (page == null) throw new System.Exception("找不到 Page_内容概览");
        var card = page.Find("卡片");
        if (card == null) throw new System.Exception("Page_内容概览 下没有 卡片");

        // 1) 改造 选择_1/2 → 大卡；删除 选择_3/4
        var kept = new List<OverviewCardUI>();
        for (int i = 1; i <= 4; i++)
        {
            var t = card.Find("选择_" + i);
            if (i <= 2)
            {
                if (t == null) throw new System.Exception("缺 选择_" + i + "，场景结构和预期不符");
                ReshapeCard(t, CardPos[i - 1], ChipX[i - 1], log);
                kept.Add(t.GetComponent<OverviewCardUI>());
            }
            else if (t != null)
            {
                Object.DestroyImmediate(t.gameObject);
                log.Add("删除 选择_" + i);
            }
        }

        // 2) 底部翻页控件（先清旧的，保证工具可重跑）
        foreach (var n in new[] { "Btn_上一页", "页码", "Btn_下一页" })
        {
            var old = card.Find(n);
            if (old != null) Object.DestroyImmediate(old.gameObject);
        }
        MainMenuBuilder.WarmFont(); // Label/Btn 用的 _font 只在整场景搭建时赋值，补丁路径必须先预热
        Text lt, lc;
        var prev = MainMenuBuilder.Btn(card, "Btn_上一页", "上一页", new Vector2(-289f, -291f), new Vector2(150f, 44f), false, out lt, 22);
        var lbl  = MainMenuBuilder.Label(card, "页码", "1 / 2", new Vector2(-29f, -293f), new Vector2(140f, 40f), 20,
                                         new Color(0.616f, 0.678f, 0.694f), TextAnchor.MiddleCenter); // 同 builder 的 MUTED
        var next = MainMenuBuilder.Btn(card, "Btn_下一页", "下一页", new Vector2(231f, -291f), new Vector2(150f, 44f), false, out lc, 22);
        log.Add("新建翻页控件：Btn_上一页 / 页码 / Btn_下一页");
        if (lbl == null || lbl.font == null) throw new System.Exception("页码 Text 缺字体（WarmFont 未生效）");
        log.Add("字体检查：页码/按钮文字字体 = " + lbl.font.name);

        // 3) 回写 MainMenuUI 引用（直接字段赋值，存场景时序列化）
        ui.ovCards     = kept.ToArray();
        ui.ovPrevPage  = prev;
        ui.ovNextPage  = next;
        ui.ovPageLabel = lbl;
        log.Add("回写 MainMenuUI：ovCards[2] / ovPrevPage / ovNextPage / ovPageLabel");

        // 3.5) 概览数据重建后，把新文案刷进场景里的卡片文字（RefreshOverview 幂等，运行时会再刷）
        ui.RefreshOverview();
        EditorSceneManager.MarkSceneDirty(sc);
        log.Add("已刷新概览卡文字（编辑器场景可见）");

        // 4) 修复：弹层根节点被禁用会让 Play 模式下 Awake 不跑、按钮监听挂不上
        //    （本机发现 Popup_确认 曾被禁用：运行时点退出/取消会完全没反应）
        foreach (var n in new[] { "Page_设置", "Page_存读档", "Page_章节选择", "Page_内容概览", "Page_大图", "Popup_确认" })
        {
            var t = canvasGO.transform.Find(n);
            if (t != null && !t.gameObject.activeSelf)
            {
                t.gameObject.SetActive(true);
                log.Add("修复：" + n + " 根节点曾被禁用 → 已启用（运行时按钮监听依赖 Awake）");
            }
        }

        // 5) 自检引用并保存
        int broken = 0;
        for (int i = 0; i < ui.ovCards.Length; i++)
            if (ui.ovCards[i] == null || ui.ovCards[i].button == null) broken++;
        if (broken > 0) throw new System.Exception("有 " + broken + " 张卡片引用不完整");
        EditorSceneManager.MarkSceneDirty(sc);
        if (!EditorSceneManager.SaveScene(sc)) throw new System.Exception("场景保存失败");
        log.Add("场景已保存 ✓");
        log.Add("后续：渲染主界面预览 + 主界面运行自检");
    }

    static void ReshapeCard(Transform t, Vector2 pos, float chipX, List<string> log)
    {
        var rt = (RectTransform)t;
        rt.anchoredPosition = pos;
        rt.sizeDelta        = CardSize;

        SetRect(t, "底",     Vector2.zero,          CardSize);
        SetRect(t, "选中框", Vector2.zero,          CardSize);
        SetRect(t, "点击区", Vector2.zero,          CardSize);
        SetRect(t, "配图",   new Vector2(0f, 29f),   new Vector2(520f, 270f));
        SetRect(t, "视角底", new Vector2(chipX, 157f), new Vector2(90f, 34f));
        SetRect(t, "视角",   new Vector2(chipX, 154f), new Vector2(90f, 34f));   // 文字比底低 3px = 用户手调
        var title = SetRect(t, "标题", new Vector2(0f, -100f), new Vector2(540f, 34f));
        var body  = SetRect(t, "概述", new Vector2(0f, -156f), new Vector2(540f, 60f));
        if (title != null) { title.fontSize = 24; title.alignment = TextAnchor.MiddleCenter; }
        if (body  != null) body.fontSize  = 18;

        var missing = new List<string>();
        foreach (var n in new[] { "底", "选中框", "点击区", "配图", "视角底", "视角", "标题", "概述" })
            if (t.Find(n) == null) missing.Add(n);
        log.Add("改造 " + t.name + " → 600×410 @ " + pos + (missing.Count > 0 ? "（缺子物体：" + string.Join("/", missing.ToArray()) + "）" : ""));
    }

    static Text SetRect(Transform parent, string name, Vector2 pos, Vector2 size)
    {
        var child = parent.Find(name);
        if (child == null) return null;
        var rt = (RectTransform)child;
        rt.anchoredPosition = pos;
        rt.sizeDelta        = size;
        return child.GetComponent<Text>();
    }
}

// 工程根存在 Assets/_overview2_trigger.txt 时，编辑器下次刷新/重编译后自动跑一次改造，
// 接着渲染主界面预览并跑运行自检（一次性任务，一次聚焦全部完成）。
[InitializeOnLoad]
public static class OverviewTwoPerPageTrigger
{
    const string TriggerFile = "Assets/_overview2_trigger.txt";
    const string ErrFile     = "../额外文件/错误_概览页改造.txt";

    static OverviewTwoPerPageTrigger()
    {
        if (!File.Exists(TriggerFile)) return;
        File.Delete(TriggerFile);
        EditorApplication.delayCall += delegate
        {
            try
            {
                OverviewTwoPerPage.Run();
                MainMenuBuilder.RenderPreviews();
                MainMenuBuilder.SmokeTest();
                if (File.Exists(ErrFile)) File.Delete(ErrFile);
                Debug.Log("[OverviewTwoPerPage] 自动改造+预览+自检 已触发完成");
            }
            catch (System.Exception e)
            {
                Directory.CreateDirectory("../额外文件");
                File.WriteAllText(ErrFile, e.ToString());
                Debug.LogError("[OverviewTwoPerPage] 自动改造失败：" + e);
            }
        };
    }
}

