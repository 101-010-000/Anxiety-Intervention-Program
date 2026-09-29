// NPC 走路动画接入（2026-09-29）：
//   背景：入场/退场（NpcEntrance）走位时写 Animator 的 Speed（0=待机 / 0.65=行走），
//   但 GameCharSwap 生成的 <角色>_Idle.controller 只有单个 Idle 状态——Speed 是给
//   FirstPersonController 留的占位参数，没有任何状态消费它 → NPC 走位全程播待机
//   （试玩实测：陆宣雨"待机姿势飘进来"）。
//   本工具把 带动画模型/<角色>/Walk.fbx 接进 <角色>_Idle.controller（幂等，扫描全部角色）：
//     ① <角色>_Walk.anim：Walk.fbx 里 clip 的循环副本（loopTime=true，照 <角色>_Idle.anim 同款）；
//        已存在则原地 CopySerialized 刷新（保 GUID，不弄断控制器引用）。
//     ② 控制器加 <角色>_Walk 状态（Motion=循环副本）+ 双向过渡：
//        Idle→Walk：Speed > 0.5；Walk→Idle：Speed < 0.5（正好接住 NpcEntrance 写的 0.65 / 0），
//        hasExitTime=false、过渡 0.15s。默认状态仍是 Idle。
//   场景引用不用动：实例 Animator 指向的就是这个 .controller 文件（改资产即生效）；
//   GameCharSwap 重跑也不会冲掉——它看到控制器已存在就跳过。
//   另带体检：Walk 剪辑若带根位移（Mixamo 下载没勾 In Place），报告里会警告——
//   那种剪辑播起来会自己往前蹭，和 NpcEntrance 的 Lerp 叠加成"漂移"。
//   菜单：Tools/干预项目/NPC走路动画/接入（扫描带Walk.fbx的角色）
//   触发器：Assets/_npcwalk_trigger.txt（丢进 Assets 下次刷新自动跑，用完自删）
//   报告：assets/_报告/_NPC走路接入.txt
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

public static class NpcWalkSetup
{
    const string NEW_ROOT = "Assets/assets/03_动作_Animation/带动画模型";
    const string ANIM_DIR = "Assets/assets/03_动作_Animation/Animators/带动画模型";
    const string REPORT = "Assets/assets/_报告/_NPC走路接入.txt";
    const float WALK_THRESHOLD = 0.5f;   // 与 NpcEntrance.SetWalk 写的 0.65/0 配套
    const float TRANSITION = 0.15f;

