// ============================================================================
// 导出给 Mixamo 的模型（去骨骼 / 只留网格）
//
// 用途：现在的角色是「CC_Base 骨架 + mixamo 动画靠 Unity 重定向」，
//       重定向本身没问题，但 CC_Base 身上那堆「扭转骨/脚趾骨」mixamo 从不驱动，
//       权重又压在它们身上 → 各种畸形。干脆把组装好的网格导出去，
//       拿去 Mixamo 自动绑定一套自己的骨架，再导回来（动画就是原生的了）。
//
// 做的事：
//   1) 读 9 个角色 prefab（角色_URP/*_可动.prefab）
//   2) 把该角色身上所有【可见】的 SkinnedMeshRenderer / MeshRenderer
//      在【rest 姿势下蒙皮后的形状】烘焙成一块合并网格（T-pose，站在原点上）
//      —— 这一步很关键：上衣/外套是 A-pose 建模的，必须用蒙皮结果而不是原始顶点，
//         否则衣服和身体会差 45°。
//   3) 写出 .fbx（ASCII，Y-up / 右手 / cm）+ .obj（同样数据，兜底用）
//      ※ 没有任何骨骼、没有任何蒙皮信息 —— Mixamo 拿到的是纯模型。
//
// 朝向：Unity 是左手系、FBX/Maya 是右手系。默认 FLIP_X=true（X 取反 + 三角形绕序反转），
//       这是 Unity↔Maya 之间轴转换的对应做法。如果导进 Mixamo 发现镜像了，
//       把 FLIP_X 改成 false 再跑一次（会多出一份 *_noflip 文件）。
//
// 菜单：Tools/干预项目/导出：给 Mixamo 用的模型（去骨骼）
// 产物：Assets/assets/_报告/导出给Mixamo/*.fbx / *.obj
// 报告：Assets/assets/_报告/_导出给Mixamo.txt
// ============================================================================
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

public static class ExportForMixamo
{
    const string CHAR_DIR = "Assets/assets/02_角色_Character/角色_URP";
    const string OUT = "../额外文件/导出给Mixamo";   // 放项目外面：不然 Unity 会把它们当模型导入（86MB）
    const string REPORT = "Assets/assets/_报告/_导出给Mixamo.txt";

    const bool FLIP_X = true;        // Unity(左手) → FBX/Maya(右手)：X 取反 + 绕序反转
    const bool ALSO_NOFLIP = false;  // （已废：官方 FBX 自己处理轴向）
    const float UNIT = 100f;         // FBX 单位：1 单位 = 1cm（Maya 惯例，170cm 的人）

    static readonly CultureInfo CI = CultureInfo.InvariantCulture;

    [MenuItem("Tools/干预项目/导出：给 Mixamo 用的模型（去骨骼）", false, 100)]
    public static void Run()
    {
        var log = new List<string>();
        log.Add("导出给 Mixamo 的模型（去骨骼、T-pose、rest 姿势蒙皮后的形状）  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("输出目录：" + Path.GetFullPath(OUT));
        log.Add("");

        Directory.CreateDirectory(OUT);
        AssetDatabase.Refresh();

        foreach (var f in Directory.GetFiles(CHAR_DIR, "*.prefab").OrderBy(p => p))
        {
            string path = f.Replace('\\', '/');
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Export(contents, log);
            }
            catch (System.Exception e)
            {
                log.Add("★ " + contents.name + " 导出失败：" + e.Message);
            }
            PrefabUtility.UnloadPrefabContents(contents);
        }

        log.Add("");
        log.Add("说明：");
        log.Add("  · 文件里【只有网格】，没有骨骼也没有蒙皮 —— Mixamo 直接当静态模型上传即可。");
        log.Add("  · 姿势 = Unity 里的 rest 姿势（标准 T-pose，双臂平伸、双腿并拢站直、站在原点）。");
        log.Add("  · 单位 = 厘米（一个 1.7m 的角色导出后高 170）。");
        log.Add("  · 如果导入 Mixamo 后发现【左右镜像了】，告诉我，把 FLIP_X 开关改一下重导即可；");
        log.Add("    这一步只是轴向约定，不影响网格本身。");
        File.WriteAllText(REPORT, string.Join("\n", log.ToArray()));
        AssetDatabase.Refresh();
        Debug.Log("[ExportForMixamo] 报告：" + REPORT);
    }

