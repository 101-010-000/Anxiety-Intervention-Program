using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;

public static class RemapMatsBuildChars
{
    static string root = "Assets/assets/02_角色_Character";
    static StringBuilder sb = new StringBuilder();
    static void Log(string m) { sb.AppendLine(m); }

    static string ShaderPath = "Assets/assets/11_着色器_Shaders/主着色器_ShaderGraph/ShaderGraph_CharacterToon+FakeOutline.shadergraph";

    public static void Run()
    {
        // 1) remap every material to CharacterToon shader (keeps its own textures/params)
        var shader = AssetDatabase.LoadAllAssetsAtPath(ShaderPath).OfType<Shader>().FirstOrDefault();
        Log("shader=" + (shader != null ? shader.name : "NULL"));
        int n = 0;
        foreach (var mp in Directory.GetFiles(root + "/Materials", "*.mat", SearchOption.AllDirectories))
        {
            string path = mp.Replace('\\', '/');
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null || shader == null) continue;
            m.shader = shader;
            EditorUtility.SetDirty(m);
            n++;
        }
        AssetDatabase.SaveAssets();
        Log("mats remapped=" + n);

        // 2) build texture-ready prefabs for 9 characters
        var lookup = new Dictionary<string, string>();
        foreach (var mp in Directory.GetFiles(root + "/Materials", "*.mat", SearchOption.AllDirectories))
        {
            string fn = Path.GetFileName(mp);
            string norm = Normalize(fn.Replace(".mat", ""));
            if (!lookup.ContainsKey(norm)) lookup[norm] = mp.Replace('\\', '/');
        }
        string combo = "Assets/assets/02_角色_Character/组合角色";
        foreach (var d in Directory.GetDirectories(combo))
        {
            string name = Path.GetFileName(d);
            if (name.StartsWith("_")) continue;
            string fbxPath = d.Replace('\\', '/') + "/" + name + ".fbx";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (model == null) { Log("no model " + fbxPath); continue; }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            int assigned = 0;
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    string norm = Normalize(mats[i].name);
                    string hit;
                    if (lookup.TryGetValue(norm, out hit))
                    {
                        mats[i] = AssetDatabase.LoadAssetAtPath<Material>(hit);
                        assigned++;
                    }
                }
                r.sharedMaterials = mats;
            }
            string outDir = root + "/角色_URP";
            Directory.CreateDirectory(outDir);
            string prefabPath = outDir + "/" + name + "_可动.prefab";
            PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
            Log($"角色 {name}: 已挂材质 {assigned} 处 -> {prefabPath}");
            Object.DestroyImmediate(inst);
        }
        AssetDatabase.SaveAssets();
        File.WriteAllText("Assets/assets/_报告/_材质接入报告.txt", sb.ToString());
        Debug.Log("DONE");
    }

    static string Normalize(string s)
    {
        return new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    }
}
