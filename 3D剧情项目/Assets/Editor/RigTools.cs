using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;

public static class RigTools
{
    static StringBuilder sb = new StringBuilder();
    static List<string> required = new List<string>{
        "Hips","Spine","Chest","UpperChest","Neck","Head",
        "LeftShoulder","LeftUpperArm","LeftLowerArm","LeftHand",
        "RightShoulder","RightUpperArm","RightLowerArm","RightHand",
        "LeftUpperLeg","LeftLowerLeg","LeftFoot","LeftToes",
        "RightUpperLeg","RightLowerLeg","RightFoot","RightToes",
        "LeftThumbDistal","LeftIndexDistal","LeftMiddleDistal","LeftRingDistal","LeftLittleDistal",
        "RightThumbDistal","RightIndexDistal","RightMiddleDistal","RightRingDistal","RightLittleDistal"
    };

    public static void Run()
    {
        string root = "Assets/assets";
        // ---- 1) characters -> Humanoid ----
        string combo = root + "/组合角色";
        var chars = new List<string>();
        foreach (var d in Directory.GetDirectories(combo))
        {
            string name = Path.GetFileName(d);
            if (name.StartsWith("_")) continue;
            string p = d.Replace('\\', '/') + "/" + name + ".fbx";
            if (File.Exists(p)) chars.Add(p);
        }
        sb.AppendLine("== 角色 Humanoid 配置 ==");
        foreach (var p in chars)
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath(p);
            imp.animationType = ModelImporterAnimationType.Human;
            imp.importAnimation = false;
            imp.SaveAndReimport();
            var hd = imp.humanDescription;
            var human = hd.human;
            var mappedNames = human.Where(h => !string.IsNullOrEmpty(h.humanName)).Select(h => h.humanName).ToList();
            var missing = required.Where(r => !mappedNames.Contains(r)).ToList();
            sb.AppendLine($"{Path.GetFileName(Path.GetDirectoryName(p))}: mapped={mappedNames.Count} missing={missing.Count} {(missing.Count>0? "缺: "+string.Join(",",missing):"OK")}");
        }

        // ---- 2) animations -> Humanoid ----
        string animDir = root + "/动画";
        sb.AppendLine("\n== 动画 Humanoid 配置 ==");
        foreach (var f in Directory.GetFiles(animDir, "*.fbx").OrderBy(x => x))
        {
            string p = f.Replace('\\', '/');
            var imp = (ModelImporter)AssetImporter.GetAtPath(p);
            imp.animationType = ModelImporterAnimationType.Human;
            imp.importAnimation = true;
            imp.SaveAndReimport();
            var clips = imp.clipAnimations;
            string clipNames = clips != null && clips.Length > 0 ? string.Join("|", clips.Select(c => c.name)) : "(importAnimation=true,待取)";
            sb.AppendLine($"{Path.GetFileName(p)}: rig=Human clips={clipNames}");
        }

        // ---- 3) material status of characters ----
        sb.AppendLine("\n== 角色材质检查 ==");
        foreach (var p in chars)
        {
            var mats = AssetDatabase.LoadAllAssetsAtPath(p).OfType<Material>().ToList();
            string broken = mats.Count(m => m == null || m.shader == null) + "";
            string sample = mats.Count > 0 ? mats[0].name + "/" + mats[0].shader.name : "-";
            sb.AppendLine($"{Path.GetFileName(Path.GetDirectoryName(p))}: 内嵌材质数={mats.Count} 首材质={sample} 异常数={broken}");
        }
        File.WriteAllText(root + "/_rig_report.txt", sb.ToString());
        AssetDatabase.SaveAssets();
        Debug.Log("RIG DONE");
    }
}
