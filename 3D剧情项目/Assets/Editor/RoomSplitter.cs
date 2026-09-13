// 把「寝室套件2」的整间房 FBX 拆成一个个独立物件，供自由摆放。
// 用法：菜单 Tools/干预项目/拆分房间模型；或放 Assets/_splitroom_trigger.txt 自动执行。
// 输出：Assets/assets/01_场景_Scene/寝室套件2_拆分/<房间>/<物件>.prefab
//       + 每房间一个 <房间>_meshes.asset（装着该房间所有拆出的网格）
//       + Assets/assets/_报告/_房间拆分.txt
//
// 为什么要两步（不是单纯按连通分块）：
//   这些房间是“一整块合并网格”，直接按顶点连通拆会碎成 125~424 个零件
//   （一张桌子 = 桌面 + 5 条腿，因为导出时它们没共享顶点）。
//   所以：连通分块 → 判定并分离“房间壳” → 其余碎块按包围盒邻近聚类 → 每个聚类 = 一个物件。
//   实测 1357 个碎块 → 17 个房间壳 + 192 个物件。
//
// 轴心在“底面中心”，拖到 y=0 即落地；原始坐标写在报告里，需要还原整间房时按坐标摆回。

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public static class RoomSplitter
{
    const string OUT_ROOT  = "Assets/assets/01_场景_Scene/寝室套件2_拆分";
    const string REPORT    = "Assets/assets/_报告/_房间拆分.txt";

    const float WELD           = 0.001f;   // 顶点位置焊接容差（1mm）
    const float CLUSTER_MARGIN = 0.01f;    // 碎块合并间距（1cm）：小于这个距离的碎块视为同一件东西
    const float SHELL_RATIO    = 0.7f;     // XZ 跨度都超过房间跨度的 70% → 判定为房间壳/结构件

    // 只拆这些（空 = 全拆）
    static readonly string[] ONLY = new string[0];

    // ------------------------------------------------------------------
    class Piece { public List<int> tris = new List<int>(); public List<int> subs = new List<int>(); public Bounds b; }

    [MenuItem("Tools/干预项目/拆分房间模型")]
    public static void Run()
    {
        string srcRoot = AssetLocator.Dir("寝室套件2");
        if (srcRoot == null) { Debug.LogError("[RoomSplitter] 找不到 寝室套件2"); return; }

        var log = new List<string>();
        log.Add("房间模型拆分报告  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("源：" + srcRoot + "   →   输出：" + OUT_ROOT);
        log.Add("拆法：连通分块 → 分离房间壳 → 其余按包围盒邻近(" + (CLUSTER_MARGIN * 100f).ToString("0") + "cm)聚类");
        log.Add("");

        var files = Directory.GetFiles(srcRoot, "*.fbx", SearchOption.AllDirectories)
                             .OrderBy(x => x).ToArray();

        // v1 和 v2 里都有 kitchen.fbx，重名会互相覆盖 → 只有重名的才加版本前缀
        var nameCount = files.GroupBy(f => Path.GetFileNameWithoutExtension(f))
                             .ToDictionary(g => g.Key, g => g.Count());

        int totalShell = 0, totalItem = 0, totalRooms = 0;
        foreach (var f in files)
        {
            string path = f.Replace('\\', '/');
            string room = Path.GetFileNameWithoutExtension(path);
            if (nameCount[room] > 1) room = PackOf(path) + "_" + room;
            if (ONLY.Length > 0 && !ONLY.Contains(room)) continue;

            var r = SplitRoom(path, room, log);
            if (r == null) continue;
            totalRooms++; totalShell += r.Value.x; totalItem += r.Value.y;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        log.Add("");
        log.Add(string.Format("合计：{0} 间房 → {1} 个房间壳 + {2} 个物件", totalRooms, totalShell, totalItem));
        log.Add("轴心在底面中心，拖到 y=0 就落地。");

        Directory.CreateDirectory("Assets/assets/_报告");
        File.WriteAllText(REPORT, string.Join("\n", log.ToArray()));
        Debug.Log("[RoomSplitter] 拆分完成，报告：" + REPORT);
    }

    static string PackOf(string path)
    {
        var m = System.Text.RegularExpressions.Regex.Match(path, @"_v(\d+)");
        return m.Success ? "v" + m.Groups[1].Value : "pack";
    }

    // ------------------------------------------------------------------
    static Vector2Int? SplitRoom(string fbxPath, string room, List<string> log)
    {
        var src = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<Mesh>().FirstOrDefault();
        if (src == null) { log.Add("★ " + room + " 没有 Mesh"); return null; }

        var goModel = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        var mrSrc = goModel != null ? goModel.GetComponentInChildren<MeshRenderer>() : null;
        var mats = mrSrc != null ? mrSrc.sharedMaterials : new Material[0];

        string roomDir = OUT_ROOT + "/" + room;
        if (AssetDatabase.IsValidFolder(roomDir)) AssetDatabase.DeleteAsset(roomDir);
        Directory.CreateDirectory(roomDir);
        AssetDatabase.Refresh();

        // ---- 1. 连通分块 ----
        var pieces = LoosePieces(src);
        if (pieces.Count == 0) { log.Add("★ " + room + " 没拆出碎块"); return null; }

        // 房间整体跨度
        var roomB = new Bounds(pieces[0].b.center, Vector3.zero);
        foreach (var p in pieces) roomB.Encapsulate(p.b);
        float rx = roomB.size.x, rz = roomB.size.z;

        // ---- 2. 分离房间壳/结构件 ----
        var shells = new List<Piece>();
        var rest = new List<Piece>();
        foreach (var p in pieces)
        {
            if (p.b.size.x > rx * SHELL_RATIO && p.b.size.z > rz * SHELL_RATIO) shells.Add(p);
            else rest.Add(p);
        }

        // ---- 3. 其余碎块按包围盒邻近聚类 ----
        var groups = Cluster(rest);

        // ---- 4. 每个聚类（+每块结构件）造一个网格 ----
        var built = new List<KeyValuePair<string, Mesh>>();
        var rows = new List<string>();

        int nShell = 0;
        foreach (var s in shells)
        {
            nShell++;
            string nm = shells.Count == 1 ? room + "_房间壳" : string.Format("{0}_结构{1}", room, nShell);
            var m = BuildMesh(src, new List<Piece> { s }, room);
            m.name = nm;
            built.Add(new KeyValuePair<string, Mesh>(nm, m));
            rows.Add(Row(nm, m.bounds, s.b.center));
        }

        // 大件排前面，方便找
        groups = groups.OrderByDescending(g => Volume(g)).ToList();
        int idx = 0;
        foreach (var g in groups)
        {
            idx++;
            var bb = GroupBounds(g);
            var m = BuildMesh(src, g, room);
            string nm = string.Format("{0}_物件{1:000}", room, idx);
            m.name = nm;
            built.Add(new KeyValuePair<string, Mesh>(nm, m));
            rows.Add(Row(nm, m.bounds, bb.center));
        }

        // ---- 5. 落盘：先存网格，再建 prefab（顺序不能反，否则 prefab 引用会悬空）----
        Mesh container = null;
        foreach (var kv in built)
        {
            if (container == null)
            {
                AssetDatabase.CreateAsset(kv.Value, roomDir + "/" + room + "_meshes.asset");
                container = kv.Value;
            }
            else AssetDatabase.AddObjectToAsset(kv.Value, container);
        }
        AssetDatabase.SaveAssets();

        foreach (var kv in built)
        {
            var go = new GameObject(kv.Key);
            go.AddComponent<MeshFilter>().sharedMesh = kv.Value;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = mats;
            PrefabUtility.SaveAsPrefabAsset(go, roomDir + "/" + kv.Key + ".prefab");
            Object.DestroyImmediate(go);
        }
        AssetDatabase.SaveAssets();

        log.Add(string.Format("{0}   原网格 {1} 顶点 / {2} 三角  →  {3} 房间壳 + {4} 物件（原始 {5} 个碎块）",
            room, src.vertexCount, TotalTris(src), shells.Count, groups.Count, pieces.Count));
        log.Add("    名称                      尺寸(米)                原始位置(米)");
        foreach (var s in rows) log.Add(s);
        log.Add("");
        return new Vector2Int(shells.Count, groups.Count);
    }

    static string Row(string name, Bounds b, Vector3 center)
    {
        return string.Format("    {0,-24} {1,6:0.00} × {2,5:0.00} × {3,6:0.00}   ({4,7:0.00},{5,6:0.00},{6,7:0.00})",
            name, b.size.x, b.size.y, b.size.z, center.x, center.y, center.z);
    }

    static float Volume(List<Piece> g)
    {
        var b = GroupBounds(g);
        return b.size.x * b.size.y * b.size.z;
    }

    static Bounds GroupBounds(List<Piece> g)
    {
        var b = g[0].b;
        for (int i = 1; i < g.Count; i++) b.Encapsulate(g[i].b);
        return b;
    }

    // ------------------------------------------------------------------ 连通分块
    static List<Piece> LoosePieces(Mesh m)
    {
        var vs = m.vertices;
        int nv = vs.Length;
        int nsub = Mathf.Max(1, m.subMeshCount);

        // 所有子网格的三角拼一起（mesh.triangles 只给子网格 0）
        var tris = new List<int>();
        var triSub = new List<int>();
        for (int s = 0; s < nsub; s++)
        {
            var t = m.GetTriangles(s);
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                tris.Add(t[i]); tris.Add(t[i + 1]); tris.Add(t[i + 2]);
                triSub.Add(s);
            }
        }
        int ntri = triSub.Count;
        if (nv == 0 || ntri == 0) return new List<Piece>();

        // 按位置焊接
        var keyToRep = new Dictionary<(int, int, int), int>(nv);
        var rep = new int[nv];
        float inv = 1f / WELD;
        for (int i = 0; i < nv; i++)
        {
            var v = vs[i];
            var k = (Mathf.RoundToInt(v.x * inv), Mathf.RoundToInt(v.y * inv), Mathf.RoundToInt(v.z * inv));
            int r;
            if (keyToRep.TryGetValue(k, out r)) rep[i] = r;
            else { keyToRep[k] = i; rep[i] = i; }
        }

        var par = new int[nv];
        for (int i = 0; i < nv; i++) par[i] = i;
        System.Func<int, int> find = null;
        find = a => { while (par[a] != a) { par[a] = par[par[a]]; a = par[a]; } return a; };
        System.Action<int, int> uni = (a, b) => { int ra = find(a), rb = find(b); if (ra != rb) par[rb] = ra; };

        for (int i = 0; i < ntri; i++)
        {
            int a = rep[tris[i * 3]], b = rep[tris[i * 3 + 1]], c = rep[tris[i * 3 + 2]];
            uni(a, b); uni(b, c);
        }

        var map = new Dictionary<int, Piece>();
        for (int i = 0; i < ntri; i++)
        {
            int root = find(rep[tris[i * 3]]);
            Piece p;
            if (!map.TryGetValue(root, out p)) { p = new Piece(); map[root] = p; }
            p.tris.Add(tris[i * 3]); p.tris.Add(tris[i * 3 + 1]); p.tris.Add(tris[i * 3 + 2]);
            p.subs.Add(triSub[i]);
        }

        var list = new List<Piece>();
        foreach (var p in map.Values)
        {
            var bb = new Bounds(vs[p.tris[0]], Vector3.zero);
            for (int i = 0; i < p.tris.Count; i++) bb.Encapsulate(vs[p.tris[i]]);
            p.b = bb; list.Add(p);
        }
        return list;
    }

    // ------------------------------------------------------------------ 邻近聚类
    static List<List<Piece>> Cluster(List<Piece> pieces)
    {
        int n = pieces.Count;
        var par = new int[n];
        for (int i = 0; i < n; i++) par[i] = i;
        System.Func<int, int> find = null;
        find = a => { while (par[a] != a) { par[a] = par[par[a]]; a = par[a]; } return a; };

        float mg = CLUSTER_MARGIN;
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                var a = pieces[i].b; var b = pieces[j].b;
                if (a.min.x - mg <= b.max.x && b.min.x - mg <= a.max.x &&
                    a.min.y - mg <= b.max.y && b.min.y - mg <= a.max.y &&
                    a.min.z - mg <= b.max.z && b.min.z - mg <= a.max.z)
                {
                    int ra = find(i), rb = find(j);
                    if (ra != rb) par[rb] = ra;
                }
            }

        var g = new Dictionary<int, List<Piece>>();
        for (int i = 0; i < n; i++)
        {
            int r = find(i);
            List<Piece> l;
            if (!g.TryGetValue(r, out l)) { l = new List<Piece>(); g[r] = l; }
            l.Add(pieces[i]);
        }
        return g.Values.ToList();
    }

    // ------------------------------------------------------------------ 合并造网格
    static Mesh BuildMesh(Mesh src, List<Piece> group, string room)
    {
        int nsub = Mathf.Max(1, src.subMeshCount);
        var sv = src.vertices;
        var sn = src.normals;
        var suv = src.uv;
        var suv2 = src.uv2;
        var sc = src.colors;
        var st = src.tangents;

        var bb = GroupBounds(group);
        var shift = new Vector3(bb.center.x, bb.min.y, bb.center.z);

        var remap = new Dictionary<int, int>(1024);
        var order = new List<int>();
        var perSub = new List<int>[nsub];
        for (int s = 0; s < nsub; s++) perSub[s] = new List<int>();

        foreach (var p in group)
        {
            for (int t = 0; t < p.subs.Count; t++)
            {
                int s = p.subs[t]; if (s < 0 || s >= nsub) s = 0;
                for (int k = 0; k < 3; k++)
                {
                    int o = p.tris[t * 3 + k];
                    int ni;
                    if (!remap.TryGetValue(o, out ni)) { ni = order.Count; remap[o] = ni; order.Add(o); }
                    perSub[s].Add(ni);
                }
            }
        }

        int n = order.Count;
        var v2 = new Vector3[n];
        for (int i = 0; i < n; i++) v2[i] = sv[order[i]] - shift;

        var m = new Mesh();
        m.name = room + "_part";
        if (n > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.vertices = v2;
        m.subMeshCount = nsub;
        for (int s = 0; s < nsub; s++) m.SetTriangles(perSub[s], s);

        if (sn != null && sn.Length == sv.Length) { var a = new Vector3[n]; for (int i = 0; i < n; i++) a[i] = sn[order[i]]; m.normals = a; }
        if (suv != null && suv.Length == sv.Length) { var a = new Vector2[n]; for (int i = 0; i < n; i++) a[i] = suv[order[i]]; m.uv = a; }
        if (suv2 != null && suv2.Length == sv.Length) { var a = new Vector2[n]; for (int i = 0; i < n; i++) a[i] = suv2[order[i]]; m.uv2 = a; }
        if (sc != null && sc.Length == sv.Length) { var a = new Color[n]; for (int i = 0; i < n; i++) a[i] = sc[order[i]]; m.colors = a; }
        if (st != null && st.Length == sv.Length) { var a = new Vector4[n]; for (int i = 0; i < n; i++) a[i] = st[order[i]]; m.tangents = a; }
        else m.RecalculateTangents();

        m.RecalculateBounds();
        return m;
    }

    static int TotalTris(Mesh m)
    {
        int n = 0;
        for (int s = 0; s < m.subMeshCount; s++) n += (int)(m.GetIndexCount(s) / 3);
        return n;
    }
}

// 工程根存在 Assets/_splitroom_trigger.txt 时，编辑器下次刷新/重编译后自动跑一次拆分。
[InitializeOnLoad]
public static class RoomSplitterTrigger
{
    const string Trigger = "Assets/_splitroom_trigger.txt";

    static RoomSplitterTrigger()
    {
        if (!File.Exists(Trigger)) return;
        EditorApplication.delayCall += () =>
        {
            if (File.Exists(Trigger)) File.Delete(Trigger);
            try { RoomSplitter.Run(); Debug.Log("[RoomSplitter] 自动拆分完成"); }
            catch (System.Exception e)
            {
                Directory.CreateDirectory("../额外文件");
                File.WriteAllText("../额外文件/错误_房间拆分.txt", e.ToString());
                Debug.LogError("[RoomSplitter] 自动拆分失败: " + e);
            }
        };
    }
}
