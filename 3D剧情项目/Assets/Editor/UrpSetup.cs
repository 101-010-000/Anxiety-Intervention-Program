using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.IO;
using System.Linq;

public static class UrpSetup
{
    static void Log(string m) { File.AppendAllText("Assets/assets/_报告/_urp_setup.txt", m + "\n"); }

    public static void Run()
    {
        File.WriteAllText("Assets/assets/_报告/_urp_setup.txt", "start\n");
        string dir = "Assets/URP";
        Directory.CreateDirectory(dir);
        Log("create renderer data");
        UniversalRendererData rdata = ScriptableObject.CreateInstance<UniversalRendererData>();
        AssetDatabase.CreateAsset(rdata, dir + "/URP_Renderer.asset");
        Log("create pipeline asset");
        UniversalRenderPipelineAsset pipe = ScriptableObject.CreateInstance<UniversalRenderPipelineAsset>();
        AssetDatabase.CreateAsset(pipe, dir + "/URP_Pipeline.asset");
        Log("wire renderer list");
        var so = new SerializedObject(pipe);
        var list = so.FindProperty("m_RendererDataList");
        list.arraySize = 1;
        list.GetArrayElementAtIndex(0).objectReferenceValue = rdata;
        so.FindProperty("m_DefaultRendererIndex").intValue = 0;
        so.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
        Log("assign to graphics (quality 留空则自动用默认 URP)");
        GraphicsSettings.defaultRenderPipeline = pipe;
        AssetDatabase.SaveAssets();
        Log("assigned default: " + (GraphicsSettings.defaultRenderPipeline != null));
        AssetDatabase.Refresh();

        // probe: compiled shader sub-assets of each shadergraph
        foreach (var sg in Directory.GetFiles("Assets/assets/11_着色器_Shaders", "*.shadergraph", SearchOption.AllDirectories))
        {
            string p = sg.Replace('\\', '/');
            var shaders = AssetDatabase.LoadAllAssetsAtPath(p).OfType<Shader>().ToArray();
            Log("shadergraph " + Path.GetFileName(p) + " -> shaders=" + string.Join(",", shaders.Select(s => s.name + "/" + (s.isSupported ? "ok" : "FAIL"))));
        }
        Log("DONE");
        Debug.Log("URP DONE");
    }
}
