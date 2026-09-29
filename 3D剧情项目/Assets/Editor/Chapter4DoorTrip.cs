// 第4章「走到门口 → 按 F → 去图书馆」的落地工具（用户 2026-09-29 反馈）。
//
// 用法：菜单 Tools/干预项目/第4章门口引导　或丢 Assets/_ch4door_trigger.txt
// 报告：assets/_报告/_第4章门口引导.txt
//
// 三件事（全部幂等，可反复跑）：
//   ① 保证门口传送系统真的能用 —— 场景里 DoorTravelSystem 的 面板/列表/关闭按钮 是空的
//      （历史遗留：UI_门口交互 画布某一轮整理时被删掉，之后没人再跑过搭建工具），
//      于是「按 F 开门」只弹提示、面板根本不会出现。本工具检测到缺件就跑 GameDoorBuilder 重建。
//   ② 第4章_图书馆座位 → 挪到【图书馆里林溪旁边那把椅子】上、朝向林溪。
//      剧本 55 步的门口传送落点就是它；原先它在 (207.5,-6)，离林溪 6.4m 还背对她。
//   ③ 宿舍书桌旁的剧情点归位：第2章_手机 / 第4章_班群通知 / 第4章_坐下看资料 / 第5章_回座位
//      → 全摆到宿舍长桌（书桌）前面。剧本原文就是「徐夏走到书桌前」「把手机从桌角拿过来」，
//      此前它们都在房间正中的工具默认位（离桌子 6 米）。
//
// ⚠ 只动这几个锚点 + 缺件时才重建门口 UI；对话/选择题/手机/WalkHint/黑幕等剧情 UI 一概不碰。
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class Chapter4DoorTrip
{
    const string MENU = "Tools/干预项目/";
    const string GAME_SCENE = "Assets/Scenes/Game.unity";
    const string REPORT = "Assets/assets/_报告/_第4章门口引导.txt";

    static readonly StringBuilder _log = new StringBuilder();

    [MenuItem(MENU + "第4章门口引导（走到门口按F去图书馆）", false, 47)]
    public static void Run()
    {
        _log.Clear();
        _log.AppendLine("第4章门口引导  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        _log.AppendLine("（剧情步骤 door：走到门口按 F → 面板选图书馆 → 落到图书馆座位 → 剧情继续）");
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
                File.WriteAllText("../额外文件/错误_第4章门口引导.txt", e.ToString());
            }
            catch { }
            Debug.LogError("[Chapter4DoorTrip] 异常：" + e);
        }
    }

    static void Core()
    {
        // 0) 保住当前打开的场景（本工具要切到 Game.unity；触发器无人值守，不能弹"要不要保存"）
        var cur = SceneManager.GetActiveScene();
        if (cur.IsValid() && cur.isDirty && cur.path != GAME_SCENE)
        {
            EditorSceneManager.SaveOpenScenes();
            _log.AppendLine("（先存了当前打开的其它场景，避免丢掉手改）");
        }

        var scene = EditorSceneManager.OpenScene(GAME_SCENE, OpenSceneMode.Single);

        // 1) 门口传送系统缺件 → 重建（GameDoorBuilder 的原职，见它自己的报告 _门口传送.txt）
        var dts = Object.FindObjectOfType<DoorTravelSystem>(true);
        bool missing = dts == null || dts.panel == null || dts.listRoot == null || dts.promptRoot == null || dts.closeButton == null;
        _log.AppendLine("【① 门口传送系统】");
        if (missing)
        {
            _log.AppendLine("  ★ 缺件（panel=" + (dts != null && dts.panel != null ? "有" : "空") +
                           " / listRoot=" + (dts != null && dts.listRoot != null ? "有" : "空") +
                           " / closeButton=" + (dts != null && dts.closeButton != null ? "有" : "空") + "）");
            _log.AppendLine("  → 调用 GameDoorBuilder 重建「门口传送 + UI_门口交互」（会重扫门触发盒/重建落点，明细见 _门口传送.txt）");
            GameDoorBuilder.Run();
            dts = Object.FindObjectOfType<DoorTravelSystem>(true);
            scene = SceneManager.GetActiveScene();
        }
        else _log.AppendLine("  = 面板/列表/关闭按钮俱全，不动");

        // 1b) 伪门清理：剧情触发盒（StoryInteractable）不是门 —— 清掉误挂的 DoorInteractable
        //     （GameDoorBuilder 已加同样的规则，这里负责把【已经挂上去的】擦掉）
        foreach (var si in Object.FindObjectsOfType<StoryInteractable>(true))
        {
            var bogus = si.GetComponent<DoorInteractable>();
            if (bogus == null) continue;
            _log.AppendLine("  − 清掉剧情触发盒上误挂的 DoorInteractable：" + PathOf(si.transform));
            Object.DestroyImmediate(bogus);
        }
        if (dts != null)
        {
            int before = dts.doors.Count;
            for (int i = dts.doors.Count - 1; i >= 0; i--)
                if (dts.doors[i] == null) dts.doors.RemoveAt(i);
            if (dts.doors.Count != before)
            {
                EditorUtility.SetDirty(dts);
                _log.AppendLine("  门口列表去空：" + before + " → " + dts.doors.Count + " 个");
            }
        }
        _log.AppendLine("  门数 " + (dts != null ? dts.doors.Count.ToString() : "?") +
                       "（教室/走廊/宿舍/食堂/办公室/图书馆 共 6 个地点都能进）");
        _log.AppendLine();

        // 2) 第4章_图书馆座位 → 林溪旁边那把椅子
        _log.AppendLine("【② 第4章_图书馆座位（门口传送落点）】");
        var seat = GameObject.Find("第4章_图书馆座位");
        if (seat == null) _log.AppendLine("  ★ 找不到 第4章_图书馆座位（先跑『多章剧情/一键搭建第2-5章』）");
        else
        {
            var lxy = FindInLoc("Loc_图书馆", "林溪_可动", "第四章角色");
            if (lxy == null) _log.AppendLine("  ★ 图书馆/第四章角色 下没找到 林溪_可动 —— 只保底把落点放在 (207.5,-6)");
            else
            {
                var chair = NearestChair(lxy.transform.position, 0.8f, 2.4f);
                _log.AppendLine("  林溪 @ " + V3(lxy.transform.position));
                if (chair == null) _log.AppendLine("  ★ 林溪 2.4m 内没有椅子 —— 落点保持原样（进 Scene 手调）");
                else
                {
                    Vector3 p = chair.position; p.y = 0f;
                    // 站在椅子上、面朝林溪（第三人称镜头在背后，正好把林溪框进去）
                    Vector3 d = lxy.transform.position - p; d.y = 0f;
                    float yaw = d.sqrMagnitude > 0.0001f ? Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg : chair.eulerAngles.y;
                    _log.AppendLine("  " + seat.transform.position.ToString("F2") + " → " + p.ToString("F2") +
                                    "（椅子 " + PathOf(chair) + "，离林溪 " + Vector3.Distance(p, lxy.transform.position).ToString("F2") + "m，朝向 " + yaw.ToString("F0") + "° 对林溪）");
                    seat.transform.position = p;
                    seat.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                    EditorUtility.SetDirty(seat);
                }
            }
        }
        _log.AppendLine();

        // 3) 宿舍书桌旁的剧情点归位
        _log.AppendLine("【③ 宿舍书桌旁的剧情点（第2/4/5章）】");
        var desk = FindChildByName("Loc_宿舍", "长桌");
        if (desk == null) _log.AppendLine("  ★ 宿舍里没找到 长桌 —— 跳过");
        else
        {
            Vector3 center = desk.position;
            // 站到桌子【朝房间中心】那一侧 1.5m（面对桌子），别贴墙/别站进桌子里
            var shell = FindChildByName("Loc_宿舍", "Shell_墙");
            Vector3 room = shell != null ? shell.position : new Vector3(80f, 0f, 0.5f);
            if (shell != null)
            {
                var rs = shell.GetComponentsInChildren<Renderer>();
                if (rs.Length > 0)
                {
                    var b = rs[0].bounds;
                    foreach (var r in rs) b.Encapsulate(r.bounds);
                    room = b.center;
                }
            }
            Vector3 inward = new Vector3(room.x - center.x, 0f, room.z - center.z);
            inward = inward.sqrMagnitude > 1e-4f ? inward.normalized : new Vector3(0f, 0f, -1f);
            Vector3 stand = center + inward * 1.5f; stand.y = 0f;
            Vector3 look = center - stand; look.y = 0f;
            float yaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;

            _log.AppendLine("  书桌（长桌）@ " + V3(center) + " → 站位 " + V3(stand) + "，朝向 " + yaw.ToString("F0") + "°（面对桌子）");
            foreach (var name in new[] { "第2章_手机", "第4章_班群通知", "第4章_坐下看资料", "第5章_回座位" })
            {
                var t = GameObject.Find(name);
                if (t == null) { _log.AppendLine("  ★ 找不到 " + name + "（跳过）"); continue; }
                _log.AppendLine("  " + name + "：" + t.transform.position.ToString("F2") + " → " + V3(stand) +
                                "（原位置离桌子 " + Vector3.Distance(t.transform.position, center).ToString("F1") + "m）");
                t.transform.position = stand;
                t.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                EditorUtility.SetDirty(t);
            }
        }
        _log.AppendLine();

        // 4) 把新落点补进第4章 runner 的锚点池（缺了也不影响：FindFadeAnchor 会全场景按名兜底）
        _log.AppendLine("【④ 第4章 runner 锚点池】");
        var system = GameObject.Find("StorySystem");
        var ch4 = system != null ? system.transform.Find("第4章") : null;
        var runner = ch4 != null ? ch4.GetComponent<StoryRunner>() : null;
        if (runner == null) _log.AppendLine("  ★ 没找到 StorySystem/第4章 的 StoryRunner");
        else if (seat == null) _log.AppendLine("  = 座位锚点不存在，跳过");
        else
        {
            bool has = false;
            if (runner.fadeAnchors != null)
                foreach (var a in runner.fadeAnchors) if (a != null && a.name == seat.name) { has = true; break; }
            if (has) _log.AppendLine("  = 锚点池里已有 " + seat.name);
            else
            {
                var list = new List<Transform>(runner.fadeAnchors ?? new Transform[0]);
                list.Add(seat.transform);
                runner.fadeAnchors = list.ToArray();
                EditorUtility.SetDirty(runner);
                _log.AppendLine("  + 锚点池 += " + seat.name + "（现在 " + list.Count + " 个）");
            }
            _log.AppendLine("  第4章 json 里的 door 步骤：to=Loc_图书馆，anchor=" + seat.name);
        }
        _log.AppendLine();

        // 5) 顺手清场：上次主角第三人称自检残留在场景根上的 ~PlayerThirdPersonSmoke
        //    （它是运行时自举的驱动，压根不该落盘：留着会在【每次 Play】自己跑一遍自检、
        //      用虚拟输入挪玩家/镜头，真实游玩也会被它搅）
        _log.AppendLine("【⑤ 场景残留清理】");
        int cleaned = 0;
        foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (go == null || !go.name.StartsWith("~PlayerThirdPersonSmoke")) continue;
            _log.AppendLine("  − 删掉残留节点 " + go.name);
            Object.DestroyImmediate(go);
            cleaned++;
        }
        if (cleaned == 0) _log.AppendLine("  = 没有残留节点");
        _log.AppendLine();

        EditorSceneManager.MarkSceneDirty(scene);
        bool ok = EditorSceneManager.SaveScene(scene);
        _log.AppendLine("保存场景 " + GAME_SCENE + "：" + (ok ? "成功 ✓" : "★失败"));

        _log.AppendLine();
        _log.AppendLine("【怎么验】");
        _log.AppendLine("  · 进 Play 选第4章 → 走到「宿舍门」（西南角）→ 底部「按 F 开门」→ 面板里只有「图书馆 / 自习区」能点 → 落到图书馆林溪旁边");        _log.AppendLine("  · 自动自检：Tools/干预项目/第4章剧情运行自检 → assets/_报告/_第4章剧情运行自检.txt");
        _log.AppendLine("  · 门口传送本身的自检：Tools/干预项目/门口传送运行自检 → assets/_报告/_门口传送运行自检.txt");
        Flush();
        Debug.Log("[Chapter4DoorTrip] 完成，报告：" + REPORT);
    }

    static void Flush()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(REPORT).Replace('/', Path.DirectorySeparatorChar));
        File.WriteAllText(REPORT, _log.ToString());
    }

    // ------------------------------------------------------------------ 小工具
    static string V3(Vector3 v) { return "(" + v.x.ToString("F2") + ", " + v.y.ToString("F2") + ", " + v.z.ToString("F2") + ")"; }

    static string PathOf(Transform t)
    {
        var s = t.name;
        for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
        return s;
    }

    /// Loc 下按名找子物体（先看指定容器，再递归）
    static GameObject FindInLoc(string locName, string objName, string container = null)
    {
        var loc = GameObject.Find(locName);
        if (loc == null) return null;
        if (!string.IsNullOrEmpty(container))
        {
            var c = FindChildByName(locName, container);
            if (c != null)
            {
                if (c.name == objName) return c.gameObject;
                foreach (var t in c.GetComponentsInChildren<Transform>(true))
                    if (t.name == objName) return t.gameObject;
            }
        }
        foreach (var t in loc.GetComponentsInChildren<Transform>(true))
            if (t.name == objName) return t.gameObject;
        return null;
    }

    static Transform FindChildByName(string locName, string childName)
    {
        var loc = GameObject.Find(locName);
        if (loc == null) return null;
        foreach (var t in loc.GetComponentsInChildren<Transform>(true))
            if (t.name == childName) return t;
        return null;
    }

    /// 离 pos 最近的椅子/凳子：半径 [minD, maxD] 内挑最近的（跳过贴着角色的那一把）
    static Transform NearestChair(Vector3 pos, float minD, float maxD)
    {
        Transform best = null;
        float bestScore = float.MaxValue;
        foreach (var t in Object.FindObjectsOfType<Transform>(true))
        {
            if (t == null || !SitSpot.IsSeatName(t.name)) continue;
            if (t.parent != null && SitSpot.IsSeatName(t.parent.name)) continue;   // 只取椅子根，别取子网格
            Vector3 p = t.position;
            float d = new Vector2(p.x - pos.x, p.z - pos.z).magnitude;
            if (d < minD || d > maxD) continue;
            if (d < bestScore) { bestScore = d; best = t; }
        }
        return best;
    }
}

// 丢 Assets/_ch4door_trigger.txt → 下次刷新/重编译后自动跑一次
[InitializeOnLoad]
public static class Chapter4DoorTripTrigger
{
    const string Trigger = "Assets/_ch4door_trigger.txt";
    const string ErrFile = "../额外文件/错误_第4章门口引导.txt";

    static Chapter4DoorTripTrigger()
    {
        if (!File.Exists(Trigger)) return;
        EditorApplication.delayCall += () =>
        {
            try
            {
                if (File.Exists(Trigger)) File.Delete(Trigger);
                if (File.Exists(Trigger + ".meta")) File.Delete(Trigger + ".meta");
                Chapter4DoorTrip.Run();
            }
            catch (System.Exception e)
            {
                Directory.CreateDirectory("../额外文件");
                File.WriteAllText(ErrFile, e.ToString());
                Debug.LogError("[Chapter4DoorTrip] 失败: " + e);
            }
        };
    }
}
