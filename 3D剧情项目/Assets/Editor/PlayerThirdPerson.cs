// ============================================================================
// 主角：第三人称（建动作控制器 + 配镜头 + 自检）
//
// 背景：Game 场景里的 Player_徐夏 原本是第一人称（相机挂在眼睛上、身体只投影看不见）。
//       现在要「把主角加上」并改成第三人称：镜头在身后看着徐夏。
//
// 分两件事：
//   ① 动画：徐夏自己那套带动画模型（Idle / Walk / Slow Run / Texting）做一个控制器
//      —— 同源、零重定向，混合树 Speed：0 待机 / 1 行走 / 2 慢跑；Phone 开关播拿手机。
//   ② 镜头：FirstPersonController（脚本已加第三人称模式）设成 thirdPerson=true，
//      相机绕角色支点（tpHeight）在身后 tpDistance 米，遇墙自动拉近；
//      主角模型挂到 Outline 层（跟 NPC 一样有描边）、所有部件正常显示。
//
// 菜单：Tools/干预项目/主角第三人称/① 配置（建控制器 + 改场景）
//       Tools/干预项目/主角第三人称/② 运行自检（进 Play 量镜头距离/能不能看见主角）
// 报告：Assets/assets/_报告/_主角第三人称.txt / _主角第三人称自检.txt
// ============================================================================
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PlayerThirdPerson
{
    const string SCENE = "Assets/Scenes/Game.unity";
    const string MODEL_DIR = "Assets/assets/03_动作_Animation/带动画模型/徐夏";
    const string ANIM_DIR = "Assets/assets/03_动作_Animation/Animators/带动画模型";
    const string CTRL = ANIM_DIR + "/徐夏_第三人称.controller";
    const string REPORT = "Assets/assets/_报告/_主角第三人称.txt";
    const string SMOKE_REPORT = "Assets/assets/_报告/_主角第三人称自检.txt";
    const string PLAYER = "Player_徐夏";
    const int OUTLINE_LAYER = 8;
    public const string SMOKE_STATE = "../额外文件/_player3p_smoke.state";

    // ============================================================ 循环副本
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
        return c;
    }

    // ============================================================ 控制器
    static AnimatorController BuildController(List<string> log)
    {
        Directory.CreateDirectory(ANIM_DIR);
        var idle = LoopCopy(MODEL_DIR + "/Idle.fbx", ANIM_DIR + "/徐夏_Idle.anim");
        var walk = LoopCopy(MODEL_DIR + "/Walk.fbx", ANIM_DIR + "/徐夏_Walk.anim");
        var run = LoopCopy(MODEL_DIR + "/Slow Run.fbx", ANIM_DIR + "/徐夏_SlowRun.anim");
        var phone = LoopCopy(MODEL_DIR + "/Texting.fbx", ANIM_DIR + "/徐夏_Texting.anim");
        if (idle == null || walk == null) { log.Add("   ★ 徐夏的 Idle/Walk 剪辑缺失，控制器没建"); return null; }

        AssetDatabase.DeleteAsset(CTRL);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(CTRL);
        ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
        ctrl.AddParameter("Phone", AnimatorControllerParameterType.Bool);
        ctrl.AddParameter("TakePhone", AnimatorControllerParameterType.Trigger);   // 剧情会 SetTrigger，参数得在

        var sm = ctrl.layers[0].stateMachine;

        // 混合树：Speed 0 → Idle / 1 → Walk / 2 → Slow Run
        var tree = new BlendTree
        {
            name = "Locomotion",
            blendType = BlendTreeType.Simple1D,
            blendParameter = "Speed",
            useAutomaticThresholds = false
        };
        AssetDatabase.AddObjectToAsset(tree, ctrl);
        var kids = new List<ChildMotion>
        {
            new ChildMotion { motion = idle, threshold = 0f, timeScale = 1f, directBlendParameter = "Speed" },
            new ChildMotion { motion = walk, threshold = 1f, timeScale = 1f, directBlendParameter = "Speed" }
        };
        if (run != null) kids.Add(new ChildMotion { motion = run, threshold = 2f, timeScale = 1f, directBlendParameter = "Speed" });
        tree.children = kids.ToArray();

        var loco = sm.AddState("Locomotion");
        loco.motion = tree;
        loco.writeDefaultValues = true;
        sm.defaultState = loco;

        // 拿手机：Phone 开关
        if (phone != null)
        {
            var ph = sm.AddState("Phone");
            ph.motion = phone;
            ph.writeDefaultValues = true;

            var toPhone = sm.AddAnyStateTransition(ph);
            toPhone.hasExitTime = false;
            toPhone.duration = 0.2f;
            toPhone.canTransitionToSelf = false;
            toPhone.AddCondition(AnimatorConditionMode.If, 0f, "Phone");

            var back = ph.AddTransition(loco);
            back.hasExitTime = false;
            back.duration = 0.2f;
            back.AddCondition(AnimatorConditionMode.IfNot, 0f, "Phone");
        }

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        log.Add(string.Format("   控制器 {0}：混合树 Speed 0=Idle / 1=Walk / 2=SlowRun{1}",
            Path.GetFileName(CTRL), phone != null ? "，另有 Phone→Texting 状态" : ""));
        return ctrl;
    }

    // ============================================================ ① 配置场景
    [MenuItem("Tools/干预项目/主角第三人称/① 配置（建控制器 + 改场景）", false, 140)]
    public static void Setup()
    {
        var log = new List<string>();
        log.Add("主角第三人称配置  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("");
        if (!OpenGame(log)) { Flush(log, REPORT); return; }

        log.Add("── 1) 动作控制器（徐夏自己的剪辑，零重定向）");
        var ctrl = BuildController(log);
        if (ctrl == null) { Flush(log, REPORT); return; }

        log.Add("");
        log.Add("── 2) 场景里的 Player_徐夏");
        var sc = SceneManager.GetActiveScene();
        GameObject player = null;
        foreach (var r in sc.GetRootGameObjects())
        {
            var t = r.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == PLAYER);
            if (t != null) { player = t.gameObject; break; }
        }
        if (player == null) { log.Add("   ★ 场景里没有 " + PLAYER); Flush(log, REPORT); return; }

        var fpc = player.GetComponent<FirstPersonController>();
        if (fpc == null) { log.Add("   ★ " + PLAYER + " 上没有 FirstPersonController"); Flush(log, REPORT); return; }

        // 主角模型（带动画模型的实例）
        GameObject model = null;
        Animator modelAnim = null;
        foreach (Transform c in player.transform)
        {
            var src = PrefabUtility.GetCorrespondingObjectFromSource(c.gameObject);
            if (src == null) continue;
            string p = AssetDatabase.GetAssetPath(src);
            if (p.Contains("/带动画模型/")) { model = c.gameObject; modelAnim = model.GetComponentInChildren<Animator>(true); break; }
        }
        if (model == null) { log.Add("   ★ 没找到主角模型（带动画模型实例）"); Flush(log, REPORT); return; }

        // 2a) 模型挂控制器 + 层 → Outline（跟 NPC 一样有描边），全部件可见
        if (modelAnim == null) modelAnim = model.AddComponent<Animator>();
        modelAnim.runtimeAnimatorController = ctrl;
        modelAnim.avatar = null;
        modelAnim.applyRootMotion = false;
        modelAnim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        foreach (var tr in model.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = OUTLINE_LAYER;
        log.Add("   模型 " + model.name + " → Animator 挂 " + Path.GetFileName(CTRL) + "，整棵子树移到 Outline 层（有描边）");

        // 2b) 玩家根上那个旧 Animator 停掉（旧骨架没了，留着会跟新 Animator 抢）
        foreach (var a in player.GetComponents<Animator>())
        {
            a.runtimeAnimatorController = null;
            a.enabled = false;
            log.Add("   根上旧 Animator 已停用");
        }

        // 2c) 控制器 → 第三人称，并把 animator 指向真正驱动骨架的那个
        fpc.thirdPerson = true;
        fpc.tpDistance = 3.4f;
        fpc.tpHeight = 1.45f;
        fpc.tpLookHeight = 1.20f;
        fpc.tpPitchMin = -45f;                        // 镜头最低压到 -45°（往上看）
        fpc.tpPitchMax = 40f;                         // 镜头最高抬到 +40°（往下看）
        fpc.tpKeepAboveGround = true;                 // 贴地保护（不然 -45° 会钻到地底下）
        fpc.tpMinCameraHeight = 0.35f;
        fpc.tpMinDistance = 0.6f;
        fpc.tpCollisionRadius = 0.22f;
        fpc.tpFollowSmooth = 0f;                       // 0 = 硬跟：镜头不带滞后，横向移动/转身时不会跟着晃
        fpc.tpReturnSpeed = 4f;
        fpc.tpBlockMask = ~(1 << OUTLINE_LAYER);      // 挡相机的只有墙/地这类，不含角色所在的 Outline 层
        fpc.turnSpeed = 720f;                         // 角色转向移动方向的速度（度/秒）
        fpc.animator = modelAnim;                     // 剧情 SetBool("Phone") 要打到这个 Animator 上
        fpc.walkSpeed = 1.6f;
        fpc.animSpeedScale = 1.0f;                    // 原速（1.35 试过效果不好，改回）
        fpc.animStartSmooth = 0.02f;                  // 起步/停下的极小缓动（≈0.05s 内到位，几乎瞬时）
        fpc.runSpeed = fpc.walkSpeed;                 // 本作设计是「只走不跑」；想开跑：把 runSpeed 改成 3.2（混合树第 2 档已经在）
        EditorUtility.SetDirty(fpc);

        fpc.ApplyBody();
        fpc.ApplyCameraNear();
        fpc.ApplyFirstPersonParts();                  // thirdPerson=true → 会把所有部件设回正常显示
        log.Add(string.Format("   镜头：距离 {0}m，支点高 {1}m，俯仰 {2}~{3}°（上抬 {3}°/下压 {2}°），贴地保护={4}（最低离地 {5}m），走 {6}m/s",
            fpc.tpDistance, fpc.tpHeight, fpc.tpPitchMin, fpc.tpPitchMax, fpc.tpKeepAboveGround, fpc.tpMinCameraHeight, fpc.walkSpeed));

        EditorSceneManager.MarkSceneDirty(sc);
        bool saved = EditorSceneManager.SaveScene(sc);
        log.Add("");
        log.Add("存场景：" + (saved ? "成功 ✓" : "★ 失败"));
        log.Add("");
        log.Add("备注：");
        log.Add("  · 操作：鼠标 = 只转镜头（角色不跟）；WASD = 相对镜头移动，角色自己转向移动方向");
        log.Add("  · 想切回第一人称：把 Player_徐夏 上 FirstPersonController 的 thirdPerson 去掉勾（自动变回“镜头跟朝向”）");
        log.Add("  · 想开跑：runSpeed 改成 3.2 即可（混合树第 2 档 = 徐夏的 Slow Run）");
        log.Add("  · 镜头手感：tpDistance（距离）/ tpHeight（支点高）/ tpLookHeight（看多高）/ turnSpeed（转身快慢）");
        log.Add("  · 剧情对话锁人的接口没变（SetLocked / SetCursorLocked），门口传送也没动");
        Flush(log, REPORT);
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

    // ============================================================ ② 运行自检（运行时驱动）
    // 真正的检测逻辑在 Assets/Scripts/Player/PlayerThirdPersonSmokeDriver.cs（协程，用真游戏帧）；
    // 这里只负责：临时挂驱动 → 进 Play → 等它写完报告 → 退出 Play、删掉临时对象。
    // 状态全靠【文件】记（进/退 Play 会域重载，静态变量会被清空 —— 之前就因为这个没人收尾）
    const string DRIVER_FLAG = "../额外文件/_player3p_done";
    const string SMOKE_STATE2 = "../额外文件/_player3p_smoke.state";
    static GameObject _tempDriver;
    static int _diagLeft;

    [MenuItem("Tools/干预项目/主角第三人称/② 运行自检（进 Play 看镜头与主角）", false, 141)]
    public static void Smoke()
    {
        var log = new List<string>();
        log.Add("主角第三人称自检（启动）  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("");
        if (!OpenGame(log)) { Flush(log, REPORT); return; }
        if (Application.isPlaying) { log.Add("★ 已经在 Play 模式，请先退出"); Flush(log, REPORT); return; }

        // ★ 不动场景（不新建对象、不 MarkSceneDirty）：场景脏着进 Play 会弹保存对话框，自动化会卡死
        if (File.Exists(DRIVER_FLAG)) File.Delete(DRIVER_FLAG);
        if (File.Exists("../额外文件/_player3p_run")) File.Delete("../额外文件/_player3p_run");
        Directory.CreateDirectory("../额外文件");
        File.WriteAllText("../额外文件/_player3p_run", System.DateTime.Now.ToString("o"));
        File.WriteAllText(SMOKE_STATE2, System.DateTime.Now.ToString("o"));
        _diagLeft = 5;
        EditorApplication.isPlaying = true;
        Debug.Log("[PlayerThirdPerson] 自检启动（跑完自动退出 Play）");
    }

    public static bool SmokeRequested() { return File.Exists(SMOKE_STATE2); }
    public static void BeginTick() { }             // 兼容旧调用

    public static void PollSmoke()
    {
        if (!File.Exists(SMOKE_STATE2)) return;

        // 诊断：进 Play 后确认驱动对象真的在（前几次打印）
        if (Application.isPlaying && _diagLeft > 0)
        {
            _diagLeft--;
            var d = Object.FindObjectOfType<PlayerThirdPersonSmokeDriver>();
            Debug.Log("[PlayerThirdPerson] 自检中…驱动对象" + (d != null ? "在 ✓" : "★ 不在（没进场景？）"));
        }

        bool done = File.Exists(DRIVER_FLAG);
        double age = (System.DateTime.Now - File.GetLastWriteTime(SMOKE_STATE2)).TotalSeconds;
        if (!done && age < 600) return;

        if (!done) Debug.LogWarning("[PlayerThirdPerson] 自检超时（600s），强制收尾");
        if (File.Exists(DRIVER_FLAG)) File.Delete(DRIVER_FLAG);
        if (File.Exists(SMOKE_STATE2)) File.Delete(SMOKE_STATE2);
        var tmp = Object.FindObjectOfType<PlayerThirdPersonSmokeDriver>();
        if (tmp != null) Object.DestroyImmediate(tmp.gameObject);
        else if (_tempDriver != null) Object.DestroyImmediate(_tempDriver);
        _tempDriver = null;
        if (Application.isPlaying) EditorApplication.isPlaying = false;
        Debug.Log("[PlayerThirdPerson] 自检收尾完成");
    }

    static void Flush(List<string> log, string path)
    {
        Directory.CreateDirectory("Assets/assets/_报告");
        File.WriteAllText(path, string.Join("\n", log.ToArray()));
        AssetDatabase.Refresh();
        Debug.Log("[PlayerThirdPerson] 报告：" + path);
    }
}

[InitializeOnLoad]
static class PlayerThirdPersonTrigger
{
    const string T1 = "Assets/_player3p_trigger.txt";
    const string T2 = "Assets/_player3psmoke_trigger.txt";
    static PlayerThirdPersonTrigger()
    {
        // ★ 常驻轮询：丢触发器文件就跑，不用改脚本、不用点回 Unity 窗口（域重载/焦点都不靠）
        EditorApplication.update -= Poll;
        EditorApplication.update += Poll;
    }

    static double _next;

    static void Poll()
    {
        PlayerThirdPerson.PollSmoke();            // 自检收尾（无论焦点/域重载，每帧都查）
        if (EditorApplication.timeSinceStartup < _next) return;
        _next = EditorApplication.timeSinceStartup + 0.5;
        if (Application.isPlaying) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

        bool go = File.Exists(T1), smoke = File.Exists(T2);
        if (!go && !smoke) return;
        try
        {
            if (go) File.Delete(T1);
            if (smoke) File.Delete(T2);
            if (smoke) PlayerThirdPerson.Smoke(); else PlayerThirdPerson.Setup();
        }
        catch (System.Exception e)
        {
            Debug.LogError("[PlayerThirdPerson] " + e);
            Directory.CreateDirectory("../额外文件");
            File.WriteAllText("../额外文件/错误_主角第三人称.txt", e.ToString());
        }
    }
}
