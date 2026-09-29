// 办公室进出流程（第三章）· 走廊方案（2026-09-29 用户定稿，替代前一版"自建门外走廊"）：
//   食堂剧情完 → 走到食堂大门 Door_G_Frame(1) 交互「去走廊」→ fade 到 Loc_走廊（食堂门位置）
//   → 沿走廊走到用户手摆的「对象112」（办公室门，走廊侧）交互「进入办公室」→ fade 进 Loc_办公室
//   → 剧情照旧 → 走到 Loc_办公室 下用户手摆的「对象112」（办公室门，屋内侧）交互「离开办公室」
//   → fade 回 Loc_走廊「对象112」位置继续剧情。
// ★ 不自建任何布景（2026-09-29 教训：用户有现成的 Loc_走廊 和手摆的门，别自己造）。
//   本工具只做三件事：① 撤销上一版方案A（门外走廊/门框墙/复制门扇）并把办公室恢复原状；
//   ② 按上面动线摆锚点+交互点（位置全部"按名解析"现成物体：Door_G_Frame / Door_B_Frame / 对象112）；
//   ③ 存场景 + 报告。走廊口缺省落在走廊唯一现成的门 Door_B 前（可手挪锚点，重跑不覆盖手调——
//   但 对象112/食堂门/走廊门 贴靠类锚点每次重跑都会重新吸附到目标物体当前位置：挪物体=挪交互点）。
// 用法：菜单 Tools/干预项目/办公室进出流程/① 恢复原状+按走廊方案重摆（幂等） / ② 只诊断
// 触发器：Assets/_officedoorflow_trigger.txt（刷新自动跑①并自删）
// 输出：Assets/assets/_报告/_办公室进出流程.txt
//
// 恢复原状的依据（上一版改动前的实测值，2026-09-29 场景 dump）：
//   南墙板 = 走廊套件 墙.fbx prefab 实例「墙_0」，Shell_墙 下 localPos(-1.54, 0.011299, -4.0008373)
//   localRot(x,-y,-z,w)=(-0.7071068, 0, 0, 0.7071068)，无缩放覆盖；
//   锚点 第3章_办公室外 world(157,0,-1.8) rotY180、第3章_办公室门 world(157,0,-2.2)。
// ⚠ 若「对象112」还没摆进 Loc_走廊 / Loc_办公室，对应锚点会缺并写进报告——摆好后再跑一次即可。

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using System.IO;

public static class OfficeDoorFlowSetup
{
    const string OUT_SCENE = "Assets/Scenes/Game.unity";
    const string REPORT    = "Assets/assets/_报告/_办公室进出流程.txt";

    // 恢复原状的精确值（见文件头）
    static readonly Vector3  OLD_WALL_LOCALPOS = new Vector3(-1.54f, 0.011299f, -4.0008373f);
    static readonly Quaternion OLD_WALL_LOCALROT = new Quaternion(-0.7071068f, 0f, 0f, 0.7071068f);
    static readonly Vector3  ANCHOR_OUT_ORIG = new Vector3(157f, 0f, -1.8f);   // rotY 180
    static readonly Vector3  ANCHOR_DOOR_ORIG = new Vector3(157f, 0f, -2.2f);  // rotY 0

    static readonly List<string> _log = new List<string>();

    // 触发器：Assets/_officedoorflow_trigger.txt（编辑器刷新/获得焦点时自动跑 ① 并自删）
    [InitializeOnLoadMethod]
    static void _TriggerHook()
    {
        const string TRIG = "Assets/_officedoorflow_trigger.txt";
        if (!File.Exists(TRIG)) return;
        File.Delete(TRIG);
        EditorApplication.delayCall += () =>
        {
            try { Run(); }
            catch (System.Exception e)
            {
                try
                {
                    Directory.CreateDirectory("../额外文件");
                    File.WriteAllText("../额外文件/错误_办公室进出流程.txt", e.ToString());
                }
                catch { }
                Debug.LogError("[办公室进出流程] 异常：" + e);
            }
        };
    }

    [MenuItem("Tools/干预项目/办公室进出流程/① 恢复原状+按走廊方案重摆（幂等）")]
    public static void Run() => RunInternal(false);

