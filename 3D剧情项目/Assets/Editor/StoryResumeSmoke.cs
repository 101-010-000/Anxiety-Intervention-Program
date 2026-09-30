// 存档续播自检（编辑器侧入口）：模拟"读取第2章 step43 的存档"→ 开 Game 场景进 Play →
// 注入 StoryResumeDriver 观察续播是否生效（静默快进的世界副作用 + 状态机推进 + 无报错）。
// 报告：assets/_报告/_存档续播运行自检.txt
// 用法：菜单 Tools/干预项目/存档续播运行自检，或丢 Assets/_resumesmoke_trigger.txt
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class StoryResumeSmoke
{
    const string GAME_SCENE = "Assets/Scenes/Game.unity";
    const string REPORT      = "Assets/assets/_报告/_存档续播运行自检.txt";
    const int    CH = 2, STEP = 43;   // 第2章第43步（用户真实存档点：覆盖 enter/leave/interact×2/fade/微信段）

    // ⚠ 会 OpenScene 丢弃未保存的场景修改 → 跑之前场景必须已保存！
    [MenuItem("Tools/干预项目/存档续播运行自检")]
    public static void Run()
    {
        _oldOptEnabled = EditorSettings.enterPlayModeOptionsEnabled;
        _oldOpt = EditorSettings.enterPlayModeOptions;
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;

        EditorSceneManager.OpenScene(GAME_SCENE, OpenSceneMode.Single);

        // 清场：场景里可能残留历史自检的临时驱动（★ResumeDriverTemp 曾被连着场景存过盘，
        // 造成一次自检两个驱动并跑、报告全部双份——找不到不报错，幂等）
        int purged = 0;
        foreach (var t in Object.FindObjectsOfType<Transform>(true))
            if (t.name == "ResumeDriverTemp") { Object.DestroyImmediate(t.gameObject); purged++; }
        if (purged > 0) Debug.Log("[StoryResumeSmoke] 清掉残留临时驱动 " + purged + " 个");

        // 复刻读档路径：SelectChapter（清旧续播标记）→ SetResume（带步号）→ 进游戏场景
        GameProgress.SelectChapter(CH);
        GameProgress.SetResume(CH, STEP);

        var go = new GameObject("ResumeDriverTemp");
        go.AddComponent<StoryResumeDriver>();
        StoryResumeDriver.Active = true;
        StoryResumeDriver.Finished = false;
        StoryResumeDriver.CfgChapter = CH;
        StoryResumeDriver.CfgStep = STEP;
        StoryResumeDriver.Lines.Clear();
        StoryResumeDriver.Errors.Clear();

        _start = EditorApplication.timeSinceStartup;
        _active = true;
        if (!_hooked) { EditorApplication.update += Poll; _hooked = true; }
        Debug.Log("[StoryResumeSmoke] 存档续播自检开始（第" + CH + "章 step " + STEP + "）");
    }

    static bool _active, _hooked;
    static double _start;
    static bool _oldOptEnabled;
    static EnterPlayModeOptions _oldOpt;

    static void Poll()
    {
        if (!_active) return;
        if (EditorApplication.timeSinceStartup - _start > 360) { Finish("超时(360 秒)"); return; }   // Game 场景在编辑器里加载+首帧着色器预热很慢，别学 120 秒那种小气值
        if (!EditorApplication.isPlaying) { EditorApplication.isPlaying = true; return; }
        if (!StoryResumeDriver.Finished) return;
        Finish(null);
    }

    static void Finish(string fail)
    {
        _active = false;
        StoryResumeDriver.Active = false;

        EditorSettings.enterPlayModeOptionsEnabled = _oldOptEnabled;
        EditorSettings.enterPlayModeOptions = _oldOpt;

        var lines = new List<string>
        {
            "存档续播运行自检   " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            "目标：第" + CH + "章 从第 " + STEP + " 步续播（每章一档 + 章内续播）",
            ""
        };
        lines.AddRange(StoryResumeDriver.Lines);
        if (!string.IsNullOrEmpty(fail)) { lines.Add(""); lines.Add("★ 中止：" + fail); }
        if (StoryResumeDriver.Errors.Count > 0)
        {
            lines.Add("");
            lines.Add("运行期报错/异常 " + StoryResumeDriver.Errors.Count + " 条：");
            lines.AddRange(StoryResumeDriver.Errors);
        }

        Directory.CreateDirectory("Assets/assets/_报告");
        File.WriteAllText(REPORT, string.Join("\n", lines.ToArray()));

        EditorApplication.isPlaying = false;
        // 临时驱动是在编辑态场景里建的，退 Play 后它还留在（未保存的）编辑态场景里——
        // 直接重开一遍磁盘场景把未保存改动连同它一起丢掉。★别学 DestroyImmediate-after-isPlaying=false：
        // 那一刻其实还在 Play 里，删的是 Play 实例，编辑态实例会活到下次保存（被存过盘一次，踩过）
        EditorSceneManager.OpenScene(GAME_SCENE, OpenSceneMode.Single);
        Debug.Log("[StoryResumeSmoke] 存档续播自检完成，报告：" + REPORT);
    }
}

// 工程根存在 Assets/_resumesmoke_trigger.txt 时，编辑器下次刷新/重编译后自动跑一次续播自检。
[InitializeOnLoad]
public static class StoryResumeSmokeTrigger
{
    const string TriggerFile = "Assets/_resumesmoke_trigger.txt";
    const string ErrFile     = "../额外文件/错误_存档续播自检.txt";

    static StoryResumeSmokeTrigger()
    {
        if (!File.Exists(TriggerFile)) return;
        File.Delete(TriggerFile);
        EditorApplication.delayCall += delegate
        {
            try
            {
                StoryResumeSmoke.Run();
                if (File.Exists(ErrFile)) File.Delete(ErrFile);
            }
            catch (System.Exception e)
            {
                Directory.CreateDirectory("../额外文件");
                File.WriteAllText(ErrFile, e.ToString());
                Debug.LogError("[StoryResumeSmoke] 失败：" + e);
            }
        };
    }
}
