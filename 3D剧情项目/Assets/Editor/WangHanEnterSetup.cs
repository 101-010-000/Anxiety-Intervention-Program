// 第三章王含入场（2026-09-30，用户定稿）：王含不能开局就坐在食堂座位上——
// 剧情到她台词前一步（第3章.json 的 enter 步骤）开始走位，走到她那把 凳子2 (9) 旁，
// 切成用户摆好的坐姿模型（王含切换）再开口说话。
// 路线（用户 2026-10-01 重定）：出发点=用户手摆的 王含_站立（西北北墙边，111.2, 7.1），
// 沿北墙直线 ~7.6m 到 凳子2(9) 后方入座（旧「东侧大动脉绕行」路线_1..4 已退役）。
// ★ 路线不得穿模（用户 2026-09-30）：对食堂【全部】碰撞体（含路人胶囊、垃圾桶、储物箱，
//   不跳过任何东西）按包围盒水平距离逐点采样，要求 ≥0.45m——自检不过会写在报告里。
// 本工具只摆两样东西（幂等，节点已存在绝不挪位/重挂）：
//   ① 站立实例 王含_站立（已绑定.fbx + 王含_Idle 控制器，含 Idle↔Walk 过渡，材质走 FBX 导入器重映射），
//      摆在门口锚点，整棵 Outline 层（同第四章 林溪_宿舍 的做法）；
//   ② 多章锚点：第3章_王含门口 / 第3章_王含落座（json enter 的 from/to 按名解析）。
//   ⚠ 用户手挪实例后要把「第3章_王含门口」锚点跟着挪到实例脚下（走位起点以锚点为准，工具会报错位）。
// 运行时行为在 StoryRunner（enter 的 seat 字段）：Begin 预藏 王含_站立 与 王含切换；到位亮坐姿、藏站立；
// lookAtDoor=false（仅此步）不强制转镜头。
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class WangHanEnterSetup
{
    const string LOC = "Loc_食堂";
    const string STAND = "王含_站立";
    const string SITTER = "王含切换";
    const string FBX = "Assets/assets/03_动作_Animation/带动画模型/王含/已绑定.fbx";
    const string CTRL = "Assets/assets/03_动作_Animation/Animators/带动画模型/王含_Idle.controller";
    const string REPORT = "Assets/assets/_报告/_第三章王含入场.txt";

    // 锚点名 →（默认摆位, yaw）。节点已存在绝不改位置（手调优先，同 ChapterStoriesSetup 约定）。
    // 路线（用户 2026-10-01 两轮重定）：王含_站立 用户手摆在西北北墙边，沿北墙走到 116.6 处
    // 斜下到 凳子2(9) 西侧红圈位（用户圈定，117.6, 6.3）站着说话；「坐吧坐吧~」后 stage 换坐姿。
    // （直连会蹭西边餐桌的 凳子2 角，v1 拐点是绕这个的；旧「东侧大动脉」路线已删）
    static readonly object[] ANCHORS =
    {
        new object[] { "第3章_王含门口",   new Vector3(111.21f, 0f,  7.12f), 90f  },
        new object[] { "第3章_王含路线_1", new Vector3(116.60f, 0f,  7.10f), 0f  },
        new object[] { "第3章_王含落座",   new Vector3(117.60f, 0f,  6.30f), 180f },
    };

    [MenuItem("Tools/干预项目/第三章王含入场（幂等）")]
    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("第三章王含入场接线（" + System.DateTime.Now + "）");

        var loc = GameObject.Find(LOC);
        if (loc == null) { Debug.LogError("[王含入场] 找不到 " + LOC); return; }
        var container = loc.transform.Find("第3章角色");
        if (container == null) container = loc.transform.Find("第三章角色");
        if (container == null) { Debug.LogError("[王含入场] " + LOC + " 下找不到「第3章角色」容器"); return; }
        var anchorsRoot = loc.transform.Find("多章锚点");
        if (anchorsRoot == null) { Debug.LogError("[王含入场] " + LOC + " 下找不到「多章锚点」"); return; }

        // ① 锚点（缺了才建）
        var doorT = (Transform)null;
        foreach (object[] a in ANCHORS)
        {
            string n = (string)a[0];
            Vector3 p = (Vector3)a[1];
            float yaw = (float)a[2];
            var t = anchorsRoot.Find(n);
            if (t == null)
            {
                var go = new GameObject(n);
                go.transform.SetParent(anchorsRoot, false);
                go.transform.position = p;
                go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                sb.AppendLine("锚点新建：" + n + " " + p + " yaw=" + yaw);
                t = go.transform;
            }
            else sb.AppendLine("锚点已有（不动）：" + n + " " + t.position);
            if (n == "第3章_王含门口") doorT = t;
        }

        // ② 站立实例（缺了才建；已有只补缺组件，绝不挪位）
        var stand = container.Find(STAND);
        if (stand == null)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(FBX);
            if (src == null) { Debug.LogError("[王含入场] 找不到模型：" + FBX); return; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            go.name = STAND;
            go.transform.SetParent(container, false);
            if (doorT != null)
            {
                go.transform.position = doorT.position;
                go.transform.rotation = Quaternion.Euler(0f, doorT.eulerAngles.y, 0f);
            }
            stand = go.transform;
            sb.AppendLine("站立实例新建：" + STAND + " @ " + go.transform.position);
        }
        else sb.AppendLine("站立实例已有（不动位）：" + STAND + " @ " + stand.position);

        // 站立实例应与门口锚点重合（走位起点按【锚点】算）；用户手挪了实例没对齐会在这报警
        if (doorT != null && Vector3.Distance(stand.position, doorT.position) > 0.05f)
            sb.AppendLine("!! 站立实例 (" + stand.position + ") 与门口锚点 (" + doorT.position +
                          ") 不重合——走位起点以锚点为准，请把锚点挪到实例脚下（或挪实例对齐锚点）");

        // Animator（FBX 不自带；控制器带 Idle↔Walk 过渡，NpcEntrance 写 Speed 即走路）
        var anim = stand.GetComponent<Animator>();
        if (anim == null)
        {
            anim = stand.gameObject.AddComponent<Animator>();
            anim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(CTRL);
            sb.AppendLine("补 Animator + " + CTRL);
        }
        else if (anim.runtimeAnimatorController == null)
        {
            anim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(CTRL);
            sb.AppendLine("补控制器：" + CTRL);
        }

        // 胶囊碰撞体（同 EnsureCharacterColliders 口径：高=包围盒高×0.96，半径=高×0.16）
        var col = stand.GetComponent<CapsuleCollider>();
        if (col == null)
        {
            var b = BoundsOf(stand);
            float h = Mathf.Max(0.5f, b.size.y) * 0.96f;
            col = stand.gameObject.AddComponent<CapsuleCollider>();
            col.height = h;
            col.radius = h * 0.16f;
            col.center = new Vector3(0f, h * 0.5f, 0f);
            sb.AppendLine("补 CapsuleCollider：高=" + h.ToString("F2") + " 半径=" + (h * 0.16f).ToString("F2"));
        }

        // 整棵 Outline 层（描边，同其他 第X章角色 成员）
        int outline = LayerMask.NameToLayer("Outline");
        if (outline >= 0) SetLayerRecursive(stand.gameObject, outline);

        // 坐姿实例（用户摆的，工具不碰它的位姿；只核对存在 + 在本章容器下）
        var sitter = container.Find(SITTER);
        if (sitter == null)
            sb.AppendLine("!! 本容器下没找到坐姿模型「" + SITTER + "」——json 的 seat 按名全场景兜底找，但请确认它摆在 " + container.name + " 下");
        else
            sb.AppendLine("坐姿实例：" + SITTER + " @ " + sitter.position + " yaw=" + sitter.eulerAngles.y.ToString("F0") +
                          "（运行时由 StoryRunner 预藏/到位亮出，编辑器里保持可见便于摆位）");

        // ③ 路线净空自检：按锚点顺序逐段采样，对【全部】碰撞体（含路人胶囊/垃圾桶/储物箱，只跳过触发器、
        //    自己和地板级大块）按包围盒水平距离算最小间距，≥0.45m 才算不穿模（用户 2026-09-30）
        sb.AppendLine("路线净空（全部碰撞体包围盒水平距离，要求 ≥0.45m）：");
        var routeNames = new string[ANCHORS.Length];
        for (int i = 0; i < ANCHORS.Length; i++) routeNames[i] = (string)((object[])ANCHORS[i])[0];
        CheckRoute(sb, loc.transform, anchorsRoot, routeNames, stand.gameObject);

        EditorSceneManager.MarkSceneDirty(loc.scene);
        EditorSceneManager.SaveOpenScenes();
        sb.AppendLine("场景已保存。");
        File.WriteAllText(REPORT, sb.ToString(), Encoding.UTF8);
        Debug.Log("[王含入场] 完成，报告见 " + REPORT + "\n" + sb.ToString());
    }

    static void CheckRoute(StringBuilder sb, Transform loc, Transform anchorsRoot, string[] names, GameObject self)
    {
        var pts = new Vector3[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            var t = anchorsRoot.Find(names[i]);
            if (t == null) { sb.AppendLine("  !! 缺锚点 " + names[i]); return; }
            pts[i] = t.position;
        }
        var obs = new System.Collections.Generic.List<Collider>();
        foreach (var c in loc.GetComponentsInChildren<Collider>(true))
        {
            if (c.isTrigger) continue;
            if (c.transform.IsChildOf(self.transform)) continue;
            var b = c.bounds;
            if (b.max.y < 0.05f || b.min.y > 1.7f) continue;          // 不在人体高度带的不参判
            if (b.size.x > 30f && b.size.z > 30f) continue;           // 地板级大块（水平距离无意义）
            obs.Add(c);
        }
        bool allOk = true;
        for (int i = 0; i < pts.Length - 1; i++)
        {
            float len = Vector3.Distance(pts[i], pts[i + 1]);
            int n = Mathf.Max(1, Mathf.CeilToInt(len / 0.2f));
            float worst = 999f; string who = "";
            for (int j = 0; j <= n; j++)
            {
                var p = Vector3.Lerp(pts[i], pts[i + 1], j / (float)n);
                foreach (var c in obs)
                {
                    var b = c.bounds;
                    float dx = Mathf.Max(Mathf.Max(b.min.x - p.x, p.x - b.max.x), 0f);
                    float dz = Mathf.Max(Mathf.Max(b.min.z - p.z, p.z - b.max.z), 0f);
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d < worst) { worst = d; who = c.name; }
                }
            }
            bool ok = worst >= 0.45f;
            if (!ok) allOk = false;
            sb.AppendLine(string.Format("  {0} → {1}（{2:F1}m）最小 {3:F2}m @ {4} {5}",
                names[i].Replace("第3章_王含", ""), names[i + 1].Replace("第3章_王含", ""), len, worst, who,
                ok ? "✓" : "!! 不足 0.45（手调锚点绕开）"));
        }
        sb.AppendLine(allOk ? "  全程净空 ≥0.45m ✓" : "  ★ 存在穿模风险段，见上");
    }

    static Bounds BoundsOf(Transform root)
    {
        var rends = root.GetComponentsInChildren<Renderer>(true);
        var b = new Bounds(root.position, Vector3.one * 0.1f);
        foreach (var r in rends) b.Encapsulate(r.bounds);
        return b;
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform c in go.transform) SetLayerRecursive(c.gameObject, layer);
    }
}
