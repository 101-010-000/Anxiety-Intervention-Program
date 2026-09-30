using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 场景碰撞体：给 Game.unity 六个地点的墙板/家具等静态网格补 MeshCollider，防穿模/掉虚空。
/// 背景（2026-09-28 用户反馈）：环境套件 FBX 导入时不带碰撞体 → 玩家能直接穿过家具和墙板；
/// 地板是 SceneBuilder 用 Cube 拼的（自带 BoxCollider）所以屋里能站住，一穿出墙就是地板外的虚空。
/// 规则：
///   · MeshFilter + Renderer、自己没有任何 Collider、最大边 ≥5cm → 加 MeshCollider（convex=false，静态）。
///   · 名字带「角色」的容器整棵跳过（角色由 ScenePostFx.EnsureCharacterColliders 配胶囊体）。
///   · 已有 Collider 的一律不动（门口触发盒 isTrigger 照旧；套件里自带碰撞体的件也照旧）。
///   · 裸边护栏：某条地板边缘 1.2m 内没有 ≥2m 宽的遮挡体（= 这条边没墙），就在边缘放无渲染的
///     「防坠护栏_方向」BoxCollider。门洞所在的边有墙板覆盖，不会误加护栏。
/// 用法：菜单 Tools/干预项目/场景碰撞体/（① 加 / ② 只诊断），或丢 Assets/_colliders_trigger.txt。
/// 报告：assets/_报告/_场景碰撞体.txt。⚠ 结束时会保存 Game.unity（含你未保存的手改）。
/// </summary>
public static class SceneColliders
{
    const string GAME_SCENE = "Assets/Scenes/Game.unity";
    const string REPORT = "Assets/assets/_报告/_场景碰撞体.txt";
    const float MIN_SIZE = 0.05f;        // 比这更小的网格当碎屑，不配碰撞体
    const float BAND_DEPTH = 1.2f;       // 裸边检查：从地板边缘往屋内看的深度
    const float WALL_MIN_SPAN = 2.0f;    // 遮挡体要 ≥2m 宽才算“这条边有墙”

    [MenuItem("Tools/干预项目/场景碰撞体/① 给设施加碰撞体（幂等）")]
    public static void RunMenu() { RunInternal(false, false); }

    [MenuItem("Tools/干预项目/场景碰撞体/② 只诊断（什么都不改）")]
    public static void DiagMenu() { RunInternal(false, true); }

    public static void RunFromTrigger() { RunInternal(true, false); }

