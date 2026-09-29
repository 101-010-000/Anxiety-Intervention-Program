using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 第4/5章跳转改造（2026-09-29 用户定稿）：剧情换地点不再走「门口传送选择面板」（door 导流），
/// 改回前三章的模式——走到指定位置按 F（interact）→ 黑屏直接落到指定落点（fade）。
/// 另做第4章林溪进宿舍演出（enter/leave，同第2章陆宣雨）：旁白说她进来之后，她真的从门口走进来；
/// 结束时她先走向门口，玩家跟上去一起「出门去图书馆」。
/// json（Assets/数据/剧情/第4/5章.json）由人工同步修改；本工具只负责场景侧，幂等（已存在的节点不动位置）：
///   ① fade 落点/演出锚点：第4章_林溪门口 / 第4章_图书馆到达 / 第5章_图书馆到达 / 第5章_宿舍到达 / 第5章_食堂到达
///   ② 门口 F 交互点：第4章_去图书馆 / 第5章_去图书馆 / 第5章_回宿舍 / 第5章_去食堂（StoryInteractable，按章归属）
///   ③ Loc_宿舍/第四章角色 容器（默认隐藏）+ 林溪_宿舍 实例（复制图书馆那位已换模的林溪；
///      ★ 必须改名——FindCharacterTransform 按名精确匹配，宿舍/图书馆两个「林溪_可动」会互相抓错）
///   ⑤ enter 站位锚点：第4章_林溪站位 / 第2章_陆宣雨站位（★ 两人各探一个净空点、分开站——用户 2026-09-29）
/// 参考位置：复用门口传送算好的 Arrive_*（门内 1.4m）与用户手摆的 第2章_陆宣雨门口，不自己猜门位。
/// 用法：菜单 Tools/干预项目/第4-5章跳转改造（幂等），或丢 Assets/_travel45_trigger.txt。
/// </summary>
public static class Chapter45TravelSetup
{
    const string GAME_SCENE = "Assets/Scenes/Game.unity";
    const string REPORT = "Assets/assets/_报告/_第4-5章跳转改造.txt";
    const string LINXI_DORM_NAME = "林溪_宿舍";

    static StringBuilder _log;

