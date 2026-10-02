// 摄像机旋转限制：把主角第三人称镜头的「俯仰上限收紧 + 水平限位」写进 Game.unity（幂等）。
//
// 背景（用户 2026-10-01）：镜头水平 360° 随便转、俯仰上抬 +40°——贴墙转身时镜头会被甩到
// 墙外看到场景外的虚空；上抬还会把镜头举到墙顶/楼板以上（支点 1.45m + 距离 3.4m + 40°
// ≈ 3.6m 高），看到“房顶内部”。运行时限位逻辑在 FirstPersonController（tpYawClamp /
// tpYawMin / tpYawMax + tpPitchMax，参考朝向开局/瞬移/剧情摆镜头后自动重开窗）；
// ★ 本工具只负责把场景里已序列化的值写到定稿值——运行时默认值只对“新实例”生效，
//   场景里旧的 tpPitchMax=40 不会自己变。
//
// 用法：菜单 Tools/干预项目/摄像机旋转限制/应用到玩家（幂等） 或丢 Assets/_camlimit_trigger.txt
// 报告：Assets/assets/_报告/_摄像机旋转限制.txt
// 想放宽/收紧：直接在 Inspector 改玩家 FirstPersonController 的对应字段（序列化在场景里，
// 重跑本工具会写回定稿值）。
// v2（2026-10-01）：补充触发器自举说明。

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CameraLimitSetup
{
    const string SCENE = "Assets/Scenes/Game.unity";
    const string REPORT = "Assets/assets/_报告/_摄像机旋转限制.txt";

    // ★ 定稿值（2026-10-01）：俯仰 -25°~+15°（-45° 会压到贴地仰视盯天花板；+40° 会越过 ~3m 墙顶）；水平 ±135°（共 270°）。
    const float PITCH_MIN = -25f;
    const float PITCH_MAX = 15f;
    const float YAW_MIN = -135f;
    const float YAW_MAX = 135f;

    [MenuItem("Tools/干预项目/摄像机旋转限制/应用到玩家（幂等）")]
    public static void Apply()
    {
        var log = new List<string>();
        log.Add("== 摄像机旋转限制：应用到玩家 ==");
        log.Add("时间：" + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        log.Add("");

        // ★ Play 模式里写场景会被 Unity 拒绝（MarkSceneDirty/SaveScene 抛 "This cannot be used
        //   during play mode"，2026-10-01 踩过：触发器在用户进 Play 的重载里跑掉，改动退出即丢）。
        if (Application.isPlaying)
        {
            log.Add("★ 当前在 Play 模式：本次跳过（改动会落在 Play 内存副本、退出即丢）。");
            log.Add("  退出 Play 后重跑本工具，或丢 Assets/_camlimit_trigger.txt 等下次域重载自动跑。");
            Flush(log);
            return;
        }

        if (!OpenGame(log)) { Flush(log); return; }
        var sc = SceneManager.GetActiveScene();

        var fpc = FindPlayerFpc(log);
        if (fpc == null) { Flush(log); return; }

        log.Add("玩家：" + fpc.gameObject.name + "（场景 " + sc.name + "）");
        log.Add(string.Format("改前：俯仰 {0}~{1}°，水平限位 {2}（{3}~{4}°）",
            fpc.tpPitchMin, fpc.tpPitchMax, fpc.tpYawClamp, fpc.tpYawMin, fpc.tpYawMax));

        fpc.tpPitchMin = PITCH_MIN;
        fpc.tpPitchMax = PITCH_MAX;
        fpc.tpYawClamp = true;
        fpc.tpYawMin = YAW_MIN;
        fpc.tpYawMax = YAW_MAX;
        EditorUtility.SetDirty(fpc);
        EditorSceneManager.MarkSceneDirty(sc);
        bool saved = EditorSceneManager.SaveScene(sc);

        log.Add(string.Format("改后：俯仰 {0}~{1}°，水平限位 {2}（{3}~{4}°）",
            fpc.tpPitchMin, fpc.tpPitchMax, fpc.tpYawClamp, fpc.tpYawMin, fpc.tpYawMax));
        log.Add("存场景：" + (saved ? "成功 ✓" : "★ 失败"));
        log.Add("");
        log.Add("说明：");
        log.Add("  · 运行时逻辑在 FirstPersonController：Look() 里把 camYaw 夹在「参考朝向 ± 范围」内（DeltaAngle 处理 ±180° 环绕）；");
        log.Add("    参考朝向在 开局 / 瞬移（Update 位移 >1m 检测）/ 剧情摆镜头（ResetCameraNow、LookTowardRoutine 结束、ReleaseCamera）自动重开窗。");
        log.Add("  · 上抬 +15° 的根据：支点 1.45m + 距离 3.4m 时，+40° 会把镜头举到 ~3.6m——越过 ~3m 墙顶 / 3.3m 楼板，看到场景外与房顶内部。");
        log.Add("  · 只约束玩家鼠标：剧情/自检直接写 camYaw 的路径（LookTowardRoutine、自检驱动）不受限，不会打架。");
        log.Add("  · 想调：Inspector 改 tpYawMin/tpYawMax/tpPitchMax 即可（序列化在场景里）；重跑本工具写回定稿值。");
        Flush(log);
    }

    static FirstPersonController FindPlayerFpc(List<string> log)
    {
        FirstPersonController fpc = null;
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (!root.name.StartsWith("Player_")) continue;
            fpc = root.GetComponent<FirstPersonController>();
            if (fpc != null) break;
        }
        if (fpc == null) log.Add("★ 场景根没找到 Player_*/FirstPersonController（确认 Game.unity 已打开）");
        return fpc;
    }

    static bool OpenGame(List<string> log)
    {
        if (!File.Exists(SCENE)) { log.Add("★ 找不到 " + SCENE); return false; }
        var active = SceneManager.GetActiveScene();
        if (active.path == SCENE) return true;
        if (active.isDirty) { EditorSceneManager.SaveScene(active); log.Add("（先把当前场景 " + active.name + " 存盘）"); }
        EditorSceneManager.OpenScene(SCENE, OpenSceneMode.Single);
        log.Add("已打开 " + SCENE);
        log.Add("");
        return true;
    }

    static void Flush(List<string> log)
    {
        var dir = Path.GetDirectoryName(REPORT);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(REPORT, string.Join("\n", log.ToArray()));
        Debug.Log(string.Join("\n", log.ToArray()));
    }
}

// 丢 Assets/_camlimit_trigger.txt → 下次刷新/重编译后自动跑一次
[InitializeOnLoad]
public static class CameraLimitTrigger
{
    const string Trigger = "Assets/_camlimit_trigger.txt";

    static CameraLimitTrigger()
    {
        if (!File.Exists(Trigger)) return;
        EditorApplication.delayCall += () =>
        {
            // ★ Play 模式里不消费触发器（写场景会被拒绝，2026-10-01 踩过）：
            //   留着文件，等退出 Play 后的下一次域重载再跑。
            if (Application.isPlaying) return;
            try
            {
                if (File.Exists(Trigger)) File.Delete(Trigger);
                CameraLimitSetup.Apply();
            }
            catch (System.Exception e)
            {
                Debug.LogError("[CameraLimitSetup] 触发器执行失败：" + e);
                File.WriteAllText("Assets/_camlimit_trigger_失败.txt", e.ToString());
            }
        };
    }
}
