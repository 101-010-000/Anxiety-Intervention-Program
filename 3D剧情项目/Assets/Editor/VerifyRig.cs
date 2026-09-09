using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Text;

public static class VerifyRig
{
    public static void Run()
    {
        var sb = new StringBuilder();
        string root = "Assets/assets";
        sb.AppendLine("== Avatar / Clip 验证 ==");
        foreach (var p in new[]{ root+"/组合角色" }.SelectMany(Directory.GetDirectories).Select(d => (d.Replace('\\','/')+"/"+Path.GetFileName(d)+".fbx"))
                     .Concat(Directory.GetFiles(root+"/动画","*.fbx").Select(f => f.Replace('\\','/'))).OrderBy(x=>x))
        {
            var av = AssetDatabase.LoadAllAssetsAtPath(p).OfType<Avatar>().FirstOrDefault();
            var clips = AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>().ToArray();
            string clipInfo = clips.Length > 0 ? string.Join("|", clips.Select(c => c.name + (c.isLooping ? "[循环]" : ""))) : "-";
            sb.AppendLine($"{Path.GetFileName(Path.GetDirectoryName(p))}/{Path.GetFileName(p)}: avatar={(av!=null? (av.isHuman?"human":"notHuman") : "none")} clips({clips.Length})={clipInfo}");
        }
        File.WriteAllText(root + "/_rig_report.txt", sb.ToString());
        Debug.Log("VERIFY DONE");
    }
}
