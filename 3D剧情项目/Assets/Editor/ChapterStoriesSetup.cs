// 多章剧情搭建（第2-5章）：一键搭运行节点 + 锚点/触发点 + 接线 + 报告（幂等）。
//
// 用法：Tools/干预项目/多章剧情/一键搭建第2-5章（幂等）
//   · StorySystem 下建「第2章」~「第5章」子节点，各挂 StoryRunner：
//     绑 Assets/数据/剧情/第N章.json、chapterIndex=N、startAnchor、fadeAnchors；
//     11 个 UI 引用从第1章 runner 原样复制（= 全章共用 UI交互 画布同一套节点，零新建 UI）。
//   · 各 Loc 下建「多章锚点」容器：章节起点、F 交互点（StoryInteractable，chapterTag=N）、
//     Touch 走动触发盒、fade 黑屏转场落点（名字= json 里 to 的值）。
//   · ★ 幂等规则：节点已存在 → 只补缺引用，【不动位置/朝向】（用户手调优先）；
//     节点不存在 → 按内置默认坐标创建（进 Unity 后可随手调）。
//   · 报告：assets/_报告/_多章剧情搭建.txt（含每章 json 步骤数校验、接线清单、待手调位置清单）。
//
// ⚠ 不要为第2-5章建任何新 UI —— 对话/选择题/手机/WalkHint/结束卡/黑幕全部复用第一章（AGENTS 约定）。
// ⚠ 本工具不碰第一章任何节点；第1章 runner 在 StorySystem 根上原样保留。
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ChapterStoriesSetup
{
    const string MENU = "Tools/干预项目/多章剧情/";
    const string REPORT = "Assets/assets/_报告/_多章剧情搭建.txt";
    const string GAME_SCENE = "Assets/Scenes/Game.unity";

    // ------------------------------------------------------------------ 章节配置（摆位默认值；已存在节点不会被覆盖，可随手在 Scene 里调）
    class FadeAnchor { public string name; public string loc; public Vector3 pos; public float rotY; }
    class InteractPoint { public string name; public string loc; public Vector3 pos; public string prompt; public bool repeat; }

    class ChapterCfg
    {
        public int ch;
        public string loc;            // 起点所在 Loc
        public Vector3 start;
        public float startRotY;
        public InteractPoint[] points;
        public FadeAnchor[] fades;
    }

    static readonly ChapterCfg[] CFG = new ChapterCfg[]
    {
        new ChapterCfg
        {
            ch = 2, loc = "Loc_宿舍", start = new Vector3(0f, 0f, -0.5f), startRotY = 180f,
            points = new InteractPoint[]
            {   // 手机交互点（书桌旁）：两次 interact 复用（oneShot=false）
                new InteractPoint { name = "第2章_手机", loc = "Loc_宿舍", pos = new Vector3(0.8f, 0f, -0.2f), prompt = "拿起手机", repeat = true },
            },
            fades = new FadeAnchor[0],
        },
        new ChapterCfg
        {
            ch = 3, loc = "Loc_食堂", start = new Vector3(-1.3f, 0f, -4.5f), startRotY = 0f,
            points = new InteractPoint[]
            {
                new InteractPoint { name = "第3章_点饭", loc = "Loc_食堂", pos = new Vector3(-1.3f, 0f, -2.5f), prompt = "去窗口点饭" },
                new InteractPoint { name = "第3章_落座", loc = "Loc_食堂", pos = new Vector3(-1.3f, 0f, 0.5f), prompt = "坐下" },
                new InteractPoint { name = "第3章_办公室门", loc = "Loc_办公室", pos = new Vector3(-3.0f, 0f, -2.2f), prompt = "进入办公室" },
            },
            fades = new FadeAnchor[]
            {
                new FadeAnchor { name = "第3章_办公室内", loc = "Loc_办公室", pos = new Vector3(-2.0f, 0f, 0.5f), rotY = 90f },
                new FadeAnchor { name = "第3章_办公室外", loc = "Loc_办公室", pos = new Vector3(-3.0f, 0f, -1.8f), rotY = 180f },
            },
        },
        new ChapterCfg
        {
            ch = 4, loc = "Loc_宿舍", start = new Vector3(0f, 0f, -0.5f), startRotY = 180f,
            points = new InteractPoint[]
            {   // 同位置两个点：班群通知消费后，第二次 interact（坐下看资料）自动落到第二个
                new InteractPoint { name = "第4章_班群通知", loc = "Loc_宿舍", pos = new Vector3(0.8f, 0f, -0.2f), prompt = "点开班群通知" },
                new InteractPoint { name = "第4章_坐下看资料", loc = "Loc_宿舍", pos = new Vector3(0.8f, 0f, -0.2f), prompt = "坐下，翻看复习资料" },
            },
            fades = new FadeAnchor[]
            {
                new FadeAnchor { name = "第4章_图书馆座位", loc = "Loc_图书馆", pos = new Vector3(7.5f, 0f, -6.0f), rotY = 180f },
            },
        },
        new ChapterCfg
        {
            ch = 5, loc = "Loc_宿舍", start = new Vector3(0f, 0f, -0.5f), startRotY = 180f,
            points = new InteractPoint[]
            {
                new InteractPoint { name = "第5章_回座位", loc = "Loc_宿舍", pos = new Vector3(0.8f, 0f, -0.2f), prompt = "回到自己的位置上" },
            },
            fades = new FadeAnchor[]
            {
                new FadeAnchor { name = "第5章_图书馆躲避", loc = "Loc_图书馆", pos = new Vector3(7.5f, 0f, -6.0f), rotY = 180f },
                new FadeAnchor { name = "第5章_宿舍座位", loc = "Loc_宿舍", pos = new Vector3(0f, 0f, -0.5f), rotY = 180f },
                new FadeAnchor { name = "第5章_食堂", loc = "Loc_食堂", pos = new Vector3(-1.3f, 0f, -4.5f), rotY = 0f },
            },
        },
    };

    // 第5章门口「出去」走动触发盒（Touch 模式：走进即触发 → 黑屏去图书馆）
    static readonly InteractPoint CH5_DOOR_TOUCH = new InteractPoint
    { name = "第5章_宿舍门口", loc = "Loc_宿舍", pos = new Vector3(-3.4f, 0f, -1.5f), prompt = "", repeat = false };

    static readonly StringBuilder _log = new StringBuilder();

    // ------------------------------------------------------------------ 入口
    [MenuItem(MENU + "一键搭建第2-5章（幂等）", false, 50)]
    public static void SetupAll()
    {
        _log.Clear();
        _log.AppendLine("多章剧情搭建（第2-5章）  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        _log.AppendLine();

        // AGENTS 第七节坑：AddComponent 前先强制导入脚本，防 MonoScript 缓存 → 缺脚本组件
        foreach (var p in new[] { "Assets/Scripts/Story/StoryRunner.cs", "Assets/Scripts/Story/StoryInteractable.cs" })
            AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh();

        var scene = EditorSceneManager.OpenScene(GAME_SCENE, OpenSceneMode.Single);
        var system = GameObject.Find("StorySystem");
        if (system == null) { Error("场景里没有 StorySystem 节点"); return; }

        StoryRunner firstRunner = null;
        foreach (var r in Object.FindObjectsOfType<StoryRunner>())
            if (r.chapterIndex == 1) { firstRunner = r; break; }
        if (firstRunner == null) { Error("没找到第1章 StoryRunner（引用模板来源）"); return; }

        _log.AppendLine("【运行节点】StorySystem/第N章");
        var fadeAnchorLists = new Dictionary<int, List<Transform>>();
        foreach (var cfg in CFG)
        {
            var runnerGo = EnsureChild(system.transform, "第" + cfg.ch + "章");
            var runner = runnerGo.GetComponent<StoryRunner>();
            if (runner == null) { runner = runnerGo.AddComponent<StoryRunner>(); Log("  + " + runnerGo.name + " 挂 StoryRunner"); }
            else Log("  = " + runnerGo.name + " 已存在，仅补缺引用");

            runner.chapterIndex = cfg.ch;
            runner.runOnStart = true;
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/数据/剧情/第" + cfg.ch + "章.json");
            if (json == null) { Warn("  第" + cfg.ch + "章.json 不存在（数据还没写？），chapterJson 暂空"); }
            runner.chapterJson = json;

            // UI 引用：整组从第1章 runner 复制（全章共用同一套 UI交互 节点）
            var so = new SerializedObject(runner);
            var tpl = new SerializedObject(firstRunner);
            foreach (var field in new[] { "dialogue", "choicePanel", "promptRoot", "promptLabel",
                                          "walkHintRoot", "walkHintLabel", "chapterCardGroup",
                                          "cardTitle", "cardSubtitle", "blackFade", "phoneChat" })
            {
                var src = tpl.FindProperty(field);
                if (src == null) continue;
                var dst = so.FindProperty(field);
                if (dst != null && dst.objectReferenceValue == null && src.objectReferenceValue != null)
                { dst.objectReferenceValue = src.objectReferenceValue; Log("    接线 " + field + " → " + src.objectReferenceValue.name); }
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            fadeAnchorLists[cfg.ch] = new List<Transform>();
        }

        _log.AppendLine();
        _log.AppendLine("【锚点与触发点】Loc_*/多章锚点（默认坐标，已存在不动，可随手手调）");
        foreach (var cfg in CFG)
        {
            var start = EnsureAnchor(cfg.loc, "第" + cfg.ch + "章_起点", cfg.start, cfg.startRotY);
            var runnerGo = system.transform.Find("第" + cfg.ch + "章");
            var runner = runnerGo.GetComponent<StoryRunner>();
            if (runner.startAnchor == null) runner.startAnchor = start;

            foreach (var p in cfg.points) EnsureInteract(p, cfg.ch);
            foreach (var f in cfg.fades) fadeAnchorLists[cfg.ch].Add(EnsureAnchor(f.loc, f.name, f.pos, f.rotY));

            // fadeAnchors：只补空（用户手动接过的引用不覆盖）
            if (runner.fadeAnchors == null || runner.fadeAnchors.Length == 0)
                runner.fadeAnchors = fadeAnchorLists[cfg.ch].ToArray();

            EditorUtility.SetDirty(runner);
            Log("  第" + cfg.ch + "章：起点=" + (runner.startAnchor != null ? runner.startAnchor.name : "缺") +
                "，fadeAnchors=" + (runner.fadeAnchors != null ? runner.fadeAnchors.Length : 0) + " 个，交互点见上");
        }

        // 第5章门口 Touch 触发盒（walk 步骤用）
        EnsureTouchBox(CH5_DOOR_TOUCH, 5);

        _log.AppendLine();
        _log.AppendLine("【json 校验】");
        foreach (var cfg in CFG)
        {
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/数据/剧情/第" + cfg.ch + "章.json");
            if (json == null) { Warn("  第" + cfg.ch + "章.json：不存在"); continue; }
            var ch = StoryChapter.FromTextAsset(json);
            if (ch == null || ch.steps == null || ch.steps.Count == 0) { Warn("  第" + cfg.ch + "章.json：解析失败或无步骤"); continue; }
            int choices = 0; foreach (var s in ch.steps) if (s.t == "choice") choices++;
            bool ok = choices == 4;
            Log("  第" + cfg.ch + "章.json：" + ch.steps.Count + " 步，choice " + choices + " 题 " + (ok ? "✓" : "★应为 4 题"));
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        _log.AppendLine();
        _log.AppendLine("【待人工确认】");
        _log.AppendLine("  1) 各章起点/交互点/fade 落点默认坐标是按 Loc 中心估的，请进 Scene 视图对照实际家具微调；");
        _log.AppendLine("     工具幂等：改完再跑不会覆盖手调（只补空引用）。");
        _log.AppendLine("  2) 自检：Tools/干预项目/第N章剧情运行自检（N=2..5）→ assets/_报告/_第N章剧情运行自检.txt。");
        _log.AppendLine("  3) 手机聊天对象头像目前沿用第一章两张（占位）；有新头像后在 手机聊天 节点 Inspector 换 avatarLeft 即可。");

        Directory.CreateDirectory(Path.GetDirectoryName(REPORT).Replace('/', Path.DirectorySeparatorChar));
        File.WriteAllText(REPORT, _log.ToString());
        Debug.Log("[ChapterStoriesSetup] 完成，报告：" + REPORT);
        EditorUtility.DisplayDialog("多章剧情搭建", "完成，详见：\n" + REPORT, "好");
    }

    [MenuItem(MENU + "只看接线状态（不改动）", false, 51)]
    public static void Inspect()
    {
        _log.Clear();
        _log.AppendLine("多章剧情接线状态  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        var scene = EditorSceneManager.OpenScene(GAME_SCENE, OpenSceneMode.Single);
        var system = GameObject.Find("StorySystem");
        if (system == null) { Error("没有 StorySystem"); return; }
        foreach (Transform child in system.transform)
        {
            var r = child.GetComponent<StoryRunner>();
            AppendRunner(_log, child.name, r);
        }
        var root = system.GetComponent<StoryRunner>();
        if (root != null) AppendRunner(_log, "StorySystem(根·第1章)", root);

        _log.AppendLine();
        _log.AppendLine("StoryInteractable（含 chapterTag）：");
        foreach (var si in Object.FindObjectsOfType<StoryInteractable>())
            Log("  " + si.gameObject.name + "  ch=" + si.chapterTag + " mode=" + si.mode + " prompt=" +
                (string.IsNullOrEmpty(si.promptText) ? ("与" + si.displayName + "交谈") : si.promptText));

        Directory.CreateDirectory(Path.GetDirectoryName(REPORT).Replace('/', Path.DirectorySeparatorChar));
        File.WriteAllText(REPORT, _log.ToString());
        Debug.Log("[ChapterStoriesSetup] 状态已写入：" + REPORT);
        EditorUtility.DisplayDialog("接线状态", "已写入：" + REPORT, "好");
    }

    static void AppendRunner(StringBuilder sb, string title, StoryRunner r)
    {
        sb.Append(title).Append("：");
        if (r == null) { sb.AppendLine("无 runner"); return; }
        sb.Append("ch=").Append(r.chapterIndex)
          .Append(" json=").Append(r.chapterJson != null ? r.chapterJson.name : "空")
          .Append(" 起点=").Append(r.startAnchor != null ? r.startAnchor.name : "空")
          .Append(" fades=").Append(r.fadeAnchors != null ? r.fadeAnchors.Length : 0)
          .Append(" UI=").Append(r.dialogue != null ? "已接" : "空").AppendLine();
    }

    // ------------------------------------------------------------------ 小工具
    static GameObject EnsureChild(Transform parent, string name)
    {
        var t = parent.Find(name);
        if (t != null) return t.gameObject;
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go;
    }

    static Transform FindLoc(string locName)
    {
        var loc = GameObject.Find(locName);
        if (loc == null) { Warn("场景里没找到 " + locName); return null; }
        return loc.transform;
    }

    /// 锚点（起点/fade 落点）：Loc 下「多章锚点」容器里的空 Transform；已存在只返回，不动位置
    static Transform EnsureAnchor(string locName, string name, Vector3 localPos, float rotY)
    {
        var loc = FindLoc(locName);
        if (loc == null) return null;
        var holder = EnsureChild(loc, "多章锚点").transform;
        var found = holder.Find(name);
        if (found != null) return found;
        var go = new GameObject(name);
        go.transform.SetParent(holder, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(0f, rotY, 0f);
        Log("  + " + locName + "/多章锚点/" + name + " @ " + localPos.ToString("F1"));
        return go.transform;
    }

    /// F 交互点：锚点 + StoryInteractable（InteractF）
    static void EnsureInteract(InteractPoint p, int chapter)
    {
        var t = EnsureAnchor(p.loc, p.name, p.pos, 0f);
        if (t == null) return;
        var si = t.GetComponent<StoryInteractable>();
        if (si == null)
        {
            si = t.gameObject.AddComponent<StoryInteractable>();
            si.mode = StoryInteractable.Mode.InteractF;
            si.radius = 2.2f;
            Log("  + " + p.name + " 挂 StoryInteractable(F)  ch=" + chapter + " prompt=" + p.prompt);
        }
        si.chapterTag = chapter;
        si.promptText = p.prompt;
        si.oneShot = !p.repeat;
        EditorUtility.SetDirty(si);
    }

    /// Touch 走动触发盒：Is Trigger 的 BoxCollider + StoryInteractable（Touch）
    static void EnsureTouchBox(InteractPoint p, int chapter)
    {
        var t = EnsureAnchor(p.loc, p.name, p.pos, 0f);
        if (t == null) return;
        var col = t.GetComponent<BoxCollider>() ?? t.gameObject.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.center = new Vector3(0f, 1f, 0f);
        col.size = new Vector3(1.6f, 2f, 1.6f);
        var si = t.GetComponent<StoryInteractable>() ?? t.gameObject.AddComponent<StoryInteractable>();
        si.mode = StoryInteractable.Mode.Touch;
        si.chapterTag = chapter;
        si.oneShot = true;
        EditorUtility.SetDirty(si);
        Log("  + " + p.name + " 挂 StoryInteractable(Touch 触发盒)  ch=" + chapter);
    }

    static void Log(string s) { _log.AppendLine(s); }
    static void Warn(string s) { _log.AppendLine("★ " + s); Debug.LogWarning("[ChapterStoriesSetup] " + s); }
    static void Error(string s) { _log.AppendLine("★ 失败：" + s); Debug.LogError("[ChapterStoriesSetup] " + s); }

    // ================================================================== NPC 入场接线（第2章陆宣雨"门口虚影渐显走近"，2026-09-27）
    // 三件事（全幂等）：
    //   1) 宿舍南门内建「第2章_陆宣雨门口」锚点（enter 步骤 from）；
    //   2) 锚点补进第2章 runner 的锚点池（fadeAnchors 与 enter 共用）；
    //   3) 陆宣雨 prefab 换挂 PC_徐夏_Walk.controller（Speed 驱动的待机+行走混合树，
    //      humanoid 跨角色重定向；关 applyRootMotion——位移由 NpcEntrance 驱动）。
    // 落点不设锚点：enter 缺省动态走到玩家面前 1.3m（见 StoryRunner.EnterRoutine）。
    const string WALK_CTRL = "Assets/assets/03_动作_Animation/Animators/PC_徐夏_Walk.controller";
    const string LXY_PREFAB = "Assets/assets/02_角色_Character/角色_URP/陆宣雨_可动.prefab";
    const string ENTRANCE_REPORT = "Assets/assets/_报告/_NPC入场接线.txt";

    [MenuItem(MENU + "NPC入场接线（第2章陆宣雨）", false, 52)]
    public static void SetupEntrance()
    {
        _log.Clear();
        _log.AppendLine("NPC 入场接线（第2章陆宣雨）  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        _log.AppendLine();

        foreach (var p in new[] { "Assets/Scripts/Story/NpcEntrance.cs", "Assets/Scripts/Story/StoryRunner.cs" })
            AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);

        var scene = EditorSceneManager.OpenScene(GAME_SCENE, OpenSceneMode.Single);
        var system = GameObject.Find("StorySystem");
        if (system == null) { Error("场景里没有 StorySystem"); return; }

        // 1) 门口起点锚点（南门内，朝 +Z 面向屋内）
        var anchor = EnsureAnchor("Loc_宿舍", "第2章_陆宣雨门口", new Vector3(2.0f, 0f, -5.4f), 0f);

        // 2) 接进第2章 runner 锚点池（只补缺，不覆盖已有数组）
        var runnerT = system.transform.Find("第2章");
        var runner = runnerT != null ? runnerT.GetComponent<StoryRunner>() : null;
        if (runner == null) { Warn("StorySystem/第2章 runner 不存在——先跑「一键搭建第2-5章」再回来跑本菜单"); }
        else if (anchor != null)
        {
            bool has = false;
            if (runner.fadeAnchors != null)
                foreach (var a in runner.fadeAnchors) if (a != null && a.name == anchor.name) { has = true; break; }
            if (!has)
            {
                var list = runner.fadeAnchors != null ? new List<Transform>(runner.fadeAnchors) : new List<Transform>();
                list.Add(anchor);
                runner.fadeAnchors = list.ToArray();
                EditorUtility.SetDirty(runner);
                Log("  + 第2章 runner 锚点池 += " + anchor.name + "（共 " + list.Count + " 个）");
            }
            else Log("  = 第2章 runner 锚点池已含 " + anchor.name);
        }

        // 3) 陆宣雨 prefab 换挂行走 controller（LoadPrefabContents → SaveAsPrefabAsset 才落盘）
        var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(WALK_CTRL);
        if (ctrl == null) Warn("找不到 " + WALK_CTRL + " —— 入场将退化为纯位移（无走路动画）");
        else
        {
            var contents = PrefabUtility.LoadPrefabContents(LXY_PREFAB);
            var an = contents.GetComponentInChildren<Animator>(true);
            if (an == null) { Warn(LXY_PREFAB + " 没有 Animator"); }
            else
            {
                bool changed = an.runtimeAnimatorController != ctrl || an.applyRootMotion;
                if (changed)
                {
                    an.runtimeAnimatorController = ctrl;
                    an.applyRootMotion = false;                              // 位移由 NpcEntrance 驱动
                    an.cullingMode = AnimatorCullingMode.AlwaysAnimate;     // 入场时可能不在视野，仍要播
                    PrefabUtility.SaveAsPrefabAsset(contents, LXY_PREFAB);
                    Log("  + 陆宣雨_可动.prefab → 挂 " + System.IO.Path.GetFileName(WALK_CTRL) + "（rootMotion 关）");
                }
                else Log("  = 陆宣雨_可动.prefab 已挂行走 controller，未改动");
            }
            PrefabUtility.UnloadPrefabContents(contents);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        _log.AppendLine();
        _log.AppendLine("【下一步】Play 第2章到微信段结束 → 陆宣雨从南门虚影渐显走到玩家面前落定。");
        _log.AppendLine("观感可调：NpcEntrance 顶部 SPEED / ALPHA_MAX；虚影颜色在 CharacterGhost.shader 默认值。");
        Directory.CreateDirectory(Path.GetDirectoryName(ENTRANCE_REPORT).Replace('/', Path.DirectorySeparatorChar));
        File.WriteAllText(ENTRANCE_REPORT, _log.ToString());
        Debug.Log("[ChapterStoriesSetup] NPC 入场接线完成，报告：" + ENTRANCE_REPORT);
        EditorUtility.DisplayDialog("NPC入场接线", "完成，详见：\n" + ENTRANCE_REPORT, "好");
    }
}