    [MenuItem("Tools/干预项目/第4-5章跳转改造（幂等）")]
    public static void Run()
    {
        _log = new StringBuilder();
        _log.AppendLine("第4/5章跳转改造（interact+fade + 林溪进宿舍）  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        _log.AppendLine();
        try
        {
            if (EditorApplication.isPlaying) { _log.AppendLine("★ 正在 Play 模式，退出后再跑。"); Flush(); return; }

            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != GAME_SCENE)
            {
                var opened = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
                                       .FirstOrDefault(s => s.path == GAME_SCENE);
                scene = opened.IsValid() ? opened : EditorSceneManager.OpenScene(GAME_SCENE, OpenSceneMode.Single);
            }
            _log.AppendLine("场景：" + scene.path + (scene.isDirty ? "（带未保存手改——会一起存盘）" : ""));
            _log.AppendLine();

            var dorm = FindInScene(scene, "Loc_宿舍");
            var lib = FindInScene(scene, "Loc_图书馆");
            var canteen = FindInScene(scene, "Loc_食堂");
            if (dorm == null || lib == null || canteen == null)
            {
                _log.AppendLine("★ 缺地点根（宿舍/图书馆/食堂）——场景不对，中止。");
                Flush(); return;
            }

            // ---------- 参考位置：Arrive_* 是门口传送自动算的「门内 1.4m」落点，直接复用 ----------
            var dormArrive = FindInScene(scene, "Arrive_宿舍");
            var libArrive = FindInScene(scene, "Arrive_图书馆");
            var canteenArrive = FindInScene(scene, "Arrive_食堂");
            var lxyDoor = FindInScene(scene, "第2章_陆宣雨门口");
            _log.AppendLine("【参考位置】");
            _log.AppendLine("  第2章_陆宣雨门口 :" + (lxyDoor != null ? Pos(lxyDoor.transform) : "★ 缺"));
            _log.AppendLine("  Arrive_宿舍     :" + (dormArrive != null ? Pos(dormArrive.transform) : "★ 缺"));
            _log.AppendLine("  Arrive_图书馆   :" + (libArrive != null ? Pos(libArrive.transform) : "★ 缺"));
            _log.AppendLine("  Arrive_食堂     :" + (canteenArrive != null ? Pos(canteenArrive.transform) : "★ 缺"));
            _log.AppendLine();

            // 宿舍门（林溪进门 + 第4章玩家出门的 F 点）：优先用户手摆的 陆宣雨门口，其次 Arrive_宿舍
            Vector3 dormDoorPos = lxyDoor != null ? lxyDoor.transform.position
                                : dormArrive != null ? dormArrive.transform.position : dorm.transform.position;
            float dormDoorYaw = lxyDoor != null ? lxyDoor.transform.eulerAngles.y
                              : dormArrive != null ? dormArrive.transform.eulerAngles.y : 0f;

            var dormAnchors = EnsureFolder(dorm.transform, "多章锚点");
            var libAnchors = EnsureFolder(lib.transform, "多章锚点");
            var canteenAnchors = EnsureFolder(canteen.transform, "多章锚点");

            // ---------- ① 锚点（fade 落点 / 演出用） ----------
            _log.AppendLine("【① fade 落点 / 演出锚点】");
            EnsureAnchor(scene, dormAnchors, "第4章_林溪门口", dormDoorPos, dormDoorYaw);
            EnsureAnchor(scene, libAnchors, "第4章_图书馆到达", libArrive);
            EnsureAnchor(scene, libAnchors, "第5章_图书馆到达", libArrive);
            EnsureAnchor(scene, dormAnchors, "第5章_宿舍到达", dormArrive);
            EnsureAnchor(scene, canteenAnchors, "第5章_食堂到达", canteenArrive);
            _log.AppendLine();

            // ---------- ② 门口 F 交互点（interact→fade 的 interact 半段） ----------
            _log.AppendLine("【② 门口 F 交互点】");
            EnsureDoorInteract(scene, dormAnchors, "第4章_去图书馆", 4, dormDoorPos, dormDoorYaw, "出门去图书馆");
            EnsureDoorInteract(scene, dormAnchors, "第5章_去图书馆", 5,
                dormArrive != null ? dormArrive.transform.position : dormDoorPos,
                dormArrive != null ? dormArrive.transform.eulerAngles.y : dormDoorYaw, "去图书馆");
            EnsureDoorInteract(scene, libAnchors, "第5章_回宿舍", 5,
                libArrive != null ? libArrive.transform.position : lib.transform.position,
                libArrive != null ? libArrive.transform.eulerAngles.y : 0f, "回宿舍");
            EnsureDoorInteract(scene, dormAnchors, "第5章_去食堂", 5,
                dormArrive != null ? dormArrive.transform.position : dormDoorPos,
                dormArrive != null ? dormArrive.transform.eulerAngles.y : dormDoorYaw, "去食堂");
            _log.AppendLine();

            // ---------- ③ 林溪_宿舍（第4章进宿舍演出用） ----------
            _log.AppendLine("【③ 林溪_宿舍 实例】");
            var containerT = dorm.transform.Find("第四章角色");
            if (containerT == null)
            {
                var c = new GameObject("第四章角色");
                c.transform.SetParent(dorm.transform, false);
                c.SetActive(false);
                containerT = c.transform;
                _log.AppendLine("  + 建 Loc_宿舍/第四章角色（默认隐藏；第4章 Begin 的容器显隐会自动亮起）");
            }
            else
                _log.AppendLine("  = Loc_宿舍/" + containerT.name + " 已存在（activeSelf=" + containerT.gameObject.activeSelf + "，不动）");

            var linxi = containerT.Find(LINXI_DORM_NAME);
            if (linxi != null)
            {
                _log.AppendLine("  = " + LINXI_DORM_NAME + " 已存在（不动） " + Pos(linxi));
            }
            else
            {
                var libContainer = lib.transform.Find("第四章角色");
                var src = libContainer != null ? libContainer.Find("林溪_可动") : null;
                if (src == null)
                {
                    _log.AppendLine("  ★ 找不到 Loc_图书馆/第四章角色/林溪_可动 —— 无法复制；请手动放一个林溪实例进容器再重跑。");
                }
                else
                {
                    var copy = Object.Instantiate(src.gameObject);
                    copy.name = LINXI_DORM_NAME;
                    copy.transform.SetParent(containerT, true);
                    copy.transform.position = dormDoorPos;
                    copy.transform.rotation = Quaternion.Euler(0f, dormDoorYaw, 0f);
                    copy.SetActive(true);

                    // 清掉坐姿/演出组件：这位是走进来的（SitHere=NPC 常坐；NpcEntrance 是运行期组件，编辑态残留一并清）
                    var stripped = new List<string>();
                    foreach (var s in copy.GetComponentsInChildren<SitHere>(true)) { stripped.Add("SitHere@" + s.gameObject.name); Object.DestroyImmediate(s); }
                    foreach (var n in copy.GetComponentsInChildren<NpcEntrance>(true)) { stripped.Add("NpcEntrance@" + n.gameObject.name); Object.DestroyImmediate(n); }
                    _log.AppendLine("  + 复制 林溪_可动 → " + LINXI_DORM_NAME + " @ " + Pos(copy.transform) + "（新模型/Idle 动画/碰撞体一并继承）");
                    _log.AppendLine(stripped.Count > 0 ? "  − 清掉组件：" + string.Join("、", stripped.ToArray()) : "  = 没有坐姿/演出组件需要清");

                    int dup = 0;
                    foreach (var root in scene.GetRootGameObjects())
                        foreach (var tr in root.GetComponentsInChildren<Transform>(true))
                            if (tr.name == LINXI_DORM_NAME) dup++;
                    _log.AppendLine(dup == 1 ? "  ✓ 全场景唯一（enter/leave 按名找的就是她）" : "  ★ 全场景有 " + dup + " 个同名实例，必须唯一！");
                }
            }
            _log.AppendLine();

            // ---------- ⑤ 走位拐点 + enter 站位锚点（林溪/陆宣雨各自独立，2026-09-29 用户定稿：都要分开） ----------
            // enter 缺省落点=「玩家面前1.3m（沿门→玩家方向硬算）」，第4章林溪正好落进书桌边的椅子群里。
            // 拐点：林溪用自己的 第4章_林溪路线_1（默认=陆宣雨拐点旁横向偏移+净空探测），两人路线互不影响；
            // 站位：各自从【自己的拐点】往书桌探净空，且两点互相隔开 ≥1m。
            // ⚠ 第一版的坑：探测胶囊底心放 0.25、半径 0.38 → 底沿 -0.13 插进地板碰撞体 →
            //   全走廊"无净空" → 两人一起退到路线拐点变成同点。底心必须抬过地板顶面。
            _log.AppendLine("【⑤ 拐点与站位（林溪/陆宣雨各自独立、分开）】");
            var standRef = FindInScene(scene, "第4章_坐下看资料");
            Vector3 playerDesk = standRef != null ? standRef.transform.position : new Vector3(80.63f, 0f, 4.5f);
            var route = FindInScene(scene, "第2章_陆宣雨路线_1");
            Vector3 lxyRouteP = route != null ? route.transform.position : new Vector3(77.6f, 0f, 0.8f);

            // ⑤-1 林溪自己的拐点（已存在=手调过，不动；缺了才建在陆宣雨拐点旁）
            var linxiRoute = FindInScene(scene, "第4章_林溪路线_1");
            Vector3 linxiRouteP;
            if (linxiRoute != null)
            {
                linxiRouteP = linxiRoute.transform.position;
                _log.AppendLine("  = 第4章_林溪路线_1 已存在（不动） " + Pos(linxiRoute.transform));
            }
            else
            {
                linxiRouteP = OffsetClear(lxyRouteP, PerpOf(dormDoorPos, playerDesk));
                var go = new GameObject("第4章_林溪路线_1");
                go.transform.SetParent(dormAnchors, false);
                go.transform.position = linxiRouteP;
                go.transform.rotation = Quaternion.Euler(0f, YawToward(linxiRouteP, playerDesk), 0f);
                _log.AppendLine("  + 第4章_林溪路线_1 @ " + V3(linxiRouteP) + "（陆宣雨拐点旁偏移，可手拖）");
            }

            // ⑤-2 站位：各自从自己的拐点往书桌探第一个净空点（陆宣雨的还要离林溪的 ≥1m）
            Vector3 linxiSpot = FirstClearSpot(linxiRouteP, playerDesk, null);
            Vector3 lxySpot = FirstClearSpot(lxyRouteP, playerDesk, linxiSpot);
            _log.AppendLine("  净空探测 → 林溪站位 " + V3(linxiSpot) +
                            "（离玩家 " + Vector3.Distance(linxiSpot, playerDesk).ToString("F1") + "m）" +
                            " / 陆宣雨站位 " + V3(lxySpot) + "（离玩家 " + Vector3.Distance(lxySpot, playerDesk).ToString("F1") + "m）" +
                            (Vector3.Distance(linxiSpot, lxySpot) >= 1f ? " ✓ 分开" : "（两点仍近，请手拖）"));
            PlaceStationAnchor(scene, dormAnchors, "第4章_林溪站位", linxiSpot, playerDesk, lxyRouteP);
            PlaceStationAnchor(scene, dormAnchors, "第2章_陆宣雨站位", lxySpot, playerDesk, lxyRouteP);
            _log.AppendLine();

            // ---------- ④ json 对齐自检 ----------
            _log.AppendLine("【④ json 对齐自检】");
            CheckJson(scene, "Assets/数据/剧情/第4章.json",
                "林溪_宿舍", "第4章_林溪门口", "第4章_去图书馆", "第4章_图书馆到达", "第4章_林溪站位", "第4章_林溪路线_1");
            CheckJson(scene, "Assets/数据/剧情/第5章.json",
                "第5章_去图书馆", "第5章_图书馆到达", "第5章_回宿舍", "第5章_宿舍到达", "第5章_去食堂", "第5章_食堂到达");
            _log.AppendLine();

            EditorSceneManager.MarkSceneDirty(scene);
            bool ok = EditorSceneManager.SaveScene(scene);
            _log.AppendLine("保存场景：" + (ok ? "成功 ✓" : "★失败（手动 Ctrl+S 兜底）"));
            _log.AppendLine();
            _log.AppendLine("【怎么验】");
            _log.AppendLine("· 第4章：旁白「林溪就抱着复习资料，走进了宿舍」→ 林溪真的从门口走进来 → 对话 → 她先走向门口");
            _log.AppendLine("  → 目标卡「走到门口，和林溪一起去图书馆」→ 门口按 F → 黑屏落在图书馆门口 →「走到林溪旁边的位置」");
            _log.AppendLine("· 第5章：三处换地点同款（宿舍→图书馆→宿舍→食堂），门口只有 F 提示，不再弹选择面板");
            _log.AppendLine("· 想调位置：直接拖 多章锚点 下的锚点/F点（按名生效）；林溪进门门位 = 第4章_林溪门口");
            _log.AppendLine("· 自动自检：Tools/干预项目/第4章、第5章剧情运行自检（⚠ 自检前场景先保存，它会重开 Game）");
        }
        catch (System.Exception e)
        {
            _log.AppendLine("★ 异常：" + e);
        }
        Flush();
    }

