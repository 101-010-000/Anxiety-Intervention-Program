using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public static class ProbeShaderProps
{
    public static void Run()
    {
        var sb = new StringBuilder();
        string dir = "Assets/assets/11_着色器_Shaders/主着色器_ShaderGraph";
        foreach (var f in Directory.GetFiles(dir, "*.shadergraph"))
        {
            var sh = AssetDatabase.LoadAllAssetsAtPath(f.Replace('\\', '/')).OfType<Shader>().FirstOrDefault();
            if (sh == null) continue;
            int n = ShaderUtil.GetPropertyCount(sh);
            var names = new System.Collections.Generic.List<string>();
            for (int i = 0; i < n; i++) names.Add(ShaderUtil.GetPropertyName(sh, i));
            sb.AppendLine("==" + sh.name + "== (" + n + ") " + string.Join(", ", names.Take(70)));
        }
        string mp = "Assets/assets/Materials/1_身体_Body/mat_base_F_body.mat";
        string txt = File.ReadAllText(mp);
        sb.AppendLine("\n== mat_base_F_body stored props ==");
        foreach (Match m in Regex.Matches(txt, @"- (_[A-Za-z0-9_]+):"))
            sb.AppendLine("  " + m.Groups[1].Value);
        File.WriteAllText("Assets/_props.txt", sb.ToString());
        Debug.Log("PROPS DONE");
    }
}
