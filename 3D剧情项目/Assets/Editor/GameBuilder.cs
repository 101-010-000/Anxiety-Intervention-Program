using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// 打包 exe（Windows64）：Tools/干预项目/打包exe（Windows64），或命令行
//   Unity.exe -batchmode -quit -projectPath <本项目> -executeMethod GameBuilder.BuildWin64 -logFile <日志>
// 输出：仓库根目录 焦虑干预游戏/焦虑干预游戏.exe（Unity 会把 _Data/UnityPlayer.dll 等伴生文件放进同一文件夹）
public class GameBuilder
{
    const string kExePath = "E:/Project/UnityGame/Anxiety-Intervention-Program/焦虑干预游戏/焦虑干预游戏.exe";
    const string kReportPath = "E:/Project/UnityGame/Anxiety-Intervention-Program/额外文件/日志_构建/打包报告.txt";

    [MenuItem("Tools/干预项目/打包exe（Windows64）")]
    public static void BuildWin64()
    {
        PlayerSettings.productName = "焦虑干预游戏"; // exe 与 _Data 文件夹的名字都由它决定
        // 2026-10-03 清晰度修复：UI 按 1920×1080 设计（贴图多为 1080p 档甚至更小），2K/4K 全屏会整屏放大发虚
        // → 默认 1080p 窗口（UI 1:1 锐利），玩家仍可 Alt+Enter 切全屏
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultIsNativeResolution = false;
        PlayerSettings.defaultScreenWidth = 1920;
        PlayerSettings.defaultScreenHeight = 1080;
        PlayerSettings.resizableWindow = true;

        var scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();
        var want = new[] { "Assets/Scenes/MainMenu.unity", "Assets/Scenes/Game.unity" };
        if (!want.All(scenes.Contains) || scenes.Length != want.Length)
        {
            var msg = "Build Settings 场景列表不符合预期（应为 MainMenu + Game 共 2 个）：\n" +
                      string.Join("\n", scenes);
            Fail(msg);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(kExePath));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = kExePath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.CompressWithLz4, // 比 LZMA 启动加载快
        });

        var sb = new StringBuilder();
        sb.AppendLine($"打包时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"结果: {report.summary.result}  大小: {report.summary.totalSize / 1024.0 / 1024.0:F1} MB  耗时: {report.summary.totalTime.TotalSeconds:F0}s");
        sb.AppendLine($"输出: {kExePath}");
        sb.AppendLine("场景: " + string.Join(", ", scenes));
        sb.AppendLine($"错误: {report.summary.totalErrors}  警告: {report.summary.totalWarnings}");
        foreach (var step in report.steps.Where(st => st.duration.TotalSeconds > 1))
            sb.AppendLine($"  {step.name}: {step.duration.TotalSeconds:F0}s");
        Directory.CreateDirectory(Path.GetDirectoryName(kReportPath));
        File.WriteAllText(kReportPath, sb.ToString(), Encoding.UTF8);
        Debug.Log("[GameBuilder] " + sb.ToString());

        if (report.summary.result != BuildResult.Succeeded || report.summary.totalErrors > 0)
            EditorApplication.Exit(1); // batchmode 下给调用方非零退出码
    }

    static void Fail(string msg)
    {
        Debug.LogError("[GameBuilder] " + msg);
        Directory.CreateDirectory(Path.GetDirectoryName(kReportPath));
        File.WriteAllText(kReportPath, "打包失败：\n" + msg, Encoding.UTF8);
        EditorApplication.Exit(1);
    }
}