    // ------------------------------------------------------------------ 小工具
    // 「from→to」走廊上第一个净空点（含左右 ±0.8m 候选，离玩家近的优先）；
    // avoid 非空时还要离它 ≥1m（陆宣雨的站位要和林溪的分开）。
    // 找不到 → 起点旁横移 1.2m（调用方日志里提示可手拖）。
    // ⚠ 胶囊底心抬到 0.55（最低沿 0.20）：必须高过地板碰撞体顶面，否则每个候选都"撞地板"（第一版的坑）。
    static Vector3 FirstClearSpot(Vector3 from, Vector3 to, Vector3? avoid)
    {
        Vector3 perp = PerpOf(from, to);
        for (int i = 0; i <= 10; i++)
        {
            float t = 0.85f - i * 0.05f;                       // 0.85 → 0.35：先试离玩家近的
            Vector3 baseP = Vector3.Lerp(from, to, Mathf.Clamp01(t)); baseP.y = 0f;
            foreach (var p in new[] { baseP, baseP + perp * 0.8f, baseP - perp * 0.8f })
            {
                if (avoid.HasValue && Vector3.Distance(p, avoid.Value) < 1f) continue;
                if (CapsuleClear(p)) return p;
            }
        }
        return from + perp * 1.2f;
    }

    // 基点旁横向偏移找净空（给林溪的拐点用：从陆宣雨拐点旁挪开一个身位）
    static Vector3 OffsetClear(Vector3 baseP, Vector3 perp)
    {
        foreach (var d in new[] { 1.2f, -1.2f, 0.8f, -0.8f, 0.5f, -0.5f })
        {
            Vector3 p = baseP + perp * d; p.y = 0f;
            if (CapsuleClear(p)) return p;
        }
        return baseP + perp * 1.2f;
    }

