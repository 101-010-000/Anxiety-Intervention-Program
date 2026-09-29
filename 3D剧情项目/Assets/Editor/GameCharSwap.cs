// ============================================================================
// Game 场景角色替换：旧的（CC_Base 骨架、重定向有问题）→ 新的 Mixamo 已绑定模型
//
// 需求（用户 2026-09-28）：
//   · 在【原来的位置、原来的层级】上，把人物模型换成 03_动作_Animation/带动画模型/<角色>/已绑定.fbx
//   · 每个角色放上它自己的 Idle 动画（同源，零重定向）
//   · 路人（= 这 9 个角色 + 路人材质覆盖）一起换，位置/层级不变
//
// 实测到的旧场景结构（试运行报告 _Game角色替换.txt）：
//   · 实例根 = 角色 prefab 根，挂 Animator(旧 avatar+旧控制器) + CapsuleCollider（道具碰撞体在这层）
//   · 根下面只有 1 个来自 prefab 的子物体（Base_F_body，CC_Base 骨架 + 一堆分件渲染器）
//   · 玩家 Player_徐夏 根上还有 CharacterController + FirstPersonController，子物体里
//     FP_相机 是【手动加的】（不是 prefab 件）—— 替换时绝不能删
//
// 做法：
//   · 保留实例根（名字/位置/旋转/层级/层/碰撞体/玩家组件都不动），只删「来自 prefab 的」子物体
//   · 新模型挂成子物体，局部变换沿用旧模型子物体；整棵子树设成旧渲染器所在的层（Outline）
//   · 材质覆盖（路人材质）按【原材质名】映射到新模型的对应子网格（名字把 . - 空格 都看成 _）
//   · <角色>_Idle.controller：clip = 该角色自己 Idle.fbx 的循环副本（同源不重定向），带旧参数名
//   · NPC 根上那个旧 Animator 停用（旧骨架没了，留着会跟新 Animator 抢同一副骨架）
//   · 玩家根上的 Animator 留用（FirstPersonController 要写 Speed），并把它自己的模型
//     加进 firstPersonShadowsOnlyParts（合并网格没法只藏躯干，只能整身只投影）
//
// 菜单：Tools/干预项目/Game角色替换/① 试运行（只报告，不改）
//       Tools/干预项目/Game角色替换/② 执行替换（换模型 + 挂 Idle + 存场景）
//       Tools/干预项目/Game角色替换/③ 运行自检（进 Play 看 Idle 真的在播 & 材质没坏）
// 报告：Assets/assets/_报告/_Game角色替换.txt / _Game角色替换自检.txt
// ============================================================================
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class GameCharSwap
{
    const string SCENE = "Assets/Scenes/Game.unity";
    const string NEW_ROOT = "Assets/assets/03_动作_Animation/带动画模型";
    const string ANIM_DIR = "Assets/assets/03_动作_Animation/Animators/带动画模型";
    const string PED_MAT = "Assets/assets/02_角色_Character/Materials/路人材质.mat";
    const string REPORT = "Assets/assets/_报告/_Game角色替换.txt";
    const string SMOKE_REPORT = "Assets/assets/_报告/_Game角色替换自检.txt";
    const string PLAYER = "Player_徐夏";

    static string San(string s) { return Regex.Replace(s, "[. \\-]", "_"); }

    // ============================================================ 目标收集
    class Target
    {
        public GameObject go;
        public string ch;
        public string prefabName;
        public bool isPlayer;
        public bool isPed;
        public string path;
        public Dictionary<string, Material> ovr;
    }

    static List<Target> Collect()
    {
        var res = new List<Target>();
        var sc = SceneManager.GetActiveScene();
        foreach (var root in sc.GetRootGameObjects())
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var go = t.gameObject;
                bool inChapter = t.parent != null && Regex.IsMatch(t.parent.name, "^第[一二三四五12345]章角色$");
                bool isPlayer = go.name == PLAYER;
                if (!inChapter && !isPlayer) continue;
                var src = PrefabUtility.GetCorrespondingObjectFromSource(go);
                if (src == null) continue;
                string pn = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(src));
                if (string.IsNullOrEmpty(pn) || !pn.EndsWith("_可动")) continue;
                var ovr = OverrideMap(go);
                string ch = pn.Substring(0, pn.Length - 3);
                if (ovr.Count == 0) ovr = DeriveOverrides(go, ch);   // 已经换过一次的实例：跟干净 FBX 比出来
                res.Add(new Target
                {
                    go = go,
                    ch = ch,
                    prefabName = pn,
                    isPlayer = isPlayer,
                    isPed = ovr.Values.Any(m => m != null && m.name == "路人材质"),
                    path = PathOf(go),
                    ovr = ovr
                });
            }
        }
        return res.OrderBy(t => t.path).ToList();
    }

    static string PathOf(GameObject go)
    {
        var parts = new List<string>();
        var t = go.transform;
        while (t != null) { parts.Add(t.name); t = t.parent; }
        parts.Reverse();
        return string.Join("/", parts.ToArray());
    }

    // 旧的「原材质名 → 覆盖材质」表（路人材质覆盖过哪些原材质；名字清洗后做 key）
    static Dictionary<string, Material> OverrideMap(GameObject inst)
    {
        var map = new Dictionary<string, Material>();
        var mods = PrefabUtility.GetPropertyModifications(inst);
        if (mods == null) return map;
        foreach (var m in mods)
        {
            if (m == null || m.propertyPath == null || !m.propertyPath.StartsWith("m_Materials.Array.data[")) continue;
            var r = m.target as Renderer;
            if (r == null) continue;
            int slot = 0;
            var mm = Regex.Match(m.propertyPath, @"\[(\d+)\]");
            if (mm.Success) slot = int.Parse(mm.Groups[1].Value);
            var org = (slot < r.sharedMaterials.Length) ? r.sharedMaterials[slot] : null;
            var ovr = m.objectReference as Material;
            if (org != null && ovr != null) map[San(org.name)] = ovr;
        }
        return map;
    }

    // 已经替换过一次的实例：旧的渲染器早没了，属性修改表里可能也空了
    // → 跟一份「干净的 FBX 实例」逐槽比，不同的就是被覆盖掉的（路人材质 / 厨师那种部分覆盖）
    static Dictionary<string, Material> DeriveOverrides(GameObject inst, string ch)
    {
        var map = new Dictionary<string, Material>();
        string fbx = NEW_ROOT + "/" + ch + "/已绑定.fbx";
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
        if (asset == null) return map;

        // 当前实例里那个新模型（来自带动画模型的 prefab 件）
        GameObject model = null;
        foreach (Transform c in inst.transform)
        {
            var src = PrefabUtility.GetCorrespondingObjectFromSource(c.gameObject);
            if (src == null) continue;
            if (AssetDatabase.GetAssetPath(src) == fbx) { model = c.gameObject; break; }
        }
        if (model == null) return map;

        var fresh = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        try
        {
            var def = new List<Material>();
            foreach (var r in fresh.GetComponentsInChildren<Renderer>(true)) def.AddRange(r.sharedMaterials);
            var cur = new List<Material>();
            foreach (var r in model.GetComponentsInChildren<Renderer>(true)) cur.AddRange(r.sharedMaterials);
            for (int i = 0; i < Mathf.Min(def.Count, cur.Count); i++)
                if (def[i] != null && cur[i] != null && def[i] != cur[i]) map[San(def[i].name)] = cur[i];
        }
        finally { Object.DestroyImmediate(fresh); }
        return map;
    }

    // ============================================================ Idle 控制器
    static string EnsureIdleController(string ch, List<string> log)
    {
        string fbx = NEW_ROOT + "/" + ch + "/Idle.fbx";
        string clipPath = ANIM_DIR + "/" + ch + "_Idle.anim";
        string ctrlPath = ANIM_DIR + "/" + ch + "_Idle.controller";
        Directory.CreateDirectory(ANIM_DIR);

        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
        var loop = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);

        if (loop == null)
        {
            var src = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>()
                          .Where(c => !c.name.StartsWith("__preview__"))
                          .OrderByDescending(c => c.length).FirstOrDefault();
            if (src == null) { log.Add("      ★ " + ch + "：Idle.fbx 里没有剪辑"); return null; }
            loop = Object.Instantiate(src);
            loop.name = ch + "_Idle";
            var st = AnimationUtility.GetAnimationClipSettings(loop);
            st.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(loop, st);
            AssetDatabase.CreateAsset(loop, clipPath);
            log.Add(string.Format("      + {0}_Idle.anim（源 Idle.fbx 的“{1}” {2:0.0}s，已设循环）", ch, src.name, src.length));
        }

        if (ctrl == null)
        {
            ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
            // 旧参数名留着，FirstPersonController 等脚本写参数时不会报错
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Phone", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("TakePhone", AnimatorControllerParameterType.Trigger);
            var sm = ctrl.layers[0].stateMachine;
            var st = sm.AddState(ch + "_Idle");
            st.motion = loop;
            st.writeDefaultValues = true;
            sm.defaultState = st;
            EditorUtility.SetDirty(ctrl);
            log.Add("      + " + Path.GetFileName(ctrlPath));
        }
        AssetDatabase.SaveAssets();
        return ctrlPath;
    }

    // ============================================================ ⓪ 刷新循环剪辑（源 FBX 换过之后）
    /// <summary>
    /// 把已生成的 <角色>_Idle.anim / <角色>_Sit.anim 用【当前 FBX 里的剪辑】原地刷一遍。
    /// 为什么需要：那些 .anim 是当初从 FBX 复制的副本，用户之后换了 FBX（比如换了个新的 Idle 动画），
    /// 控制器还指着旧副本 → 播的还是老动画。这里原地改（不清除/重建资产），GUID 不变 → 引用不断。
    /// </summary>
    [MenuItem("Tools/干预项目/Game角色替换/⓪ 刷新角色循环剪辑（源 FBX 换过之后）", false, 118)]
    public static void RefreshLoopClips()
    {
        var log = new List<string>();
        log.Add("刷新角色循环剪辑  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("");
        if (!Directory.Exists(NEW_ROOT)) { log.Add("★ 找不到 " + NEW_ROOT); Flush(log, REPORT); return; }

        int done = 0;
        foreach (var dir in Directory.GetDirectories(NEW_ROOT).OrderBy(p => p))
        {
            string ch = Path.GetFileName(dir);
            foreach (var pair in new[] { new[] { "Idle.fbx", ch + "_Idle.anim" }, new[] { "Sitting Idle.fbx", ch + "_Sit.anim" } })
            {
                string fbx = (dir + "/" + pair[0]).Replace('\\', '/');
                string dst = ANIM_DIR + "/" + pair[1];
                if (!File.Exists(fbx)) continue;
                var dstClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(dst);
                if (dstClip == null) { log.Add("  " + ch + "：" + pair[1] + " 不存在（跳过，跑一次坐姿/替换工具会建）"); continue; }
                var src = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>()
                              .Where(c => !c.name.StartsWith("__preview__"))
                              .OrderByDescending(c => c.length).FirstOrDefault();
                if (src == null) { log.Add("  ★ " + ch + "：" + pair[0] + " 里没有剪辑"); continue; }

                float oldLen = dstClip.length;
                dstClip.ClearCurves();
                foreach (var b in AnimationUtility.GetCurveBindings(src))
                    AnimationUtility.SetEditorCurve(dstClip, b, AnimationUtility.GetEditorCurve(src, b));
                foreach (var b in AnimationUtility.GetObjectReferenceCurveBindings(src))
                    AnimationUtility.SetObjectReferenceCurve(dstClip, b, AnimationUtility.GetObjectReferenceCurve(src, b));
                var st = AnimationUtility.GetAnimationClipSettings(dstClip);
                st.loopTime = true;
                AnimationUtility.SetAnimationClipSettings(dstClip, st);
                EditorUtility.SetDirty(dstClip);
                done++;
                log.Add(string.Format("  {0,-8} {1,-16} {2:0.00}s → {3:0.00}s（曲线 {4} 条）✓",
                    ch, pair[1], oldLen, src.length, AnimationUtility.GetCurveBindings(src).Length));
            }
        }
        AssetDatabase.SaveAssets();
        log.Add("");
        log.Add("合计刷新 " + done + " 份循环剪辑（GUID 不变，控制器引用不受影响）");
        Flush(log, REPORT);
    }

    // ============================================================ ① 试运行
    [MenuItem("Tools/干预项目/Game角色替换/① 试运行（只报告，不改）", false, 120)]
    public static void DryRun()
    {
        var log = new List<string>();
        log.Add("Game 场景角色替换 —— 试运行（只读）  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("");
        if (!OpenGame(log)) { Flush(log, REPORT); return; }

        var targets = Collect();
        log.Add("找到角色实例 " + targets.Count + " 个（路人 " + targets.Count(t => t.isPed) + " 个）");
        log.Add("");
        foreach (var t in targets)
        {
            log.Add("════ " + t.path);
            log.Add(string.Format("   {0}  路人={1} 玩家={2} layer={3}  根上：{4}",
                t.ch, t.isPed, t.isPlayer, LayerMask.LayerToName(t.go.layer), CompList(t.go)));
            log.Add("   材质覆盖：" + (t.ovr.Count == 0 ? "无" : string.Join("、", t.ovr.Select(kv => kv.Key + "→" + kv.Value.name))));
            log.Add("");
        }
        log.Add("说明：结构细节见上一版试运行报告（根节点持碰撞体 + 1 个 prefab 子物体；玩家有 FP_相机）");
        Flush(log, REPORT);
    }

    static string CompList(GameObject go)
    {
        var l = new List<string>();
        foreach (var c in go.GetComponents<Component>())
        {
            if (c == null) { l.Add("⚠丢失脚本"); continue; }
            if (c is Transform) continue;
            l.Add(c.GetType().Name);
        }
        return l.Count == 0 ? "（无）" : string.Join("、", l);
    }

    static string Fmt(Vector3 v) { return string.Format("({0:0.##},{1:0.##},{2:0.##})", v.x, v.y, v.z); }

    static bool OpenGame(List<string> log)
    {
        if (!File.Exists(SCENE)) { log.Add("★ 找不到 " + SCENE); return false; }
        var active = SceneManager.GetActiveScene();
        if (active.path == SCENE) return true;
        if (active.isDirty)
        {
            EditorSceneManager.SaveScene(active);
            log.Add("（先把当前场景 " + active.name + " 存盘）");
        }
        EditorSceneManager.OpenScene(SCENE, OpenSceneMode.Single);
        log.Add("已打开 " + SCENE);
        log.Add("");
        return true;
    }

    // ============================================================ ② 执行
    [MenuItem("Tools/干预项目/Game角色替换/② 执行替换（换模型 + 挂 Idle + 存场景）", false, 121)]
    public static void Execute()
    {
        var log = new List<string>();
        log.Add("Game 场景角色替换 —— 执行  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("");
        if (!OpenGame(log)) { Flush(log, REPORT); return; }

        var targets = Collect();
        log.Add("角色实例 " + targets.Count + " 个（路人 " + targets.Count(t => t.isPed) + "）");
        log.Add("");
        log.Add("── 控制器 / 循环剪辑");
        var ctrls = new Dictionary<string, string>();
        foreach (var ch in targets.Select(t => t.ch).Distinct().OrderBy(x => x))
            ctrls[ch] = EnsureIdleController(ch, log);
        log.Add("");
        log.Add("── 逐个替换");

        int ok = 0, bad = 0;
        foreach (var t in targets)
        {
            try { Swap(t, ctrls[t.ch], log); ok++; }
            catch (System.Exception e)
            {
                bad++;
                log.Add("   ★ " + t.path + " 失败：" + e.Message);
                Debug.LogError("[GameCharSwap] " + t.path + " : " + e);
            }
        }
        log.Add("");
        log.Add("替换完成：成功 " + ok + "，失败 " + bad);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        bool saved = EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        log.Add("存场景 " + SCENE + "：" + (saved ? "成功 ✓" : "★ 失败"));
        Flush(log, REPORT);
    }

    static void Swap(Target t, string ctrlPath, List<string> log)
    {
        string fbx = NEW_ROOT + "/" + t.ch + "/已绑定.fbx";
        var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
        if (modelAsset == null) { log.Add("   ★ " + t.path + "：读不到 " + fbx); return; }

        var root = t.go;
        var rootT = root.transform;

        // --- 记录旧信息
        int layer = root.layer;
        var rl = root.GetComponentsInChildren<Renderer>(true).Select(r => r.gameObject.layer).ToList();
        if (rl.Count > 0) layer = rl.GroupBy(x => x).OrderByDescending(g => g.Count()).First().Key;

        // 模型子物体（来自 prefab 的那个）的局部变换
        Vector3 mPos = Vector3.zero; Quaternion mRot = Quaternion.identity; Vector3 mScale = Vector3.one;
        foreach (Transform c in rootT)
            if (PrefabUtility.GetCorrespondingObjectFromSource(c.gameObject) != null)
            { mPos = c.localPosition; mRot = c.localRotation; mScale = c.localScale; break; }

        // 需要重建的碰撞体：只处理【长在要删掉的子物体里】的那些（根上的留着不动）
        var lostColliders = root.GetComponentsInChildren<CapsuleCollider>(true)
                                .Where(c => c.gameObject != root)
                                .Select(c => new[] { c.radius, c.height, c.center.x, c.center.y, c.center.z, c.direction, c.isTrigger ? 1f : 0f })
                                .ToList();

        // --- 删旧模型（只删 prefab 件；手动加的（FP_相机 等）留着）
        int removed = 0, kept = 0;
        var rm = new List<GameObject>();
        foreach (Transform c in rootT)
        {
            bool fromPrefab = PrefabUtility.GetCorrespondingObjectFromSource(c.gameObject) != null;
            bool hasCam = c.GetComponentInChildren<Camera>(true) != null;
            if (fromPrefab && !hasCam) rm.Add(c.gameObject); else kept++;
        }
        foreach (var go in rm) { Object.DestroyImmediate(go); removed++; }

        // --- 挂新模型
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, SceneManager.GetActiveScene());
        inst.name = t.ch + "_已绑定";
        inst.transform.SetParent(rootT, false);
        inst.transform.localPosition = mPos;
        inst.transform.localRotation = mRot;
        inst.transform.localScale = mScale;
        foreach (var tr in inst.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = layer;

        foreach (var c in lostColliders)
        {
            var cap = inst.AddComponent<CapsuleCollider>();
            cap.radius = c[0]; cap.height = c[1];
            cap.center = new Vector3(c[2], c[3], c[4]);
            cap.direction = (int)c[5];
            cap.isTrigger = c[6] > 0.5f;
        }

        // --- 材质覆盖（按清洗后的原材质名 → 新子网格）
        int overridden = 0;
        if (t.ovr.Count > 0)
        {
            foreach (var smr in inst.GetComponentsInChildren<Renderer>(true))
            {
                var mats = smr.sharedMaterials;
                bool ch2 = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material rep;
                    if (mats[i] != null && t.ovr.TryGetValue(San(mats[i].name), out rep)) { mats[i] = rep; ch2 = true; overridden++; }
                }
                if (ch2) smr.sharedMaterials = mats;
            }
        }

        // --- 新模型上的 Animator 播 Idle（Mixamo 导的 FBX 里没有 Animator，缺就自己补一个）
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
        int animNew = 0;
        var anims = inst.GetComponentsInChildren<Animator>(true).ToList();
        if (anims.Count == 0) anims.Add(inst.AddComponent<Animator>());
        foreach (var a in anims)
        {
            a.runtimeAnimatorController = ctrl;
            a.avatar = null;                       // Generic（路径动画），不要旧的人形 avatar
            a.applyRootMotion = false;
            a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animNew++;
        }

        // --- 实例根上原本的 Animator
        string rootAnim = "";
        var fpc = root.GetComponent("FirstPersonController");
        foreach (var a in root.GetComponents<Animator>())
        {
            if (t.isPlayer)
            {
                // 玩家：留着给 FirstPersonController 写 Speed（参数已加进控制器），但骨架是新模型的
                a.runtimeAnimatorController = ctrl;
                a.avatar = null;
                a.applyRootMotion = false;
                a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                rootAnim = "玩家：接新控制器（参数 Speed/Phone/TakePhone 都在）";
            }
            else
            {
                a.runtimeAnimatorController = null;
                a.enabled = false;
                rootAnim = "NPC：旧 Animator 已停用（旧骨架已删）";
            }
        }

        // --- 玩家：把自己模型加进"第一人称只投影"名单（合并网格没法只藏躯干）
        string fpsNote = "";
        if (t.isPlayer && fpc != null)
        {
            var f = fpc.GetType();
            var field = f.GetField("firstPersonShadowsOnlyParts");
            if (field != null)
            {
                var old = (string[])field.GetValue(fpc) ?? new string[0];
                var list = new List<string>(old.Where(s => !string.IsNullOrEmpty(s)));
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                    if (!list.Contains(r.gameObject.name)) list.Add(r.gameObject.name);
                field.SetValue(fpc, list.ToArray());
                var ap = f.GetMethod("ApplyFirstPersonParts");
                if (ap != null) ap.Invoke(fpc, null);
                fpsNote = "第一人称只投影名单 ← [" + string.Join(",", list) + "]";
            }
        }

        log.Add(string.Format("   {0,-44} {1,-10} 删旧模型 {2}(留 {3}) → {4}  layer={5} 补碰撞体 {6} 材质覆盖 {7} 新Animator {8}{9}{10}",
            t.path, t.ch + (t.isPed ? "/路人" : ""), removed, kept, inst.name, LayerMask.LayerToName(layer),
            lostColliders.Count, overridden, animNew,
            string.IsNullOrEmpty(rootAnim) ? "" : " | " + rootAnim,
            string.IsNullOrEmpty(fpsNote) ? "" : " | " + fpsNote));
    }

    // ============================================================ ③ 运行自检（Play 模式）
    public const string SMOKE_STATE = "../额外文件/_gamechkar_smoke.state";
    static bool _smokeRunning;
    static int _smokeStartFrame;
    static string _smokeLog;
    public static bool SmokeRequested() { return File.Exists(SMOKE_STATE); }

    [MenuItem("Tools/干预项目/Game角色替换/③ 运行自检（进 Play 看 Idle 真的在播）", false, 122)]
    public static void SmokeTest()
    {
        if (_smokeRunning) { Debug.LogWarning("[GameCharSwap] 自检已在跑"); return; }
        var log = new List<string>();
        log.Add("Game 角色替换自检  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("");
        if (!OpenGame(log)) { Flush(log, SMOKE_REPORT); return; }
        if (Application.isPlaying) { log.Add("★ 已经在 Play 模式，请先退出"); Flush(log, SMOKE_REPORT); return; }

        _smokeLog = string.Join("\n", log.ToArray());
        Directory.CreateDirectory("../额外文件");
        File.WriteAllText(SMOKE_STATE, System.DateTime.Now.ToString("o"));
        BeginSmokeTick();
        EditorApplication.isPlaying = true;      // 进 Play 会域重载 → 回来靠 [InitializeOnLoad] 重新接管
    }

    // 域重载后重新挂上（幂等）
    public static void BeginSmokeTick()
    {
        _smokeRunning = true;
        _smokeStartFrame = 0;
        _smokeSecond = false;
        EditorApplication.update -= SmokeTick;
        EditorApplication.update += SmokeTick;
    }

    static string ReadSmokeLog()
    {
        if (!string.IsNullOrEmpty(_smokeLog)) return _smokeLog;
        return "Game 角色替换自检  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n（域重载后继续）\n";
    }

    class Snap
    {
        public GameObject inst;            // 实例根
        public Animator a;
        public Transform probe;            // 用来判断“真的在动”的一根骨头
        public Vector3 p0, p1;
        public float t0, t1;
        public SkinnedMeshRenderer smr;
        public string name;
    }

    static void SmokeTick()
    {
        if (!Application.isPlaying) return;
        if (_smokeStartFrame == 0) { _smokeStartFrame = Time.frameCount; return; }
        int elapsed = Time.frameCount - _smokeStartFrame;
        if (elapsed < 60) return;

        // 第一遍：把每个角色实例找出来
        if (!_smokeSecond)
        {
            _smokePending = new List<Snap>();
            foreach (var smr in Object.FindObjectsOfType<SkinnedMeshRenderer>())
            {
                if (smr.sharedMesh == null) continue;
                var a = smr.GetComponentInParent<Animator>();
                if (a == null || a.runtimeAnimatorController == null) continue;
                if (smr.GetComponentInParent<Animator>() != a) continue;
                var t = smr.transform;
                // 实例根 = 模型（FBX 实例）的父亲；名字就是「组长_可动 / 路人 (3) / Player_徐夏」
                var model = a.gameObject;
                GameObject inst = (model.transform.parent != null ? model.transform.parent : model.transform).gameObject;
                var probe = (smr.bones != null && smr.bones.Length > 0 && smr.bones[0] != null) ? smr.bones[0] : t;
                _smokePending.Add(new Snap
                {
                    inst = inst, a = a, probe = probe, smr = smr,
                    name = inst.name + " ← " + model.name,
                    t0 = a.GetCurrentAnimatorStateInfo(0).normalizedTime,
                    p0 = probe.position
                });
            }
            _smokeSecond = true;
            _smokeFrame2 = Time.frameCount;
            return;
        }

        if (Time.frameCount - _smokeFrame2 < 20) return;
        foreach (var s in _smokePending)
        {
            if (s.a != null) s.t1 = s.a.GetCurrentAnimatorStateInfo(0).normalizedTime;
            if (s.probe != null) s.p1 = s.probe.position;
        }

        var log = new List<string>(ReadSmokeLog().Split('\n'));
        int moved = 0, still = 0;
        log.Add("角色实例（按驱动蒙皮的 Animator 找）：" + _smokePending.Count);
        foreach (var s in _smokePending.OrderBy(s => s.name))
        {
            float dp = Vector3.Distance(s.p0, s.p1);
            bool ok = dp > 0.0005f;                       // 骨头真的动了
            if (ok) moved++; else still++;
            float foot = 0;
            if (s.inst != null) foot = s.inst.transform.position.y;
            log.Add(string.Format("   {0,-34} {1,-6} t {2:0.000}→{3:0.000}  骨头位移 {4:0.0000}m  {5}",
                s.name,
                s.smr != null ? (s.smr.sharedMaterials.Length + " 槽") : "-",
                s.t0, s.t1, dp, ok ? "✓ 在播" : "★ 没动"));
        }
        log.Add("");
        log.Add("在动的 " + moved + " / " + _smokePending.Count + (still == 0 ? " ✓" : "  ★没动 " + still + " 个"));

        int magenta = 0, nullMat = 0, ped = 0;
        var pedMat = AssetDatabase.LoadAssetAtPath<Material>(PED_MAT);
        foreach (var smr in Object.FindObjectsOfType<SkinnedMeshRenderer>())
            foreach (var m in smr.sharedMaterials)
            {
                if (m == null) { nullMat++; continue; }
                if (m == pedMat) ped++;
                if (m.shader == null || m.shader.name.Contains("InternalErrorShader")) magenta++;
            }
        log.Add("材质：空槽 " + nullMat + "，洋红 " + magenta + "，路人材质槽 " + ped +
                ((nullMat == 0 && magenta == 0) ? " ✓" : " ★"));
        log.Add("");
        log.Add("（编辑模式下是 rest 姿势，Play 里才是 Idle；真要看画面截一张 Play 图）");

        Flush(log, SMOKE_REPORT);
        _smokeRunning = false;
        _smokeSecond = false;
        EditorApplication.update -= SmokeTick;
        if (File.Exists(SMOKE_STATE)) File.Delete(SMOKE_STATE);
        if (Application.isPlaying) EditorApplication.isPlaying = false;
    }

    static bool _smokeSecond;
    static int _smokeFrame2;
    static List<Snap> _smokePending;

    static void Flush(List<string> log, string path)
    {
        Directory.CreateDirectory("Assets/assets/_报告");
        File.WriteAllText(path, string.Join("\n", log.ToArray()));
        AssetDatabase.Refresh();
        Debug.Log("[GameCharSwap] 报告：" + path);
    }
}

[InitializeOnLoad]
static class GameCharSwapTrigger
{
    const string T1 = "Assets/_gcharprobe_trigger.txt";
    const string T2 = "Assets/_gcharsvap_trigger.txt";
    const string T3 = "Assets/_gcharsmoke_trigger.txt";
    const string T4 = "Assets/_gcharclips_trigger.txt";
    static double _next;

    // ★ 常驻轮询：丢触发器文件就跑，不靠域重载/焦点（用户随手丢文件就能触发）
    static GameCharSwapTrigger()
    {
        EditorApplication.update -= Poll;
        EditorApplication.update += Poll;
    }

    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < _next) return;
        _next = EditorApplication.timeSinceStartup + 0.5;
        if (Application.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return;

        bool probe = File.Exists(T1), go = File.Exists(T2), smoke = File.Exists(T3), clips = File.Exists(T4);
        if (!probe && !go && !smoke && !clips) return;
        try
        {
            if (go) GameCharSwap.Execute();
            else if (smoke) GameCharSwap.SmokeTest();
            else if (clips) GameCharSwap.RefreshLoopClips();
            else GameCharSwap.DryRun();
            foreach (var f in new[] { T1, T2, T3, T4 }) if (File.Exists(f)) File.Delete(f);   // 成功才删
        }
        catch (System.Exception e)
        {
            Debug.LogError("[GameCharSwap] " + e);
            Directory.CreateDirectory("../额外文件");
            File.WriteAllText("../额外文件/错误_Game角色替换.txt", e.ToString());
        }
    }
}