    [MenuItem("Tools/干预项目/NPC走路动画/接入（扫描带Walk.fbx的角色）", false, 130)]
    public static void Run()
    {
        var log = new List<string>();
        log.Add("NPC 走路动画接入  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("规则：Idle↔Walk，Speed " + WALK_THRESHOLD + " 切换（NpcEntrance 写 0.65/0），过渡 " + TRANSITION + "s");
        log.Add("");

        Directory.CreateDirectory(ANIM_DIR);
        var dirs = Directory.GetDirectories(NEW_ROOT).OrderBy(d => d).ToArray();
        int done = 0, skipped = 0;

        foreach (var dir in dirs)
        {
            string ch = Path.GetFileName(dir);
            string walkFbx = Path.Combine(dir, "Walk.fbx").Replace('\\', '/');
            string ctrlPath = ANIM_DIR + "/" + ch + "_Idle.controller";
            if (!File.Exists(walkFbx)) { continue; }                    // 没有 Walk 素材的角色不碰
            if (!File.Exists(ctrlPath))
            {
                log.Add("★ " + ch + "：没有 " + Path.GetFileName(ctrlPath) + "（先跑 Game角色替换 生成）——跳过");
                skipped++;
                continue;
            }

            log.Add("════ " + ch);
            var loop = EnsureLoopCopy(ch, walkFbx, log);
            if (loop == null) { skipped++; continue; }
            WireController(ch, ctrlPath, loop, log);
            done++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        log.Add("");
        log.Add("合计：接入 " + done + " 个角色，跳过 " + skipped + " 个");
        log.Add("注：场景实例 Animator 指向的就是 <角色>_Idle.controller，无需改场景；编辑模式下角色显示 rest T-pose 正常。");

        Flush(log, REPORT);
    }

    // ------------------------------------------------------------ ① 循环副本
    static AnimationClip EnsureLoopCopy(string ch, string walkFbx, List<string> log)
    {
        var src = AssetDatabase.LoadAllAssetsAtPath(walkFbx).OfType<AnimationClip>()
                      .Where(c => !c.name.StartsWith("__preview__"))
                      .OrderByDescending(c => c.length).FirstOrDefault();
        if (src == null) { log.Add("   ★ Walk.fbx 里没有剪辑（导入没成功？）"); return null; }

        string clipPath = ANIM_DIR + "/" + ch + "_Walk.anim";
        var loop = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        if (loop == null)
        {
            loop = Object.Instantiate(src);
            loop.name = ch + "_Walk";
            AssetDatabase.CreateAsset(loop, clipPath);
            log.Add(string.Format("   + {0}_Walk.anim（源 Walk.fbx 的“{1}” {2:0.00}s，GUID 新建）", ch, src.name, src.length));
        }
        else
        {
            EditorUtility.CopySerialized(src, loop);                    // 原地刷新，GUID 不变
            loop.name = ch + "_Walk";
            log.Add(string.Format("   = {0}_Walk.anim 已存在，原地刷新（源“{1}” {2:0.00}s）", ch, src.name, src.length));
        }

        var st = AnimationUtility.GetAnimationClipSettings(loop);
        st.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(loop, st);
        EditorUtility.SetDirty(loop);

        var range = HorizontalDrift(src);
        if (range > 0.25f)
            log.Add(string.Format("   ★ 警告：剪辑根位移约 {0:0.00}m/圈（Mixamo 下载没勾 In Place？）——播起来会自己往前蹭，建议重下", range));
        else
            log.Add(string.Format("   根位移体检：水平约 {0:0.00}m/圈 ✓（原地走）", range));
        return loop;
    }

    // 髋骨水平（x/z）关键帧最大跨度——原地走的应在 0.1m 量级，带根位移的每圈往前 ~1m
    static float HorizontalDrift(AnimationClip clip)
    {
        float max = 0f;
        foreach (var b in AnimationUtility.GetCurveBindings(clip))
        {
            if (!b.path.EndsWith("Hips") || !b.propertyName.StartsWith("m_LocalPosition")) continue;
            if (b.propertyName.EndsWith(".y")) continue;
            var curve = AnimationUtility.GetEditorCurve(clip, b);
            if (curve == null || curve.keys.Length == 0) continue;
            float mn = float.MaxValue, mx = float.MinValue;
            foreach (var k in curve.keys) { mn = Mathf.Min(mn, k.value); mx = Mathf.Max(mx, k.value); }
            max = Mathf.Max(max, mx - mn);
        }
        return max;
    }

    // ------------------------------------------------------------ ② 控制器接线
    static void WireController(string ch, string ctrlPath, AnimationClip loop, List<string> log)
    {
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
        if (ctrl == null) { log.Add("   ★ 控制器加载失败：" + ctrlPath); return; }
        if (!ctrl.parameters.Any(p => p.name == "Speed"))
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);

        var sm = ctrl.layers[0].stateMachine;
        var idle = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == ch + "_Idle") ?? sm.defaultState;
        if (idle == null) { log.Add("   ★ 找不到 Idle 状态"); return; }
        var walk = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == ch + "_Walk" || s.name == "Walk");

        bool addedState = false;
        if (walk == null)
        {
            // AnimatorState 没有 position——节点坐标在 ChildAnimatorState 上，用 AddState 的带坐标重载摆到 Idle 右边
            var idleChild = sm.states.FirstOrDefault(s => s.state == idle);
            Vector3 walkPos = (idleChild.state != null ? idleChild.position : Vector3.zero) + new Vector3(250, 0, 0);
            walk = sm.AddState(ch + "_Walk", walkPos);
            walk.writeDefaultValues = true;
            addedState = true;
        }
        walk.motion = loop;
        sm.defaultState = idle;                                         // 默认待机，别让 Walk 抢默认

