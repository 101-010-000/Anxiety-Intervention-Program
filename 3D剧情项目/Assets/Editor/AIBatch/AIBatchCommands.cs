using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AIBatch
{
    /// <summary>
    /// 供 AI 通过 `Unity.exe -batchmode -executeMethod AIBatch.Commands.<方法名>` 调用的命令入口。
    /// 约定：所有结果都输出带 [AIBATCH] 前缀的日志，用 EditorApplication.Exit 返回受控退出码（0=成功，1=失败），
    /// 外层包装脚本依据这两点判定成败并提取错误。
    /// </summary>
    public static class Commands
    {
        const string Tag = "[AIBATCH]";

        /// <summary>
        /// 编译与资产验证。batchmode 启动时 Unity 会先编译全部脚本：
        /// 项目里任何脚本有编译错误，本方法都不会被调用（Unity 直接报错退出）。
        /// 因此方法能执行到，就证明 Assembly-CSharp 与 Assembly-CSharp-Editor 均编译通过；
        /// 在此基础上再统计 Console 错误数，把导入、初始化阶段的报错也纳入判定。
        /// </summary>
        public static void Validate()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

            int errors = ConsoleErrorCount();
            if (errors > 0)
            {
                Debug.LogError($"{Tag} VALIDATE_FAIL (console errors: {errors})");
                EditorApplication.Exit(1);
            }

            Debug.Log($"{Tag} VALIDATE_OK");
            EditorApplication.Exit(0);
        }

        /// <summary>
        /// 按 Build Settings 当前配置出包。可用 -aiBuildOutput=&lt;目录&gt; 指定输出目录，
        /// 未指定时输出到 Builds/AIBatchBuild。
        /// </summary>
        public static void Build()
        {
            string output = GetArg("-aiBuildOutput");
            if (string.IsNullOrEmpty(output))
                output = "Builds/AIBatchBuild";

            var scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                Debug.LogError($"{Tag} BUILD_FAIL (Build Settings 中没有勾选任何场景)");
                EditorApplication.Exit(1);
            }

            var target = EditorUserBuildSettings.activeBuildTarget;
            // Windows 平台要求 locationPathName 是 exe 文件；WebGL 要求是目录。其余平台按目录处理。
            string location = (target == BuildTarget.StandaloneWindows || target == BuildTarget.StandaloneWindows64)
                ? $"{output}/build.exe"
                : output;

            Debug.Log($"{Tag} BUILD_START target={target} scenes={scenes.Length} output={location}");
            BuildReport report = BuildPipeline.BuildPlayer(scenes, location, target, BuildOptions.None);

            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"{Tag} BUILD_FAIL result={report.summary.result} errors={report.summary.totalErrors} output={location}");
                EditorApplication.Exit(1);
            }

            Debug.Log($"{Tag} BUILD_OK size={report.summary.totalSize} output={location}");
            EditorApplication.Exit(0);
        }

        static string GetArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }

        /// <summary>
        /// LogEntries 是 internal API，反射读取 Console 当前错误数；反射失败时返回 0，
        /// 退化为只依赖"方法被调用即编译通过"的判定。
        /// </summary>
        static int ConsoleErrorCount()
        {
            try
            {
                Type t = Type.GetType("UnityEditor.LogEntries, UnityEditor");
                MethodInfo m = t.GetMethod("GetCountsByType", BindingFlags.Static | BindingFlags.NonPublic);
                object[] counts = { 0, 0, 0 };
                m.Invoke(null, counts);
                return (int)counts[0];
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{Tag} LogEntries 反射失败，跳过 Console 错误统计: {e.Message}");
                return 0;
            }
        }
    }
}
