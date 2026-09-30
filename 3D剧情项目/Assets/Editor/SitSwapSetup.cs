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
        _log.AppendLine("【② 坐姿模型（默认隐藏）】");
        GameObject sit = null;
        foreach (Transform c in fpc.transform)
            if (c.name.Contains("坐姿")) { sit = c.gameObject; break; }

        if (sit == null)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(SEAT_FBX);
            if (fbx == null) { _log.AppendLine("  ★ 找不到 " + SEAT_FBX); Flush(); return; }
            sit = (GameObject)PrefabUtility.InstantiatePrefab(fbx, fpc.transform);
            sit.name = SIT_MODEL_NAME;
            _log.AppendLine("  + 新建 " + SIT_MODEL_NAME + "（实例化 " + SEAT_FBX + "）");
        }
        else _log.AppendLine("  = 已有 " + sit.name + "，复用");

        // 层级/层：和站立模型一致（描边层），位置归零（坐下时 SitSpot 会摆）
        sit.transform.localPosition = Vector3.zero;
        sit.transform.localRotation = Quaternion.identity;
        sit.transform.localScale = standing != null ? standing.transform.localScale : Vector3.one;
        if (standing != null) SetLayerRecursively(sit, standing.layer);

        var an = sit.GetComponentInChildren<Animator>(true);
        if (an == null)
        {
            // ★ Mixamo 导回的 FBX 本身不带 Animator（同 GameCharSwap 的坑）→ 自己加在模型根上
            an = sit.GetComponent<Animator>();
            if (an == null) an = sit.AddComponent<Animator>();
            _log.AppendLine("  + 模型上没有 Animator，已补一个（模型根上）");
        }
        {
            var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(SIT_CTRL);
            if (ctrl != null && an.runtimeAnimatorController != ctrl) an.runtimeAnimatorController = ctrl;
            an.applyRootMotion = false;
            an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            an.updateMode = AnimatorUpdateMode.Normal;
            _log.AppendLine("  Animator 控制器 = " + (an.runtimeAnimatorController != null ? an.runtimeAnimatorController.name : "★空") +
                            "（Sit 状态由 Sitting 参数进入）");
        }
        if (sit.GetComponent<SitHere>() == null) { sit.AddComponent<SitHere>(); _log.AppendLine("  + 挂 SitHere（Start 置 Sitting=true）"); }
        if (sit.activeSelf) { sit.SetActive(false); _log.AppendLine("  已设为隐藏（默认不显示）"); }

        // ③ 第一人称：两个模型都列进「只投影」名单
        _log.AppendLine();
        _log.AppendLine("【③ 第一人称隐藏名单】");
        var list = new List<string>(fpc.firstPersonShadowsOnlyParts ?? new string[0]);
        foreach (var nm in new[] { standing != null ? standing.name : null, sit.name })
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
            foreach (var s in spots)
            {
                s.enabled = true;
                s.seatedModel = sit;
                s.hideStandingModel = true;
                s.showPrompt = true;
                s.onlyChapters = new int[0];                 // 不限章节（要限就自己填）
                EditorUtility.SetDirty(s);
                wired++;
                _log.AppendLine("    = " + PathOf(s.transform) + "  mode=" + s.mode + "  radius=" + s.radius +
                                "  → 坐姿模型 " + sit.name);
            }
        }
        _log.AppendLine("  共接线 " + wired + " 个座位点");

        EditorSceneManager.MarkSceneDirty(scene);
        bool ok = EditorSceneManager.SaveScene(scene);
        _log.AppendLine();
        _log.AppendLine("保存场景 " + GAME_SCENE + "：" + (ok ? "成功 ✓" : "★失败"));
        _log.AppendLine();
        _log.AppendLine("【怎么验】");
        _log.AppendLine("  · 进 Play → 走到宿舍书桌旁 / 食堂座位旁 → 屏幕上出现「按 F 坐下」→ 按 F：");
        _log.AppendLine("    站立模型消失、坐姿徐夏出现（第三人称看得最清楚）；按 WASD 或再按 F 起身换回来");
        _log.AppendLine("  · 剧情锚点那三个（第3章_落座 / 第4章_坐下看资料 / 第5章_回座位）是「剧情锁住自动坐」模式：");
        _log.AppendLine("    玩家被剧情锁在该点 1.3m 内就会自动换模型");
        _log.AppendLine("  · 位置/朝向不满意：直接改那些 SitSpot 的 radius / seat（坐姿模型的朝向跟凳子一致）");
        Flush();
        Debug.Log("[SitSwapSetup] 完成，报告：" + REPORT);
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
