// 选择题面板「题干标题」配色（用户 2026-10-01）：
//   面板背景是透明的、标题直接压在深色 Dim 上，原来的深蓝灰（0.16,0.20,0.26）几乎看不见
//   → 改成【蓝白色】（默认 #CCE3FF），在深色遮罩上清晰、又跟项目的淡蓝配色一致。
//   只改 ChoicePanel.titleLabel（题干）；选项行/确认按钮的配色不动。
//
// 用法：菜单 Tools/干预项目/选择题：题干标题改蓝白色  或丢 Assets/_choicetitle_trigger.txt
// 报告：Assets/assets/_报告/_选择题标题配色.txt
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class ChoiceTitleColor
{
    const string MENU = "Tools/干预项目/";
    const string GAME_SCENE = "Assets/Scenes/Game.unity";
    const string REPORT = "Assets/assets/_报告/_选择题标题配色.txt";

    /// <summary>蓝白色（比纯白偏蓝，压在深色 Dim 上够亮、又不刺眼）</summary>
    public static readonly Color BLUE_WHITE = new Color(0.80f, 0.89f, 1f, 1f);   // ≈ #CCE3FF

    static readonly StringBuilder _log = new StringBuilder();

    [MenuItem(MENU + "选择题：题干标题改蓝白色", false, 53)]
    public static void Run()
    {
        _log.Clear();
        _log.AppendLine("选择题题干标题配色  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        _log.AppendLine("目标颜色：蓝白 #CCE3FF  rgb(0.80, 0.89, 1.00)");
        _log.AppendLine();
        try { Core(); }
        catch (System.Exception e)
        {
            _log.AppendLine();
            _log.AppendLine("★ 异常中止：" + e);
            Flush();
            try
            {
                Directory.CreateDirectory("../额外文件");
                File.WriteAllText("../额外文件/错误_选择题标题配色.txt", e.ToString());
            }
            catch { }
            Debug.LogError("[ChoiceTitleColor] " + e);
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

        var cur = SceneManager.GetActiveScene();
        if (cur.IsValid() && cur.path != GAME_SCENE)
        {
            EditorSceneManager.SaveOpenScenes();
            EditorSceneManager.OpenScene(GAME_SCENE, OpenSceneMode.Single);
        }
        else if (cur.IsValid() && cur.isDirty)
        {
            EditorSceneManager.SaveOpenScenes();
            _log.AppendLine("（先把当前场景的未保存改动存了）");
        }
        var scene = SceneManager.GetActiveScene();

        var cp = Object.FindObjectOfType<ChoicePanel>(true);
        if (cp == null) { _log.AppendLine("★ 场景里没有 ChoicePanel"); Flush(); return; }
        if (cp.titleLabel == null) { _log.AppendLine("★ ChoicePanel.titleLabel 没接线（题干标题）"); Flush(); return; }

        _log.AppendLine("题干标题节点：" + PathOf(cp.titleLabel.transform));
        _log.AppendLine("  字号 " + cp.titleLabel.fontSize + " / 粗体 " + cp.titleLabel.fontStyle +
                        " / 对齐 " + cp.titleLabel.alignment + " / 字体 " +
                        (cp.titleLabel.font != null ? cp.titleLabel.font.name : "★空"));
        _log.AppendLine("  改前颜色 " + ColorText(cp.titleLabel.color));
        if (cp.titleLabel.color == BLUE_WHITE)
        {
            _log.AppendLine("  已是目标蓝白色，不动 ✓");
        }
        else
        {
            cp.titleLabel.color = BLUE_WHITE;
            EditorUtility.SetDirty(cp.titleLabel);
            _log.AppendLine("  改后颜色 " + ColorText(cp.titleLabel.color) + " ✓");
        }
        if (cp.titleLabel.material != null && cp.titleLabel.material.HasProperty("_Color"))
        {
            // 一般 uGUI Text 用默认材质，这里只是提示；不动它
            _log.AppendLine("  （注意：Text 上有材质覆盖 " + cp.titleLabel.material.name + "，颜色可能被材质压住）");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        bool ok = EditorSceneManager.SaveScene(scene);
        _log.AppendLine();
        _log.AppendLine("保存场景 " + GAME_SCENE + "：" + (ok ? "成功 ✓" : "★失败"));
        _log.AppendLine();
        _log.AppendLine("怎么验：Play 进任意一章的干预选择题 → 选项行上方那句题干应是蓝白色，在深色遮罩上清晰可读。");
        Flush();
        Debug.Log("[ChoiceTitleColor] 完成，报告：" + REPORT);
    }

    static string ColorText(Color c)
    {
        return string.Format("rgba({0:0.00}, {1:0.00}, {2:0.00}, {3:0.00})  #{4}", c.r, c.g, c.b, c.a,
                             ColorUtility.ToHtmlStringRGB(c));
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
}

// 丢 Assets/_choicetitle_trigger.txt → 下次刷新/重编译后自动跑一次
[InitializeOnLoad]
public static class ChoiceTitleColorTrigger
{
    const string Trigger = "Assets/_choicetitle_trigger.txt";

    static ChoiceTitleColorTrigger()
    {
        if (!File.Exists(Trigger)) return;
        EditorApplication.delayCall += () =>
        {
            try
            {
                if (File.Exists(Trigger)) File.Delete(Trigger);
                if (File.Exists(Trigger + ".meta")) File.Delete(Trigger + ".meta");
                ChoiceTitleColor.Run();
            }
            catch (System.Exception e)
            {
                Directory.CreateDirectory("../额外文件");
                File.WriteAllText("../额外文件/错误_选择题标题配色.txt", e.ToString());
                Debug.LogError("[ChoiceTitleColor] " + e);
            }
        };
    }
}
