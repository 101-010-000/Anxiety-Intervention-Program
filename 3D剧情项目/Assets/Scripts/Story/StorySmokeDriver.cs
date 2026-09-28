// 剧情运行自检（Play 模式协程驱动，参数化到任意章 1-5）：
//   走完目标章全部步骤 —— 入场淡入 → 开场旁白 → F 交互 → 对话段 → ① → 微信段（手机 UI）
//   → ② → 走动/黑屏转场 → ③④ → 结束卡，断言干预题全走、权限状态正确、无运行期报错。
//
// ★ 教训（同 DoorSmokeDriver）：EditorApplication.update 的 tick ≠ 游戏帧，
//   等待一律用 yield return null 等真帧。编辑器侧入口在 PhoneChatSmoke（按章菜单）。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class StorySmokeDriver : MonoBehaviour
{
    public static bool Requested;
    public static bool Finished;
    public static int TargetChapter = 1;          // 编辑器入口设置（1-5）
    public static readonly List<string> Lines = new List<string>();
    public static readonly List<string> Errors = new List<string>();

    int _sawTyping, _sawGap, _sawNarFree, _sawChoice, _sawWalk, _sawInteract, _sawFade, _sawEnter;

    void Awake()
    {
        // 抓运行期报错进报告
        Application.logMessageReceived += OnLog;
    }

    void OnDestroy()
    {
        Application.logMessageReceived -= OnLog;
    }

    static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            Errors.Add(type + ": " + condition);
    }

    IEnumerator Start()
    {
        if (!Requested) { Finished = true; yield break; }
        Lines.Clear(); Errors.Clear();
        Lines.Add("—— 第" + TargetChapter + "章剧情运行自检 ——");

        // 等【本章】runner 起来：多 runner 并存时不能信 StoryRunner.Instance（谁后 Awake 谁占）
        StoryRunner r = null;
        float t0 = Time.realtimeSinceStartup;
        while (r == null && Time.realtimeSinceStartup - t0 < 10f)
        {
            foreach (var sr in FindObjectsOfType<StoryRunner>())
                if (sr != null && sr.chapterIndex == TargetChapter) { r = sr; break; }
            if (r == null) yield return null;
        }
        if (r == null) { Fail("第" + TargetChapter + "章的 StoryRunner 没起来（检查工具是否搭好/组件是否缺脚本）"); yield break; }
        if (!r.Finished && r.TotalSteps == 0) r.Begin();          // 编辑器直接 Play 时兜底开跑
        r.debugStayInScene = true;               // 自检不真跳回主菜单
        Lines.Add("StoryRunner 就绪，总步骤 " + r.TotalSteps);
        if (r.TotalSteps < 40) Fail("步骤数不对：" + r.TotalSteps + "（检查 json）");

        int safety = 6000;                     // 帧预算上限，防死循环。★按"帧"计而不是秒：
                                              // 编辑器负载高（多 Unity 实例/后台编译）时帧率低，
                                              // 同样的打字机步骤要吃更多帧——1600 不够（第5章踩过，
                                              // 走到 87/92 耗尽）。真死锁由下方"同步骤 600 帧"判据兜住。
        int lastIdx = -1, stuck = 0;
        while (!r.Finished && safety-- > 0)
        {
            switch (r.CurrState)
            {
                case StoryRunner.State.Typing: _sawTyping++; break;
                case StoryRunner.State.Gap: _sawGap++; break;
                case StoryRunner.State.NarFree: _sawNarFree++; break;
                case StoryRunner.State.Choice: _sawChoice++; break;
                case StoryRunner.State.WaitWalk: _sawWalk++; break;
                case StoryRunner.State.WaitInteract: _sawInteract++; break;
                case StoryRunner.State.Fade: _sawFade++; break;
                case StoryRunner.State.Enter: _sawEnter++; break;
            }

            // 防卡死：步骤号长时间不动 → 失败
            if (r.StepIndex == lastIdx) { if (++stuck > 600) { Fail("卡在步骤 " + r.StepIndex + "，状态 " + r.CurrState); yield break; } }
            else { lastIdx = r.StepIndex; stuck = 0; }

            r.DebugAdvance();
            yield return null;                 // ★ 等真游戏帧
        }

        if (r.Finished)
        {
            Lines.Add("走完全部 " + r.StepIndex + "/" + r.TotalSteps + " 步 ✓");
            Lines.Add("状态计数：Typing " + _sawTyping + " / Gap " + _sawGap + " / 开场旁白 " + _sawNarFree
                      + " / Choice " + _sawChoice + " / Walk " + _sawWalk + " / Interact " + _sawInteract
                      + " / Fade " + _sawFade + " / Enter " + _sawEnter);
            if (_sawChoice == 0) Fail("干预面板没开过");
            if (TargetChapter == 1)
            {   // 第1章既有门槛：walk/interact 必须都等待过；其它章按各自剧本可无
                if (_sawWalk == 0) Fail("走动段没等待过");
                if (_sawInteract == 0) Fail("F 交互段没等待过");
            }
            if (Errors.Count == 0) Lines.Add("运行期报错：无 ✓");
            else Lines.Add("★ 运行期报错 " + Errors.Count + " 条（见下）");
        }
        else Fail("没走完（安全步数耗尽），停在步骤 " + r.StepIndex + " 状态 " + r.CurrState);

        Finished = true;
    }

    static void Fail(string msg)
    {
        Lines.Add("★ 失败：" + msg);
        Finished = true;
    }
}
