// 剧情运行自检（编辑器侧入口，参数化到第 1-5 章）。运行时的走表逻辑在 StorySmokeDriver（Play 模式协程），
// 这里只负责：进 Play → 置 Requested → 轮询 Finished → 写报告 → 退出 Play。
// 自检报告：assets/_报告/_第N章剧情运行自检.txt
// 用法：菜单 Tools/干预项目/第N章剧情运行自检，或丢 Assets/_storyNsmoke_trigger.txt（N=1..5）
// （旧入口在归档的 Chapter1StoryBuilder 里，已与现在的 ChoicePanel API 脱节，不能再用 —— 2026-09-27）
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PhoneChatSmoke
{
    const string GAME_SCENE = "Assets/Scenes/Game.unity";

    // ⚠ 自检会 OpenScene(GAME_SCENE, Single) 丢弃未保存的场景修改 → 跑之前场景必须已保存！
    public static void SmokeTest(int chapter)
    {
        _oldOptEnabled = EditorSettings.enterPlayModeOptionsEnabled;
        _oldOpt = EditorSettings.enterPlayModeOptions;
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;

        EditorSceneManager.OpenScene(GAME_SCENE, OpenSceneMode.Single);

        // ★ 自检强制选定目标章：SelectedChapter 残留上次游玩的章时，那个章的 runner 会在后台
        //   自动 Begin（抢交互点接线/挪玩家/抢 UI），污染自检环境。强制后只有目标章开跑。
        GameProgress.SelectChapter(chapter);

        StorySmokeDriver.Requested = true;
        StorySmokeDriver.Finished = false;
        StorySmokeDriver.TargetChapter = chapter;
        StorySmokeDriver.Lines.Clear();
        StorySmokeDriver.Errors.Clear();

        _start = EditorApplication.timeSinceStartup;
        _active = true;
        if (!_hooked) { EditorApplication.update += SmokePoll; _hooked = true; }
        Debug.Log("[PhoneChatSmoke] 第" + chapter + "章剧情运行自检开始");
    }

    [MenuItem("Tools/干预项目/第一章剧情运行自检", false, 41)]
    public static void SmokeTest1() { SmokeTest(1); }

    [MenuItem("Tools/干预项目/第二章剧情运行自检", false, 42)]
    public static void SmokeTest2() { SmokeTest(2); }

    [MenuItem("Tools/干预项目/第三章剧情运行自检", false, 43)]
    public static void SmokeTest3() { SmokeTest(3); }

    [MenuItem("Tools/干预项目/第四章剧情运行自检", false, 44)]
    public static void SmokeTest4() { SmokeTest(4); }

    [MenuItem("Tools/干预项目/第五章剧情运行自检", false, 45)]
    public static void SmokeTest5() { SmokeTest(5); }

    static bool _active, _hooked;
    static double _start;
    static bool _oldOptEnabled;
    static EnterPlayModeOptions _oldOpt;

    static void SmokePoll()
    {
        if (!_active) return;
        if (EditorApplication.timeSinceStartup - _start > 300) { Finish("超时(300 秒)"); return; }
        if (!EditorApplication.isPlaying) { EditorApplication.isPlaying = true; return; }
        // 兜底开跑在 StorySmokeDriver 里做（按 TargetChapter 找 runner，多 runner 并存时不能信 Instance）
        if (!StorySmokeDriver.Finished) return;
        Finish(null);
    }

    static void Finish(string fail)
    {
        _active = false;
        StorySmokeDriver.Requested = false;
        int chapter = StorySmokeDriver.TargetChapter;

        EditorSettings.enterPlayModeOptionsEnabled = _oldOptEnabled;
        EditorSettings.enterPlayModeOptions = _oldOpt;

        var lines = new List<string>
        {
            "第" + chapter + "章剧情运行自检（真进 Play 模式走一遍）  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
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

        string report = "Assets/assets/_报告/_第" + chapter + "章剧情运行自检.txt";
        Directory.CreateDirectory(Path.GetDirectoryName(report).Replace('/', Path.DirectorySeparatorChar));
        File.WriteAllText(report, string.Join("\n", lines.ToArray()));

        EditorApplication.isPlaying = false;
        Debug.Log("[PhoneChatSmoke] 第" + chapter + "章剧情运行自检完成，报告：" + report);
    }
}

// 工程里存在 Assets/_storyNsmoke_trigger.txt（N=1..5）时，编辑器下次刷新/重编译后自动跑一次对应章自检。
// （自检只进 Play 走表 + 写报告 + 退 Play，不改场景 → 安全；但跑之前场景要先保存）
[InitializeOnLoad]
public static class PhoneChatSmokeTrigger
{
    static PhoneChatSmokeTrigger()
    {
        EditorApplication.delayCall += Check;
    }

    static void Check()
    {
        for (int ch = 1; ch <= 5; ch++)
        {
            int chapter = ch;                       // 闭包捕获用
            string trigger = "Assets/_story" + chapter + "smoke_trigger.txt";
            if (!File.Exists(trigger)) continue;
            try
            {
                File.Delete(trigger);
                if (File.Exists(trigger + ".meta")) File.Delete(trigger + ".meta");
                PhoneChatSmoke.SmokeTest(chapter);
            }
            catch (System.Exception e)
            {
                Directory.CreateDirectory("../额外文件");
                File.WriteAllText("../额外文件/错误_第" + chapter + "章自检.txt", e.ToString());
                throw;
            }
        }
    }
}
