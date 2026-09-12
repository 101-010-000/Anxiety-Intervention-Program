using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Text;

public static class ProbeNames
{
    static string[] cands = {
        "_BaseMap","BaseMap","_MainTex","MainTex","_RGB_Map","RGBMap","_ColorRamp","ColorRamp",
        "_Normal","Normal","_BumpMap","_Metallic","Metallic","_OcclusionMap","OcclusionMap",
        "_BaseColor","BaseColor","_Color","Color","_Emission","_DetailMap","_OutlineColor","OutlineColor",
        "_RimColor","RimColor","_ToonRamp","_FaceDir","_ShadeMap","ShadeMap","_MaskMap","_Tint","Tint",
        "_Albedo","Albedo","_Texture2D","_SampleTexture2D"
    };

    public static void Run()
    {
        Shader.WarmupAllShaders();
        var sb = new StringBuilder();
        string dir = "Assets/assets/11_着色器_Shaders/主着色器_ShaderGraph";
        foreach (var f in Directory.GetFiles(dir, "*.shadergraph"))
        {
            var sh = AssetDatabase.LoadAllAssetsAtPath(f.Replace('\\', '/')).OfType<Shader>().FirstOrDefault();
            if (sh == null) continue;
            var m = new Material(sh);
            var yes = cands.Where(c => m.HasProperty(c)).ToList();
            sb.AppendLine(sh.name + " => " + string.Join(",", yes));
            Object.DestroyImmediate(m);
        }
        File.WriteAllText("Assets/assets/_报告/_probenames.txt", sb.ToString());
        Debug.Log("PROBENAMES DONE");
    }
}