    [MenuItem("Tools/干预项目/办公室进出流程/② 只诊断")]
    public static void Diagnose() => RunInternal(true);

    static void RunInternal(bool diagnoseOnly)
    {
        _log.Clear();
        Log("办公室进出流程（走廊方案）  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        Log("");

        var cur = SceneManager.GetActiveScene();
        if (cur.path != OUT_SCENE)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(OUT_SCENE, OpenSceneMode.Single);
            cur = SceneManager.GetActiveScene();
        }

        var office = GameObject.Find("Loc_办公室");
        if (office == null) { Log("★ 找不到 Loc_办公室"); Flush(); return; }

        // ---- ① 撤销方案A：删自建物，恢复原南墙板 ----
        RevertPlanA(office.transform, diagnoseOnly);
        if (!diagnoseOnly)
            EnsureAnchorsOriginal(office.transform);   // 办公室外锚点还原位（走廊方案不再用它，仅恢复现场）；诊断模式不动场景

        if (diagnoseOnly) { DiagnoseAnchors(); Flush(); return; }

        // ---- ② 走廊方案锚点/交互点 ----
        var corridor = GameObject.Find("Loc_走廊");
        var canteen = GameObject.Find("Loc_食堂");
        if (corridor == null) Log("★ 找不到 Loc_走廊");
        if (canteen == null) Log("★ 找不到 Loc_食堂");

        var anchors = EnsureContainer(office.transform, "多章锚点");

        // 食堂门交互点（去走廊）：贴 Door_G_Frame，往食堂里挪 1.2m
        Transform canteenDoor = FindInSubtree(canteen != null ? canteen.transform : null, "Door_G_Frame");
        if (canteenDoor != null)
        {
            Vector3 p = PullToward(canteenDoor.position, canteen.transform.position, 1.2f);
            var t = EnsureAnchor(anchors, "第3章_食堂门", p, true);
            EnsureInteract(t, "食堂大门", "去走廊");
            Log(string.Format("  + 第3章_食堂门 → 食堂大门里侧 ({0:0.00}, 0, {1:0.00})「按F 去走廊」", p.x, p.z));
        }
        else Log("★ Loc_食堂 找不到 Door_G_Frame（食堂门交互点没摆）");

        // 走廊口（fade 落点）：走廊唯一现成的门 Door_B 前，往走廊中心线挪 1m（手挪锚点可改）
        Transform hallDoor = FindInSubtree(corridor != null ? corridor.transform : null, "Door_B_Frame");
        if (hallDoor != null)
        {
            Vector3 p = PullToward(hallDoor.position, corridor.transform.position, 1.0f);
            EnsureAnchor(anchors, "第3章_走廊口", p, false);   // 缺省位=Door_B 前；手挪锚点后重跑不覆盖
            Log(string.Format("  + 第3章_走廊口 → Door_B 前 ({0:0.00}, 0, {1:0.00})（已存在则不覆盖手调）", p.x, p.z));
        }
        else Log("★ Loc_走廊 找不到 Door_B_Frame（走廊口落点没摆）");

        // 对象112：用户手摆的办公室门（走廊侧 + 屋内侧）
        Transform hall112 = FindInSubtree(corridor != null ? corridor.transform : null, "对象112");
        Transform office112 = FindInSubtree(office.transform, "对象112");
        if (hall112 == null) Log("★ Loc_走廊 下没有「对象112」——摆好门后再跑一次本工具（幂等）");
        if (office112 == null) Log("★ Loc_办公室 下没有「对象112」——摆好门后再跑一次本工具（幂等）");

        if (hall112 != null)
        {
            // 走廊侧进入交互点 + 退场 fade 落点，都贴走廊的对象112
            var t = EnsureAnchor(anchors, "第3章_办公室门",
                                 new Vector3(hall112.position.x, 0f, hall112.position.z), true);
            EnsureInteract(t, "办公室门", "进入办公室");
            Log(string.Format("  ↺ 第3章_办公室门 → 走廊对象112 ({0:0.00}, 0, {1:0.00})「按F 进入办公室」",
                hall112.position.x, hall112.position.z));
            EnsureAnchor(anchors, "第3章_走廊尾",
                         new Vector3(hall112.position.x, 0f, hall112.position.z), true);
            Log(string.Format("  + 第3章_走廊尾 → 走廊对象112（出办公室落点）"));
        }
        if (office112 != null)
        {
            var t = EnsureAnchor(anchors, "第3章_办公室出口",
                                 new Vector3(office112.position.x, 0f, office112.position.z), true);
            EnsureInteract(t, "办公室门", "离开办公室");
            Log(string.Format("  ↺ 第3章_办公室出口 → 屋内对象112 ({0:0.00}, 0, {1:0.00})「按F 离开办公室」",
                office112.position.x, office112.position.z));
        }

        // ---- 李老师：进屋落点面对面（用户 2026-09-29 需求②）----
        // （需求①切视角走 StoryRunner 的 cut 步骤 + CutawayCamera，主角不动，无需锚点）
        Transform lls = FindInSubtree(office.transform, "李老师_可动");
        if (lls != null)
        {
            float herYaw = lls.eulerAngles.y;
            Log(string.Format("  · 李老师_可动 ({0:0.00}, {1:0.00}, {2:0.00})  yaw≈{3:0}°（朝向由你手摆控制，工具不动她）",
                lls.position.x, lls.position.y, lls.position.z, herYaw));
        }
        else Log("★ Loc_办公室 下找不到 李老师_可动");

        if (lls != null && office112 != null)
        {
            var indoor = anchors.Find("第3章_办公室内");
            if (indoor == null)
            {
                var go = new GameObject("第3章_办公室内");
                go.transform.SetParent(anchors, false);
                indoor = go.transform;
            }
            Vector3 dir = office112.position - lls.position; dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.back;
            dir.Normalize();
            Vector3 p = lls.position + dir * 1.7f; p.y = 0f;
            indoor.position = p;
            Vector3 f2 = lls.position - p; f2.y = 0f;
            if (f2.sqrMagnitude > 1e-4f) indoor.rotation = Quaternion.LookRotation(f2);
            Log(string.Format("  ↺ 第3章_办公室内 → 进屋门与李老师连线上、离李老师1.7m ({0:0.00}, 0, {1:0.00})，玩家正对她（面对面）",
                indoor.position.x, indoor.position.z));
        }
        else if (lls == null) Log("★ 进屋落点没改：需要 李老师_可动 和 屋内对象112 都在");

        DiagnoseAnchors();
        WarnDoorTravelOverlap(anchors);

        // ---- ③ 存盘 ----
        bool ok = EditorSceneManager.SaveScene(cur, OUT_SCENE);
        Log("");
        Log("保存场景：" + (ok ? "成功 ✓（含你未保存的手改，仓库惯例）" : "★ 失败"));
        Log("后续：跑第3章剧情运行自检验证整条动线。");
        Flush();
    }