    static bool CapsuleClear(Vector3 p)
    {
        return !Physics.CheckCapsule(p + Vector3.up * 0.55f, p + Vector3.up * 1.65f, 0.35f, ~0, QueryTriggerInteraction.Ignore);
    }

    static Vector3 PerpOf(Vector3 from, Vector3 to)
    {
        var d = to - from; d.y = 0f;
        var p = new Vector3(-d.z, 0f, d.x);
        return p.sqrMagnitude > 1e-4f ? p.normalized : Vector3.right;
    }

    // 站位锚点落位：没有→建；还停在上一轮的拐点兜底位（=工具产物）→重摆；手拖过的（≠拐点）→不动。
    static void PlaceStationAnchor(Scene scene, Transform parent, string name, Vector3 pos, Vector3 faceTo, Vector3 oldFallback)
    {
        var exist = FindInScene(scene, name);
        if (exist != null)
        {
            if (Vector3.Distance(exist.transform.position, oldFallback) < 0.6f)
            {
                exist.transform.position = pos;
                exist.transform.rotation = Quaternion.Euler(0f, YawToward(pos, faceTo), 0f);
                EditorUtility.SetDirty(exist);
                _log.AppendLine("  ~ " + name + " 还在上一轮的拐点兜底位 → 重摆到 " + V3(pos));
            }
            else
                _log.AppendLine("  = " + name + " 已存在（手调过，不动） " + Pos(exist.transform));
            return;
        }
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(0f, YawToward(pos, faceTo), 0f);
        _log.AppendLine("  + " + name + " @ " + V3(pos) + "  ← " + parent.parent.name + "（可手拖，按名生效）");
    }

