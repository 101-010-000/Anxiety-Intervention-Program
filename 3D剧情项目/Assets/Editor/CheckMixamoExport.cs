// 自检：把导出的 fbx 重新丢给 Unity 自己的 FBX 导入器读一遍
// —— 能读出来（有网格、顶点数对）才说明手写的 FBX 格式是合规的。
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class CheckMixamoExport
{
    const string SRC = "../额外文件/导出给Mixamo";
    const string TMP = "Assets/_fbxcheck";
    const string REPORT = "Assets/assets/_报告/_导出FBX自检.txt";

    [MenuItem("Tools/干预项目/自检：导出的 FBX 能不能被导入", false, 101)]
    public static void Run()
    {
        var log = new List<string>();
        log.Add("导出的 FBX 自检（用 Unity 自己的 FBX 导入器读一遍）  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("");
        Directory.CreateDirectory(TMP);
        AssetDatabase.Refresh();

        var files = Directory.GetFiles(SRC, "*.fbx").OrderBy(p => p).ToList();
        foreach (var f in files)
        {
            string name = Path.GetFileName(f);
            string dst = TMP + "/" + name;
            File.Copy(f, dst, true);
            AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);

            var go = AssetDatabase.LoadAssetAtPath<GameObject>(dst);
            var meshes = AssetDatabase.LoadAllAssetsAtPath(dst).OfType<Mesh>().ToList();
            int tris = 0, verts = 0;
            var ms = new List<string>();
            foreach (var m in meshes)
            {
                verts += m.vertexCount;
                tris += m.triangles != null ? m.triangles.Length / 3 : 0;
                ms.Add(m.name + "(" + m.vertexCount + ")");
            }
            string line = (go == null ? "★ 整个文件读不进来" : "子节点 " + go.GetComponentsInChildren<Transform>(true).Length);
            if (meshes.Count > 0)
            {
                var b = meshes[0].bounds;
                line += string.Format("  包围盒(米) X {0:0.00}~{1:0.00} Y {2:0.00}~{3:0.00} Z {4:0.00}~{5:0.00}",
                    b.min.x, b.max.x, b.min.y, b.max.y, b.min.z, b.max.z);
            }
            log.Add(string.Format("{0,-26} 网格 {1} 块 顶点 {2} 三角 {3}   {4}",
                name, meshes.Count, verts, tris, line));
            if (meshes.Count > 0) log.Add("     网格名：" + string.Join(" / ", ms));
            AssetDatabase.DeleteAsset(dst);
        }

        AssetDatabase.DeleteAsset(TMP);
        AssetDatabase.Refresh();
        log.Add("");
        log.Add("结论：上面每个文件都应该「网格 1 块、顶点 ~10000、三角 ~17000」。");
        log.Add("      若全是 0 / 读不进来 → 手写的 FBX 格式不被接受，得换导出方式。");
        File.WriteAllText(REPORT, string.Join("\n", log.ToArray()));
        Debug.Log("[CheckMixamoExport] " + REPORT);
    }
}

[InitializeOnLoad]
static class CheckMixamoExportTrigger
{
    const string T = "Assets/_fbxcheck_trigger.txt";
    static CheckMixamoExportTrigger()
    {
        if (!File.Exists(T)) return;
        EditorApplication.delayCall += () =>
        {
            try { if (File.Exists(T)) File.Delete(T); CheckMixamoExport.Run(); }
            catch (System.Exception e) { Debug.LogError("[CheckMixamoExport] " + e); }
        };
    }
}