    // ------------------------------------------------------------------ 撤销方案A
    static void RevertPlanA(Transform office, bool diagnoseOnly)
    {
        int removed = 0;

        var strip = office.Find("Shell_门外走廊");
        if (strip != null) { if (!diagnoseOnly) Object.DestroyImmediate(strip.gameObject); Log("  − 删 Shell_门外走廊（方案A自建）"); removed++; }

        var content = office.Find("Content");
        var door = FindDirectChild(content, "办公室门");
        if (door != null) { if (!diagnoseOnly) Object.DestroyImmediate(door); Log("  − 删 办公室门（方案A复制的门扇）"); removed++; }

        var walls = office.Find("Shell_墙");
        var frameWall = FindDirectChild(walls, "门框墙");
        if (frameWall != null) { if (!diagnoseOnly) Object.DestroyImmediate(frameWall); Log("  − 删 门框墙_南门（方案A替换件）"); removed++; }

        // 南墙板：z≈-4 处没有墙板就按原值补回来
        bool southBack = false;
        if (walls != null)
            foreach (Transform t in walls)
            {
                var b = BoundsOfWorld(t.gameObject);
                if (b.HasValue && Mathf.Abs(b.Value.center.z - (office.position.z - 4f)) < 0.6f) { southBack = true; break; }
            }
        if (!southBack)
        {
            if (!diagnoseOnly)
            {
                string path = AssetLocator.FileNamed("墙.fbx");
                var asset = path == null ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset != null && walls != null)
                {
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset, office.gameObject.scene);
                    inst.name = "墙_0";
                    inst.transform.SetParent(walls, false);
                    inst.transform.localPosition = OLD_WALL_LOCALPOS;
                    inst.transform.localRotation = OLD_WALL_LOCALROT;
                    southBack = true;
                }
            }
            if (southBack) { Log("  + 南墙板 墙_0 已按原值恢复（localPos/rot 取自改动前 dump）"); removed++; }
            else Log(diagnoseOnly ? "  南墙板：缺失（跑①会按改动前 dump 的原值恢复）" : "★ 南墙板没恢复（找不到 墙.fbx）");
        }
        if (removed == 0) Log("  （方案A物件已不在，无需撤销）");
    }

    // ------------------------------------------------------------------ 锚点/交互点
    static void EnsureAnchorsOriginal(Transform office)
    {
        var anchors = EnsureContainer(office, "多章锚点");
        var t = anchors.Find("第3章_办公室外");
        if (t != null)
        {
            t.position = ANCHOR_OUT_ORIG;
            t.rotation = Quaternion.Euler(0f, 180f, 0f);
            Log("  ↺ 第3章_办公室外 恢复原位 (157.00, 0, -1.80)（走廊方案不再用它，仅还原现场）");
        }
    }

    static Transform EnsureContainer(Transform parent, string name)
    {
        var c = parent.Find(name);
        if (c != null) return c;
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static Transform EnsureAnchor(Transform anchors, string name, Vector3 pos, bool snapEveryRun)
    {
        var t = anchors.Find(name);
        if (t == null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(anchors, false);
            t = go.transform;
            t.position = pos;               // 新建才摆位
        }
        else if (snapEveryRun)
        {
            t.position = pos;               // 贴靠类（源=对象112/现成门）：每次重跑吸附，挪物体=挪交互点
        }
        // snapEveryRun=false 且已存在 → 手调优先，绝不动（如 走廊口）
        return t;
    }

    static void EnsureInteract(Transform anchor, string displayName, string prompt)
    {
        var si = anchor.GetComponent<StoryInteractable>();
        if (si == null)
        {
            WarmScript("Assets/Scripts/Story/StoryInteractable.cs");
            si = anchor.gameObject.AddComponent<StoryInteractable>();
            si.mode = StoryInteractable.Mode.InteractF;
            si.radius = 2.2f;
        }
        si.chapterTag = 3;
        si.displayName = displayName;
        si.promptText = prompt;
    }

    // 剧情交互点（半径2.2）若和门口传送的触发盒重叠，玩家会同时看到两个 F 提示、按 F 双触发。
    // 这里只检测+报告（食堂大门的传送功能要保留，不能擅自删）；真干扰了再决定挪锚点还是屏蔽传送。
    static void WarnDoorTravelOverlap(Transform anchors)
    {
        var doors = Object.FindObjectsOfType<DoorInteractable>();
        if (doors.Length == 0) return;
        foreach (Transform a in anchors)
        {
            var si = a.GetComponent<StoryInteractable>();
            if (si == null) continue;
            float r = si.radius;
            foreach (var d in doors)
            {
                var col = d.GetComponent<Collider>();
                if (col == null || !col.enabled) continue;
                var b = col.bounds;
                var p = a.position; p.y = b.center.y;
                var closest = new Vector3(Mathf.Clamp(p.x, b.min.x, b.max.x), p.y, Mathf.Clamp(p.z, b.min.z, b.max.z));
                if ((closest - p).sqrMagnitude <= r * r)
                {
                    Log(string.Format("  ⚠ {0}（半径{1:0.0}）与门口传送触发盒「{2}」重叠——站得太贴门可能双 F 提示；" +
                        "提示出现就按 F、别再往门框里走。真干扰了告诉我，挪锚点或临时屏蔽传送。",
                        a.name, r, d.gameObject.name));
                    break;
                }
            }
        }
    }

    static void DiagnoseAnchors()
    {
        var office = GameObject.Find("Loc_办公室");
        if (office == null) return;
        var anchors = office.transform.Find("多章锚点");
        if (anchors == null) { Log("  （Loc_办公室/多章锚点 不存在）"); return; }
        Log("  锚点现状：");
        foreach (Transform t in anchors)
            Log(string.Format("    {0}  world=({1:0.00}, {2:0.00}, {3:0.00}){4}",
                t.name, t.position.x, t.position.y, t.position.z,
                t.GetComponent<StoryInteractable>() != null
                    ? "  [F] " + (string.IsNullOrEmpty(t.GetComponent<StoryInteractable>().promptText) ? "与" + t.GetComponent<StoryInteractable>().displayName + "交谈" : t.GetComponent<StoryInteractable>().promptText)
                    : ""));
    }

    // ------------------------------------------------------------------ 小工具
    static GameObject FindDirectChild(Transform parent, string prefix)
    {
        if (parent == null) return null;
        foreach (Transform t in parent)
            if (t.name.StartsWith(prefix)) return t.gameObject;
        return null;
    }

    static Transform FindInSubtree(Transform root, string prefix)
    {
        if (root == null) return null;
        // 整棵子树递归（任意深度、含隐藏）；有同名精确匹配时优先，避免「对象112 (1)」抢在前头
        Transform best = null;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t == root) continue;
            if (t.name == prefix) return LogFound(t);
            if (best == null && t.name.StartsWith(prefix)) best = t;
        }
        return best != null ? LogFound(best) : null;
    }

    static Transform LogFound(Transform t)
    {
        if (t.name.StartsWith("对象112"))
            Log(string.Format("  · 找到「{0}」 ({1:0.00}, {2:0.00}, {3:0.00})，父：{4}{5}",
                t.name, t.position.x, t.position.y, t.position.z,
                t.parent != null ? t.parent.name : "?", ""));
        return t;
    }

    static Vector3 PullToward(Vector3 from, Vector3 toward, float dist)
    {
        var d = toward - from; d.y = 0f;
        if (d.sqrMagnitude < 1e-6f) return new Vector3(from.x, 0f, from.z);
        d = d.normalized * dist;
        return new Vector3(from.x + d.x, 0f, from.z + d.z);
    }

    static Bounds? BoundsOfWorld(GameObject go)
    {
        bool any = false;
        var total = new Bounds();
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            var mf = r.GetComponent<MeshFilter>();
            Bounds b;
            if (mf != null && mf.sharedMesh != null)
            {
                b = mf.sharedMesh.bounds;
                var m = r.transform.localToWorldMatrix;
                var c = m.MultiplyPoint3x4(b.center);
                var e = new Vector3(
                    Mathf.Abs(m.m00) * b.extents.x + Mathf.Abs(m.m01) * b.extents.y + Mathf.Abs(m.m02) * b.extents.z,
                    Mathf.Abs(m.m10) * b.extents.x + Mathf.Abs(m.m11) * b.extents.y + Mathf.Abs(m.m12) * b.extents.z,
                    Mathf.Abs(m.m20) * b.extents.x + Mathf.Abs(m.m21) * b.extents.y + Mathf.Abs(m.m22) * b.extents.z);
                b = new Bounds(c, e * 2f);
            }
            else
            {
                var smr = r as SkinnedMeshRenderer;
                if (smr == null || smr.sharedMesh == null) continue;
                b = smr.bounds;
            }
            if (!any) { total = b; any = true; }
            else total.Encapsulate(b);
        }
        return any ? (Bounds?)total : null;
    }

    static void WarmScript(string path)
    {
        try { AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate); } catch { }
    }

    static void Log(string s)
    {
        _log.Add(s);
        Debug.Log("[办公室进出流程] " + s);
    }

    static void Flush()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(REPORT).Replace('/', Path.DirectorySeparatorChar));
        File.WriteAllText(REPORT, string.Join("\n", _log.ToArray()));
    }
}
