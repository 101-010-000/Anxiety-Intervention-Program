// 坐姿「换模型」接线（用户 2026-09-30 定稿）：
//   场景里预先摆一个【默认隐藏的坐姿模型】（挂在 Player_徐夏 下、名字带「坐姿」），
//   玩家触发坐下（按 F / 剧情锁住自动坐）时 → 站立模型隐藏 + 坐姿模型显示；
//   按 WASD / 走开 / 再按 F → 切回站立模型。
//
// 用法：菜单 Tools/干预项目/坐姿换模型：接线（宿舍 + 食堂） 或丢 Assets/_sitswap_trigger.txt
// 报告：Assets/assets/_报告/_坐姿换模型.txt
//
// 做四件事（幂等，可反复跑）：
//   ① 给 FirstPersonController 认下【站立模型】（standingModel，默认 auto：第一个带蒙皮的子物体）
//   ② 在 Player_徐夏 下建/复用一个 `徐夏_坐姿`（= 带动画模型/徐夏/已绑定.fbx 的实例，
//      Animator 用 徐夏_第三人称.controller + SitHere 强制坐姿状态，默认 SetActive(false)）
//   ③ 第一人称把站立/坐姿模型都列进 firstPersonShadowsOnlyParts（不然坐姿模型的头会怼进相机）
//   ④ 宿舍 + 食堂里所有 SitSpot：启用组件、坐姿模型指过去、hideStandingModel=true、不限章节
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
    const string SIT_MODEL_NAME = "徐夏_坐姿";
    static readonly string[] LOCS = { "Loc_宿舍", "Loc_食堂" };

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

        _log.AppendLine();
        _log.AppendLine("【② 坐姿变体（默认隐藏；一个地点一份）】");
        var variants = FindSitVariants();
        if (variants.Count == 0)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(SEAT_FBX);
            if (fbx == null) { _log.AppendLine("  ★ 场景里没有坐姿变体，也找不到 " + SEAT_FBX); Flush(); return; }
            var g = (GameObject)PrefabUtility.InstantiatePrefab(fbx, fpc.transform);
            g.name = SIT_MODEL_NAME;
            variants.Add(g);
            _log.AppendLine("  + 场景里没有坐姿变体 → 新建 " + SIT_MODEL_NAME);
        }
        foreach (var v in variants)
        {
            // 挪出「第N章角色」容器：容器按章隐藏时它跟着没 → 那个地点的座位就换不了模型
            var holder = HolderFor(v);
            if (holder != null && v.transform.parent != holder)
            {
                _log.AppendLine("  从 " + PathOf(v.transform) + " 挪到 " + PathOf(holder) + "（不然容器按章隐藏时它就没了）");
                v.transform.SetParent(holder, true);
            }
            if (v.transform.parent == fpc.transform) v.transform.localPosition = Vector3.zero;
            if (standing != null) { v.transform.localScale = standing.transform.localScale; SetLayerRecursively(v, standing.layer); }

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

            if (v.activeSelf) { v.SetActive(false); _log.AppendLine("  已设隐藏：" + PathOf(v.transform)); }
            EditorUtility.SetDirty(v);
            _log.AppendLine("  = 坐姿变体 " + PathOf(v.transform) + "  控制器 " +
                            (an.runtimeAnimatorController != null ? an.runtimeAnimatorController.name : "★空") + "  （Sit 状态由 Sitting 参数进入）");
        }

        // ③ 第一人称：两个模型都列进「只投影」名单
        _log.AppendLine();
        _log.AppendLine("【③ 第一人称隐藏名单】");
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

        // ④ 宿舍 + 食堂的 SitSpot
        _log.AppendLine();
        _log.AppendLine("【④ 宿舍 / 食堂的座位点】");
        int wired = 0;
        foreach (var locName in LOCS)
        {
            var loc = GameObject.Find(locName);
            if (loc == null) { _log.AppendLine("  ★ 场景里没有 " + locName); continue; }
            var spots = loc.GetComponentsInChildren<SitSpot>(true);
            _log.AppendLine("  " + locName + "：" + spots.Length + " 个 SitSpot");
            // 这个地点用哪个坐姿变体：优先“本来就在这个地点里的那个”（宿舍一个、食堂一个）
            GameObject sit = null;
            foreach (var v in variants)
                if (v.transform.IsChildOf(loc.transform) || PathOf(v.transform).Contains(locName)) { sit = v; break; }
            if (sit == null) sit = NearestVariant(variants, loc.transform.position);
            if (sit == null) sit = variants[0];
            _log.AppendLine("    地点用的坐姿变体 = " + PathOf(sit.transform));
            foreach (var s in spots)
            {
                // ★ 用户 2026-09-30 定稿：**只有剧情里要坐的才坐** ——
                //   自由「按 F 坐下」的点一律关掉（组件 disabled，不删节点；想开回来把 enabled 勾上即可）
                bool storySeat = s.mode == SitMode.剧情锁住自动坐下;
                s.enabled = storySeat;
                s.seatedModel = sit;
                s.hideStandingModel = true;
                s.showPrompt = storySeat;
                s.onlyChapters = new int[0];                 // 不限章节（要限就自己填）
                EditorUtility.SetDirty(s);
                if (storySeat) wired++;
                _log.AppendLine("    " + (storySeat ? "= 剧情座位 " : "− 关掉自由坐下 ") + PathOf(s.transform) +
                                "  mode=" + s.mode + "  radius=" + s.radius +
                                (storySeat ? "  → 坐姿模型 " + PathOf(sit.transform) : "（要开把 SitSpot 勾上）"));
            }
        }
        _log.AppendLine("  共接线 " + wired + " 个座位点");

        EditorSceneManager.MarkSceneDirty(scene);
        bool ok = EditorSceneManager.SaveScene(scene);
        _log.AppendLine();
        _log.AppendLine("保存场景 " + GAME_SCENE + "：" + (ok ? "成功 ✓" : "★失败"));
        _log.AppendLine();
        _log.AppendLine("【怎么验】");
        _log.AppendLine("  · 只有【剧情要坐】的座位才坐（自由「按 F 坐下」的点已关掉）：");
        _log.AppendLine("    走到座位旁 → 剧情把玩家锁住（对话）→ 站立模型消失、坐姿徐夏出现；");
        _log.AppendLine("    剧情放开/走开 → 换回站立模型");
        _log.AppendLine("  · 剧情锚点那三个（第3章_落座 / 第4章_坐下看资料 / 第5章_回座位）是「剧情锁住自动坐」模式：");
        _log.AppendLine("    玩家被剧情锁在该点 1.3m 内就会自动换模型");
        _log.AppendLine("  · 位置/朝向不满意：直接改那些 SitSpot 的 radius / seat（坐姿模型的朝向跟凳子一致）");
        Flush();
        Debug.Log("[SitSwapSetup] 完成，报告：" + REPORT);
    }

    /// <summary>
    /// 场景里的「玩家坐姿变体」：名字带「切换 / 坐姿」的、源是 带动画模型/徐夏/Sitting Idle.fbx 的实例
    /// （用户按地点各摆一份，例：Loc_宿舍/第五章角色/玩家切换、Loc_食堂/第五章角色/玩家切换）。
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

    static GameObject NearestVariant(List<GameObject> list, Vector3 p)
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
