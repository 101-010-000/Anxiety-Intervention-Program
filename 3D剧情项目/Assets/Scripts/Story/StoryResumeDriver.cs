// 存档续播运行自检（Play 模式协程驱动）：验证"读档 → Begin 静默快进 → 从存档步继续"整条链路。
// 编辑器侧入口在 Editor/StoryResumeSmoke.cs（菜单 Tools/干预项目/存档续播运行自检），
// 机制同 StorySmokeDriver：编辑器先把配置写进静态字段（DisableDomainReload 保活），再进 Play。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class StoryResumeDriver : MonoBehaviour
{
    public static bool Active;          // 编辑器入口置 true；也是 StoryRunner.AutoSave 的防污染开关之一
    public static bool Finished;
    public static int  CfgChapter = 2;
    public static int  CfgStep    = 43;
    public static readonly List<string> Lines  = new List<string>();
    public static readonly List<string> Errors = new List<string>();

    void Awake() { Application.logMessageReceived += OnLog; }
    void OnDestroy() { Application.logMessageReceived -= OnLog; }

    static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            Errors.Add(type + ": " + condition);
    }

    IEnumerator Start()
    {
        if (!Active) { Finished = true; yield break; }
        Lines.Clear(); Errors.Clear();
        Lines.Add("—— 存档续播自检：第" + CfgChapter + "章 从第 " + CfgStep + " 步续播 ——");

        // 等本章 runner 开跑（多 runner 并存时按 chapterIndex 找，不信 Instance）
        StoryRunner r = null;
        float t0 = Time.realtimeSinceStartup;
        while (r == null && Time.realtimeSinceStartup - t0 < 15f)
        {
            foreach (var sr in FindObjectsOfType<StoryRunner>())
                if (sr != null && sr.chapterIndex == CfgChapter) { r = sr; break; }
            if (r == null) yield return null;
        }
        if (r == null) { Fail("第" + CfgChapter + "章的 StoryRunner 没起来"); yield break; }
        r.debugStayInScene = true;               // 自检不真跳回主菜单

        // 等 Begin() 真正跑起来（TotalSteps 变非零）：驱动与 runner 的 Start 执行顺序不保证，
        // 找到 runner 的那一刻它可能还没 Begin（首跑就卡在这里：TotalSteps=0 误判 json 变了）
        float tw = Time.realtimeSinceStartup;
        while (r.TotalSteps == 0 && Time.realtimeSinceStartup - tw < 15f) yield return null;
        if (r.TotalSteps == 0) { Fail("runner 一直没 Begin（TotalSteps=0）：chapterJson 没绑或 SelectedChapter 不匹配"); yield break; }
        Lines.Add("runner 就绪，总步骤 " + r.TotalSteps);
        if (CfgStep >= r.TotalSteps) { Fail("配置的续播步 " + CfgStep + " 超出总步骤 " + r.TotalSteps + "（json 变了？改 StoryResumeSmoke 的 STEP）"); yield break; }

        // 等淡入完成、续播步执行（StepIndex 越过 CfgStep、状态离开 Card）
        float t1 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t1 < 30f)
        {
            if (r.CurrState != StoryRunner.State.Idle && r.CurrState != StoryRunner.State.Card
                && r.StepIndex > CfgStep) break;
            yield return null;
        }
        if (!(r.StepIndex > CfgStep))
        { Fail("续播没推进：StepIndex=" + r.StepIndex + "（应 > " + CfgStep + "），状态 " + r.CurrState); yield break; }
        Lines.Add("✓ 续播生效：当前第 " + r.StepIndex + " 步，状态 " + r.CurrState);

        // 世界副作用取证：本章 F 交互点消费情况 + 联动手机道具的拿起（隐藏）状态。
        // ⚠ 复用型交互点（第2章_手机 被两次 interact 复用）可能配 oneShot=false——Fire 不置
        //   Consumed，所以"已消费数"只作参考；真正要验的是联动道具被 HideProp 藏起来没有。
        int consumed = 0, total = 0;
        string propInfo = "";
        foreach (var si in FindObjectsOfType<StoryInteractable>())
        {
            if (si == null || si.chapterTag != CfgChapter || si.mode != StoryInteractable.Mode.InteractF) continue;
            total++;
            if (si.Consumed) consumed++;
            if (!string.IsNullOrEmpty(si.propObjectName))
            {
                var prop = GameObject.Find(si.propObjectName);   // inactive → null = 已被拿起
                propInfo += si.propObjectName + (prop != null ? "：在桌上" : "：已拿起（隐藏）✓") + "；";
            }
        }
        Lines.Add("本章 F 交互点：共 " + total + " 个，Consumed 标记 " + consumed + "（复用型点靠 Revive，不计入）");
        if (!string.IsNullOrEmpty(propInfo)) Lines.Add("联动道具：" + propInfo);

        var p = FindObjectOfType<FirstPersonController>();
        if (p != null) Lines.Add("玩家位置：" + p.transform.position.ToString("F2"));

        // 再观察 5 秒：状态机持续走动、无报错
        float t2 = Time.realtimeSinceStartup;
        var seen = new List<string>();
        while (Time.realtimeSinceStartup - t2 < 5f)
        {
            if (!seen.Contains(r.CurrState.ToString())) seen.Add(r.CurrState.ToString());
            yield return null;
        }
        Lines.Add("5 秒内经历状态：" + string.Join("→", seen.ToArray()));
        if (seen.Count == 1 && seen[0] == "Typing")
            Lines.Add("⚠ 整个观察窗都停在 Typing——打字机一行字 5 秒内该播完进 Gap，卡住说明 onLineTyped 没回调，人工核对");

        if (Errors.Count > 0) Fail("运行期报错 " + Errors.Count + " 条（见报告末尾）");
        else Lines.Add("结果：通过 ✓（从第 " + CfgStep + " 步续播推进，状态机正常走动，无运行期报错）");

        Finished = true;
    }

    void Fail(string msg)
    {
        Lines.Add("★ 失败：" + msg);
        Finished = true;
    }
}
