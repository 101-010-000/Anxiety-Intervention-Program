// 第一章剧情运行自检（编辑器侧入口）。运行时的走表逻辑在 StorySmokeDriver（Play 模式协程），
// 这里只负责：进 Play → 置 Requested → 轮询 Finished → 写报告 → 退出 Play。
// 自检报告：assets/_报告/_第一章剧情运行自检.txt
// 用法：菜单 Tools/干预项目/第一章剧情运行自检，或丢 Assets/_story1smoke_trigger.txt
// （旧入口在归档的 Chapter1StoryBuilder 里，已与现在的 ChoicePanel API 脱节，不能再用 —— 2026-09-27）
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PhoneChatSmoke
{
    const string GAME_SCENE = "Assets/Scenes/Game.unity";
    const string REPORT = "Assets/assets/_报告/_第一章剧情运行自检.txt";

    static bool _active, _hooked, _forcedBegin;
    static double _start;
    static bool _oldOptEnabled;
    static EnterPlayModeOptions _oldOpt;

    [MenuItem("Tools/干预项目/第一章剧情运行自检", false, 41)]
    public static void SmokeTest()
    {
        _oldOptEnabled = EditorSettings.enterPlayModeOptionsEnabled;
        _oldOpt = EditorSettings.enterPlayModeOptions;
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;

        EditorSceneManager.OpenScene(GAME_SCENE, OpenSceneMode.Single);

        StorySmokeDriver.Requested = true;
        StorySmokeDriver.Finished = false;
        StorySmokeDriver.Lines.Clear();
        StorySmokeDriver.Errors.Clear();
        _forcedBegin = false;

        _start = EditorApplication.timeSinceStartup;
        _active = true;
        if (!_hooked) { EditorApplication.update += SmokePoll; _hooked = true; }
        Debug.Log("[PhoneChatSmoke] 第一章剧情运行自检开始");
    }

    static void SmokePoll()
    {
        if (!_active) return;
        if (EditorApplication.timeSinceStartup - _start > 300) { Finish("超时(300 秒)"); return; }
        if (!EditorApplication.isPlaying) { EditorApplication.isPlaying = true; return; }
        // 兜底：编辑器直接进 Play 时 GameProgress 往往没选中本章，runOnStart 不会自动开跑 → 强制 Begin
        if (!_forcedBegin && StoryRunner.Instance != null && StoryRunner.Instance.TotalSteps == 0)
        {
            _forcedBegin = true;
            StoryRunner.Instance.Begin();
        }
        if (!StorySmokeDriver.Finished) return;
        Finish(null);
    }

    static void Finish(string fail)
    {
        _active = false;
        StorySmokeDriver.Requested = false;

        EditorSettings.enterPlayModeOptionsEnabled = _oldOptEnabled;
        EditorSettings.enterPlayModeOptions = _oldOpt;

        var lines = new List<string>
        {
            "第一章剧情运行自检（真进 Play 模式走一遍）  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            ""
        };
        lines.AddRange(StorySmokeDriver.Lines);
        if (!string.IsNullOrEmpty(fail)) { lines.Add(""); lines.Add("★ 中止：" + fail); }
        if (StorySmokeDriver.Errors.Count > 0)
        {
            lines.Add("");
            lines.Add("运行期报错/异常 " + StorySmokeDriver.Errors.Count + " 条：");
            lines.AddRange(StorySmokeDriver.Errors);
        }
        lines.Add("");
        lines.Add("注：编辑器伪造不了 Input.GetKeyDown，交互/推进走的是 DebugAdvance()/Fire()，");
        lines.Add("    与真实按键是同一入口。");

        Directory.CreateDirectory(Path.GetDirectoryName(REPORT).Replace('/', Path.DirectorySeparatorChar));
        File.WriteAllText(REPORT, string.Join("\n", lines.ToArray()));

        EditorApplication.isPlaying = false;
        Debug.Log("[PhoneChatSmoke] 第一章剧情运行自检完成，报告：" + REPORT);
    }
}

// 工程里存在 Assets/_story1smoke_trigger.txt 时，编辑器下次刷新/重编译后自动跑一次。
[InitializeOnLoad]
public static class PhoneChatSmokeTrigger
{
    const string Trigger = "Assets/_story1smoke_trigger.txt";
    const string ErrFile = "../额外文件/错误_第一章自检.txt";

    static PhoneChatSmokeTrigger()
    {
        if (File.Exists(Trigger))
        {
            EditorApplication.delayCall += () =>
            {
                try
                {
                    if (File.Exists(Trigger)) File.Delete(Trigger);
                    if (File.Exists(Trigger + ".meta")) File.Delete(Trigger + ".meta");
                    PhoneChatSmoke.SmokeTest();
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
