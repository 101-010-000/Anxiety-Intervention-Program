// 第1章微信段切视角到林溪：场景接线（幂等，2026-09-30）
//
// 背景：第1章.json 已在「手机震动了一下」前插 {"t":"cut","who":"林溪","camH":1.15,"lookH":0.95}、
//   在选择题后插 {"t":"cut"}（切回）。用户手摆了坐姿实例：
//   GameRoot/Locations/Loc_宿舍/第一章角色/林溪（源 = 带动画模型/林溪/Sitting Idle.fbx，
//   Generic 无 Avatar——和 Idle/Walk 同源零重定向，直接用，SitSetup 同款）。
//   没有 Animator/控制器时它是静止绑定姿势，切过去不会播坐姿 → 本工具补线。
//
// 做什么（缺啥补啥，已有的不动）：
//   ① 找实例：GO 名恰为「林溪」、祖先容器匹配 ^第[0-9一二三四五]章角色$（限容器，防误接线）；
//      同名多个时取容器下最浅的（实例根），仍并列 → 报警告中止。
//      ★ 第4章那位换装版叫「林溪_宿舍」，不同名，永远不会被抓到。
//   ② Animator：无则加（加在模型根上——Generic 按节点路径回放，路径相对 Animator 所在节点），
//      controller = assets/03_动作_Animation/Animators/带动画模型/林溪_Idle.controller
//      （SitSetup 已给它加过 Sitting 参数 + Sit 状态，剪辑与实例同源）。
//   ③ SitHere（Assets/Scripts/Game/SitSpot.cs，Start 把 Sitting 置 true）：无则加。
//      先例：陆宣雨_可动_坐着 同款接法。
//   ④ 自检（编辑器即可）：控制器参数含 Sitting、Sit 状态存在且 Motion=林溪_Sit、
//      AnyState→Sit 过渡条件、Renderer 包围盒中心离地高度（坐姿应明显低于站姿 ~0.9-1.0m）。
//   报告 assets/_报告/_第1章林溪切视角.txt；有改动则保存 Game.unity。重跑零改动。
//
// 菜单：Tools/干预项目/第1章林溪切视角/接线（幂等）
// 触发器：Assets/_linxicutview_trigger.txt（丢进 Assets 下次刷新自动跑，用完自删）
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class LinxiCutviewSetup
{
    const string CHAR_NAME = "林溪";
    const string CTRL_PATH = "Assets/assets/03_动作_Animation/Animators/带动画模型/林溪_Idle.controller";
    const string REPORT = "Assets/assets/_报告/_第1章林溪切视角.txt";
    // ★ 只认第1章容器：食堂 第3章角色 下另有一个裸名坐姿「林溪」（第3章落座那位），
    //   按任意章容器放行会和她并列候选、触发防误接中止，接线永远落不了地。
    static readonly Regex Ch1ContainerRe = new Regex("^第(?:一|1)章角色$");

    [MenuItem("Tools/干预项目/第1章林溪切视角/接线（幂等）", false, 130)]
    public static void Run()
    {
        var log = new List<string>();
        log.Add("第1章林溪切视角：场景接线  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("目标：Loc_宿舍/第一章角色 下的坐姿实例「" + CHAR_NAME + "」（Sitting Idle.fbx）播林溪_Sit");
        log.Add("");

        var t = FindInstance(log);
        if (t == null)
        {
            log.Add("");
            log.Add("★ 中止：没有可信的接线对象（见上方警告）。场景未改。");
            Flush(log);
            return;
        }

        log.Add("── 实例");
        log.Add("  路径：" + PathOf(t));
        log.Add("  摆位：pos=" + t.position.ToString("F3") + "  yaw=" + t.eulerAngles.y.ToString("F1") + "°  active=" + t.gameObject.activeInHierarchy);
        var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
        if (src != null)
            log.Add("  源资产：" + AssetDatabase.GetAssetPath(src));
        log.Add("");

        bool changed = EnsureRig(t, log);
        log.Add("");
        SelfCheck(t, log);

        if (changed)
        {
            var scene = t.gameObject.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.Add("");
            log.Add("已保存场景：" + scene.path);
        }
        else
        {
            log.Add("");
            log.Add("本次零改动（幂等重跑）。");
        }

        Flush(log);
    }

    // ------------------------------------------------------------ ① 找实例
    static Transform FindInstance(List<string> log)
    {
        Transform best = null;
        int bestDepth = int.MaxValue;
        var candidates = new List<Transform>();
        foreach (var t in Object.FindObjectsOfType<Transform>(true))   // 含禁用
        {
            if (t.name != CHAR_NAME) continue;
            if (!HasChapterContainer(t)) continue;
            candidates.Add(t);
        }
        if (candidates.Count == 0)
        {
            log.Add("★ 警告：全场景（含禁用）找不到「名字恰为 " + CHAR_NAME + " 且祖先带 第一章角色 容器」的实例。");
            log.Add("  请确认 Game.unity 已打开、实例摆在 Loc_宿舍/第一章角色 下。");
            return null;
        }
        foreach (var t in candidates)
        {
            int d = DepthUnderContainer(t);
            if (d < bestDepth) { bestDepth = d; best = t; }
        }
        // 并列最浅 = 分不清谁是实例根 → 宁可不接
        int tie = candidates.Count(t => DepthUnderContainer(t) == bestDepth);
        if (tie > 1)
        {
            log.Add("★ 警告：容器下找到 " + tie + " 个同名同深度的「" + CHAR_NAME + "」，分不清实例根：");
            foreach (var t in candidates) log.Add("    " + PathOf(t));
            return null;
        }
        log.Add("候选 " + candidates.Count + " 个（取容器下最浅 = 实例根）：");
        foreach (var t in candidates) log.Add("  " + (t == best ? "★ " : "    ") + PathOf(t));
        return best;
    }

    static bool HasChapterContainer(Transform t)
    {
        for (var p = t.parent; p != null; p = p.parent)
            if (Ch1ContainerRe.IsMatch(p.name)) return true;
        return false;
    }

    static int DepthUnderContainer(Transform t)
    {
        int d = 0;
        for (var p = t.parent; p != null; p = p.parent)
        {
            d++;
            if (Ch1ContainerRe.IsMatch(p.name)) break;
        }
        return d;
    }

    static string PathOf(Transform t)
    {
        var names = new List<string>();
        for (var p = t; p != null; p = p.parent) names.Insert(0, p.name);
        return string.Join("/", names.ToArray());
    }

    // ------------------------------------------------------------ ②③ 接线（缺啥补啥）
    static bool EnsureRig(Transform t, List<string> log)
    {
        bool changed = false;
        var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(CTRL_PATH);
        var anim = t.GetComponent<Animator>();

        // 设计要求：实例缺 Animator 且控制器资产也找不到 → 报错落盘，不静默（触发器会写 额外文件/错误_*.txt）
        if (anim == null && ctrl == null)
            throw new System.IO.FileNotFoundException("实例「" + CHAR_NAME + "」缺 Animator，且找不到控制器资产：" + CTRL_PATH);

        log.Add("── 接线");
        if (anim == null)
        {
            anim = t.gameObject.AddComponent<Animator>();
            changed = true;
            log.Add("  + Animator（加在模型根上；Generic 按节点路径回放）");
        }
        else
        {
            log.Add("  = Animator 已存在");
        }
        if (anim.runtimeAnimatorController != ctrl)
        {
            anim.runtimeAnimatorController = ctrl;
            EditorUtility.SetDirty(anim);
            changed = true;
            log.Add("  + controller = " + CTRL_PATH);
        }
        else
        {
            log.Add("  = controller 已是 " + Path.GetFileName(CTRL_PATH));
        }

        if (t.GetComponent<SitHere>() == null)
        {
            t.gameObject.AddComponent<SitHere>();
            changed = true;
            log.Add("  + SitHere（Start 把 Sitting 置 true，进场景即坐）");
        }
        else
        {
            log.Add("  = SitHere 已存在");
        }
        return changed;
    }

    // ------------------------------------------------------------ ④ 自检（编辑器即可）
    static void SelfCheck(Transform t, List<string> log)
    {
        log.Add("── 自检");
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(CTRL_PATH);
        if (ctrl == null) { log.Add("  ★ 控制器加载失败：" + CTRL_PATH); return; }

        bool hasSitting = ctrl.parameters.Any(p => p.name == "Sitting");
        log.Add("  控制器参数含 Sitting：" + (hasSitting ? "✓" : "★无（先跑 SitSetup：生成剪辑 + 加 Sit 状态）"));

        var sm = ctrl.layers[0].stateMachine;
        var sit = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == "Sit");
        if (sit != null)
        {
            string motion = sit.motion != null ? sit.motion.name : "★空";
            log.Add("  Sit 状态：" + (motion == CHAR_NAME + "_Sit" ? "✓" : "★") + " Motion=" + motion);
        }
        else
        {
            log.Add("  ★ 控制器里没有 Sit 状态（先跑 Tools/干预项目/坐姿：生成剪辑 + 加 Sit 状态）");
        }
        var toSit = sm.anyStateTransitions.Where(tr => tr.destinationState == sit).ToList();
        if (toSit.Count > 0)
        {
            var tr = toSit[0];
            string conds = string.Join(" & ", tr.conditions.Select(c => c.mode + " " + c.parameter).ToArray());
            log.Add("  AnyState→Sit 过渡：" + toSit.Count + " 条（" + conds + "）");
        }
        else
        {
            log.Add("  ★ AnyState→Sit 过渡缺失（SitHere 置了 Sitting 也没人消费）");
        }

        // 坐姿体检：蒙皮网格包围盒中心离地高度（坐姿 ~0.5-0.7m，站姿 ~0.9-1.0m）
        var rends = t.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (rends.Length > 0)
        {
            float h = rends.Max(r => r.bounds.max.y) - rends.Min(r => r.bounds.min.y);
            log.Add(string.Format("  渲染包围盒：中心离地 {0:0.00}m，总高 {1:0.00}m（坐姿应明显低于站姿 ~1.7m）",
                rends.Min(r => r.bounds.min.y) + h * 0.5f, h));
        }
        else
        {
            log.Add("  ★ 实例下没有 SkinnedMeshRenderer");
        }
        log.Add("  说明：编辑模式下该实例可能仍显示绑定/T-pose，Play 里 SitHere 才会置 Sitting——不是坏了。");
    }

    // ------------------------------------------------------------ 报告
    static void Flush(List<string> log)
    {
        Directory.CreateDirectory("Assets/assets/_报告");
        File.WriteAllText(REPORT, string.Join("\n", log.ToArray()));
        AssetDatabase.Refresh();
        Debug.Log("[LinxiCutviewSetup] 报告：" + REPORT);
    }
}

// 丢 Assets/_linxicutview_trigger.txt → 下次刷新/重编译后自动跑一次
[InitializeOnLoad]
public static class LinxiCutviewTrigger
{
    const string Trigger = "Assets/_linxicutview_trigger.txt";
    const string ErrFile = "../额外文件/错误_第1章林溪切视角.txt";

    static LinxiCutviewTrigger()
    {
        if (!File.Exists(Trigger)) return;
        EditorApplication.delayCall += () =>
        {
            try
            {
                if (File.Exists(Trigger)) File.Delete(Trigger);
                if (File.Exists(Trigger + ".meta")) File.Delete(Trigger + ".meta");
                LinxiCutviewSetup.Run();
            }
            catch (System.Exception e)
            {
                Directory.CreateDirectory("../额外文件");
                File.WriteAllText(ErrFile, e.ToString());
                Debug.LogError("[LinxiCutviewSetup] 失败: " + e);
            }
        };
    }
}