    static float YawToward(Vector3 from, Vector3 to)
    {
        var d = to - from; d.y = 0f;
        return d.sqrMagnitude > 1e-4f ? Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg : 0f;
    }

    static string V3(Vector3 v) { return "(" + v.x.ToString("F2") + ", " + v.y.ToString("F2") + ", " + v.z.ToString("F2") + ")"; }

    static void CheckJson(Scene scene, string file, params string[] names)
    {
        _log.AppendLine("  " + file);
        if (!File.Exists(file)) { _log.AppendLine("    ★ 文件不存在"); return; }
        string txt = File.ReadAllText(file);
        foreach (var n in names)
        {
            bool inJson = txt.Contains(n);
            bool inScene = FindInScene(scene, n) != null;
            _log.AppendLine("    " + n + "：json " + (inJson ? "✓" : "★缺") + " / 场景 " + (inScene ? "✓" : "★缺"));
        }
        bool doorLeft = txt.Contains("\"t\": \"door\"");
        _log.AppendLine(doorLeft ? "    ★ json 里还有 door 步骤（会弹选择面板）！" : "    ✓ 已无 door 步骤");
    }

    static Transform EnsureFolder(Transform parent, string name)
    {
        var t = parent.Find(name);
        if (t != null) return t;
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        _log.AppendLine("  + 建目录 " + parent.name + "/" + name);
        return go.transform;
    }

