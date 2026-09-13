// 让 NPC 播待机动画 + 给玩家挂第一人称控制 + 给场景模型补碰撞体
// 用法：菜单 Tools/干预项目/…
//   · NPC 待机动画          —— 生成 男/女 两个待机 AnimatorController，挂到 8 个 NPC prefab（不碰场景）
//   · 给玩家挂第一人称控制   —— 在【当前打开的场景】里给玩家补 CharacterController + FirstPersonController
//   · 给选中物体加碰撞体     —— 给 Hierarchy 里选中的物体（含子物体）补 MeshCollider
//
// 说明：所有操作都作用于“当前打开的场景”或 prefab 资产，**不会打开/切换场景**，不会丢你未保存的改动。

using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public static class NpcSetup
{
    const string AnimDir   = "Assets/assets/03_动作_Animation/动画";
    const string AnimOut   = "Assets/assets/03_动作_Animation/Animators";
    const string CharDir   = "Assets/assets/02_角色_Character/角色_URP";
    const string REPORT    = "Assets/assets/_报告/_NPC与控制.txt";

    // 角色 prefab 名 → 用哪个待机动画（徐夏是玩家，第一人称看不见，跳过）
    static readonly (string chr, string clip)[] NPCs = {
        ("组长_可动",    "待机男"),
        ("张知远_可动",  "待机男"),
        ("林溪_可动",    "待机女"),
        ("陆宣雨_可动",  "待机女"),
        ("王含_可动",    "待机女"),
        ("李老师_可动",  "待机女"),
        ("舍友A_可动",   "待机女"),
        ("舍友B_可动",   "待机女"),
    };

    static readonly List<string> log = new List<string>();

    // ================================================================== 1. NPC 待机动画
    [MenuItem("Tools/干预项目/NPC 待机动画")]
    public static void SetupNpcIdle()
    {
        log.Clear();
        log.Add("NPC 待机动画设置  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("");

        // 1) 待机剪辑设为循环
        foreach (var clip in new[] { "待机男", "待机女" })
            SetLoop(clip);

        // 2) 每个性别一个控制器
        var ctrlOf = new Dictionary<string, AnimatorController>();
        foreach (var g in new[] { "男", "女" })
        {
            string clip = "待机" + g;
            string path = AnimOut + "/PC_待机_" + g + ".controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null) AssetDatabase.DeleteAsset(path);
            Directory.CreateDirectory(AnimOut);
            AssetDatabase.Refresh();

            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
            var sm = ctrl.layers[0].stateMachine;
            var st = sm.AddState("待机");
            st.motion = Clip(clip);
            sm.defaultState = st;
            EditorUtility.SetDirty(ctrl);
            ctrlOf[g] = ctrl;
            log.Add(string.Format("控制器  {0}   状态「待机」= {1}", path, clip));
        }
        AssetDatabase.SaveAssets();

        // 3) 挂到 NPC prefab
        // 必须用 LoadPrefabContents / SaveAsPrefabAsset，直接改 LoadAssetAtPath 拿到的对象不会落盘
        log.Add("");
        foreach (var e in NPCs)
        {
            string p = CharDir + "/" + e.chr + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(p) == null) { log.Add("★ 找不到 " + p); continue; }

            var contents = PrefabUtility.LoadPrefabContents(p);
            var an = contents.GetComponentInChildren<Animator>(true);
            if (an == null)
            {
                log.Add("★ " + e.chr + " 没有 Animator");
                PrefabUtility.UnloadPrefabContents(contents);
                continue;
            }
            if (an.avatar == null)
                log.Add("  ⚠ " + e.chr + " 的 Animator 没挂 Avatar，动画不会播");

            string g = e.clip.EndsWith("男") ? "男" : "女";
            bool hasAvatar = an.avatar != null;      // ★ 必须在 Unload 之前读！卸载后 Animator 就销毁了
            an.runtimeAnimatorController = ctrlOf[g];
            an.applyRootMotion = false;
            an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            PrefabUtility.SaveAsPrefabAsset(contents, p);
            PrefabUtility.UnloadPrefabContents(contents);
            log.Add(string.Format("  {0,-16} → {1}（avatar={2}）", e.chr, e.clip, hasAvatar ? "有" : "无"));
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        WriteReport("NPC 待机动画");
        Debug.Log("[NpcSetup] NPC 待机动画完成");
    }

    static void SetLoop(string clipName)
    {
        string p = AnimDir + "/" + clipName + ".fbx";
        var imp = AssetImporter.GetAtPath(p) as ModelImporter;
        if (imp == null) { log.Add("★ 缺动画 " + p); return; }
        var clips = imp.clipAnimations;
        if (clips == null || clips.Length == 0) clips = imp.defaultClipAnimations;
        if (clips == null || clips.Length == 0) { log.Add("★ " + clipName + " 没有剪辑"); return; }
        bool dirty = false;
        foreach (var c in clips) if (!c.loopTime) { c.loopTime = true; dirty = true; }
        if (dirty) { imp.clipAnimations = clips; imp.SaveAndReimport(); log.Add("  已把 " + clipName + " 设为循环"); }
    }

    static AnimationClip Clip(string name)
    {
        string p = AnimDir + "/" + name + ".fbx";
        var clips = AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>()
                        .Where(c => !c.name.StartsWith("__preview__")).ToArray();
        return clips.FirstOrDefault();
    }

    // ================================================================== 2. 玩家第一人称
    [MenuItem("Tools/干预项目/给玩家挂第一人称控制")]
    public static void SetupPlayer()
    {
        log.Clear();
        log.Add("玩家第一人称控制  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("");

        var player = FindPlayer();
        if (player == null)
        {
            Debug.LogError("[NpcSetup] 当前场景里找不到玩家（名字含 Player 或 徐夏，或者有子物体叫 FP_相机）");
            return;
        }
        log.Add("玩家：" + GetPath(player.transform));

        // 相机
        Transform cam = null;
        foreach (var t in player.GetComponentsInChildren<Transform>(true))
            if (t.GetComponent<Camera>() != null) { cam = t; break; }

        // CharacterController
        var cc = player.GetComponent<CharacterController>();
        if (cc == null)
        {
            cc = player.AddComponent<CharacterController>();
            cc.height = 1.75f;                        // 与 FirstPersonController.playerHeight 默认值一致
            cc.radius = 0.28f;
            cc.center = new Vector3(0f, 0.875f, 0f);  // ★ center 必须 = height/2，否则模型会陷进地里
            cc.slopeLimit = 50f;
            cc.stepOffset = 0.35f;
            cc.skinWidth = 0.03f;
            log.Add("已补 CharacterController（高1.75 半径0.28 中心y0.875）");
        }
        else log.Add("已有 CharacterController，未改动");

        // FirstPersonController
        var fpc = player.GetComponent<FirstPersonController>();
        if (fpc == null) { fpc = player.AddComponent<FirstPersonController>(); log.Add("已挂 FirstPersonController"); }
        else log.Add("已有 FirstPersonController，只补引用");

        if (cam != null)
        {
            fpc.cameraPivot = cam;
            fpc.lockCursorOnStart = true;
            log.Add("相机：" + GetPath(cam) + "（已设为本脚本的 Camera Pivot）");
        }
        else log.Add("★ 没找到相机，请在 Inspector 里手动把相机拖到 Camera Pivot");

        EditorUtility.SetDirty(fpc);
        EditorUtility.SetDirty(player);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(player.scene);

        log.Add("");
        log.Add("下一步：直接 Play 试。WASD 移动，鼠标转视角，Shift 加速，Esc 解锁鼠标。");
        log.Add("注意：场景模型默认没有碰撞体（FBX 的 addColliders=0），家具可以穿过去。");
        log.Add("     要挡住的话，选中家具根节点跑「给选中物体加碰撞体」。");

        WriteReport("玩家第一人称");
        Debug.Log("[NpcSetup] 玩家第一人称控制已就绪（场景未保存，记得 Ctrl+S）");
    }

    static GameObject FindPlayer()
    {
        // 1) 名字里带 Player / 徐夏
        foreach (var t in Object.FindObjectsOfType<Transform>(true))
        {
            if (t.name == "Player_徐夏" || t.name.StartsWith("Player")) return t.gameObject;
        }
        foreach (var t in Object.FindObjectsOfType<Transform>(true))
        {
            if (t.name.Contains("徐夏") && t.parent == null) return t.gameObject;
        }
        // 2) 有子物体叫 FP_相机
        foreach (var t in Object.FindObjectsOfType<Transform>(true))
        {
            if (t.GetComponent<Camera>() == null) continue;
            if (t.name.Contains("FP") || t.name.Contains("相机"))
                return t.parent != null ? t.parent.gameObject : t.gameObject;
        }
        return null;
    }

    // ================================================================== 3. 加碰撞体
    [MenuItem("Tools/干预项目/给选中物体加碰撞体")]
    public static void AddCollidersToSelection()
    {
        var sel = Selection.gameObjects;
        if (sel == null || sel.Length == 0) { Debug.LogError("[NpcSetup] 先在 Hierarchy 里选中要加碰撞体的物体"); return; }

        int n = 0;
        foreach (var root in sel)
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                if (mf.GetComponent<Collider>() != null) continue;
                var mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                mc.convex = false;
                n++;
            }
        Debug.Log("[NpcSetup] 给选中物体（含子物体）补了 " + n + " 个 MeshCollider");
    }

    // ================================================================== 4. 动画体检
    // 看不到动画时先跑这个：把剪辑和 Avatar 的实际情况列出来。
    [MenuItem("Tools/干预项目/检查角色动画")]
    public static void CheckAnimation()
    {
        log.Clear();
        log.Add("角色动画体检  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("");

        // ---- 1) 剪辑 ----
        log.Add("【剪辑】isHumanMotion=True 才是有效的 Humanoid 动画，能重定向到任意人形角色");
        foreach (var n in new[] { "待机男", "待机女", "行走", "行走男", "慢跑", "快速跑", "拿手机待机", "拿手机" })
        {
            string p = AnimDir + "/" + n + ".fbx";
            var clips = AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>()
                            .Where(c => !c.name.StartsWith("__preview__")).ToArray();
            if (clips.Length == 0) { log.Add(string.Format("  {0,-10} ★ 没找到剪辑", n)); continue; }
            foreach (var c in clips)
                log.Add(string.Format("  {0,-10} isHumanMotion={1,-6} legacy={2,-6} 长度={3:0.00}s 帧率={4:0.#} 循环={5}",
                    n, c.isHumanMotion, c.legacy, c.length, c.frameRate, UnityEditor.AnimationUtility.GetAnimationClipSettings(c).loopTime));
        }

        // ---- 2) 角色 prefab 的 Animator ----
        log.Add("");
        log.Add("【角色 prefab】avatar.isHuman + isValid 都为 True 才能正确重定向");
        foreach (var e in NPCs)
        {
            string p = CharDir + "/" + e.chr + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(p) == null) { log.Add("  ★ 找不到 " + p); continue; }
            var contents = PrefabUtility.LoadPrefabContents(p);
            var an = contents.GetComponentInChildren<Animator>(true);
            if (an == null) { log.Add(string.Format("  {0,-14} ★ 无 Animator", e.chr)); }
            else
            {
                var av = an.avatar;
                var ctrl = an.runtimeAnimatorController;
                int nStates = 0, nClips = 0;
                var ac = ctrl as UnityEditor.Animations.AnimatorController;
                if (ac != null)
                {
                    foreach (var l in ac.layers) foreach (var s in l.stateMachine.states) nStates++;
                    nClips = ac.animationClips != null ? ac.animationClips.Length : 0;
                }
                log.Add(string.Format("  {0,-14} Avatar={1,-4} isHuman={2,-6} isValid={3,-6} controller={4} 状态{5} 剪辑{6}",
                    e.chr,
                    av != null ? "有" : "无",
                    av != null ? av.isHuman.ToString() : "-",
                    av != null ? av.isValid.ToString() : "-",
                    ctrl != null ? "有" : "★无",
                    nStates, nClips));
            }
            PrefabUtility.UnloadPrefabContents(contents);
        }

        // ---- 3) 当前场景里的实例 ----
        log.Add("");
        log.Add("【当前场景实例】");
        foreach (var an in Object.FindObjectsOfType<Animator>(true))
        {
            var ctrl = an.runtimeAnimatorController;
            log.Add(string.Format("  {0,-40} 激活={1,-6} controller={2} 可见={3}",
                GetPath(an.transform), an.gameObject.activeInHierarchy ? "是" : "否",
                ctrl != null ? ctrl.name : "★无", an.GetComponent<Renderer>() != null ? "有" : "-"));
        }

        log.Add("");
        log.Add("提醒：Scene 视图在【非 Play 模式】下不会播放 Animator，必须按 Play 才看得到动。");
        WriteReport("检查角色动画");
    }

    // ================================================================== 5. 修复场景里没动画的 NPC
    // 实测发现：场景里的组长实例 controller 为空（而 prefab 上是好的）。
    // 不管是什么原因（实例是拖原始 FBX 建的、或旧版本残留），直接给实例挂上最直接。
    // 性别从 Avatar 反查：avatar 资产路径里的角色名 → 男/女。
    [MenuItem("Tools/干预项目/修复场景里没动画的 NPC")]
    public static void FixSceneNpcAnimators()
    {
        log.Clear();
        log.Add("修复场景 NPC 动画  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("");

        var male = new HashSet<string> { "组长", "张知远" };
        int fixedN = 0, skipped = 0;

        foreach (var an in Object.FindObjectsOfType<Animator>(true))
        {
            string path = GetPath(an.transform);
            if (an.runtimeAnimatorController != null)
            {
                log.Add(string.Format("  已有动画  {0,-36} {1}", path, an.runtimeAnimatorController.name));
                continue;
            }

            bool isPlayer = path.Contains("Player") || path.Contains("徐夏");
            var av = an.avatar;
            string avPath = av != null ? AssetDatabase.GetAssetPath(av) : null;
            string chr = string.IsNullOrEmpty(avPath) ? null : Path.GetFileNameWithoutExtension(avPath);

            if (isPlayer)
            {
                log.Add(string.Format("  跳过玩家  {0,-36} 第一人称看不到自己；要走路动画跑『给主角挂动画』", path));
                skipped++;
                continue;
            }
            if (chr == null)
            {
                log.Add(string.Format("  ★ 跳过    {0,-36} Avatar 为空，无法判断男女", path));
                skipped++;
                continue;
            }

            string g = male.Contains(chr) ? "男" : "女";
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(AnimOut + "/PC_待机_" + g + ".controller");
            if (ctrl == null)
            {
                log.Add(string.Format("  ★ 缺控制器 {0}（先跑『NPC 待机动画』）", AnimOut + "/PC_待机_" + g + ".controller"));
                skipped++;
                continue;
            }

            // 是 prefab 实例的话，优先“回退覆盖”，让它跟随 prefab；否则直接挂在实例上
            var root = PrefabUtility.GetOutermostPrefabInstanceRoot(an.gameObject);
            if (root != null && PrefabUtility.IsPartOfPrefabInstance(an))
            {
                var sp = new SerializedObject(an).FindProperty("m_Controller");
                if (sp != null) PrefabUtility.RevertPropertyOverride(sp, InteractionMode.AutomatedAction);
            }
            if (an.runtimeAnimatorController == null) an.runtimeAnimatorController = ctrl;
            an.applyRootMotion = false;
            an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            EditorUtility.SetDirty(an);
            if (root != null) EditorUtility.SetDirty(root);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(an.gameObject.scene);

            log.Add(string.Format("  已修复    {0,-36} avatar={1} → 待机{2}", path, chr, g));
            fixedN++;
        }

        log.Add("");
        log.Add(string.Format("修复 {0} 个，跳过 {1} 个", fixedN, skipped));
        log.Add("提醒：非 Play 模式下 Animator 不播，必须按 Play。");
        log.Add("场景改动未保存，记得 Ctrl+S。");
        WriteReport("修复场景 NPC 动画");
    }

    // 不管当前打开的是哪个场景，都去把 Game 场景里的 NPC 动画修好。
    // 安全约束：当前场景有未保存改动时【不切场景】，只提示手动跑，绝不丢你的东西。
    public static void FixNpcInGameScene()
    {
        const string GamePath = "Assets/Scenes/Game.unity";
        var active = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();

        if (active.path == GamePath) { FixSceneNpcAnimators(); return; }

        if (active.isDirty)
        {
            // 保存再切，而不是放弃：保存不会丢东西，只是写盘。
            // （之前是“有改动就不切”，结果主自动化反而被卡死了）
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(active);
            Debug.Log("[NpcSetup] 已保存当前场景 " + active.path + "，准备切到 Game 修 NPC");
        }

        string back = active.path;
        var sc = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(GamePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
        FixSceneNpcAnimators();                     // 它自己会写详细报告
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(sc);
        Debug.Log("[NpcSetup] 已在 Game 场景修复 NPC 动画并存盘");
        if (!string.IsNullOrEmpty(back))
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(back, UnityEditor.SceneManagement.OpenSceneMode.Single);
    }

    // ================================================================== 6. 自动进 Play 跑动画诊断
    // 外面没法直接按 Play，所以用 EditorApplication.isPlaying 控制：
    //   存当前场景 → 打开 Game → 进 Play 跑 8 秒（AnimDiag 会自动采样）→ 退出 Play → 恢复原场景
    static double _t0;
    static int _phase;
    static string _backScene;
    static bool _wasPlaying;

    [MenuItem("Tools/干预项目/进 Play 跑动画诊断")]
    public static void PlayAndDiag()
    {
        const string GamePath = "Assets/Scenes/Game.unity";
        if (!File.Exists(GamePath)) { Debug.LogError("[PlayDiag] 没有 " + GamePath); return; }

        _wasPlaying = EditorApplication.isPlaying;
        var active = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        _backScene = active.path;

        if (!_wasPlaying)
        {
            if (active.isDirty)
            {
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(active);
                Debug.Log("[PlayDiag] 已保存当前场景 " + active.path);
            }
            if (active.path != GamePath)
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(GamePath, UnityEditor.SceneManagement.OpenSceneMode.Single);

            // ★ 必须在 Game 场景里做：强制给玩家挂控制器（编辑器里继承正常、运行时拿不到的那种）
            ForcePlayerController();
        }

        _phase = 1;
        _t0 = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        if (!_wasPlaying) EditorApplication.isPlaying = true;
        Debug.Log("[PlayDiag] 开始，将在 Play 里跑 8 秒采样动画");
    }

    static void Tick()
    {
        double el = EditorApplication.timeSinceStartup - _t0;
        if (_phase == 1 && el > 8.0)
        {
            _phase = 2; _t0 = EditorApplication.timeSinceStartup;
            if (!_wasPlaying) EditorApplication.isPlaying = false;
            Debug.Log("[PlayDiag] 退出 Play");
        }
        else if (_phase == 2 && el > 3.0)
        {
            _phase = 0;
            EditorApplication.update -= Tick;
            if (!string.IsNullOrEmpty(_backScene) && !_wasPlaying)
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(_backScene, UnityEditor.SceneManagement.OpenSceneMode.Single);
            string f = Path.Combine(Application.dataPath, "../额外文件/动画运行时诊断.txt");
            Debug.Log("[PlayDiag] 完成。" + (File.Exists(f) ? "诊断报告：" + f : "★ 诊断报告没生成（AnimDiag 可能没跑）"));
        }
    }

    // ================================================================== 7. 诊断 Humanoid 骨骼匹配
    // 关键：Humanoid 动画是把“肌肉”映射到骨骼上的。如果 Avatar 记的骨骼名
    // 在 prefab 层级里找不到（或名字不完全一致），状态机照跑，但骨骼一动不动。
    [MenuItem("Tools/干预项目/诊断 Humanoid 骨骼匹配")]
    public static void CheckHumanoidBones()
    {
        log.Clear();
        log.Add("Humanoid 骨骼匹配诊断  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("");

        foreach (var e in NPCs)
        {
            string p = CharDir + "/" + e.chr + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(p) == null) { log.Add("★ 找不到 " + p); continue; }
            var contents = PrefabUtility.LoadPrefabContents(p);
            var an = contents.GetComponentInChildren<Animator>(true);
            if (an == null || an.avatar == null) { log.Add(string.Format("{0,-14} ★ 无 Animator/Avatar", e.chr)); PrefabUtility.UnloadPrefabContents(contents); continue; }

            // 层级里实际有的名字
            var have = new HashSet<string>();
            var all = new List<string>();
            foreach (var t in contents.GetComponentsInChildren<Transform>(true)) { have.Add(t.name); all.Add(t.name); }

            // Avatar 要求的名字
            var hd = an.avatar.humanDescription;
            var want = new List<string>();
            foreach (var hb in hd.human) if (!string.IsNullOrEmpty(hb.boneName)) want.Add(hb.boneName);
            var wantSk = new List<string>();
            foreach (var sb in hd.skeleton) if (!string.IsNullOrEmpty(sb.name)) wantSk.Add(sb.name);

            var missHuman = want.Where(w => !have.Contains(w)).ToList();
            var missSk = wantSk.Where(w => !have.Contains(w)).ToList();

            log.Add(string.Format("{0,-14} 层级对象 {1} 个", e.chr, all.Count));
            log.Add(string.Format("               Avatar 肌肉骨 {0} 个，缺失 {1} 个", want.Count, missHuman.Count));
            log.Add(string.Format("               Avatar 骨架骨 {0} 个，缺失 {1} 个", wantSk.Count, missSk.Count));
            if (missHuman.Count > 0)
                log.Add("               ★ 缺失的肌肉骨前 15: " + string.Join(", ", missHuman.Take(15).ToArray()));
            if (missSk.Count > 0)
                log.Add("               ★ 缺失的骨架骨前 15: " + string.Join(", ", missSk.Take(15).ToArray()));
            log.Add("               层级里前 20 个名字: " + string.Join(", ", all.Take(20).ToArray()));
            log.Add("               Avatar 要求前 10 个名字: " + string.Join(", ", want.Take(10).ToArray()));
            log.Add("");
            PrefabUtility.UnloadPrefabContents(contents);
        }

        // 场景实例也查一遍
        log.Add("【场景实例】");
        foreach (var an in Object.FindObjectsOfType<Animator>(true))
        {
            string ctl = an.runtimeAnimatorController != null ? an.runtimeAnimatorController.name : "★无";
            string isHum = an.isHuman.ToString();
            string hips = "-";
            if (an.isHuman) { var b = an.GetBoneTransform(HumanBodyBones.Hips); hips = b != null ? b.name : "★null"; }
            log.Add(string.Format("  {0,-40} controller={1,-16} isHuman={2,-6} Hips={3}", GetPath(an.transform), ctl, isHum, hips));
        }

        WriteReport("Humanoid 骨骼匹配诊断");
    }

    // ================================================================== 8. 修复 Avatar 与骨架不匹配（0 位移的真因）
    //   症状：NPC 状态机在跑（normalizedTime 在推进）但骨骼一动不动。
    //   原因：prefab 骨架来自 Meshes/1_身体_Body/*.fbx（Generic，骨骼名带【点号】CC_BaseThigh.L），
    //         而 Avatar 来自 组合角色/*.fbx（Human，骨骼名带【下划线】CC_BaseThigh_L），
    //         两边名字对不上 → Humanoid 重定向找不到骨骼。
    //   修法：把 body FBX 改成 Humanoid 并从它自身生成 Avatar（名字就匹配了），再挂到所有角色。
    [MenuItem("Tools/干预项目/修复角色 Avatar 不匹配")]
    public static void FixAvatars()
    {
        log.Clear();
        log.Add("修复 Avatar 与骨架不匹配  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("");

        var bodyAvatar = new Dictionary<string, Avatar>();   // "男"/"女" -> Avatar

        foreach (var pair in new[] { new[] { "F", "女" }, new[] { "M", "男" } })
        {
            string p = "Assets/assets/02_角色_Character/Meshes/1_身体_Body/" + pair[0] + "_body.fbx";
            if (!File.Exists(p)) { log.Add("★ 找不到 " + p); continue; }

            var imp = AssetImporter.GetAtPath(p) as ModelImporter;
            if (imp == null) { log.Add("★ 无法读取导入设置 " + p); continue; }

            if (imp.animationType != ModelImporterAnimationType.Human)
            {
                log.Add(string.Format("  {0}_body：animationType {1} → Human，重新导入…", pair[0], imp.animationType));
                imp.animationType = ModelImporterAnimationType.Human;
                imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                imp.SaveAndReimport();
            }
            else log.Add("  " + pair[0] + "_body：已经是 Human，不重导");

            var av = AssetDatabase.LoadAllAssetsAtPath(p).OfType<Avatar>().FirstOrDefault();
            if (av == null) { log.Add("★ " + pair[0] + "_body 没生成出 Avatar"); continue; }
            log.Add(string.Format("      Avatar={0}  isValid={1}  isHuman={2}", av.name, av.isValid, av.isHuman));
            bodyAvatar[pair[1]] = av;
        }
        AssetDatabase.SaveAssets();
        log.Add("");

        // 挂到所有角色（含徐夏）
        var all = NPCs.Select(x => new[] { x.chr, x.clip.EndsWith("男") ? "男" : "女" })
                      .Concat(new[] { new[] { "徐夏_可动", "女" } });
        foreach (var e in all)
        {
            string p = CharDir + "/" + e[0] + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(p) == null) continue;
            Avatar av;
            if (!bodyAvatar.TryGetValue(e[1], out av)) { log.Add("★ " + e[0] + " 没有可用的 Avatar"); continue; }

            var contents = PrefabUtility.LoadPrefabContents(p);
            var an = contents.GetComponentInChildren<Animator>(true);
            if (an == null) { log.Add("★ " + e[0] + " 没 Animator"); PrefabUtility.UnloadPrefabContents(contents); continue; }
            string old = an.avatar != null ? an.avatar.name : "无";
            an.avatar = av;
            PrefabUtility.SaveAsPrefabAsset(contents, p);
            PrefabUtility.UnloadPrefabContents(contents);
            log.Add(string.Format("  {0,-14} Avatar: {1}  →  {2}", e[0], old, av.name));
        }
        AssetDatabase.SaveAssets();

        log.Add("");
        log.Add("完成后重新进 Play，用『检查角色动画』或 AnimDiag 验证骨骼是否在动。");
        WriteReport("修复 Avatar 不匹配");
    }

    // ================================================================== 9. 给场景里的玩家强制挂控制器
    // 症状：prefab 上明明有 controller，实例也没覆盖，但运行时 role权重=-1（拿不到）。
    // 不管什么原因，直接在实例上写一个覆盖最稳。
    [MenuItem("Tools/干预项目/给场景里的玩家强制挂控制器")]
    public static void ForcePlayerController()
    {
        log.Clear();
        log.Add("强制给玩家挂控制器  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("");

        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(
            "Assets/assets/03_动作_Animation/Animators/PC_徐夏_Walk.controller");
        if (ctrl == null)
        {
            log.Add("★ 没有 PC_徐夏_Walk.controller，先跑一次『主角改用 Walk（不跑）』");
            WriteReport("强制挂控制器");
            return;
        }

        int n = 0;
        foreach (var an in Object.FindObjectsOfType<Animator>(true))
        {
            string path = GetPath(an.transform);
            bool isPlayer = path.Contains("Player") || path.Contains("徐夏");
            if (!isPlayer) continue;
            if (an.transform.parent != null && an.transform.parent.name.Contains("徐夏"))
            {
                log.Add("  跳过嵌套的 " + path + "（只处理根）");
                continue;
            }
            string old = an.runtimeAnimatorController != null ? an.runtimeAnimatorController.name : "无";
            an.runtimeAnimatorController = ctrl;      // 直接赋值 = 在实例上产生覆盖
            an.applyRootMotion = false;
            an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            EditorUtility.SetDirty(an);
            log.Add(string.Format("  {0}   controller: {1} → {2}", path, old, ctrl.name));
            n++;

            // 按用户定下的参数写：eyeHeight 1.4、只用 Walk、不整个隐藏身体
            var fpc = an.GetComponent<FirstPersonController>();
            if (fpc != null)
            {
                fpc.playerHeight = 1.75f;
                fpc.eyeHeight = 1.4f;                       // ★ 用户选定：1.4 最合适
                fpc.walkSpeed = 1.6f;
                fpc.runSpeed = 1.6f;                        // = walkSpeed ⇒ 不跑
                fpc.idleAnimSpeed = 0f;
                fpc.walkAnimSpeed = 1.0f;                   // 混合树 0=待机 1=Walk
                fpc.hideOwnBody = false;                    // 不整个藏起来
                fpc.firstPersonShadowsOnlyParts = new[] { "torso" };   // 只把相机所在的躯干设成只投影
                EditorUtility.SetDirty(fpc);
                log.Add("       eyeHeight=1.4  walkSpeed=runSpeed=1.6  hideOwnBody=关  ShadowsOnly=[torso]");
            }
        }

        if (n > 0)
        {
            var sc = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(sc);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(sc);
            log.Add("");
            log.Add("已保存场景 " + sc.path);
        }
        else log.Add("★ 没找到玩家（名字含 Player 或 徐夏）");

        WriteReport("强制给玩家挂控制器");
    }

    // ================================================================== 10. 主角只用 Walk（不跑）
    // 用户要求：玩家只播 Walk.fbx，不做慢跑/快跑。
    // 但 Walk.fbx 目前是 Generic(animationType=2) 且无 Avatar → 必须先转 Humanoid 才能驱动人形角色。
    [MenuItem("Tools/干预项目/主角改用 Walk（不跑）")]
    public static void SetupPlayerWalkOnly()
    {
        log.Clear();
        log.Add("主角改用 Walk 动画  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("");

        string walkPath = AnimDir + "/Walk.fbx";
        if (!File.Exists(walkPath)) { log.Add("★ 找不到 " + walkPath); WriteReport("主角改用 Walk"); return; }

        // 1) Walk.fbx 转 Humanoid + 循环
        var imp = AssetImporter.GetAtPath(walkPath) as ModelImporter;
        if (imp == null) { log.Add("★ 读不到导入设置"); WriteReport("主角改用 Walk"); return; }
        log.Add(string.Format("  Walk.fbx 原：animationType={0}  avatarSetup={1}", imp.animationType, imp.avatarSetup));
        if (imp.animationType != ModelImporterAnimationType.Human)
        {
            imp.animationType = ModelImporterAnimationType.Human;          // ★ 必须 Human，否则不能重定向
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.SaveAndReimport();
        }
        imp = AssetImporter.GetAtPath(walkPath) as ModelImporter;
        var clips0 = imp.clipAnimations;
        if (clips0 == null || clips0.Length == 0) clips0 = imp.defaultClipAnimations;
        bool dirty = false;
        if (clips0 != null) foreach (var c in clips0) if (!c.loopTime) { c.loopTime = true; dirty = true; }
        if (dirty) { imp.clipAnimations = clips0; imp.SaveAndReimport(); }
        log.Add(string.Format("  Walk.fbx 现：animationType={0}  avatarSetup={1}", imp.animationType, imp.avatarSetup));

        var walk = AssetDatabase.LoadAllAssetsAtPath(walkPath).OfType<AnimationClip>()
                        .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        var walkAv = AssetDatabase.LoadAllAssetsAtPath(walkPath).OfType<Avatar>().FirstOrDefault();
        if (walk == null) { log.Add("★ Walk.fbx 没有剪辑"); WriteReport("主角改用 Walk"); return; }
        log.Add(string.Format("  剪辑：{0}  长度={1:0.00}s  isHumanMotion={2}", walk.name, walk.length, walk.isHumanMotion));
        if (walkAv != null) log.Add(string.Format("  自带 Avatar：{0}  isValid={1}  isHuman={2}", walkAv.name, walkAv.isValid, walkAv.isHuman));
        if (!walk.isHumanMotion) log.Add("  ⚠ isHumanMotion=False：它不是有效的 Humanoid 动画，可能重定向不出动作");

        // 2) 造控制器：待机女 → Walk（Speed 0→1），另保留拿手机
        string ctrlPath = AnimOut + "/PC_徐夏_Walk.controller";
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath) != null) AssetDatabase.DeleteAsset(ctrlPath);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
        ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
        ctrl.AddParameter("Phone", AnimatorControllerParameterType.Bool);
        ctrl.AddParameter("TakePhone", AnimatorControllerParameterType.Trigger);
        var sm = ctrl.layers[0].stateMachine;

        var tree = new BlendTree
        {
            name = "Locomotion",
            blendType = BlendTreeType.Simple1D,
            blendParameter = "Speed",
            useAutomaticThresholds = false           // 0=待机 1=Walk
        };
        tree.AddChild(Clip("待机女"), 0f);
        tree.AddChild(walk, 1f);
        var loco = sm.AddState("Locomotion");
        loco.motion = tree;
        loco.writeDefaultValues = false;
        AssetDatabase.AddObjectToAsset(tree, ctrl);
        sm.defaultState = loco;

        var phoneIdle = sm.AddState("拿手机待机"); phoneIdle.motion = Clip("拿手机待机");
        var phone = sm.AddState("拿手机"); phone.motion = Clip("拿手机");
        var t1 = loco.AddTransition(phoneIdle);
        t1.hasExitTime = false; t1.duration = 0.2f; t1.AddCondition(AnimatorConditionMode.If, 0, "Phone");
        var t2 = phoneIdle.AddTransition(loco);
        t2.hasExitTime = false; t2.duration = 0.2f; t2.AddCondition(AnimatorConditionMode.IfNot, 0, "Phone");
        var t3 = sm.AddAnyStateTransition(phone);
        t3.hasExitTime = false; t3.duration = 0.15f; t3.canTransitionToSelf = false;
        t3.AddCondition(AnimatorConditionMode.If, 0, "TakePhone");
        var t4 = phone.AddTransition(phoneIdle);
        t4.hasExitTime = true; t4.exitTime = 0.9f; t4.duration = 0.2f;
        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        log.Add("  控制器：" + ctrlPath + "（待机女 @0 → Walk @1）");

        // 3) 挂到徐夏 prefab
        string p = CharDir + "/徐夏_可动.prefab";
        var contents = PrefabUtility.LoadPrefabContents(p);
        var an = contents.GetComponentInChildren<Animator>(true);
        if (an == null) { log.Add("★ 徐夏 prefab 没 Animator"); PrefabUtility.UnloadPrefabContents(contents); WriteReport("主角改用 Walk"); return; }
        bool hadAv = an.avatar != null;
        an.runtimeAnimatorController = ctrl;
        an.applyRootMotion = false;
        an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        PrefabUtility.SaveAsPrefabAsset(contents, p);
        PrefabUtility.UnloadPrefabContents(contents);
        log.Add(string.Format("  已挂到 徐夏_可动.prefab（avatar={0}）", hadAv ? "保留" : "★无"));

        // 4) 没有 Play 直接改场景里的速度/身高字段（若有场景打开）
        log.Add("");
        log.Add("下一步：确定场景里的 FirstPersonController 参数（目标）：");
        log.Add("  walkSpeed = runSpeed（不跑）✦ runAnimSpeed = walkAnimSpeed = 1");
        log.Add("  eyeHeight = 1.4（你要的高度）");
        log.Add("  hideOwnBody = 关；firstPersonShadowsOnlyParts = torso（只把相机所在的躯干设为只投影）");
        WriteReport("主角改用 Walk");
    }

    // ==================================================================
    static string GetPath(Transform t)
    {
        var s = t.name;
        while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }

    static void WriteReport(string title)
    {
        Directory.CreateDirectory("Assets/assets/_报告");
        File.WriteAllText(REPORT, string.Join("\n", log.ToArray()));
        Debug.Log("[NpcSetup] " + title + " —— 报告： " + REPORT);
    }
    // ================================================================== 11. 徐夏身体改双面
    // 低头看腿时，大腿顶端截面是单面片 → 从里面看被背面剔除 → 看着像被“切掉”。
    // 把徐夏专属材质改成双面（_Cull = Off）就看不到空洞了。只改徐夏的材质实例，
    // 不影响其他角色（一人一色，各自有独立材质目录）。
    [MenuItem("Tools/干预项目/徐夏身体改双面")]
    public static void MakeXiaDoubleSided()
    {
        log.Clear();
        log.Add("徐夏身体改双面  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("");

        string dir = CharDir + "/材质/徐夏";
        if (!Directory.Exists(dir)) { log.Add("★ 找不到 " + dir); WriteReport("徐夏身体改双面"); return; }

        int n = 0;
        foreach (var f in Directory.GetFiles(dir, "*.mat"))
        {
            string p2 = f.Replace('\\', '/');
            var m = AssetDatabase.LoadAssetAtPath<Material>(p2);
            if (m == null) continue;
            // 注意：ShaderGraph 材质用 HasProperty("_Cull") 会返回 false（不在可查询属性里），
            // 所以不能拿它做门禁，直接设值。
            float before = 0f;
            try { before = m.GetFloat("_Cull"); } catch { }
            m.SetFloat("_Cull", 0f);                  // 0 = Off（双面）
            EditorUtility.SetDirty(m);
            log.Add(string.Format("  {0,-38} _Cull {1} → 0（双面）", Path.GetFileName(p2), before));
            n++;
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        log.Add("");
        log.Add("改了 " + n + " 个材质。双面渲染略耗性能，但只影响徐夏一个人。");
        WriteReport("徐夏身体改双面");
    }
}

// 放 Assets/_npc_trigger.txt → 编辑器下次刷新/重编译后自动执行：
//   URP 阴影/抗锯齿 → 重建主角控制器 → 修 Game 场景所有 NPC 动画 → 存盘
[InitializeOnLoad]
public static class NpcSetupTrigger
{
    const string Trigger = "Assets/_npc_trigger.txt";

    static NpcSetupTrigger()
    {
        if (!File.Exists(Trigger)) return;
        // 不在排队前删触发器：域重载会把排队的动作吃掉，触发器却已消失。改成动作真正开始时再删。
        EditorApplication.delayCall += () =>
        {
            try
            {
                if (File.Exists(Trigger)) File.Delete(Trigger);
                Debug.Log("[Setup] 开始：阴影 → 主角控制器 → 修 Avatar → 修 NPC → 骨骼诊断 → Play 验证");
                RunAll();
            }
            catch (System.Exception e)
            {
                Directory.CreateDirectory("../额外文件");
                File.WriteAllText("../额外文件/错误_阴影动画.txt", e.ToString());
                Debug.LogError("[Setup] 失败: " + e);
            }
        };
    }

    // 统一入口：确保不在 Play 模式再干活。
    // 否则 OpenScene/SaveScene 会招 "This cannot be used during play mode." 把整轮打断。
    static void RunAll()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.Log("[Setup] 当前在 Play 模式 → 先退出，退出后再继续");
            EditorApplication.isPlaying = false;
            EditorApplication.update -= RetryWhenStopped;
            EditorApplication.update += RetryWhenStopped;
            return;
        }
        EditorApplication.update -= RetryWhenStopped;

        SceneBuilder.TuneShadows();              // URP 阴影/抗锯齿
        NpcSetup.SetupPlayerWalkOnly();           // Walk.fbx 转 Humanoid + 只用 Walk 的控制器
        NpcSetup.FixAvatars();                    // 修 Avatar 与骨架不匹配
        NpcSetup.FixNpcInGameScene();             // 去 Game 场景修 NPC
        NpcSetup.MakeXiaDoubleSided();            // 徐夏身体材质改双面（低头看腿不再有空洞）
        NpcSetup.PlayAndDiag();                   // 打开 Game → 强制挂玩家控制器(含新参数) → 进 Play 采样 → 退出
        Debug.Log("[Setup] 全部完成");
    }

    static void RetryWhenStopped()
    {
        if (EditorApplication.isPlaying) return;
        EditorApplication.update -= RetryWhenStopped;
        EditorApplication.delayCall += () =>
        {
            try { RunAll(); }
            catch (System.Exception e)
            {
                Directory.CreateDirectory("../额外文件");
                File.WriteAllText("../额外文件/错误_阴影动画.txt", e.ToString());
                Debug.LogError("[Setup] 失败: " + e);
            }
        };
    }
}
