// 角色资源预览场景：给每个角色挂一个【不一样】的动画，方便一次看全部动作。
//
// 用法：菜单 Tools/干预项目/角色预览场景：每个角色挂一个动画
// 输出：Assets/Scenes/角色资源预览场景.unity
//       + 每个角色一个 controller（assets/03_动作_Animation/Animators/预览/<角色>.controller）
//       + 循环用的剪辑副本（assets/03_动作_Animation/预览Clip/*.anim）
//       + 报告 assets/_报告/_角色动画预览.txt
//
// 设计要点：
//   · 9 个角色 ↔ 9 个动画，按名字排序后一一对应；动画多于角色就轮流（重复不重要）
//   · 角色身上的 Animator/Avatar 是 prefab 自带的（人形 Avatar），工具只换 m_Controller，
//     所以动作会自动重定向到各自身形上
//   · ★ 不改原动画 FBX 的导入设置：一次性动作（拿手机 / Walk）单独做一份 loopTime=true 的
//     .anim 副本给预览用 —— 直接改 FBX 的 loopTime 会影响游戏里"拿手机"这种只播一次的动作
//   · applyRootMotion 保持 false：否则走/跑会把角色带跑偏，看不出动作本身

using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public static class AnimPreviewSetup
{
    const string SCENE      = "Assets/Scenes/角色资源预览场景.unity";
    const string ANIM_DIR   = "Assets/assets/03_动作_Animation/动画";
    const string CTRL_DIR   = "Assets/assets/03_动作_Animation/Animators/预览";
    const string CLIP_DIR   = "Assets/assets/03_动作_Animation/预览Clip";
    const string FONT_PATH  = "Assets/assets/05_UI/字体_Font/中文_Deng.ttf";
    const string REPORT     = "Assets/assets/_报告/_角色动画预览.txt";

    const bool   ADD_LABEL  = true;    // 每个角色头顶加一行动画名（看不清就设 false 再跑一次）
    const float  LABEL_Y    = 2.05f;

    [MenuItem("Tools/干预项目/角色预览场景：每个角色挂一个动画", false, 40)]
    public static void Run() => RunInternal(false);

    public static void RunFromTrigger() => RunInternal(true);

    static void RunInternal(bool fromTrigger)
    {
        var log = new List<string>();
        log.Add("角色动画预览布置  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("");

        if (!File.Exists(SCENE)) { Debug.LogError("[AnimPreviewSetup] 找不到 " + SCENE); return; }
        var cur = SceneManager.GetActiveScene();
        if (cur.path != SCENE)
        {
            // 触发器只会在这个预览场景上动手：先切过去（批处理/触发器模式下不问"要不要存"，
            // 免得把编辑器卡在弹窗上；正常跑菜单时才会问）
            bool ask = !fromTrigger && !Application.isBatchMode;
            if (ask && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(SCENE, OpenSceneMode.Single);
        }
        var scene = SceneManager.GetActiveScene();

        // ---------- 1) 收集动画剪辑（按 FBX 文件名排序，保证每次一样）----------
        var sources = new List<KeyValuePair<string, AnimationClip>>();
        if (!Directory.Exists(ANIM_DIR)) { log.Add("★ 没有动画目录 " + ANIM_DIR); Write(log); return; }
        foreach (var fbx in Directory.GetFiles(ANIM_DIR, "*.fbx").OrderBy(p => Path.GetFileName(p)))
        {
            string path = fbx.Replace('\\', '/');
            string baseName = Path.GetFileNameWithoutExtension(path);
            var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                            .Where(c => !c.name.StartsWith("__preview__")).ToList();
            if (clips.Count == 0) { log.Add("  ★ " + baseName + "：没有剪辑，跳过"); continue; }
            // 一个 FBX 里有多个 take 的话全收，名字带上 take 名
            foreach (var c in clips)
                sources.Add(new KeyValuePair<string, AnimationClip>(
                    clips.Count > 1 ? baseName + "/" + c.name : baseName, c));
        }
        if (sources.Count == 0) { log.Add("★ 一个动画剪辑都没找到"); Write(log); return; }
        log.Add("动画剪辑 " + sources.Count + " 个：" + string.Join("、", sources.Select(s => s.Key).ToArray()));

        // ---------- 2) 做循环副本（不动原 FBX）----------
        Directory.CreateDirectory(CLIP_DIR);
        AssetDatabase.Refresh();
        var loopClips = new List<AnimationClip>();
        foreach (var kv in sources)
        {
            string safe = kv.Key.Replace("/", "_");
            string dst = CLIP_DIR + "/" + safe + ".anim";
            var exist = AssetDatabase.LoadAssetAtPath<AnimationClip>(dst);
            // 已经有就直接用；原剪辑换了名字/删了才会重建
            if (exist != null)
            {
                loopClips.Add(exist);
                continue;
            }
            var copy = Object.Instantiate(kv.Value);
            copy.name = safe;
            var st = AnimationUtility.GetAnimationClipSettings(copy);
            st.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(copy, st);
            AssetDatabase.CreateAsset(copy, dst);
            loopClips.Add(AssetDatabase.LoadAssetAtPath<AnimationClip>(dst));
        }
        log.Add("循环副本 " + loopClips.Count + " 个 → " + CLIP_DIR + "（原 FBX 的 loopTime 一律没动）");
        AssetDatabase.SaveAssets();

        // ---------- 3) 找场景里的角色 ----------
        // ⚠ 一个角色身上往往有【两个】Animator：prefab 根上一个、模型内部一个。
        //   只有"离 SkinnedMeshRenderer 最近的那个"才真的在驱动身体，
        //   所以反过来从每个蒙皮网格往上找最近的 Animator —— 那才是要换 Controller 的。
        var all = Object.FindObjectsOfType<Animator>(true)
                        .Where(a => a != null && a.gameObject.scene == scene).ToList();
        var drivers = new HashSet<Animator>();
        foreach (var smr in Object.FindObjectsOfType<SkinnedMeshRenderer>(true))
        {
            if (smr == null || smr.gameObject.scene != scene) continue;
            var an = smr.GetComponentInParent<Animator>();
            if (an != null) drivers.Add(an);
        }
        if (drivers.Count == 0) drivers = new HashSet<Animator>(all);   // 兜底

        log.Add("场景里 Animator 共 " + all.Count + " 个，其中真正驱动蒙皮的 " + drivers.Count + " 个：");
        foreach (var a in all.OrderBy(x => RootName(x.transform)).ThenBy(x => Depth(x.transform)))
        {
            int skins = a.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length;
            log.Add(string.Format("  {0,-9} {1}  （子树蒙皮 {2} 个）{3}",
                drivers.Contains(a) ? "★驱动" : "·备用", PathOf(a.transform), skins,
                a.runtimeAnimatorController != null ? "  controller=" + a.runtimeAnimatorController.name : "  controller=无"));
        }
        log.Add("");

        // 每个角色根节点只留一个
        var animators = new List<Animator>();
        foreach (var g in drivers.GroupBy(a => RootName(a.transform)).OrderBy(g => g.Key))
        {
            var best = g.OrderByDescending(a => Depth(a.transform)).First();
            animators.Add(best);
            if (g.Count() > 1)
                log.Add("  ⚠ " + g.Key + " 有 " + g.Count() + " 个驱动 Animator，取最内层的：" + PathOf(best.transform));
        }
        log.Add("角色 " + animators.Count + " 个");
        log.Add("");

        // ---------- 4) 一人一个 controller ----------
        Directory.CreateDirectory(CTRL_DIR.Replace('/', Path.DirectorySeparatorChar));
        AssetDatabase.Refresh();

        var font = ADD_LABEL ? AssetDatabase.LoadAssetAtPath<Font>(FONT_PATH) : null;
        if (ADD_LABEL && font == null) log.Add("  ★ 找不到中文字体 " + FONT_PATH + "，标签会缺字");

        var cam = Object.FindObjectOfType<Camera>();
        var used = new List<string>();
        for (int i = 0; i < animators.Count; i++)
        {
            var an = animators[i];
            var kv = sources[i % sources.Count];
            var clip = loopClips[i % loopClips.Count];
            string charName = RootName(an.transform);

            string ctrlPath = CTRL_DIR + "/" + charName + ".controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath) != null)
                AssetDatabase.DeleteAsset(ctrlPath);
            AssetDatabase.Refresh();

            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
            var sm = ctrl.layers[0].stateMachine;
            var st = sm.AddState(clip.name);
            st.motion = clip;
            st.writeDefaultValues = true;
            sm.defaultState = st;

            an.runtimeAnimatorController = ctrl;
            an.applyRootMotion = false;
            an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            EditorUtility.SetDirty(an);
            used.Add(charName + " → " + kv.Key);

            if (ADD_LABEL) MakeLabel(an.transform.root, clip.name, font, cam);
        }

        log.Add("角色 ↔ 动画 对应：");
        foreach (var u in used) log.Add("  " + u);
        log.Add("");

        MarkLabelNodes(animators, log);
        EditorSceneManager.MarkSceneDirty(scene);
        bool ok = EditorSceneManager.SaveScene(scene);
        log.Add("");
        log.Add("保存场景：" + SCENE + "  " + (ok ? "成功" : "★失败"));
        Write(log);
        Debug.Log("[AnimPreviewSetup] 完成，报告：" + REPORT);
    }

    static int Depth(Transform t)
    {
        int d = 0;
        for (var p = t.parent; p != null; p = p.parent) d++;
        return d;
    }

    static string PathOf(Transform t)
    {
        var sb = new System.Text.StringBuilder(t.name);
        for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
        return sb.ToString();
    }

    static string RootName(Transform t)
    {
        // 取 prefab 实例的根名（角色的名字），不是 Animator 所在子节点的名字
        var root = t;
        while (root.parent != null) root = root.parent;
        return root.name;
    }

    /// 头顶加一行动画名。标签是场景里的普通节点，重复跑会先删掉旧的。
    static void MakeLabel(Transform charT, string clipName, Font font, Camera cam)
    {
        // 先清掉这个角色下所有旧标签（含上一版挂错位置留下的）
        foreach (var old in charT.GetComponentsInChildren<Transform>(true)
                               .Where(t => t != charT && t.name.StartsWith("标签_")).ToList())
            Object.DestroyImmediate(old.gameObject);

        var go = new GameObject("标签_" + clipName);
        go.transform.SetParent(charT, false);
        go.transform.localPosition = new Vector3(0f, LABEL_Y, 0f);

        var tm = go.AddComponent<TextMesh>();
        tm.text = clipName;
        tm.font = font;
        tm.fontSize = 72;
        tm.characterSize = 0.035f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = new Color(0.12f, 0.14f, 0.18f, 1f);
        if (font != null) tm.font = font;

        var mr = go.GetComponent<MeshRenderer>();
        if (font != null) mr.sharedMaterial = font.material;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        // 正对相机（静态摆一次就够，预览场景相机基本不动）
        if (cam != null)
        {
            Vector3 dir = cam.transform.position - go.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 1e-4f) go.transform.rotation = Quaternion.LookRotation(-dir.normalized, Vector3.up);
        }
    }

    static void MarkLabelNodes(List<Animator> animators, List<string> log)
    {
        int n = 0;
        foreach (var t in Object.FindObjectsOfType<Transform>(true))
            if (t != null && t.name.StartsWith("标签_")) n++;
        log.Add("头顶标签 " + n + " 个（应该 = 角色数；不想看就设 ADD_LABEL=false 再跑一次）");
    }

    // ================================================================== 运行自检
    // 步骤跑在 Play 模式的协程里（Assets/Scripts/Game/AnimPreviewSmokeDriver.cs），
    // 编辑器只负责：置标志 → 进 Play → 轮询 Finished → 写报告 → 退出。
    const string SMOKE_REPORT = "Assets/assets/_报告/_角色动画预览自检.txt";
    static bool _smokeActive, _smokeHooked;
    static double _smokeStart;
    static bool _optEnabled;
    static EnterPlayModeOptions _opt;

    [MenuItem("Tools/干预项目/角色预览场景：动画运行自检", false, 41)]
    public static void SmokeTest()
    {
        if (!File.Exists(SCENE)) { Debug.LogError("[AnimPreviewSetup] 找不到 " + SCENE); return; }

        _optEnabled = EditorSettings.enterPlayModeOptionsEnabled;
        _opt = EditorSettings.enterPlayModeOptions;
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;

        EditorSceneManager.OpenScene(SCENE, OpenSceneMode.Single);
        AnimPreviewSmokeDriver.Requested = true;
        AnimPreviewSmokeDriver.Finished = false;
        AnimPreviewSmokeDriver.Lines.Clear();
        AnimPreviewSmokeDriver.Errors.Clear();

        _smokeStart = EditorApplication.timeSinceStartup;
        _smokeActive = true;
        if (!_smokeHooked) { EditorApplication.update += SmokePoll; _smokeHooked = true; }
        Debug.Log("[AnimPreviewSetup] 角色动画运行自检开始");
    }

    static void SmokePoll()
    {
        if (!_smokeActive) return;
        if (EditorApplication.timeSinceStartup - _smokeStart > 240) { SmokeFinish("超时（240 秒）"); return; }
        if (!EditorApplication.isPlaying) { EditorApplication.isPlaying = true; return; }
        if (!AnimPreviewSmokeDriver.Finished) return;
        SmokeFinish(null);
    }

    static void SmokeFinish(string fail)
    {
        _smokeActive = false;
        AnimPreviewSmokeDriver.Requested = false;
        EditorSettings.enterPlayModeOptionsEnabled = _optEnabled;
        EditorSettings.enterPlayModeOptions = _opt;

        var outLines = new List<string>();
        outLines.Add("角色动画预览运行自检（真进 Play 跑一遍）  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        outLines.Add("");
        outLines.AddRange(AnimPreviewSmokeDriver.Lines);
        if (!string.IsNullOrEmpty(fail)) { outLines.Add(""); outLines.Add("★ 中止：" + fail); }
        if (AnimPreviewSmokeDriver.Errors.Count > 0)
        {
            outLines.Add("");
            outLines.Add("运行期报错/异常 " + AnimPreviewSmokeDriver.Errors.Count + " 条：");
            outLines.AddRange(AnimPreviewSmokeDriver.Errors);
        }
        else { outLines.Add(""); outLines.Add("运行期报错：无 ✓"); }

        Directory.CreateDirectory(Path.GetDirectoryName(SMOKE_REPORT).Replace('/', Path.DirectorySeparatorChar));
        File.WriteAllText(SMOKE_REPORT, string.Join("\n", outLines.ToArray()));

        EditorApplication.isPlaying = false;
        Debug.Log("[AnimPreviewSetup] 角色动画运行自检完成，报告：" + SMOKE_REPORT);
    }

    static void Write(List<string> log)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(REPORT).Replace('/', Path.DirectorySeparatorChar));
        log.Add("");
        log.Add("———————————————————————————— 怎么用 ————————————————————————————");
        log.Add("· 直接开 Play 看：9 个角色各播一个动作，标题就是动作名");
        log.Add("· 换某个角色播哪个动作：改本脚本里的配对方式，或者直接在场景里");
        log.Add("  选中该角色的 Animator → Controller 换一个预览 controller");
        log.Add("· 想给某个角色换动作：把 Animators/预览/<角色>.controller 里那个 state 的 Motion 换掉即可");
        log.Add("· 想加新动画：把 FBX 丢进 assets/03_动作_Animation/动画/，再跑一次本工具");
        log.Add("  （工具会给新动画做循环副本；原 FBX 的导入设置一律不动，游戏里的『拿手机』等不受影响）");
        log.Add("· 不想要头顶标签：ADD_LABEL=false 再跑一次");
        File.WriteAllText(REPORT, string.Join("\n", log.ToArray()));
    }

    // ================================================================== 单个角色换动画
    // 只改一个角色的预览动画（不动其他人、不跑整场景重建）。
    // 菜单 Tools/干预项目/角色预览：换某个角色的动画（或丢 _animswap_trigger.txt，
    // 文件内容写「角色名|剪辑名」，例如  徐夏|待机女）。
    const string SWAP_REPORT = "Assets/assets/_报告/_角色动画单换.txt";

    [MenuItem("Tools/干预项目/角色预览：把徐夏换回 待机女（默认）", false, 44)]
    public static void SwapXuXiaDefault() => SwapOne("徐夏", "待机女");

    public static void SwapOne(string charName, string clipKey)
    {
        var log = new List<string>();
        log.Add("预览动画单换  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("目标：" + charName + " → " + clipKey);
        log.Add("");

        if (!File.Exists(SCENE)) { log.Add("★ 找不到场景 " + SCENE); WriteSwap(log); return; }
        if (SceneManager.GetActiveScene().path != SCENE)
            EditorSceneManager.OpenScene(SCENE, OpenSceneMode.Single);
        var scene = SceneManager.GetActiveScene();

        // 剪辑（预览专用循环副本）
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(CLIP_DIR + "/" + clipKey + ".anim");
        if (clip == null) { log.Add("★ 找不到循环副本 " + CLIP_DIR + "/" + clipKey + ".anim"); WriteSwap(log); return; }

        // 角色根节点
        GameObject root = null;
        foreach (var t in Object.FindObjectsOfType<Transform>(true))
            if (t != null && t.parent == null && t.name == charName) { root = t.gameObject; break; }
        if (root == null) { log.Add("★ 场景里找不到角色 " + charName); WriteSwap(log); return; }

        // 驱动 Animator = 离蒙皮网格最近的那个（一个角色身上常有两个）
        Animator driver = null; int bestDepth = -1;
        foreach (var a in root.GetComponentsInChildren<Animator>(true))
        {
            bool hasSkin = a.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0;
            if (!hasSkin) continue;
            int d = Depth(a.transform);
            if (d > bestDepth) { bestDepth = d; driver = a; }
        }
        if (driver == null) { log.Add("★ " + charName + " 下面没有驱动 Animator"); WriteSwap(log); return; }

        log.Add("角色下所有 Animator 的换前状态：");
        foreach (var a in root.GetComponentsInChildren<Animator>(true))
            log.Add(string.Format("  {0,-46} controller={1}  avatar={2}",
                PathOf(a.transform), a.runtimeAnimatorController != null ? a.runtimeAnimatorController.name : "无",
                a.avatar != null ? a.avatar.name : "无"));
        log.Add("驱动者（最深、有蒙皮）：" + PathOf(driver.transform));

        // 控制器：有就改里面的 state，没有就建一个
        string ctrlPath = CTRL_DIR + "/" + charName + ".controller";
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
        if (ctrl == null)
        {
            Directory.CreateDirectory(CTRL_DIR);
            AssetDatabase.Refresh();
            ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
        }
        var sm = ctrl.layers[0].stateMachine;
        var st = sm.states.Length > 0 ? sm.states[0].state : sm.AddState(clipKey);
        string oldMotion = st.motion != null ? st.motion.name + " (" + AssetDatabase.GetAssetPath(st.motion) + ")" : "无";
        st.name = clipKey;
        st.motion = clip;
        st.writeDefaultValues = true;
        sm.defaultState = st;
        EditorUtility.SetDirty(ctrl);
        log.Add("控制器 " + ctrlPath + "：" + oldMotion + "  →  " + clipKey + " (" + AssetDatabase.GetAssetPath(clip) + ")");

        // 挂上去（两个 Animator 都指同一个 controller，避免外层残留的旧 controller 抢戏）
        foreach (var a in root.GetComponentsInChildren<Animator>(true))
        {
            if (a.GetComponentInChildren<SkinnedMeshRenderer>(true) == null) continue;
            a.runtimeAnimatorController = ctrl;
            a.applyRootMotion = false;
            a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            EditorUtility.SetDirty(a);
        }

        // 头顶标签
        var font = AssetDatabase.LoadAssetAtPath<Font>(FONT_PATH);
        MakeLabel(root.transform, clipKey, font, Object.FindObjectOfType<Camera>());
        log.Add("头顶标签已改成 标签_" + clipKey);

        EditorSceneManager.MarkSceneDirty(scene);
        bool ok = EditorSceneManager.SaveScene(scene);
        log.Add("保存场景：" + SCENE + "  " + (ok ? "成功" : "★失败"));
        AssetDatabase.SaveAssets();
        WriteSwap(log);
    }

    static void WriteSwap(List<string> log)
    {
        Directory.CreateDirectory("Assets/assets/_报告");
        File.WriteAllText(SWAP_REPORT, string.Join("\n", log.ToArray()));
        Debug.Log("[AnimPreviewSetup] 单换报告：" + SWAP_REPORT);
    }
}

// 工程里存在 Assets/_animpreview_trigger.txt 时自动跑一次。
// ⚠ 新约定是尽量别用自动触发器（会覆盖手改）；这里只是给"懒得开菜单"留个口子。
[InitializeOnLoad]
public static class AnimPreviewSetupTrigger
{
    const string Trigger = "Assets/_animpreview_trigger.txt";
    const string SwapFile = "Assets/_animswap_trigger.txt";
    const string Smoke   = "Assets/_animpreviewsmoke_trigger.txt";
    const string ErrFile = "../额外文件/错误_角色动画预览.txt";

    static AnimPreviewSetupTrigger()
    {
        if (File.Exists(Smoke))
        {
            EditorApplication.delayCall += () =>
            {
                try
                {
                    if (File.Exists(Smoke)) File.Delete(Smoke);
                    if (File.Exists(Smoke + ".meta")) File.Delete(Smoke + ".meta");
                    AnimPreviewSetup.SmokeTest();
                }
                catch (System.Exception e)
                {
                    Directory.CreateDirectory("../额外文件");
                    File.WriteAllText("../额外文件/错误_角色动画预览自检.txt", e.ToString());
                    Debug.LogError("[AnimPreviewSetup] 自检失败: " + e);
                }
            };
            return;
        }

        if (File.Exists(SwapFile))
        {
            EditorApplication.delayCall += () =>
            {
                try
                {
                    string spec = File.ReadAllText(SwapFile).Trim();
                    if (File.Exists(SwapFile)) File.Delete(SwapFile);
                    if (File.Exists(SwapFile + ".meta")) File.Delete(SwapFile + ".meta");
                    var a = spec.Split('|');
                    if (a.Length >= 2 && a[0].Trim().Length > 0) AnimPreviewSetup.SwapOne(a[0].Trim(), a[1].Trim());
                    else Debug.LogError("[AnimPreviewSetup] _animswap_trigger.txt 内容应为「角色名|剪辑名」，例如 徐夏|待机女");
                }
                catch (System.Exception e)
                {
                    Directory.CreateDirectory("../额外文件");
                    File.WriteAllText("../额外文件/错误_角色动画单换.txt", e.ToString());
                    Debug.LogError("[AnimPreviewSetup] 单换失败: " + e);
                }
            };
            return;
        }

        if (!File.Exists(Trigger)) return;
        EditorApplication.delayCall += () =>
        {
            try
            {
                if (File.Exists(Trigger)) File.Delete(Trigger);
                if (File.Exists(Trigger + ".meta")) File.Delete(Trigger + ".meta");
                AnimPreviewSetup.RunFromTrigger();
                if (File.Exists(ErrFile)) File.Delete(ErrFile);
                Debug.Log("[AnimPreviewSetup] 自动布置完成");
            }
            catch (System.Exception e)
            {
                Directory.CreateDirectory("../额外文件");
                File.WriteAllText(ErrFile, e.ToString());
                Debug.LogError("[AnimPreviewSetup] 自动布置失败: " + e);
            }
        };
    }
}
