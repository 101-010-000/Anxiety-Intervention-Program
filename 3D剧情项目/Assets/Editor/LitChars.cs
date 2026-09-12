using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;

public static class LitChars
{
    static string root = "Assets/assets/02_角色_Character";
    static StringBuilder sb = new StringBuilder();

    static Dictionary<string, string> LoadGuidMap()
    {
        var map = new Dictionary<string, string>();
        foreach (var meta in Directory.GetFiles(root, "*.meta", SearchOption.AllDirectories))
        {
            string t = File.ReadAllText(meta);
            var m = Regex.Match(t, @"guid: ([0-9a-f]{32})");
            if (m.Success) map[m.Groups[1].Value] = meta.Substring(0, meta.Length - 5).Replace('\\', '/');
        }
        return map;
    }

    static Texture2D TexByGuid(Dictionary<string, string> map, string txt, string prop)
    {
        var m = Regex.Match(txt, "- " + prop + @":\n\s+m_Texture: \{fileID: \d+, guid: ([0-9a-f]{32})");
        if (!m.Success) return null;
        string path;
        if (!map.TryGetValue(m.Groups[1].Value, out path) || !path.EndsWith(".png")) return null;
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void ConvertOne(string matPath, Dictionary<string, string> map, string outDir)
    {
        string txt = File.ReadAllText(matPath);
        var albedo = TexByGuid(map, txt, "_BaseMap");
        if (albedo == null) albedo = TexByGuid(map, txt, "_MainTex");
        if (albedo == null) albedo = TexByGuid(map, txt, "_RGB_Map");
        var bump = TexByGuid(map, txt, "_BumpMap");
        if (bump == null) bump = TexByGuid(map, txt, "_Normal");
        var metallic = TexByGuid(map, txt, "_Metallic");
        var occ = TexByGuid(map, txt, "_OcclusionMap");

        string name = Path.GetFileName(matPath);
        string outPath = outDir + "/" + name;
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.name = name;
        if (albedo != null) m.SetTexture("_BaseMap", albedo);
        if (bump != null) m.SetTexture("_BumpMap", bump);
        if (occ != null) m.SetTexture("_OcclusionMap", occ);
        if (metallic != null) m.SetTexture("_MetallicGlossMap", metallic);
        AssetDatabase.CreateAsset(m, outPath);
    }

    public static void Run()
    {
        var map = LoadGuidMap();
        sb.AppendLine("guid map=" + map.Count);
        string outDir = root + "/Materials_URPLit";
        if (Directory.Exists(outDir)) AssetDatabase.DeleteAsset(outDir);
        Directory.CreateDirectory(outDir);
        int made = 0;
        foreach (var mp in Directory.GetFiles(root + "/Materials", "*.mat", SearchOption.AllDirectories))
        {
            // keep category subfolder name so per-part lookup can reuse pattern
            string cat = Path.GetFileName(Path.GetDirectoryName(mp));
            string sub = outDir + "/" + cat;
            Directory.CreateDirectory(sub);
            ConvertOne(mp.Replace('\\', '/'), map, sub);
            made++;
        }
        AssetDatabase.SaveAssets();
        sb.AppendLine("lit mats=" + made);

        // ---- assign to chars by normalized material-name match against original stem ----
        var lookup = new Dictionary<string, string>();
        foreach (var mp in Directory.GetFiles(outDir, "*.mat", SearchOption.AllDirectories))
        {
            string stem = Path.GetFileNameWithoutExtension(mp);
            lookup[Norm(stem)] = mp.Replace('\\', '/');
        }
        string urpDir = root + "/角色_URP";
        foreach (var f in Directory.GetFiles(urpDir, "*_可动.prefab"))
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(f.Replace('\\', '/'));
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(go);
            int assigned = 0;
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    string key = Norm(mats[i].name);
                    string hit;
                    if (lookup.TryGetValue(key, out hit)) { mats[i] = AssetDatabase.LoadAssetAtPath<Material>(hit); assigned++; }
                }
                r.sharedMaterials = mats;
            }
            string p2 = f.Replace('\\', '/');
            PrefabUtility.SaveAsPrefabAsset(inst, p2);
            sb.AppendLine("prefab " + Path.GetFileNameWithoutExtension(f) + " assigned=" + assigned);
            Object.DestroyImmediate(inst);
        }
        AssetDatabase.SaveAssets();
        File.WriteAllText("Assets/assets/_报告/_lit_report.txt", sb.ToString());
        Debug.Log("LIT DONE");
    }

    static string Norm(string s)
    {
        return new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    }
}