    // 参考物重载：调用方传的是 FindInScene 的 GameObject（Arrive_* 等），取其位置/朝向当锚点
    static void EnsureAnchor(Scene scene, Transform parent, string name, GameObject src)
    {
        if (src != null) EnsureAnchor(scene, parent, name, src.transform.position, src.transform.eulerAngles.y);
        else EnsureAnchor(scene, parent, name, parent.position, 0f);
    }

    static void EnsureAnchor(Scene scene, Transform parent, string name, Vector3 pos, float yaw)
    {
        var exist = FindInScene(scene, name);
        if (exist != null) { _log.AppendLine("  = " + name + " 已存在（不动） " + Pos(exist.transform)); return; }
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        _log.AppendLine("  + " + name + " @ " + Pos(go.transform) + "  ← " + parent.parent.name + "（可手拖，按名生效）");
    }

    static void EnsureDoorInteract(Scene scene, Transform parent, string name, int chapter, Vector3 pos, float yaw, string prompt)
    {
        var exist = FindInScene(scene, name);
        Transform t = exist != null ? exist.transform : null;
        bool isNew = t == null;
        if (isNew)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            t = go.transform;
        }
        var si = t.GetComponent<StoryInteractable>();
        if (si == null) si = t.gameObject.AddComponent<StoryInteractable>();
        si.mode = StoryInteractable.Mode.InteractF;
        si.chapterTag = chapter;
        si.radius = 2.2f;
        si.oneShot = true;
        si.promptText = prompt;
        EditorUtility.SetDirty(si);
        _log.AppendLine("  " + (isNew ? "+" : "=") + " " + name + "  ch=" + chapter + " @ " + Pos(t) +
                        " prompt=" + prompt + (isNew ? "" : "（已存在：只刷参数不动位置）"));
    }

    static GameObject FindInScene(Scene scene, string name)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var hit = FindRecursive(root.transform, name);
            if (hit != null) return hit.gameObject;
        }
        return null;
    }

    static Transform FindRecursive(Transform t, string name)
    {
        if (t.name == name) return t;
        for (int i = 0; i < t.childCount; i++)
        {
            var hit = FindRecursive(t.GetChild(i), name);
            if (hit != null) return hit;
        }
        return null;
    }

    static string Pos(Transform t) { return "(" + t.position.x.ToString("F2") + ", " + t.position.y.ToString("F2") + ", " + t.position.z.ToString("F2") + ")"; }

    static void Flush()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(REPORT).Replace('/', Path.DirectorySeparatorChar));
        File.WriteAllText(REPORT, _log.ToString(), new UTF8Encoding(false));
        Debug.Log("[Chapter45Travel] 完成，报告：" + REPORT);
    }
}

// 工程里存在 Assets/_travel45_trigger.txt 时，编辑器下次刷新/重编译后自动跑一次（同 _phonech2_trigger 的路子）。
[InitializeOnLoad]
public static class Chapter45TravelTrigger
{
    const string Trigger = "Assets/_travel45_trigger.txt";
    const string ErrFile = "../额外文件/错误_第4-5章跳转改造.txt";

    static Chapter45TravelTrigger()
    {
        if (!File.Exists(Trigger)) return;
        EditorApplication.delayCall += () =>
        {
            try
            {
                if (File.Exists(Trigger)) File.Delete(Trigger);
                if (File.Exists(Trigger + ".meta")) File.Delete(Trigger + ".meta");
                Chapter45TravelSetup.Run();
            }
            catch (System.Exception e)
            {
                Directory.CreateDirectory("../额外文件");
                File.WriteAllText(ErrFile, e.ToString());
                Debug.LogError("[Chapter45Travel] 触发失败: " + e);
            }
        };
    }
}
