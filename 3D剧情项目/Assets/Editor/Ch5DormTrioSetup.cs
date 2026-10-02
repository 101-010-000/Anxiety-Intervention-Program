// 第5章宿舍三人组接线（幂等，2026-10-01）：
//   第五章角色 容器下的 陆宣雨_可动_坐着 / 舍友A / 舍友B 需要"开场坐着、剧情到点起身走路"：
//     · SitHere（Start 置 Sitting=true → 控制器 Sit 态；NpcEntrance.SetWalk 起步清 Sitting → Walk 态）
//     · 舍友A/B 是 Sitting Idle.fbx 裸实例（无动画机）→ 补根 Animator（<角色>_Idle.controller）；
//       同源骨架 Generic 剪辑按路径播放，无需 Avatar（陆宣雨_可动_坐着 同款，avatar 本来就是空）
//     · 控制器需含 Sitting 参数 + Sit 状态（SitSetup 范式：AnyState→Sit(If)，Sit→默认态(IfNot)），
//       缺了就补（Sit 剪辑 = <角色>_Sit.anim，没有就从 Sitting Idle.fbx 生成循环副本）
//   ⚠ execute_code 动态加的组件不会真正落盘（2026-10-01 实测：SaveOpenScenes=True 但 YAML 无此组件），
//     必须走 Editor 脚本 + 菜单/触发器，所以有这个工具。
//   菜单：Tools/干预项目/第5章宿舍三人组/接线（幂等）
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class Ch5DormTrioSetup
{
    const string ANIM_DIR = "Assets/assets/03_动作_Animation/Animators/带动画模型";
    const string MODEL_DIR = "Assets/assets/03_动作_Animation/带动画模型";
    const string PARAM = "Sitting";

    [MenuItem("Tools/干预项目/第5章宿舍三人组/接线（幂等）", false, 160)]
    public static void Run()
    {
        var log = new System.Collections.Generic.List<string>();
        var dorm = GameObject.Find("Loc_宿舍");
        if (dorm == null) { Debug.LogError("[Ch5DormTrio] 找不到 Loc_宿舍"); return; }
        Transform cont = null;
        foreach (Transform t in dorm.GetComponentsInChildren<Transform>(true))
            if (t.name == "第五章角色" && t.parent != null && t.parent.name == "Loc_宿舍") { cont = t; break; }
        if (cont == null) { Debug.LogError("[Ch5DormTrio] Loc_宿舍 下没有 第五章角色 容器"); return; }

        foreach (Transform t in cont)
        {
            if (t.name != "陆宣雨_可动_坐着" && t.name != "舍友A" && t.name != "舍友B") continue;
            // ★ 陆宣雨 2026-10-01 用户定稿：第5章全程站着，不接 SitHere（舍友A/B 照旧坐姿）
            bool wantsSit = t.name != "陆宣雨_可动_坐着";
            log.Add("════ " + t.name + (wantsSit ? "" : "（站着，不接坐姿）"));

            // ① 根 Animator（活着的那个即可）
            Animator live = t.GetComponentsInChildren<Animator>(true)
                .FirstOrDefault(a => a.enabled && a.runtimeAnimatorController != null);
            if (live == null)
            {
                var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ANIM_DIR + "/" + t.name + "_Idle.controller");
                if (ctrl == null) { log.Add("   ★ 缺控制器 " + t.name + "_Idle.controller——先跑 Game角色替换"); continue; }
                live = t.gameObject.AddComponent<Animator>();
                live.runtimeAnimatorController = ctrl;
                live.avatar = null;     // Generic 剪辑按骨骼路径播放，无需 Avatar
                log.Add("   + 根 Animator（" + ctrl.name + "，avatar=null）");
            }
            else log.Add("   = 活动画机已在（" + live.runtimeAnimatorController.name + "）");

            // ② 控制器的 Sitting 参数 + Sit 状态（缺才补）
            var ac = live.runtimeAnimatorController as AnimatorController;
            if (ac != null)
            {
                if (!ac.parameters.Any(p => p.name == PARAM))
                {
                    ac.AddParameter(PARAM, AnimatorControllerParameterType.Bool);
                    EditorUtility.SetDirty(ac);
                    log.Add("   + 控制器补 Sitting 参数");
                }
                var sm = ac.layers[0].stateMachine;
                var sit = sm.states.FirstOrDefault(s => s.state.name == "Sit").state;
                if (sit == null)
                {
                    var clipPath = ANIM_DIR + "/" + t.name + "_Sit.anim";
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                    if (clip == null) clip = EnsureSitClip(t.name, log);
                    if (clip != null)
                    {
                        sit = sm.AddState("Sit");
                        sit.writeDefaultValues = true;
                        sit.motion = clip;
                        var to = sm.AddAnyStateTransition(sit);
                        to.hasExitTime = false; to.duration = 0.15f; to.canTransitionToSelf = false;
                        to.AddCondition(AnimatorConditionMode.If, 0f, PARAM);
                        var back = sit.AddTransition(sm.defaultState);
                        back.hasExitTime = false; back.duration = 0.15f;
                        back.AddCondition(AnimatorConditionMode.IfNot, 0f, PARAM);
                        EditorUtility.SetDirty(ac);
                        log.Add("   + 控制器补 Sit 状态（双向过渡）");
                    }
                }
                else log.Add("   = Sit 状态已在");
            }

            // ③ SitHere（Start 置 Sitting=true）。
            //   ⚠ 先清 missing-script 占位：并发会话覆写场景后 SitHere 会变坏引用（GetComponent<SitHere>=null），
            //   直接 AddComponent 会在坏引用旁边再挂一个，越积越多
            if (!wantsSit)
            {
                var old = t.GetComponent<SitHere>();
                if (old != null) { Object.DestroyImmediate(old); log.Add("   - 移除 SitHere（站着）"); }
                else log.Add("   = 无 SitHere（站着，符合定稿）");
                continue;
            }
            int broken = 0;
            var comps = t.GetComponents<Component>();
            for (int i = comps.Length - 1; i >= 0; i--)
                if (comps[i] == null) { Object.DestroyImmediate(comps[i], true); broken++; }
            if (broken > 0) log.Add("   - 清掉 missing-script 坏引用 ×" + broken);
            if (t.GetComponent<SitHere>() == null)
            {
                t.gameObject.AddComponent<SitHere>();
                log.Add("   + SitHere");
            }
            else log.Add("   = SitHere 已在");
        }

        AssetDatabase.SaveAssets();
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        log.Add("场景已保存: " + scene.path);
        Debug.Log("[Ch5DormTrio]\n" + string.Join("\n", log.ToArray()));
        System.IO.Directory.CreateDirectory("Assets/assets/_报告");
        System.IO.File.WriteAllText("Assets/assets/_报告/_第5章宿舍三人组.txt",
            "第5章宿舍三人组接线  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n" + string.Join("\n", log.ToArray()));
    }

    // Sitting Idle.fbx 里的剪辑 → 循环副本 <角色>_Sit.anim（照 NpcWalkSetup 的循环副本套路）
    static AnimationClip EnsureSitClip(string ch, System.Collections.Generic.List<string> log)
    {
        var fbx = MODEL_DIR + "/" + ch + "/Sitting Idle.fbx";
        AnimationClip src = null;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbx))
        {
            var c = o as AnimationClip;
            if (c == null || c.name.StartsWith("__preview__")) continue;
            if (src == null || c.length > src.length) src = c;
        }
        if (src == null) { log.Add("   ★ " + fbx + " 里没有剪辑"); return null; }
        var path = ANIM_DIR + "/" + ch + "_Sit.anim";
        var loop = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (loop == null)
        {
            loop = Object.Instantiate(src);
            loop.name = ch + "_Sit";
            AssetDatabase.CreateAsset(loop, path);
            log.Add("   + " + ch + "_Sit.anim 新建（源 " + src.name + " " + src.length.ToString("F2") + "s）");
        }
        else
        {
            EditorUtility.CopySerialized(src, loop);
            loop.name = ch + "_Sit";
            log.Add("   = " + ch + "_Sit.anim 已在，原地刷新");
        }
        var st = AnimationUtility.GetAnimationClipSettings(loop);
        st.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(loop, st);
        EditorUtility.SetDirty(loop);
        return loop;
    }
}
