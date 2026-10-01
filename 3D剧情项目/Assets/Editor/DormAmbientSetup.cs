// 第四章宿舍氛围角色接线（幂等，2026-10-01）
//
// 背景：第4章宿舍开场只有 林溪_宿舍（enter 前预藏）→ 空荡荡。用户手摆了三个氛围角色进
//   Loc_宿舍/第四章角色（舍友A / 王含坐着 / 舍友B，源 = 带动画模型的 Idle.fbx / Sitting Idle.fbx），
//   运行时由 StoryRunner.ApplyChapterNpcVisibility 按章显隐（第4章显示、其它章整棵隐藏），
//   林溪_宿舍 照旧由 enter/leave 步骤按剧本点亮/隐藏——这些引擎侧本来就对，不用改。
//   但裸拖进来的 FBX 实例缺三样仓库角色惯例配置：
//   ① 层不在 Outline —— 其它角色都有描边，这三个没有；
//   ② 根上没碰撞体 —— 玩家会直接穿过去（EnsureCharacterColliders 只在后期大菜单 ① 里跑）；
//   ③ 没有 Animator —— Mixamo 导回的 FBX 不带（不加 = rest 姿势：舍友A/B 会 T-pose 站桩）。
//
// 做什么（缺啥补啥，已有的不动；只动 Loc_宿舍/第四章角色 的直接子实例）：
//   ① 整棵子树 Layer = Outline；
//   ② 根节点补 CapsuleCollider（尺寸公式照抄 ScenePostFx.FitCapsule：高=包围盒高×0.96、
//      半径=高×0.16 钳 0.12~0.50，不按 X 跨度算——T-pose 手臂会撑出巨大胶囊）；
//   ③ 实例名去掉「坐着/Sitting」后能在 Animators/带动画模型/ 找到 <名>_Idle.controller 的：
//      实例根补 Animator（FBX 与剪辑同源，Generic 零重定向，同 LinxiCutviewSetup 先例）；
//      名字带「坐着/Sitting」的再加 SitHere（Start 置 Sitting=true → 控制器 AnyState→Sit
//      播坐姿循环，同 陆宣雨_可动_坐着 先例）；找不到同名控制器就保持静态姿势并写进报告。
//   ⚠ 已接好线的实例（如 林溪_宿舍：子模型上挂着 林溪_Idle）会被「已有可用 Animator」检测放行，
//     原样跳过，不会被覆盖。
//   报告 assets/_报告/_第四章宿舍氛围角色.txt；有改动则保存 Game.unity（连同用户未存的手改）。
//
// 菜单：Tools/干预项目/第四章宿舍氛围角色/接线（幂等）
// 触发器：Assets/_dormambient_trigger.txt（丢进 Assets 下次刷新自动跑，用完自删）
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DormAmbientSetup
{
    const string REPORT = "Assets/assets/_报告/_第四章宿舍氛围角色.txt";
    const string CTRL_DIR = "Assets/assets/03_动作_Animation/Animators/带动画模型";

    [MenuItem("Tools/干预项目/第四章宿舍氛围角色/接线（幂等）", false, 140)]
    public static void Run()
    {
        var log = new List<string>();
        log.Add("第四章宿舍氛围角色：接线  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));

        var container = FindContainer(log);
        if (container == null) { log.Add(""); log.Add("★ 中止：找不到 Loc_宿舍/第四章角色。场景未改。"); Flush(log); return; }

        int layer = LayerMask.NameToLayer("Outline");
        if (layer < 0) { log.Add(""); log.Add("★ 中止：没有 Outline 层（先跑一次 场景后期效果/① 一键布置 建层）。场景未改。"); Flush(log); return; }

        log.Add("容器：" + PathOf(container) + "  activeSelf=" + container.gameObject.activeSelf
                + "（运行时 Begin 还会按章强制显隐，这里只是编辑器现场）");
        log.Add("");

        bool changed = false;
        for (int i = 0; i < container.childCount; i++)
        {
            var go = container.GetChild(i).gameObject;
            log.Add("── " + go.name + (go.activeSelf ? "" : "（当前隐藏，照样接线）"));
            changed |= TagLayer(go, layer, log);
            changed |= FitCapsule(go, log);
            changed |= EnsureAnimator(go, log);
            log.Add("");
        }

        if (changed)
        {
            var scene = container.gameObject.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.Add("已保存场景：" + scene.path);
        }
        else
        {
            log.Add("本次零改动（幂等重跑）。");
        }
        Flush(log);
    }

    // ------------------------------------------------------------ 找容器（只认 宿舍 那份；图书馆等 Loc 也有 第四章角色，不碰）
    static Transform FindContainer(List<string> log)
    {
        var locsRoot = GameObject.Find("Locations");
        Transform dorm = null;
        if (locsRoot != null)
            foreach (Transform c in locsRoot.transform)
                if (c.name == "Loc_宿舍") { dorm = c; break; }
        if (dorm == null)   // 兜底：按名全场景找（含禁用）
            foreach (var t in Object.FindObjectsOfType<Transform>(true))
                if (t.name == "Loc_宿舍") { dorm = t; break; }
        if (dorm == null) { log.Add("★ 场景里没有 Loc_宿舍"); return null; }

        for (int i = 0; i < dorm.childCount; i++)
            if (dorm.GetChild(i).name == "第四章角色") return dorm.GetChild(i);
        log.Add("★ " + PathOf(dorm) + " 下没有「第四章角色」容器");
        return null;
    }

    // ------------------------------------------------------------ ① 描边层
    static bool TagLayer(GameObject root, int layer, List<string> log)
    {
        int changed = 0, total = 0;
        Apply(root.transform);
        log.Add("  层 → Outline：子树 " + total + " 个节点，本次改 " + changed + "（其余本就是）");
        return changed > 0;

        void Apply(Transform t)
        {
            total++;
            if (t.gameObject.layer != layer) { t.gameObject.layer = layer; changed++; }
            foreach (Transform c in t) Apply(c);
        }
    }

    // ------------------------------------------------------------ ② 根胶囊碰撞体（公式照抄 ScenePostFx.FitCapsule）
    static bool FitCapsule(GameObject go, List<string> log)
    {
        if (go.GetComponent<CapsuleCollider>() != null)
        {
            log.Add("  碰撞体：已有 CapsuleCollider，不动");
            return false;
        }
        var b = BoundsOf(go);
        if (!b.HasValue) { log.Add("  ★ 没有渲染器，配不了碰撞体"); return false; }
        var bb = b.Value;
        float h = bb.size.y;
        if (h < 0.5f) { log.Add(string.Format("  ★ 渲染包围盒高 {0:0.00}m < 0.5m，不像个人，不配", h)); return false; }

        float r = Mathf.Clamp(h * 0.16f, 0.12f, 0.50f);
        float cap = Mathf.Max(h * 0.96f, r * 2.01f);
        var col = go.AddComponent<CapsuleCollider>();
        col.direction = 1;
        col.radius = r;
        col.height = cap;
        col.isTrigger = false;
        var t = go.transform;
        Vector3 worldCenter = new Vector3(bb.center.x, bb.min.y + cap * 0.5f, bb.center.z);
        col.center = t.InverseTransformPoint(worldCenter);
        EditorUtility.SetDirty(col);
        log.Add(string.Format("  + CapsuleCollider（高 {0:0.00}m / 半径 {1:0.00}m，包围盒高 {2:0.00}m）", cap, r, h));
        return true;
    }

    static Bounds? BoundsOf(GameObject go)
    {
        bool has = false;
        var b = new Bounds();
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (!has) { b = r.bounds; has = true; }
            else b.Encapsulate(r.bounds);
        }
        return has ? (Bounds?)b : null;
    }

    // ------------------------------------------------------------ ③ Animator（+ 坐姿的 SitHere）
    static bool EnsureAnimator(GameObject go, List<string> log)
    {
        // 已有能用的（enabled 且有 controller，在根或子树都算）→ 齐了，原样跳过
        foreach (var a in go.GetComponentsInChildren<Animator>(true))
        {
            if (a.enabled && a.runtimeAnimatorController != null)
            {
                log.Add("  Animator：已有（" + a.runtimeAnimatorController.name + " @" + a.name + "），不动");
                return false;
            }
        }

        string key = go.name.Replace("坐着", "").Replace("Sitting", "").Trim();
        string path = CTRL_DIR + "/" + key + "_Idle.controller";
        var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(path);
        if (ctrl == null)
        {
            log.Add("  ★ 找不到控制器 " + path + "，保持静态姿势（不瞎接）");
            return false;
        }

        var anim = go.GetComponent<Animator>();
        if (anim == null) { anim = go.AddComponent<Animator>(); log.Add("  + Animator（加在实例根上；Generic 按节点路径回放）"); }
        anim.runtimeAnimatorController = ctrl;
        EditorUtility.SetDirty(anim);
        log.Add("  + controller = " + key + "_Idle");

        bool seated = go.name.Contains("坐着") || go.name.ToLower().Contains("sitting");
        if (seated && go.GetComponent<SitHere>() == null)
        {
            go.AddComponent<SitHere>();
            log.Add("  + SitHere（Start 置 Sitting=true → AnyState→Sit 播坐姿循环）");
        }
        return true;
    }

    static string PathOf(Transform t)
    {
        var names = new List<string>();
        for (var p = t; p != null; p = p.parent) names.Insert(0, p.name);
        return string.Join("/", names.ToArray());
    }

    // ------------------------------------------------------------ 报告
    static void Flush(List<string> log)
    {
        Directory.CreateDirectory("Assets/assets/_报告");
        File.WriteAllText(REPORT, string.Join("\n", log.ToArray()));
        AssetDatabase.Refresh();
        Debug.Log("[DormAmbientSetup] 报告：" + REPORT);
    }
}

// 丢 Assets/_dormambient_trigger.txt → 下次刷新/重编译后自动跑一次
[InitializeOnLoad]
public static class DormAmbientTrigger
{
    const string Trigger = "Assets/_dormambient_trigger.txt";
    const string ErrFile = "../额外文件/错误_第四章宿舍氛围角色.txt";

    static DormAmbientTrigger()
    {
        if (!File.Exists(Trigger)) return;
        EditorApplication.delayCall += () =>
        {
            try
            {
                if (File.Exists(Trigger)) File.Delete(Trigger);
                if (File.Exists(Trigger + ".meta")) File.Delete(Trigger + ".meta");
                DormAmbientSetup.Run();
            }
            catch (System.Exception e)
            {
                Directory.CreateDirectory("../额外文件");
                File.WriteAllText(ErrFile, e.ToString());
                Debug.LogError("[DormAmbientSetup] 失败: " + e);
            }
        };
    }
}
