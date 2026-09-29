// ============================================================================
// 坐姿接入：给「加了 Sitting Idle.fbx」的角色生成循环剪辑 + 在控制器里加 Sit 状态
//
// 背景：剧情里坐下是刚需（第1章教室第三排、第2章宿舍书桌/陆宣雨搬椅子、第3章食堂落座（interact）、
//       第4章宿舍坐下看资料（interact）、第5章回座位）。
//       Mixamo 导回的 `Sitting Idle.fbx` 和 Idle/Walk 一样是 Generic 无 Avatar → 同源零重定向，直接用。
//
// 做的事（每个有 Sitting Idle.fbx 的角色）：
//   ① 生成循环副本 Animators/带动画模型/<角色>_Sit.anim
//   ② 给该角色的控制器加 Bool 参数 `Sitting` + 状态 `Sit`：
//         AnyState → Sit（Sitting == true）；Sit → 默认状态（Sitting == false）
//      控制器：<角色>_Idle.controller（NPC 场景实例在用）与 徐夏_第三人称.controller（主角）
//
// 菜单：Tools/干预项目/坐姿：生成剪辑 + 加 Sit 状态
// 报告：Assets/assets/_报告/_坐姿接入.txt
// ============================================================================
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class SitSetup
{
    const string MODEL_DIR = "Assets/assets/03_动作_Animation/带动画模型";
    const string ANIM_DIR = "Assets/assets/03_动作_Animation/Animators/带动画模型";
    const string SIT_FBX = "Sitting Idle.fbx";
    const string PARAM = "Sitting";
    const string REPORT = "Assets/assets/_报告/_坐姿接入.txt";

    [MenuItem("Tools/干预项目/坐姿：生成剪辑 + 加 Sit 状态", false, 152)]
    public static void Run()
    {
        var log = new List<string>();
        log.Add("坐姿接入  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("");
        Directory.CreateDirectory(ANIM_DIR);

        int okCh = 0, ctrlDone = 0;
        foreach (var dir in Directory.GetDirectories(MODEL_DIR).OrderBy(p => p))
        {
            string ch = Path.GetFileName(dir);
            string fbx = (dir + "/" + SIT_FBX).Replace('\\', '/');
            if (!File.Exists(Path.Combine(dir, SIT_FBX)) && !File.Exists(fbx))
            {
                log.Add("  " + ch + "：没有 " + SIT_FBX + "（跳过）");
                continue;
            }

            // ---- ① 循环剪辑
            string clipPath = ANIM_DIR + "/" + ch + "_Sit.anim";
            var clip = LoopCopy(fbx, clipPath);
            if (clip == null) { log.Add("  ★ " + ch + "：" + SIT_FBX + " 里没有剪辑"); continue; }
            okCh++;
            log.Add(string.Format("  {0}：剪辑 {1}（{2:0.0}s，循环）", ch, Path.GetFileName(clipPath), clip.length));

            // ---- ② 控制器加 Sit 状态
            foreach (var ctrlName in new[] { ch + "_Idle.controller", "徐夏_第三人称.controller" })
            {
                if (ctrlName != "徐夏_第三人称.controller" && ctrlName != ch + "_Idle.controller") continue;
                // 主角控制器只在徐夏那份上改
                if (ctrlName == "徐夏_第三人称.controller" && ch != "徐夏") continue;
                string ctrlPath = ANIM_DIR + "/" + ctrlName;
                var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
                if (ctrl == null) { log.Add("      （没有 " + ctrlName + "，跳过）"); continue; }
                if (AddSitState(ctrl, clip))
                {
                    ctrlDone++;
                    log.Add("      " + ctrlName + "：已加 Sitting 参数 + Sit 状态 ✓");
                }
                else log.Add("      " + ctrlName + "：Sit 状态已存在，只更新了动作 ✓");
            }
        }

        log.Add("");
        log.Add("── ③ 场景里给坐下锚点挂 SitSpot（玩家自动坐下/起身）");
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != "Assets/Scenes/Game.unity")
        {
            var cur = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (cur.isDirty) UnityEditor.SceneManagement.EditorSceneManager.SaveScene(cur);
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Game.unity",
                UnityEditor.SceneManagement.OpenSceneMode.Single);
        }
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        int spots = 0, fixedMode = 0;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var tr in root.GetComponentsInChildren<Transform>(true))
            {
                string n = tr.name;
                bool storyAnchor = n.Contains("落座") || n.Contains("坐下") || n.Contains("回座位");
                bool freeSpot = n.Contains("可坐点");
                if (!storyAnchor && !freeSpot) continue;

                var spot = tr.GetComponent<SitSpot>();
                if (spot == null) { spot = tr.gameObject.AddComponent<SitSpot>(); spots++; log.Add("  挂上 SitSpot：" + PathOf(tr)); }
                else log.Add("  已有 SitSpot：" + PathOf(tr));

                // 剧情锚点 = 锁住自动坐；玩家自己的可坐点 = 按 F 坐（用户 2026-09-29 定稿）
                var want = storyAnchor ? SitMode.剧情锁住自动坐下 : SitMode.按F坐下;
                if (spot.mode != want) { spot.mode = want; fixedMode++; }

                // 自检：椅子找得到吗、点在不在空中
                var seat = spot.seat != null ? spot.seat : spot.FindNearestSeatPublic();
                log.Add(string.Format("     模式={0}  位置={1}  板凳={2}",
                    want, tr.position.ToString("F2"), seat != null ? seat.name : "★ 附近没找到板凳/椅子"));
                if (tr.parent == null)
                    log.Add("     ⚠ 这个可坐点在【场景根】下（不在任何 Loc_* 里），确认是你要的位置");
            }
        if (spots > 0 || fixedMode > 0)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            bool ok = UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            log.Add("  存场景：" + (ok ? "成功 ✓" : "★ 失败"));
        }
        else log.Add("  （没有新增/调整，可能都挂过了）");

        log.Add("");
        log.Add(string.Format("合计：{0} 个角色有坐姿剪辑，改了 {1} 个控制器，新挂座位 {2} 个", okCh, ctrlDone, spots));
        log.Add("下一步（运行时）：座位点上挂 SitSpot（玩家自动坐下/起身）、NPC 用 SitHere，见 Assets/Scripts/Game/SitSpot.cs");
        Flush(log);
    }

    static AnimationClip LoopCopy(string fbx, string dst)
    {
        var exist = AssetDatabase.LoadAssetAtPath<AnimationClip>(dst);
        if (exist != null) return exist;
        var src = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>()
                      .Where(c => !c.name.StartsWith("__preview__"))
                      .OrderByDescending(c => c.length).FirstOrDefault();
        if (src == null) return null;
        var c = Object.Instantiate(src);
        c.name = Path.GetFileNameWithoutExtension(dst);
        var st = AnimationUtility.GetAnimationClipSettings(c);
        st.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(c, st);
        AssetDatabase.CreateAsset(c, dst);
        AssetDatabase.SaveAssets();
        return c;
    }

    static bool AddSitState(AnimatorController ctrl, AnimationClip clip)
    {
        bool isNew = true;
        foreach (var p in ctrl.parameters)
            if (p.name == PARAM) { isNew = false; break; }
        if (isNew) ctrl.AddParameter(PARAM, AnimatorControllerParameterType.Bool);

        var sm = ctrl.layers[0].stateMachine;
        AnimatorState sit = null;
        foreach (var st in sm.states)
            if (st.state.name == "Sit") { sit = st.state; break; }

        if (sit == null)
        {
            sit = sm.AddState("Sit");
            sit.writeDefaultValues = true;
            // AnyState → Sit
            var to = sm.AddAnyStateTransition(sit);
            to.hasExitTime = false;
            to.duration = 0.15f;
            to.canTransitionToSelf = false;
            to.AddCondition(AnimatorConditionMode.If, 0f, PARAM);
            // Sit → 默认状态（Idle / Locomotion）
            var back = sit.AddTransition(sm.defaultState);
            back.hasExitTime = false;
            back.duration = 0.15f;
            back.AddCondition(AnimatorConditionMode.IfNot, 0f, PARAM);
        }
        sit.motion = clip;
        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        return isNew;
    }

    static string PathOf(Transform t)
    {
        var l = new List<string>();
        while (t != null) { l.Add(t.name); t = t.parent; }
        l.Reverse();
        return string.Join("/", l.ToArray());
    }

    static void Flush(List<string> log)
    {
        Directory.CreateDirectory("Assets/assets/_报告");
        File.WriteAllText(REPORT, string.Join("\n", log.ToArray()));
        AssetDatabase.Refresh();
        Debug.Log("[SitSetup] 报告：" + REPORT);
    }
}

// 触发器：常驻轮询（丢 Assets/_sit_trigger.txt 就跑）
[InitializeOnLoad]
static class SitSetupTrigger
{
    const string T = "Assets/_sit_trigger.txt";
    static double _next;
    static SitSetupTrigger()
    {
        EditorApplication.update -= Poll;
        EditorApplication.update += Poll;
    }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < _next) return;
        _next = EditorApplication.timeSinceStartup + 0.5;
        if (Application.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (!File.Exists(T)) return;
        try { File.Delete(T); SitSetup.Run(); }
        catch (System.Exception e)
        {
            Debug.LogError("[SitSetup] " + e);
            Directory.CreateDirectory("../额外文件");
            File.WriteAllText("../额外文件/错误_坐姿接入.txt", e.ToString());
        }
    }
}
