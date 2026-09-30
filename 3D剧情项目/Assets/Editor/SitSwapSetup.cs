// 坐姿「换模型」接线（用户 2026-09-30 定稿；2026-10-01 改为使用【用户手摆的坐姿模型】）
//   宿舍 / 食堂的剧情座位（第3章_落座 / 第4章_坐下看资料 / 第5章_回座位）：
//   剧情把玩家锁住（对话中）→ 站立模型整个藏掉 + 亮出坐姿模型（用户摆在椅子上的徐夏 Sitting Idle）；
//   对话结束（解锁）后按 WASD / 走开 / 被传走 → 收起坐姿模型、站立模型回来。
//   ★ 两个模型不会同时可见；坐姿期间 WASD 不会让模型移动（SitSpot.cs 里锁住时不起身）。
//
// 用法：菜单 Tools/干预项目/坐姿换模型：接线（宿舍 + 食堂） 或丢 Assets/_sitswap_trigger.txt
// 报告：Assets/assets/_报告/_坐姿换模型.txt
//
// 做五件事（幂等，可反复跑）：
//   ① 给 FirstPersonController 认下【站立模型】（standingModel）
//   ② 收集场景里所有「徐夏坐姿变体」（带动画模型/徐夏/Sitting Idle.fbx 的实例，名字带 切换/坐姿/徐夏）：
//      · 从「第N章角色」容器挪出 → 挂到该 Loc 的「多章锚点」下（容器按章隐藏时它跟着没，座位就换不了模型）
//      · 默认 SetActive(false)、补 Animator + 徐夏_第三人称.controller + SitHere、层跟着站立模型（吃描边）
//   ③ 每个地点选一份「主坐姿模型」：优先用户最新手摆的（名字带「任务视角」），其次「玩家切换」，
//      再其次离地点最近的；其余变体保持隐藏（不删）
//   ④ 宿舍 / 食堂的剧情座位：坐姿模型指过去 + hideStandingModel + useSeatedModelPose
//      + 座位标记点（= 剧情 F 交互点）对齐到坐姿模型（X/Z + 朝向）+ 判定半径补到 >= 2.2m
//   ⑤ 第一人称「只投影」名单里带上所有坐姿模型名（不然第一人称下坐姿模型的头会怼进相机）
//
// ⚠ 会保存场景（先把当前打开的改动存下来再动手）；Play 模式下不跑。
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SitSwapSetup
{
    const string MENU = "Tools/干预项目/";
    const string GAME_SCENE = "Assets/Scenes/Game.unity";
    const string REPORT = "Assets/assets/_报告/_坐姿换模型.txt";
    const string SEAT_FBX = "Assets/assets/03_动作_Animation/带动画模型/徐夏/已绑定.fbx";
    const string SIT_CTRL = "Assets/assets/03_动作_Animation/Animators/带动画模型/徐夏_第三人称.controller";
    static readonly string[] LOCS = { "Loc_宿舍", "Loc_食堂" };

    // 剧情座位的判定半径：**必须 >= 剧情 F 交互点（StoryInteractable.radius = 2.2）**，
    // 否则玩家站在「按 F」提示范围内按下 F、却因为离座位点超过坐姿半径而不会坐下（站着对话）。
    const float STORY_SEAT_RADIUS = 2.2f;

    static readonly StringBuilder _log = new StringBuilder();

    [MenuItem(MENU + "坐姿换模型：接线（宿舍 + 食堂）", false, 48)]
    public static void Run()
    {
        _log.Clear();
        _log.AppendLine("坐姿换模型接线  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        _log.AppendLine("（坐下 = 藏站立模型 + 显示坐姿模型；起身 = 换回来）");
        _log.AppendLine();
        try { Core(); }
        catch (System.Exception e)
        {
            _log.AppendLine();
            _log.AppendLine("★ 异常中止：" + e);
            Flush();
            try
            {
                Directory.CreateDirectory("../额外文件");
                File.WriteAllText("../额外文件/错误_坐姿换模型.txt", e.ToString());
            }
            catch { }
            Debug.LogError("[SitSwapSetup] " + e);
        }
    }

    static void Core()
    {
        if (EditorApplication.isPlaying)
        {
            _log.AppendLine("★ 正在 Play 模式，先停下再跑（运行时改的场景不会存盘）");
            Flush();
            return;
        }

        // 0) 保住当前打开的改动，再切到 Game.unity
        var cur = SceneManager.GetActiveScene();
        if (cur.IsValid() && cur.path != GAME_SCENE)
        {
            EditorSceneManager.SaveOpenScenes();
            EditorSceneManager.OpenScene(GAME_SCENE, OpenSceneMode.Single);
        }
        else if (cur.IsValid() && cur.isDirty)
        {
            EditorSceneManager.SaveOpenScenes();        // 你自己在编辑器里改的先存下来
            _log.AppendLine("（先把当前场景的未保存改动存了）");
        }
        var scene = SceneManager.GetActiveScene();

        var fpc = Object.FindObjectOfType<FirstPersonController>(true);
        if (fpc == null) { _log.AppendLine("★ 场景里没有 FirstPersonController"); Flush(); return; }
        _log.AppendLine("【① 站立模型】");
        _log.AppendLine("  玩家 = " + PathOf(fpc.transform) + "，模型子物体 = " +
                        string.Join("、", fpc.transform.Cast<Transform>().Select(t => t.name).ToArray()));

        var standing = fpc.standingModel;
        if (standing == null)
        {
            foreach (Transform c in fpc.transform)
            {
                if (c.GetComponentInChildren<Camera>(true) != null) continue;
                if (c.name.Contains("坐姿")) continue;
                if (c.GetComponentInChildren<SkinnedMeshRenderer>(true) != null) { standing = c.gameObject; break; }
            }
        }
        if (standing == null) _log.AppendLine("  ★ 找不到站立模型（自己带蒙皮的子物体）");
        else
        {
            fpc.standingModel = standing;
            EditorUtility.SetDirty(fpc);
            _log.AppendLine("  standingModel = " + standing.name);
        }

        // ② 收集所有坐姿变体（先记录，再挪）
        _log.AppendLine();
        _log.AppendLine("【② 场景里的徐夏坐姿变体（Sitting Idle.fbx 的实例）】");
        var variants = FindSitVariants();
        if (variants.Count == 0)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(SEAT_FBX);
            if (fbx == null) { _log.AppendLine("  ★ 场景里没有坐姿变体，也找不到 " + SEAT_FBX); Flush(); return; }
            var g = (GameObject)PrefabUtility.InstantiatePrefab(fbx, fpc.transform);
            g.name = "徐夏_坐姿";
            variants.Add(g);
            _log.AppendLine("  + 场景里没有坐姿变体 → 新建 徐夏_坐姿");
        }
        if (variants.Count > 0)
        {
            foreach (var v in variants)
                _log.AppendLine("  · " + PathOf(v.transform) + "  位置 " + v.transform.position.ToString("F2") +
                                "  朝向 " + v.transform.eulerAngles.y.ToString("F0") + "°  active=" + v.activeSelf);
        }
        _log.AppendLine();
        _log.AppendLine("【②′ 坐姿变体整理】");
        foreach (var v in variants)
        {
            // 挪出「第N章角色」容器：容器按章隐藏时它跟着没 → 那个地点的座位就换不了模型
            var holder = HolderFor(v);
            if (holder != null && v.transform.parent != holder)
            {
                _log.AppendLine("  → " + v.name + "：从 " + PathOf(v.transform) + " 挪到 " + PathOf(holder) +
                                "（不然容器按章隐藏时它就没了；世界位置保持不变）");
                v.transform.SetParent(holder, true);
            }
            if (v.transform.parent == fpc.transform) v.transform.localPosition = Vector3.zero;
            if (standing != null) { v.transform.localScale = standing.transform.localScale; SetLayerRecursively(v, standing.layer); }
            if (v.activeSelf) { v.SetActive(false); _log.AppendLine("  · 已设隐藏：" + PathOf(v.transform)); }
        }

        // ③ 每个地点选主坐姿模型 + 补齐 Animator / SitHere
        _log.AppendLine();
        _log.AppendLine("【③ 每个地点用哪份坐姿模型】");
        var primary = new Dictionary<string, GameObject>();
        foreach (var locName in LOCS)
        {
            var loc = GameObject.Find(locName);
            if (loc == null) { _log.AppendLine("  ★ 场景里没有 " + locName); continue; }
            var pick = PickSitForLoc(variants, loc.transform);
            primary[locName] = pick;
            _log.AppendLine("  " + locName + " → " + (pick != null ? PathOf(pick.transform) : "★没找到") +
                            (pick != null ? "（位置 " + pick.transform.position.ToString("F2") + " 朝向 " +
                                            pick.transform.eulerAngles.y.ToString("F0") + "°）" : ""));
        }
        foreach (var kv in primary)
        {
            var v = kv.Value;
            if (v == null) continue;
            // 清掉上一次跑歪时加在【骨头】上的 Animator / SitHere（加到 mixamorig:* 上会毁姿势）
            int cleaned = 0;
            foreach (var tt in v.GetComponentsInChildren<Transform>(true))
            {
                if (tt.gameObject == v) continue;
                var sh0 = tt.GetComponent<SitHere>();
                if (sh0 != null) { Object.DestroyImmediate(sh0); cleaned++; }
                var an0 = tt.GetComponent<Animator>();
                if (an0 != null) { Object.DestroyImmediate(an0); cleaned++; }
            }
            if (cleaned > 0) _log.AppendLine("  − 清掉 " + v.name + " 骨头上多余的组件 " + cleaned + " 个");

            // 坐姿要真坐着：Mixamo 的 FBX 不带 Animator → 补一个 + 徐夏_第三人称（Sit 状态）+ SitHere
            var an = v.GetComponent<Animator>();
            if (an == null) { an = v.AddComponent<Animator>(); _log.AppendLine("  + " + v.name + " 没有 Animator，已补一个"); }
            var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(SIT_CTRL);
            if (ctrl != null && an.runtimeAnimatorController != ctrl) an.runtimeAnimatorController = ctrl;
            an.applyRootMotion = false;
            an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            an.updateMode = AnimatorUpdateMode.Normal;
            if (v.GetComponent<SitHere>() == null) v.AddComponent<SitHere>();
            if (v.activeSelf) v.SetActive(false);
            EditorUtility.SetDirty(v);
            _log.AppendLine("  = 坐姿模型 " + PathOf(v.transform) + "  控制器 " +
                            (an.runtimeAnimatorController != null ? an.runtimeAnimatorController.name : "★空") +
                            "  （Sit 状态由 Sitting 参数进入）");
        }

        // ④ 座位点接线 + 把座位标记点（= 剧情 F 交互点）对齐到坐姿模型
        _log.AppendLine();
        _log.AppendLine("【④ 宿舍 / 食堂的座位点】");
        int wired = 0;
        foreach (var locName in LOCS)
        {
            var loc = GameObject.Find(locName);
            if (loc == null) continue;
            var sit = primary.ContainsKey(locName) ? primary[locName] : null;
            var spots = loc.GetComponentsInChildren<SitSpot>(true);
            _log.AppendLine("  " + locName + "：" + spots.Length + " 个 SitSpot" +
                            (sit != null ? "，用 " + PathOf(sit.transform) : "，★没有坐姿模型"));
            foreach (var s in spots)
            {
                // ★ 用户 2026-09-30 定稿：**只有剧情里要坐的才坐** ——
                //   自由「按 F 坐下」的点一律关掉（组件 disabled，不删节点；想开回来把 enabled 勾上即可）
                bool storySeat = s.mode == SitMode.剧情锁住自动坐下;
                s.enabled = storySeat;
                s.seatedModel = sit;
                s.hideStandingModel = true;
                s.showPrompt = storySeat;
                s.useSeatedModelPose = true;                  // 坐姿跟模型（用户摆哪儿坐哪儿）
                s.onlyChapters = new int[0];                  // 不限章节（要限就自己填）
                if (storySeat) wired++;

                string moved = "";
                if (storySeat && sit != null)
                {
                    // F 交互点跟着坐姿模型走：玩家走到椅子边按 F，就地坐下（不然人在标记点、坐姿在别处，
                    // 视觉上会「瞬移 1 米多」——2026-10-01 用户摆的坐姿模型不在老标记点上）
                    var p = s.transform.position;
                    var m = sit.transform.position;
                    float dxz = new Vector2(p.x - m.x, p.z - m.z).magnitude;
                    p.x = m.x; p.z = m.z;
                    s.transform.position = p;
                    s.transform.rotation = Quaternion.Euler(0f, sit.transform.eulerAngles.y, 0f);
                    if (s.radius < STORY_SEAT_RADIUS) s.radius = STORY_SEAT_RADIUS;
                    moved = "  标记点" + (dxz > 0.05f ? "从 " + p.ToString("F2") + " 前挪 " + dxz.ToString("F2") + "m" : "已在模型处（差 " + dxz.ToString("F2") + "m）") +
                            "，朝向 " + sit.transform.eulerAngles.y.ToString("F0") + "°，半径 " + s.radius.ToString("F1") + "m";
                }
                EditorUtility.SetDirty(s);
                _log.AppendLine("    " + (storySeat ? "= 剧情座位 " : "− 关掉自由坐下 ") + PathOf(s.transform) +
                                "  mode=" + s.mode + "  radius=" + s.radius +
                                (storySeat && sit != null ? "  → 坐姿模型 " + PathOf(sit.transform) : "（要开把 SitSpot 勾上）") + moved);
            }
        }
        _log.AppendLine("  共接线 " + wired + " 个座位点");

        // ⑤ 第一人称：站立 + 所有坐姿模型都列进「只投影」名单
        _log.AppendLine();
        _log.AppendLine("【⑤ 第一人称隐藏名单】");
        var list = new List<string>(fpc.firstPersonShadowsOnlyParts ?? new string[0]);
        int dropped = list.RemoveAll(x => !string.IsNullOrEmpty(x) && x.Contains(":"));
        if (dropped > 0) _log.AppendLine("  − 清掉名单里误加的骨头名 " + dropped + " 条");
        var names = new List<string>();
        if (standing != null) names.Add(standing.name);
        foreach (var v in variants) names.Add(v.name);
        foreach (var nm in names)
        {
            if (string.IsNullOrEmpty(nm)) continue;
            if (list.Any(x => x == nm)) { _log.AppendLine("  = 已有 " + nm); continue; }
            list.Add(nm);
            _log.AppendLine("  + " + nm);
        }
        fpc.firstPersonShadowsOnlyParts = list.ToArray();
        EditorUtility.SetDirty(fpc);

        EditorSceneManager.MarkSceneDirty(scene);
        bool ok = EditorSceneManager.SaveScene(scene);
        _log.AppendLine();
        _log.AppendLine("保存场景 " + GAME_SCENE + "：" + (ok ? "成功 ✓" : "★失败"));
        _log.AppendLine();
        _log.AppendLine("【怎么验】");
        _log.AppendLine("  · 只有【剧情要坐】的座位才坐（自由「按 F 坐下」的点已关掉）：");
        _log.AppendLine("    走到座位旁 → 剧情把玩家锁住（对话）→ 站立模型消失、坐姿徐夏出现；");
        _log.AppendLine("    对话结束（解锁）后按 WASD / 走开 → 换回站立模型（坐姿期间 WASD 不会动）");
        _log.AppendLine("  · 坐姿位置/朝向 = 用户在场景里摆的坐姿模型的位置/朝向；座位标记点已对齐到模型");
        _log.AppendLine("  · 位置/朝向不满意：直接把坐姿模型拖到想要的地方/转个方向，重跑本工具或直接 Play 生效");
        Flush();
        Debug.Log("[SitSwapSetup] 完成，报告：" + REPORT);
    }

    /// <summary>
    /// 场景里的「徐夏坐姿变体」：名字带「切换 / 坐姿 / 徐夏」的、源是 带动画模型/徐夏/Sitting*.fbx 的实例
    /// （用户按地点各摆一份，例：Loc_宿舍/第五章角色/徐夏任务视角切换）。
    /// ★ 只取 prefab/FBX 实例的【根】：FindObjectsOfType 会把 mixamorig:* 骨头也列出来（踩过）。
    /// </summary>
    static List<GameObject> FindSitVariants()
    {
        var res = new List<GameObject>();
        foreach (var go in Object.FindObjectsOfType<GameObject>(true))
        {
            if (PrefabUtility.GetOutermostPrefabInstanceRoot(go) != go) continue;
            var src = PrefabUtility.GetCorrespondingObjectFromSource(go);
            if (src == null) continue;
            string path = AssetDatabase.GetAssetPath(src);
            if (!path.Contains("带动画模型/徐夏") || !path.Contains("Sitting")) continue;
            if (!(go.name.Contains("切换") || go.name.Contains("坐姿") || go.name == "徐夏")) continue;
            res.Add(go);
        }
        return res;
    }

    /// <summary>这个地点用哪份坐姿模型：优先用户最新手摆的（名字带「任务视角」）→「玩家切换」→ 离地点最近</summary>
    static GameObject PickSitForLoc(List<GameObject> variants, Transform loc)
    {
        var pool = variants.Where(v => v.transform.IsChildOf(loc)).ToList();
        if (pool.Count == 0) pool = variants;                      // 兜底：全场景里找
        var pick = pool.FirstOrDefault(v => v.name.Contains("任务视角"));
        if (pick != null) return pick;
        pick = pool.FirstOrDefault(v => v.name.Contains("玩家切换"));
        if (pick != null) return pick;
        return Nearest(pool, loc.position);
    }

    /// <summary>坐姿变体该挂哪儿：挪出「第N章角色」容器 → 挂在最近的 Loc 的「多章锚点」下（永远激活）</summary>
    static Transform HolderFor(GameObject v)
    {
        Transform loc = null;
        for (var t = v.transform.parent; t != null; t = t.parent)
            if (t.name.StartsWith("Loc_")) { loc = t; break; }
        if (loc == null) return v.transform.parent;                 // 不在任何 Loc 里：不动
        foreach (Transform c in loc)
            if (c.name == "多章锚点") return c;
        return loc;
    }

    static GameObject Nearest(List<GameObject> list, Vector3 p)
    {
        GameObject best = null; float bestD = float.MaxValue;
        foreach (var v in list)
        {
            float d = (v.transform.position - p).sqrMagnitude;
            if (d < bestD) { bestD = d; best = v; }
        }
        return best;
    }

    static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform c in go.transform) SetLayerRecursively(c.gameObject, layer);
    }

    static string PathOf(Transform t)
    {
        var s = t.name;
        for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
        return s;
    }

    static void Flush()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(REPORT).Replace('/', Path.DirectorySeparatorChar));
        File.WriteAllText(REPORT, _log.ToString());
    }
}

// 丢 Assets/_sitswap_trigger.txt → 下次刷新/重编译后自动跑一次
[InitializeOnLoad]
public static class SitSwapSetupTrigger
{
    const string Trigger = "Assets/_sitswap_trigger.txt";

    static SitSwapSetupTrigger()
    {
        if (!File.Exists(Trigger)) return;
        EditorApplication.delayCall += () =>
        {
            try
            {
                if (File.Exists(Trigger)) File.Delete(Trigger);
                if (File.Exists(Trigger + ".meta")) File.Delete(Trigger + ".meta");
                SitSwapSetup.Run();
            }
            catch (System.Exception e)
            {
                Directory.CreateDirectory("../额外文件");
                File.WriteAllText("../额外文件/错误_坐姿换模型.txt", e.ToString());
                Debug.LogError("[SitSwapSetup] " + e);
            }
        };
    }
}
