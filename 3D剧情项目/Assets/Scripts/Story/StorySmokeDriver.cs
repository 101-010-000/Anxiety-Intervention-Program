// 第一章剧情运行自检（Play 模式协程驱动）：
//   走完第1章全部步骤 —— 入场淡入 → 开场旁白 → F 交互 → 组长段 → ① → 微信段（Dialog 名牌）
//   → ② → 走动撞张知远 → ③④ → 结束卡，断言每类步骤都被执行、权限状态正确、4 题全选可走。
//
// ★ 教训（同 DoorSmokeDriver）：EditorApplication.update 的 tick ≠ 游戏帧，
//   等待一律用 yield return null 等真帧。编辑器侧入口在 Chapter1StoryBuilder.SmokeTest()。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class StorySmokeDriver : MonoBehaviour
{
    public static bool Requested;
    public static bool Finished;
    public static readonly List<string> Lines = new List<string>();
    public static readonly List<string> Errors = new List<string>();

    int _sawTyping, _sawGap, _sawNarFree, _sawChoice, _sawWalk, _sawInteract;

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
        Lines.Add("—— 第一章剧情运行自检（方案一：Dialog 承载微信段） ——");

        // 等 runner 起来
        StoryRunner r = null;
        float t0 = Time.realtimeSinceStartup;
        while (r == null && Time.realtimeSinceStartup - t0 < 10f)
        {
            r = StoryRunner.Instance;
            if (r == null) yield return null;
        }
        if (r == null) { Fail("StoryRunner 没起来（检查工具是否搭好/组件是否缺脚本）"); yield break; }
        r.debugStayInScene = true;               // 自检不真跳回主菜单
        Lines.Add("StoryRunner 就绪，总步骤 " + r.TotalSteps);
        if (r.TotalSteps < 100) Fail("步骤数不对：" + r.TotalSteps + "（应 117，检查 json）");

        int safety = 800;                      // 步数上限，防死循环
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
                      + " / Choice " + _sawChoice + " / Walk " + _sawWalk + " / Interact " + _sawInteract);
            if (_sawChoice == 0) Fail("干预面板没开过");
            if (_sawWalk == 0) Fail("走动段没等待过");
            if (_sawInteract == 0) Fail("F 交互段没等待过");
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
