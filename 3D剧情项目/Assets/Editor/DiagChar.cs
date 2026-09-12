using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Text;

public static class DiagChar
{
    public static void Run()
    {
        var sb = new StringBuilder();
        string dir = "Assets/assets/02_角色_Character/角色_URP";
        foreach (var f in Directory.GetFiles(dir, "*.prefab").OrderBy(x => x))
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(f.Replace('\\', '/'));
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(go);
            sb.AppendLine("==" + Path.GetFileNameWithoutExtension(f) + "==");
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    string tx = (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null) ? "T" : "-";
                    sb.AppendLine("  " + r.gameObject.name + " | mat=" + m.name + " | shader=" + (m.shader != null ? m.shader.name : "NULL") + " | base" + tx);
                }
            }
            Object.DestroyImmediate(inst);
        }
        File.WriteAllText("Assets/assets/_报告/_diagchar.txt", sb.ToString());
        Debug.Log("DIAG DONE");
    }
}