    // ------------------------------------------------------------------ 主流程
    static void RunInternal(bool fromTrigger, bool diagOnly)
    {
        var log = new List<string>();
        log.Add("场景碰撞体" + (diagOnly ? "（只诊断，未改动）" : "") + "  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("");
        try
        {
            if (EditorApplication.isPlaying)
            {
                log.Add("★ 正在 Play 模式，退出 Play 后再跑一次。");
                return;
            }

            var cur = SceneManager.GetActiveScene();
            bool wasDirty = cur.isDirty;
            if (cur.path != GAME_SCENE)
            {
                if (fromTrigger)
                {
                    log.Add("跳过：当前活动场景不是 Game.unity（触发器不替你切场景）。请打开 Game.unity 后用菜单 ①。");
                    return;
                }
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { log.Add("已取消（场景没保存）。"); return; }
                EditorSceneManager.OpenScene(GAME_SCENE, OpenSceneMode.Single);
            }

            var scene = SceneManager.GetActiveScene();
            var locsRoot = GameObject.Find("Locations");
            if (locsRoot == null) { log.Add("★ 场景里没有 Locations 节点。"); return; }
            if (wasDirty && !diagOnly) log.Add("注意：跑之前场景就有未保存的手改，本次结束时会一并保存。");

            int totAdded = 0, totExist = 0, totBarrier = 0, locCount = 0;

            foreach (Transform loc in locsRoot.transform)
            {
                if (loc.name.StartsWith("Loc_")) locCount++;

                // --- 1) 补 MeshCollider ---
                int added = 0, exist = 0, tiny = 0, noRen = 0, charSkip = 0, wallPanels = 0;
                var addedNames = new List<string>();
                foreach (var mf in loc.GetComponentsInChildren<MeshFilter>(false))
                {
                    var go = mf.gameObject;
                    if (UnderKey(loc, go.transform, "角色")) { charSkip++; continue; }
                    var ren = go.GetComponent<Renderer>();
                    if (ren == null || mf.sharedMesh == null) { noRen++; continue; }
                    if (ren is SkinnedMeshRenderer) { charSkip++; continue; }
                    bool wall = IsWallPanel(loc, go.transform);
                    if (go.GetComponent<Collider>() != null)
                    {
                        exist++; totExist++;
                        if (wall) wallPanels++;
                        continue;
                    }
                    var b = ren.bounds;
                    if (Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) < MIN_SIZE) { tiny++; continue; }
                    if (wall) wallPanels++;
                    if (diagOnly)
                    {
                        added++;
                        addedNames.Add("  缺 → " + RelPath(go.transform, loc));
                        continue;
                    }
                    var col = go.AddComponent<MeshCollider>();
                    col.sharedMesh = mf.sharedMesh;
                    col.convex = false;                       // 静态非凸：形状精确，也不需要 Rigidbody
                    added++; totAdded++;
                    addedNames.Add("  + " + RelPath(go.transform, loc));
                }

                // --- 2) 裸边护栏 ---
                Physics.SyncTransforms();                     // 让刚加的碰撞体立刻能查
                int barriers = 0;
                var bareEdges = new List<string>();
                var floor = FindFloorCollider(loc);
                var verify = new List<string>();
                if (floor == null)
                {
                    verify.Add("  ★ 找不到地板碰撞体（名字带「地板」）——玩家会掉下去，先去场景里查地板！");
                }
                else
                {
                    var fb = floor.bounds;
                    string[] edgeNames = { "北(+Z)", "南(-Z)", "东(+X)", "西(-X)" };
                    for (int d = 0; d < 4; d++)
                    {
                        if (!EdgeIsBare(loc, floor, fb, d)) continue;
                        bareEdges.Add(edgeNames[d]);
                        if (!diagOnly && EnsureBarrier(loc, d, fb)) barriers++;
                        else if (diagOnly) barriers++;        // 诊断模式只统计“会加几根”
                    }

                    // --- 3) 射线验证（编辑器物理，真查碰撞体在不在）---
                    Physics.SyncTransforms();                 // 护栏是刚建的，先同步再打射线
                    var o = fb.center + Vector3.up * 0.9f;    // 胸口高度
                    string[] dn = { "北(+Z)", "南(-Z)", "东(+X)", "西(-X)" };
                    Vector3[] dv = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
                    float[] ext = { fb.extents.z, fb.extents.z, fb.extents.x, fb.extents.x };
                    for (int i = 0; i < 4; i++)
                    {
                        if (Physics.Raycast(o, dv[i], out var h, ext[i] + 8f, ~0, QueryTriggerInteraction.Ignore))
                        {
                            bool ok = h.distance <= ext[i] + 1.5f;
                            verify.Add(string.Format("  射线{0}：{1} {2:0.0}m 处挡在「{3}」（墙面预计 {4:0.0}m）",
                                dn[i], ok ? "✓" : "⚠", h.distance, h.collider.name, ext[i]));
                        }
                        else
                            verify.Add(string.Format("  射线{0}：★ {1:0.0}m 内完全没有遮挡（该方向没墙且没护栏）",
                                dn[i], ext[i] + 8f));
                    }
                    if (Physics.Raycast(fb.center + Vector3.up * 3f, Vector3.down, out var hd, 4f, ~0, QueryTriggerInteraction.Ignore))
                        verify.Add(string.Format("  射线向下：✓ {0:0.0}m 处挡在「{1}」", hd.distance, hd.collider.name));
                    else
                        verify.Add("  射线向下：★ 地板没挡住（玩家会掉下去）");
                }
                totBarrier += barriers;

                // --- 分节输出 ---
                log.Add(string.Format("【{0}】新增 {1} / 已有 {2}（其中墙面板 {3}）/ 过小 {4} / 无渲染器 {5} / 角色跳过 {6} / 护栏 {7}",
                    loc.name, added, exist, wallPanels, tiny, noRen, charSkip, barriers));
                if (bareEdges.Count > 0)
                    log.Add("  裸边（没有墙的边）：" + string.Join("、", bareEdges) + " → 已放防坠护栏");
                log.AddRange(verify);
                if (addedNames.Count > 0)
                {
                    log.Add("  明细：");
                    log.AddRange(addedNames);
                }
                log.Add("");
            }

            log.Add(string.Format("汇总：{0} 地点，新增 MeshCollider {1} 个，已有 {2} 个，防坠护栏 {3} 根{4}",
                locCount, totAdded, totExist, totBarrier, diagOnly ? "（诊断模式：一个都没加）" : ""));

            if (!diagOnly)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                bool ok = EditorSceneManager.SaveScene(scene);
                log.Add("保存场景：" + (ok ? "成功 ✓" : "★ 失败"));
            }
            log.Add("");
            log.Add("说明：");
            log.Add("  · MeshCollider 全是静态非凸（形状精确；全场景本来就没有 Rigidbody）");
            log.Add("  · 角色（第X章角色 容器）没动——它们的胶囊体由 ScenePostFx.EnsureCharacterColliders 管");
            log.Add("  · 家具多在 Outline 层：第三人称镜头 tpBlockMask 排除该层，新碰撞体不会把镜头拉近；");
            log.Add("    墙/护栏在 Default 层，镜头贴墙正常收臂（期望行为）");
            log.Add("  · 门洞方向射线穿出属正常（出门靠 F 传送）；若某扇门扇是敞开模型，走出去由");
            log.Add("    掉出世界保护（FirstPersonController y<-20 拉回）兜底");
            log.Add("  · NPC 走位是 transform 直移，不受碰撞体影响；重跑幂等（已有 Collider 的全跳过）");
        }
        catch (System.Exception e)
        {
            log.Add("");
            log.Add("★ 异常中断：" + e);
            Debug.LogError(e);
            try
            {
                Directory.CreateDirectory("../额外文件");
                File.WriteAllText("../额外文件/错误_场景碰撞体.txt", e.ToString());
            }
            catch { }
        }
        finally { WriteReport(log); }
    }

    // ------------------------------------------------------------------ 子步骤
    /// 节点到 loc 之间任何一级名字带 key（如「角色」）→ 整棵是角色，跳过。
    static bool UnderKey(Transform loc, Transform node, string key)
    {
        var t = node;
        while (t != null && t != loc)
        {
            if (t.name.Contains(key)) return true;
            t = t.parent;
        }
        return false;
    }

    /// 墙面板 = Shell_墙 子树下的网格（SceneBuilder.Wall 拼的墙板/门框墙/窗墙）。
    static bool IsWallPanel(Transform loc, Transform node)
    {
        var t = node;
        while (t != null && t != loc)
        {
            if (t.name.Contains("Shell_墙")) return true;
            t = t.parent;
        }
        return false;
    }

    static Collider FindFloorCollider(Transform loc)
    {
        foreach (var col in loc.GetComponentsInChildren<Collider>())
            if (col.name.Contains("地板")) return col;        // Shell/Shell_地板（Cube 自带 BoxCollider）
        return null;
    }

    /// 这条地板边缘在 BAND_DEPTH × 2.8m 的检查带里，找不到 ≥WALL_MIN_SPAN 宽的遮挡体 → 裸边。
    static bool EdgeIsBare(Transform loc, Collider floor, Bounds fb, int dir)
    {
        const float halfH = 1.4f;                              // 检查带从地板面向上 2.8m
        Vector3 c = fb.center, e = fb.extents;
        Vector3 center, size;
        if (dir == 0) { center = new Vector3(c.x, fb.max.y + halfH, c.z + e.z - BAND_DEPTH / 2); size = new Vector3(e.x * 2 + 0.4f, halfH * 2, BAND_DEPTH); }
        else if (dir == 1) { center = new Vector3(c.x, fb.max.y + halfH, c.z - e.z + BAND_DEPTH / 2); size = new Vector3(e.x * 2 + 0.4f, halfH * 2, BAND_DEPTH); }
        else if (dir == 2) { center = new Vector3(c.x + e.x - BAND_DEPTH / 2, fb.max.y + halfH, c.z); size = new Vector3(BAND_DEPTH, halfH * 2, e.z * 2 + 0.4f); }
        else { center = new Vector3(c.x - e.x + BAND_DEPTH / 2, fb.max.y + halfH, c.z); size = new Vector3(BAND_DEPTH, halfH * 2, e.z * 2 + 0.4f); }
        var band = new Bounds(center, size);
        bool alongX = dir <= 1;

        foreach (var col in loc.GetComponentsInChildren<Collider>())
        {
            if (col.isTrigger || col == floor) continue;
            var cb = col.bounds;
            if (cb.size.y < 1.0f) continue;                    // 地板/矮物不算墙
            if (!cb.Intersects(band)) continue;
            float span = alongX ? cb.size.x : cb.size.z;
            if (span >= WALL_MIN_SPAN) return false;           // 有 ≥2m 宽的遮挡 = 有墙
        }
        return true;
    }

    /// 在裸边上放无渲染的 BoxCollider 挡条（幂等：已有同名护栏就跳过）。
    static bool EnsureBarrier(Transform loc, int dir, Bounds fb)
    {
        string[] names = { "防坠护栏_北", "防坠护栏_南", "防坠护栏_东", "防坠护栏_西" };
        if (loc.Find(names[dir]) != null) return false;
        var go = new GameObject(names[dir]);
        go.transform.SetParent(loc, false);
        float cy = fb.max.y + 1.5f;
        Vector3 pos, size;
        if (dir == 0) { pos = new Vector3(fb.center.x, cy, fb.max.z); size = new Vector3(fb.size.x + 1f, 3f, 0.4f); }
        else if (dir == 1) { pos = new Vector3(fb.center.x, cy, fb.min.z); size = new Vector3(fb.size.x + 1f, 3f, 0.4f); }
        else if (dir == 2) { pos = new Vector3(fb.max.x, cy, fb.center.z); size = new Vector3(0.4f, 3f, fb.size.z + 1f); }
        else { pos = new Vector3(fb.min.x, cy, fb.center.z); size = new Vector3(0.4f, 3f, fb.size.z + 1f); }
        go.transform.position = pos;                    // ★ 先摆到位再加碰撞体：编辑器物理可能按
        var bc = go.AddComponent<BoxCollider>();        //   创建时的位姿烘焙，不先摆位会查到旧位置
        bc.size = size;
        return true;
    }

    static string RelPath(Transform node, Transform loc)
    {
        var sb = new StringBuilder();
        var t = node;
        while (t != null && t != loc)
        {
            if (sb.Length > 0) sb.Insert(0, "/");
            sb.Insert(0, t.name);
            t = t.parent;
        }
        return sb.ToString();
    }

    static void WriteReport(List<string> log)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(REPORT));
            File.WriteAllText(REPORT, string.Join("\n", log), new UTF8Encoding(false));
            Debug.Log("[SceneColliders] 报告：" + REPORT);
        }
        catch (System.Exception e) { Debug.LogError("[SceneColliders] 报告写失败：" + e); }
    }
}

// 工程里存在 Assets/_colliders_trigger.txt 时，编辑器下次刷新/重编译后自动跑一次（同 GameDoorBuilder 的路子）。
[InitializeOnLoad]
public static class SceneCollidersTrigger
{
    const string Trigger = "Assets/_colliders_trigger.txt";
    const string ErrFile = "../额外文件/错误_场景碰撞体.txt";

    static SceneCollidersTrigger()
    {
        if (!File.Exists(Trigger)) return;
        EditorApplication.delayCall += () =>
        {
            try
            {
                if (File.Exists(Trigger)) File.Delete(Trigger);
                if (File.Exists(Trigger + ".meta")) File.Delete(Trigger + ".meta");
                SceneColliders.RunFromTrigger();
            }
            catch (System.Exception e)
            {
                Directory.CreateDirectory("../额外文件");
                File.WriteAllText(ErrFile, e.ToString());
                Debug.LogError("[SceneColliders] 触发失败: " + e);
            }
        };
    }
}
