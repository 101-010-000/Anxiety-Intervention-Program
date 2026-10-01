// stage/老师视角段运行时诊断（2026-10-01 一次性）：把 stage 是否执行、玩家渲染器清单、
// 有谁把渲染器重新打开等事实追加写到 额外文件/_stage诊断.txt——StoryRunner 的看门狗和
// SitSpot.Stand 的堆栈都走这里。问题定位后本文件与各处调用一起删。
#if UNITY_EDITOR
using System.IO;
using UnityEngine;

public static class StageDiag
{
    // ★ 额外文件/ 在【仓库根】（3D剧情项目 的上一级）：Assets → 3D剧情项目 → 仓库根，所以要两个 ".."
    public static readonly string FilePath =
        System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "..", "额外文件", "_stage诊断.txt"));

    public static void Reset()
    {
        try { File.WriteAllText(FilePath, "==== " + System.DateTime.Now.ToString("HH:mm:ss") + " ====\n"); }
        catch { /* 诊断写入绝不影响游戏 */ }
    }

    public static void Log(string msg)
    {
        try { File.AppendAllText(FilePath, Time.realtimeSinceStartup.ToString("F1") + "s  " + msg + "\n"); }
        catch { /* 诊断写入绝不影响游戏 */ }
    }
}
#endif
