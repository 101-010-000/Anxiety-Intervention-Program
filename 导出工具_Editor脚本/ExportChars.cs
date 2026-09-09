using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;
using System.Linq;
using System.Reflection;

public static class ExportChars
{
    static MethodInfo exportM2 = null;

    public static void Run()
    {
        var sb = new System.Text.StringBuilder();
        System.Type exporterType = null;
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            exporterType = asm.GetTypes().FirstOrDefault(t => t.Name == "ModelExporter");
            if (exporterType != null) break;
        }
        if (exporterType == null) { Debug.LogError("no ModelExporter"); return; }
        exportM2 = exporterType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "ExportObjects" && m.GetParameters().Length == 2
                && m.GetParameters()[1].ParameterType == typeof(Object[]));
        if (exportM2 == null) { Debug.LogError("ExportObjects not found"); return; }

        string only = null;
        if (File.Exists("Assets/_only.txt"))
            only = File.ReadAllText("Assets/_only.txt").Trim();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        string outRoot = "Assets/角色FBX";
        foreach (var d in Directory.GetDirectories(outRoot))
        {
            string name = Path.GetFileName(d);
            if (name.StartsWith("_")) continue;
            if (only != null && name != only) continue;
            string prefabPath = d.Replace('\\', '/') + "/" + name + ".prefab";
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (go == null) continue;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(go);
            string fbxPath = d.Replace('\\', '/') + "/" + name + ".fbx";
            if (File.Exists(fbxPath)) File.Delete(fbxPath);
            string result;
            try { result = (string)exportM2.Invoke(null, new object[] { fbxPath, new Object[] { inst } }); }
            catch (System.Exception ex) { result = "EX: " + (ex.InnerException != null ? ex.InnerException.Message : ex.Message); }
            sb.AppendLine($"{name}: result={(result ?? "null")} size={(File.Exists(fbxPath) ? new FileInfo(fbxPath).Length : -1)}");
            Object.DestroyImmediate(inst);
        }
        File.WriteAllText("Assets/_export.txt", sb.ToString());
        Debug.Log("EXPORT DONE");
    }
}