    // ------------------------------------------------------------------ 单个角色
    static void Export(GameObject root, List<string> log)
    {
        string name = root.name;
        var verts = new List<Vector3>();
        float rawMinY = float.MaxValue, rawMaxY = float.MinValue;      // 原始网格数据的高度（米）
        float skinMinY = float.MaxValue, skinMaxY = float.MinValue;   // 蒙皮结果的高度（可能差一个 cm/m 约定缩放）
        var norms = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        var matIdx = new List<int>();          // 每个三角形属于哪个材质槽
        var mats = new List<Material>();

        var rootPos = root.transform.position;
        var rootRot = root.transform.rotation;

        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || !r.gameObject.activeInHierarchy) continue;
            if (r is TrailRenderer || r is LineRenderer || r is ParticleSystemRenderer) continue;

            Mesh mesh = null;
            Matrix4x4[] M = null;
            var smr = r as SkinnedMeshRenderer;
            if (smr != null)
            {
                mesh = smr.sharedMesh;
                if (mesh == null || smr.bones == null) continue;
                var bind = mesh.bindposes;
                if (bind.Length != smr.bones.Length) { log.Add("  ★ " + smr.name + "：bindposes 数 ≠ bones 数，跳过"); continue; }
                M = new Matrix4x4[smr.bones.Length];
                for (int i = 0; i < M.Length; i++)
                    M[i] = (smr.bones[i] != null ? smr.bones[i].localToWorldMatrix : Matrix4x4.identity) * bind[i];
            }
            else
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null) continue;
                mesh = mf.sharedMesh;
                if (mesh == null) continue;
                M = new[] { r.transform.localToWorldMatrix };
            }

            Vector3[] pos; Vector3[] nrm; Vector2[] uv; int[] tri; BoneWeight[] bw;
            if (!ReadMesh(mesh, out pos, out nrm, out uv, out tri, out bw)) { log.Add("  ★ " + mesh.name + "：读不到网格数据，跳过"); continue; }

            // 材质槽
            int baseV = verts.Count;
            var srcMats = r.sharedMaterials;
            int[] slotOf = new int[Mathf.Max(1, srcMats.Length)];
            for (int i = 0; i < slotOf.Length; i++)
            {
                var mat = i < srcMats.Length ? srcMats[i] : null;
                int idx = mats.IndexOf(mat);
                if (idx < 0) { mats.Add(mat); idx = mats.Count - 1; }
                slotOf[i] = idx;
            }

            for (int v = 0; v < pos.Length; v++)
            {
                if (pos[v].y < rawMinY) rawMinY = pos[v].y;
                if (pos[v].y > rawMaxY) rawMaxY = pos[v].y;
            }

            // 顶点：蒙皮到 rest 姿势 → 再转到角色根局部空间
            for (int v = 0; v < pos.Length; v++)
            {
                Vector3 p = Vector3.zero, n = Vector3.zero;
                if (bw != null && bw.Length == pos.Length && M.Length > 1)
                {
                    var w = bw[v];
                    AddW(ref p, ref n, M, w.boneIndex0, w.weight0, pos[v], nrm != null && v < nrm.Length ? nrm[v] : Vector3.up);
                    AddW(ref p, ref n, M, w.boneIndex1, w.weight1, pos[v], nrm != null && v < nrm.Length ? nrm[v] : Vector3.up);
                    AddW(ref p, ref n, M, w.boneIndex2, w.weight2, pos[v], nrm != null && v < nrm.Length ? nrm[v] : Vector3.up);
                    AddW(ref p, ref n, M, w.boneIndex3, w.weight3, pos[v], nrm != null && v < nrm.Length ? nrm[v] : Vector3.up);
                }
                else
                {
                    p = M[0].MultiplyPoint3x4(pos[v]);
                    n = M[0].MultiplyVector(nrm != null && v < nrm.Length ? nrm[v] : Vector3.up);
                }
                verts.Add(Quaternion.Inverse(rootRot) * (p - rootPos));
                if (p.y < skinMinY) skinMinY = p.y;
                if (p.y > skinMaxY) skinMaxY = p.y;
                norms.Add((Quaternion.Inverse(rootRot) * (n.sqrMagnitude > 1e-8f ? n.normalized : Vector3.up)).normalized);
                uvs.Add(uv != null && v < uv.Length ? uv[v] : Vector2.zero);
            }

            // 三角形（按子网格分材质）
            int subCount = mesh.subMeshCount;
            for (int s = 0; s < subCount; s++)
            {
                int slot = slotOf[Mathf.Min(s, slotOf.Length - 1)];
                var st = GetIndices(mesh, s);
                for (int i = 0; i + 2 < st.Length; i += 3)
                {
                    tris.Add(baseV + st[i]);
                    tris.Add(baseV + st[i + 1]);
                    tris.Add(baseV + st[i + 2]);
                    matIdx.Add(slot);
                }
            }
        }

        if (verts.Count == 0) { log.Add("★ " + name + "：一个顶点都没收集到，跳过"); return; }

        // ★ 缩放自校准：原始网格数据是按「米」建的（鞋子 y≈0~0.1、裙子 0.6~1.0），
        //   而蒙皮结果被 FBX 里的 cm/m 约定放大了 N 倍。用两者高度比拉回真实尺寸，并把脚底对到 y=0。
        float rawH = Mathf.Max(0.01f, rawMaxY - rawMinY);
        float skinH = Mathf.Max(0.01f, skinMaxY - skinMinY);
        float k = rawH / skinH;
        for (int i = 0; i < verts.Count; i++)
            verts[i] = (verts[i] - new Vector3(0, skinMinY, 0)) * k;      // 缩到真实尺寸，并且脚底对到 y=0
        log.Add(string.Format("  [缩放校正] 原始网格高 {0:0.000}m ÷ 蒙皮高 {1:0.000} = ×{2:0.####}", rawH, skinH, k));

        // ① 官方 FBX Exporter（首选）：直接拿 Unity 空间的网格交给它，轴向转换由官方包处理
        bool ok = WriteFbxOfficial(name, verts, norms, uvs, tris, matIdx, mats, log);

        // ② OBJ（兜底，Mixamo 也收 OBJ）：这套自己写，翻 X 符合 Y-up 右手系观感
        WriteOne(name, verts, norms, uvs, tris, matIdx, mats, false, log);
        // 官方 FBX 自己会做轴向转换，不需要 noflip 版本；OBJ 保留一份即可
    }

    static void AddW(ref Vector3 p, ref Vector3 n, Matrix4x4[] M, int bi, float w, Vector3 v, Vector3 nv)
    {
        if (w <= 0f || bi < 0 || bi >= M.Length) return;
        p += M[bi].MultiplyPoint3x4(v) * w;
        n += M[bi].MultiplyVector(nv) * w;
    }

    // ------------------------------------------------------------------ 官方 FBX 导出（反射调用，没有包也不会编译不过）
    static bool WriteFbxOfficial(string name, List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs,
                                 List<int> tris, List<int> matIdx, List<Material> mats, List<string> log)
    {
        var t = System.Type.GetType("UnityEditor.Formats.Fbx.Exporter.ModelExporter, Unity.Formats.Fbx.Editor");
        if (t == null)
        {
            log.Add("  ★ 没有 FBX Exporter 包（跑一次菜单「工具：安装 FBX Exporter 包」）→ 这轮只出了 OBJ");
            return false;
        }
        var ex = t.GetMethod("ExportObject", new[] { typeof(string), typeof(Object) });
        if (ex == null) { log.Add("  ★ FBX Exporter API 不匹配，跳过 FBX"); return false; }

        // 合成网格（Unity 原始空间：米、脚底 y=0、面向 +Z）
        var mesh = new Mesh();
        mesh.name = name;
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        int nSub = Mathf.Max(1, mats.Count);
        var bySlot = new List<int>[nSub];
        for (int i = 0; i < nSub; i++) bySlot[i] = new List<int>();
        for (int i = 0; i + 2 < tris.Count; i += 3)
        {
            int slot = Mathf.Clamp(i / 3 < matIdx.Count ? matIdx[i / 3] : 0, 0, nSub - 1);
            bySlot[slot].Add(tris[i]); bySlot[slot].Add(tris[i + 1]); bySlot[slot].Add(tris[i + 2]);
        }
        mesh.subMeshCount = nSub;
        for (int i = 0; i < nSub; i++) mesh.SetTriangles(bySlot[i], i);
        mesh.RecalculateBounds();

        var go = new GameObject("~mixamo_" + name);
        try
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var ms = new Material[nSub];
            for (int i = 0; i < nSub; i++) ms[i] = (i < mats.Count && mats[i] != null) ? mats[i] : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mr.sharedMaterials = ms;

            string path = Path.GetFullPath(OUT + "/" + name + ".fbx");
            object ret = ex.Invoke(null, new object[] { path, go });
            string retPath = ret as string;
            bool ok = !string.IsNullOrEmpty(retPath) && File.Exists(path);
            log.Add(string.Format("      [官方FBX] {0}  顶点 {1} 三角 {2} 材质 {3}  {4} KB  {5}",
                name + ".fbx", verts.Count, tris.Count / 3, nSub,
                File.Exists(path) ? (new FileInfo(path).Length / 1024).ToString() : "0", ok ? "✓" : "★ 失败 ret=" + (retPath ?? "null")));
            return ok;
        }
        catch (System.Exception e)
        {
            log.Add("  ★ 官方 FBX 导出异常：" + e.Message);
            return false;
        }
        finally
        {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(mesh);
        }
    }

    // ------------------------------------------------------------------ 写文件
    static void WriteOne(string name, List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs,
                         List<int> tris, List<int> matIdx, List<Material> mats, bool noFlip, List<string> log)
    {
        string suffix = noFlip ? "_noflip" : "";
        var V = new Vector3[verts.Count];
        var N = new Vector3[norms.Count];
        for (int i = 0; i < V.Length; i++)
        {
            var p = verts[i]; var n = norms[i];
            if (!noFlip) { p.x = -p.x; n.x = -n.x; }          // X 取反（Unity→FBX 右手系）
            V[i] = p * UNIT;
            N[i] = n;
        }
        var T = tris.ToArray();
        if (!noFlip)                                          // 翻轴要反绕序，否则面朝里
            for (int i = 0; i + 2 < T.Length; i += 3) { int t = T[i]; T[i] = T[i + 2]; T[i + 2] = t; }

        string fbx = OUT + "/" + name + suffix + ".fbx";
        string obj = OUT + "/" + name + suffix + ".obj";
        // （手写的 ASCII FBX 不被 Unity 接受，已废——FBX 一律走官方 Exporter）
        File.WriteAllText(obj, BuildObj(name, V, N, uvs, T), new UTF8Encoding(false));

        // 包围盒自检
        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        foreach (var v in V) { min = Vector3.Min(min, v); max = Vector3.Max(max, v); }
        log.Add(string.Format("{0,-12} 顶点 {1,6}  三角 {2,6}  材质 {3,2}  →  {4}.obj  ({5} KB)",
            name + suffix, V.Length, T.Length / 3, mats.Count, name + suffix,
            (new FileInfo(obj).Length / 1024)));
        log.Add(string.Format("             包围盒 X {0,7:0.1}~{1,7:0.1}   Y {2,7:0.1}~{3,7:0.1}   Z {4,7:0.1}~{5,7:0.1}（cm）",
            min.x, max.x, min.y, max.y, min.z, max.z));
        log.Add("             材质顺序：" + string.Join(" / ", mats.Select(m => m != null ? m.name : "无")));
    }

    static string BuildObj(string name, Vector3[] V, Vector3[] N, List<Vector2> uv, int[] T)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# " + name + "  exported from Unity by ExportForMixamo（无骨骼，T-pose）");
        sb.AppendLine("o " + name);
        foreach (var v in V) sb.AppendLine("v " + F(v.x) + " " + F(v.y) + " " + F(v.z));
        foreach (var t in uv) sb.AppendLine("vt " + F(t.x) + " " + F(t.y));
        foreach (var n in N) sb.AppendLine("vn " + F(n.x) + " " + F(n.y) + " " + F(n.z));
        for (int i = 0; i + 2 < T.Length; i += 3)
        {
            int a = T[i] + 1, b = T[i + 1] + 1, c = T[i + 2] + 1;
            sb.AppendLine(string.Format("f {0}/{0}/{0} {1}/{1}/{1} {2}/{2}/{2}", a, b, c));
        }
        return sb.ToString();
    }

    // 极简但合法的 ASCII FBX 7.4（只有网格 + 材质槽，没有骨骼/蒙皮/动画）
    static string BuildFbx(string name, Vector3[] V, Vector3[] N, List<Vector2> uv, int[] T, List<int> matIdx, List<Material> mats)
    {
        var sb = new StringBuilder();
        sb.AppendLine("; FBX 7.4.0 project file");
        sb.AppendLine("; " + name + " - exported from Unity (mesh only, no skeleton)");
        sb.AppendLine("FBXHeaderExtension:  {");
        sb.AppendLine("\tFBXHeaderVersion: 1003");
        sb.AppendLine("\tFBXVersion: 7400");
        sb.AppendLine("\tCreationTimeStamp:  { Version: 1000 Year: 2024 Month: 1 Day: 1 Hour: 0 Minute: 0 Second: 0 Millisecond: 0 }");
        sb.AppendLine("\tCreator: \"UnityExportForMixamo\"");
        sb.AppendLine("}");
        sb.AppendLine("GlobalSettings:  {");
        sb.AppendLine("\tVersion: 1000");
        sb.AppendLine("\tProperties70:  {");
        sb.AppendLine("\t\tP: \"UpAxis\", \"int\", \"Integer\", \"\",1");
        sb.AppendLine("\t\tP: \"UpAxisSign\", \"int\", \"Integer\", \"\",1");
        sb.AppendLine("\t\tP: \"FrontAxis\", \"int\", \"Integer\", \"\",2");
        sb.AppendLine("\t\tP: \"FrontAxisSign\", \"int\", \"Integer\", \"\",1");
        sb.AppendLine("\t\tP: \"CoordAxis\", \"int\", \"Integer\", \"\",0");
        sb.AppendLine("\t\tP: \"CoordAxisSign\", \"int\", \"Integer\", \"\",1");
        sb.AppendLine("\t\tP: \"UnitScaleFactor\", \"double\", \"Number\", \"\",100");
        sb.AppendLine("\t}");
        sb.AppendLine("}");
        int nMats = Mathf.Max(1, mats.Count);
        sb.AppendLine("Definitions:  {");
        sb.AppendLine("\tVersion: 100");
        sb.AppendLine("\tCount: " + (2 + nMats));
        sb.AppendLine("\tObjectType: \"Model\" { Count: 1 }");
        sb.AppendLine("\tObjectType: \"Geometry\" { Count: 1 }");
        sb.AppendLine("\tObjectType: \"Material\" { Count: " + nMats + " }");
        sb.AppendLine("}");

        sb.AppendLine("Objects:  {");
        // Geometry
        sb.AppendLine("\tGeometry: 1000000, \"Geometry::" + name + "\", \"Mesh\" {");
        sb.Append("\t\tVertices: *" + (V.Length * 3) + " { a: ");
        for (int i = 0; i < V.Length; i++) { if (i > 0) sb.Append(","); sb.Append(F(V[i].x) + "," + F(V[i].y) + "," + F(V[i].z)); }
        sb.AppendLine(" }");
        // 多边形顶点索引：三角形最后一个索引取反再 -1（FBX 的“多边形结束”编码）
        sb.Append("\t\tPolygonVertexIndex: *" + T.Length + " { a: ");
        for (int i = 0; i + 2 < T.Length; i += 3)
        {
            if (i > 0) sb.Append(",");
            sb.Append(T[i] + "," + T[i + 1] + "," + (-T[i + 2] - 1));
        }
        sb.AppendLine(" }");
        sb.AppendLine("\t\tGeometryVersion: 124");
        // 法线（按多边形顶点、直接存）
        sb.AppendLine("\t\tLayerElementNormal: 0 {");
        sb.AppendLine("\t\t\tVersion: 101");
        sb.AppendLine("\t\t\tName: \"\"");
        sb.AppendLine("\t\t\tMappingInformationType: \"ByPolygonVertex\"");
        sb.AppendLine("\t\t\tReferenceInformationType: \"Direct\"");
        sb.Append("\t\t\tNormals: *" + (T.Length * 3) + " { a: ");
        for (int i = 0; i < T.Length; i++)
        {
            var n = N[T[i]];
            if (i > 0) sb.Append(",");
            sb.Append(F(n.x) + "," + F(n.y) + "," + F(n.z));
        }
        sb.AppendLine(" }");
        sb.AppendLine("\t\t}");
        // UV
        sb.AppendLine("\t\tLayerElementUV: 0 {");
        sb.AppendLine("\t\t\tVersion: 101");
        sb.AppendLine("\t\t\tName: \"UVMap\"");
        sb.AppendLine("\t\t\tMappingInformationType: \"ByPolygonVertex\"");
        sb.AppendLine("\t\t\tReferenceInformationType: \"Direct\"");
        sb.Append("\t\t\tUV: *" + (T.Length * 2) + " { a: ");
        for (int i = 0; i < T.Length; i++)
        {
            var t = (uv != null && T[i] < uv.Count) ? uv[T[i]] : Vector2.zero;
            if (i > 0) sb.Append(",");
            sb.Append(F(t.x) + "," + F(t.y));
        }
        sb.AppendLine(" }");
        sb.AppendLine("\t\t}");
        // 材质槽（按多边形）
        sb.AppendLine("\t\tLayerElementMaterial: 0 {");
        sb.AppendLine("\t\t\tVersion: 101");
        sb.AppendLine("\t\t\tName: \"\"");
        sb.AppendLine("\t\t\tMappingInformationType: \"ByPolygon\"");
        sb.AppendLine("\t\t\tReferenceInformationType: \"IndexToDirect\"");
        sb.Append("\t\t\tMaterials: *" + (T.Length / 3) + " { a: ");
        var perPoly = new List<int>();
        for (int p = 0; p < T.Length / 3; p++) perPoly.Add(p < matIdx.Count ? matIdx[p] : 0);
        for (int p = 0; p < perPoly.Count; p++) { if (p > 0) sb.Append(","); sb.Append(perPoly[p]); }
        sb.AppendLine(" }");
        sb.AppendLine("\t\t}");
        sb.AppendLine("\t\tLayer: 0 {");
        sb.AppendLine("\t\t\tVersion: 100");
        sb.AppendLine("\t\t\tLayerElement:  { Type: \"LayerElementNormal\" TypedIndex: 0 }");
        sb.AppendLine("\t\t\tLayerElement:  { Type: \"LayerElementMaterial\" TypedIndex: 0 }");
        sb.AppendLine("\t\t\tLayerElement:  { Type: \"LayerElementUV\" TypedIndex: 0 }");
        sb.AppendLine("\t\t}");
        sb.AppendLine("\t}");
        // Model
        sb.AppendLine("\tModel: 2000000, \"Model::" + name + "\", \"Mesh\" {");
        sb.AppendLine("\t\tVersion: 232");
        sb.AppendLine("\t\tProperties70:  {");
        sb.AppendLine("\t\t\tP: \"Lcl Translation\", \"Lcl Translation\", \"\", \"A\",0,0,0");
        sb.AppendLine("\t\t\tP: \"Lcl Rotation\", \"Lcl Rotation\", \"\", \"A\",0,0,0");
        sb.AppendLine("\t\t\tP: \"Lcl Scaling\", \"Lcl Scaling\", \"\", \"A\",1,1,1");
        sb.AppendLine("\t\t}");
        sb.AppendLine("\t\tShading: T");
        sb.AppendLine("\t\tCulling: \"CullingOff\"");
        sb.AppendLine("\t}");
        // Materials
        for (int i = 0; i < nMats; i++)
        {
            string mn = (mats.Count > i && mats[i] != null) ? mats[i].name : ("slot" + i);
            sb.AppendLine("\tMaterial: " + (3000000 + i) + ", \"Material::" + mn + "\", \"\" {");
            sb.AppendLine("\t\tVersion: 102");
            sb.AppendLine("\t\tShadingModel: \"phong\"");
            sb.AppendLine("\t\tMultiLayer: 0");
            sb.AppendLine("\t}");
        }
        sb.AppendLine("}");
        // Connections
        sb.AppendLine("Connections:  {");
        sb.AppendLine("\tC: \"OO\",1000000,2000000");
        sb.AppendLine("\tC: \"OO\",2000000,0");
        for (int i = 0; i < nMats; i++)
            sb.AppendLine("\tC: \"OO\"," + (3000000 + i) + ",2000000");
        sb.AppendLine("}");
        return sb.ToString();
    }

    static string F(float v) { return v.ToString("0.######", CI); }

    // ------------------------------------------------------------------ 读网格
    static bool ReadMesh(Mesh mesh, out Vector3[] pos, out Vector3[] nrm, out Vector2[] uv, out int[] tri, out BoneWeight[] bw)
    {
        pos = mesh.vertices; nrm = mesh.normals; uv = mesh.uv; tri = mesh.triangles; bw = mesh.boneWeights;
        if (pos != null && pos.Length > 0 && tri != null && tri.Length > 0) return true;
        try
        {
            var md = Mesh.AcquireReadOnlyMeshData(mesh);
            var d = md[0];
            var tp = new NativeArray<Vector3>(d.vertexCount, Allocator.Temp);
            d.GetVertices(tp); pos = tp.ToArray(); tp.Dispose();
            var tn = new NativeArray<Vector3>(d.vertexCount, Allocator.Temp);
            d.GetNormals(tn); nrm = tn.ToArray(); tn.Dispose();
            var tu = new NativeArray<Vector2>(d.vertexCount, Allocator.Temp);
            d.GetUVs(0, tu); uv = tu.ToArray(); tu.Dispose();
            var acc = new List<int>();
            for (int s = 0; s < d.subMeshCount; s++)
            {
                var sm = d.GetSubMesh(s);
                if (sm.topology != MeshTopology.Triangles) continue;
                var arr = new NativeArray<int>(sm.indexCount, Allocator.Temp);
                d.GetIndices(arr, s);
                for (int i = 0; i < arr.Length; i++) acc.Add(arr[i]);
                arr.Dispose();
            }
            tri = acc.ToArray();
            md.Dispose();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[ExportForMixamo] MeshData 读取失败 " + mesh.name + " : " + e.Message);
            return false;
        }
        return pos != null && pos.Length > 0 && tri != null && tri.Length > 0;
    }

    static int[] GetIndices(Mesh mesh, int sub)
    {
        try
        {
            var md = Mesh.AcquireReadOnlyMeshData(mesh);
            var d = md[0];
            var sm = d.GetSubMesh(sub);
            var arr = new NativeArray<int>(sm.indexCount, Allocator.Temp);
            d.GetIndices(arr, sub);
            int[] r = arr.ToArray();
            arr.Dispose();
            md.Dispose();
            return r;
        }
        catch { return new int[0]; }
    }
}

[InitializeOnLoad]
static class ExportForMixamoTrigger
{
    const string T = "Assets/_mixamoexp_trigger.txt";
    static ExportForMixamoTrigger()
    {
        if (!File.Exists(T)) return;
        EditorApplication.delayCall += () =>
        {
            try
            {
                if (File.Exists(T)) File.Delete(T);
                ExportForMixamo.Run();
            }
            catch (System.Exception e)
            {
                Debug.LogError("[ExportForMixamo] " + e);
                Directory.CreateDirectory("../额外文件");
                File.WriteAllText("../额外文件/错误_导出Mixamo.txt", e.ToString());
            }
        };
    }
}