        // 过渡重建（幂等：先清旧的这两条再加）
        idle.transitions = idle.transitions.Where(t => t.destinationState != walk).ToArray();
        walk.transitions = walk.transitions.Where(t => t.destinationState != idle).ToArray();

        var toWalk = idle.AddTransition(walk);
        toWalk.hasExitTime = false;
        toWalk.duration = TRANSITION;
        toWalk.AddCondition(AnimatorConditionMode.Greater, WALK_THRESHOLD, "Speed");

        var toIdle = walk.AddTransition(idle);
        toIdle.hasExitTime = false;
        toIdle.duration = TRANSITION;
        toIdle.AddCondition(AnimatorConditionMode.Less, WALK_THRESHOLD, "Speed");

        EditorUtility.SetDirty(ctrl);
        log.Add("   " + (addedState ? "+" : "=") + " 控制器：状态 Idle / " + walk.name + "，过渡 I→W（Speed>" + WALK_THRESHOLD + "）、W→I（Speed<" + WALK_THRESHOLD + "），默认=Idle");
    }

    // ------------------------------------------------------------ 报告 + 复核
    static void Flush(List<string> log, string path)
    {
        // 写报告前复核一遍落盘结果（照 _带动画模型材质 的口径：看事实，不看返回值）
        log.Add("");
        log.Add("── 复核（重新从磁盘读）");
        var dirs = Directory.GetDirectories(NEW_ROOT).OrderBy(d => d).ToArray();
        foreach (var dir in dirs)
        {
            string ch = Path.GetFileName(dir);
            string ctrlPath = ANIM_DIR + "/" + ch + "_Idle.controller";
            if (!File.Exists(Path.Combine(dir, "Walk.fbx").Replace('\\', '/')) || !File.Exists(ctrlPath)) continue;

            var loop = AssetDatabase.LoadAssetAtPath<AnimationClip>(ANIM_DIR + "/" + ch + "_Walk.anim");
            bool isLoop = loop != null && AnimationUtility.GetAnimationClipSettings(loop).loopTime;
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
            var walk = ctrl != null ? ctrl.layers[0].stateMachine.states.Select(s => s.state)
                          .FirstOrDefault(s => s.name == ch + "_Walk" || s.name == "Walk") : null;
            int conds = 0;
            if (walk != null)
                foreach (var t in walk.transitions) conds += t.conditions.Length;
            log.Add(string.Format("  {0}：Walk.anim 循环={1}；控制器 Walk 状态={2}（Motion={3}，过渡条件 {4} 条）",
                ch,
                loop == null ? "无副本" : (isLoop ? "✓" : "★否"),
                walk != null ? "✓" : "★无",
                walk != null && walk.motion != null ? walk.motion.name : "★空",
                conds));
        }

        Directory.CreateDirectory("Assets/assets/_报告");
        File.WriteAllText(path, string.Join("\n", log.ToArray()));
        AssetDatabase.Refresh();
        Debug.Log("[NpcWalkSetup] 报告：" + path);
    }
}

// 丢 Assets/_npcwalk_trigger.txt → 下次刷新/重编译后自动跑一次
[InitializeOnLoad]
public static class NpcWalkSetupTrigger
{
    const string Trigger = "Assets/_npcwalk_trigger.txt";
    const string ErrFile = "../额外文件/错误_NPC走路接入.txt";

    static NpcWalkSetupTrigger()
    {
        if (!File.Exists(Trigger)) return;
        EditorApplication.delayCall += () =>
        {
            try
            {
                if (File.Exists(Trigger)) File.Delete(Trigger);
                if (File.Exists(Trigger + ".meta")) File.Delete(Trigger + ".meta");
                NpcWalkSetup.Run();
            }
            catch (System.Exception e)
            {
                Directory.CreateDirectory("../额外文件");
                File.WriteAllText(ErrFile, e.ToString());
                Debug.LogError("[NpcWalkSetup] 失败: " + e);
            }
        };
    }
}
