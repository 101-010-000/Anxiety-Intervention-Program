using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.IO;

public static class BuildChars
{
    static string meshPath = "Assets/Meshes";
    static string matRoot = "Assets/Materials";
    static string outRoot = "Assets/角色FBX";
    static Dictionary<string, Material> fixedMats = new Dictionary<string, Material>();
    static Dictionary<string, Transform> _boneCache = new Dictionary<string, Transform>();
    static StringBuilder log = new StringBuilder();

    static readonly Dictionary<string,string> MatBySlot = new Dictionary<string,string>()
    {
        {"mat_body_F","mat_base_F_body"},{"mat_face_F","mat_base_F_face"},
        {"mat_body_M","mat_base_M_body"},{"mat_face_M","mat_base_M_face"},
        {"mat_eyelashes","mat_eyelashes"},{"mat_eye","mat_base_eye.001"},
        {"mat_eye_highlight","mat_eye_highlight"},{"mat_mouth","mat_base_mouth"},
        {"mat_eyebrows","mat_eyebrows"},
        {"mat_top.001_shirt","mat_top.001_tshirt"},{"mat_top.002_jacket","mat_top.002_jacket"},
        {"mat_top.003","mat_top.003_sweater"},{"mat_top.004","mat_top.004_shirt"},
        {"mat_top.005","mat_top.005_shirt_vest"},{"mat_top.006","mat_top.006_openshirt"},
        {"mat_top.009_hoodie","mat_top.009_hoodie"},{"mat_top.010_shirt+tie","mat_top.010_shirtntie"},
        {"mat_top.011_schoolblazer","mat_top.011_blazer"},{"mat_top.008_longsleevedshirt","mat_top.008_longsleeved-A"},
        {"mat_bot.001_pants","mat_bot.001_pants"},{"mat_bot.002_shorts","mat_bot.002_shorts"},
        {"mat_bot.003_shortskirt","mat_bot.003_skirt"},{"mat_bot.004_skinny","mat_bot.004_skinnyjeans"},
        {"mat_bot.005_shorts","mat_bot.005_shorts"},
        {"mat_shoes.001_boots","mat_shoes.001_boots"},{"mat_boots.001","mat_shoes.001_boots"},
        {"mat_shoes.002_sneakers","mat_shoes.002_sneakers"},
        {"mat_shoes.003_flipflop","mat_shoes.003_flipflops"},{"mat_shoes.004_sandals","mat_shoes.004_sandals"},
        {"mat_shoes.006_lowsneaker","mat_shoes.006_lowsneakers"},{"mat_shoes.007_chelsea","mat_shoes.007_chelseas"},
        {"hair_long.001","mat_hair_long.001"},{"hair_medium.001","mat_hair_medium.001"},{"hair_medium.02","mat_hair_medium.002"},
        {"hair_short.001","mat_hair_short.001"},{"hair_short.02","mat_hair_short.002"},{"hair_short.03","mat_hair_short.003"},{"hair_short.04","mat_hair_short.004"},
        {"hair_tied.01","mat_hair_tied.001"},{"hair_tied.01_bun","mat_hair_tied - bun"},
        {"hair_tied.01_ponytail","mat_hair_tied.001 - ponytail"},{"hair_tied.02","mat_hair_tied.002"},
        {"hair_tied.01_down","mat_hair_tied.001"},{"hair_tied.02_pigtails","mat_hair_tied.002 - twintails"},
        {"hair_tied.02_updouble","mat_hair_tied.002"},{"hair_tied.02_upbuns","mat_hair_tied - bun"},
        {"hair_bangs.001","mat_hair_bangs.001"},{"hair_bangs.02","mat_hair_bangs.002"},{"hair_bangs.03","mat_hair_bangs.003"},
        {"hair_afro.01","mat_hair_afro.001"},{"hair_afro.02","mat_hair_afro.002"},{"hair_afro.03","mat_hair_afro.003"},
        {"hair_curly.001","mat_hair_curly.001"},{"hair_curly.002","mat_hair_curly.002"},
        {"buzzcut","mat_hair_buzzcut"},{"hair_buzzcut","mat_hair_buzzcut"},
        {"mat_glasses.001_square","mat_glasses.001_square"},{"mat_glasses.002_round","mat_glasses.001_round"},
        {"mat_glasses_01_square","mat_glasses.001_square"},{"mat_glasses_02_round","mat_glasses.001_round"},
        {"glasses","glasses"},
        {"mat_acc_001_scarf","mat_acc.001_scarf"},
        {"mat_hat.001_baseballcap","mat_hat.001_baseballcap"},{"mat_hat.002_beanie","mat_hat.002_beanie"}
    };

    class CharCfg
    {
        public string Name; public bool Male; public string Head; public string Hair; public string Hair2;
        public string Bangs; public string Top; public string Bot; public string Shoes; public string Glasses; public string Scarf;
        public string[] RemoveSkin;
    }

    static readonly CharCfg[] Characters =
    {
        new CharCfg{ Name="徐夏", Male=false, Head="F_head.001", Hair="C_hair_medium.001", Bangs="C_hair_bangs.001",
            Top="F_top.003_sweater", Bot="F_bot.004_skinnyjeans", Shoes="F_shoes.006_lowsneakers",
            RemoveSkin=new[]{"torso","hips","shoulders","arms_upper","arms_lower","legs_upper"} },
        new CharCfg{ Name="林溪", Male=false, Head="F_head.002", Hair="C_hair_tied.002", Hair2="C_hair_tied.002_twintails", Bangs="C_hair_bangs.002",
            Top="F_top.009_hoodie", Bot="F_bot.003_shortskirt", Shoes="F_shoes.002_sneakers",
            RemoveSkin=new[]{"torso","hips","shoulders","arms_upper","arms_lower","feet"} },
        // 陆宣雨: 仅追加删除 内衣段 torso.001（其余不动）
        new CharCfg{ Name="陆宣雨", Male=false, Head="F_head.003", Hair="C_hair_short.002",
            Top="F_top.002_jacket", Bot="F_bot.001_pants", Shoes="F_shoes.007_chelsea",
            RemoveSkin=new[]{"hips","shoulders","arms_upper","arms_lower","legs_upper","torso.001"} },
        new CharCfg{ Name="王含", Male=false, Head="F_head.001", Hair="C_hair_long.001", Bangs="C_hair_bangs.003",
            Top="F_top.005_shirtvest", Bot="F_bot.002_shorts", Shoes="F_shoes.001_boots", Scarf="C_acc.001_scarf",
            RemoveSkin=new[]{"torso","hips","shoulders","arms_upper","feet"} },
        new CharCfg{ Name="李老师", Male=false, Head="F_head.004", Hair="C_hair_tied.001", Hair2="C_hair_tied.001_bun", Bangs="C_hair_bangs.002",
            Top="F_top.011_blazer", Bot="F_bot.001_pants", Shoes="F_shoes.007_chelsea", Glasses="C_glasses.02_round",
            RemoveSkin=new[]{"hips","shoulders","arms_upper","arms_lower","legs_upper"} },
        new CharCfg{ Name="舍友A", Male=false, Head="F_head.002", Hair="C_hair_tied.001", Hair2="C_hair_tied.001_ponytail", Bangs="C_hair_bangs.001",
            Top="F_top.001_shirt", Bot="F_bot.005_bigshorts", Shoes="F_shoes.003_flipflop",
            RemoveSkin=new[]{"torso","hips","shoulders"} },
        new CharCfg{ Name="舍友B", Male=false, Head="F_head.003", Hair="C_hair_curly.002", Bangs="C_hair_bangs.003",
            Top="F_top.008_longsleeved", Bot="F_bot.002_shorts", Shoes="F_shoes.004_sandals",
            RemoveSkin=new[]{"torso","hips","shoulders","arms_upper","arms_lower"} },
        new CharCfg{ Name="组长", Male=true, Head="M_head.001", Hair="C_hair_short.001",
            Top="M_top.002_jacket", Bot="M_bot.001_pants", Shoes="F_shoes.002_sneakers",
            RemoveSkin=new[]{"hips","shoulders","arms_upper","arms_lower","legs_upper"} },
        new CharCfg{ Name="张知远", Male=true, Head="M_head.002", Hair="C_hair_short.002",
            Top="M_top.001_shirt", Bot="M_bot.005_bigshorts", Shoes="F_shoes.002_sneakers",
            RemoveSkin=new[]{"torso","hips","shoulders","feet"} }
    };

    static string ResolveMesh(string file)
    {
        foreach (var f in Directory.GetFiles(meshPath, file, SearchOption.AllDirectories))
            return f.Replace('\\', '/');
        return meshPath + "/" + file;
    }

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        if (Directory.Exists(outRoot)) AssetDatabase.DeleteAsset(outRoot);
        Directory.CreateDirectory(outRoot);
        foreach (var c in Characters) BuildOne(c);
        File.WriteAllText(outRoot + "/_皮肤删减说明.txt", log.ToString());
        Debug.Log("BUILD DONE");
    }

    static Material ResolveSlotMat(string slot, GameObject asset, int idx)
    {
        string fn;
        if (MatBySlot.TryGetValue(slot, out fn) && fixedMats.ContainsKey(fn + ".mat")) return fixedMats[fn + ".mat"];
        var ms = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(asset)).OfType<Material>().ToArray();
        return ms.FirstOrDefault(x => x.name == slot) ?? (idx >= 0 && idx < ms.Length ? ms[idx] : null);
    }

    static Transform FindBone(Transform root, string name)
    {
        if (_boneCache.ContainsKey(name)) return _boneCache[name];
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) { _boneCache[name] = t; return t; }
        return null;
    }
    static Transform FindBoneNC(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    static void BuildOne(CharCfg c)
    {
        log.AppendLine($"\n### {c.Name}");
        _boneCache.Clear();
        var bodyAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ResolveMesh((c.Male ? "M_body" : "F_body") + ".fbx"));
        var root = new GameObject(c.Name);
        var body = (GameObject)PrefabUtility.InstantiatePrefab(bodyAsset);
        body.name = "Base_Body";
        body.transform.SetParent(root.transform, false);

        if (fixedMats.Count == 0)
        {
            foreach (var mp in Directory.GetFiles(matRoot, "*.mat", SearchOption.AllDirectories))
            {
                string fn = Path.GetFileName(mp);
                var m = AssetDatabase.LoadAssetAtPath<Material>(mp.Replace('\\', '/'));
                if (m == null) continue;
                m.shader = Shader.Find("Standard");
                m.SetTexture("_MainTex", null); m.SetTexture("_BumpMap", null);
                m.color = new Color(0.82f, 0.82f, 0.84f, 1f);
                fixedMats[fn] = m;
            }
        }

        if (c.RemoveSkin != null)
        {
            var segs = body.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                          .Where(s => s.gameObject.name.Contains("_body_")).ToList();
            foreach (var s in segs)
            {
                string nm = s.gameObject.name.ToLower();
                if (c.RemoveSkin.Any(k => nm.Contains(k)))
                {
                    log.AppendLine($"  remove skin: {s.gameObject.name}");
                    Object.DestroyImmediate(s.gameObject);
                }
            }
        }

        foreach (var smr in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mats = smr.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var slot = mats[i] != null ? mats[i].name : "";
                var m = ResolveSlotMat(slot, bodyAsset, i);
                if (m != null) mats[i] = m;
            }
            smr.sharedMaterials = mats;
        }

        AttachCC(body.transform, c.Head + ".fbx", "Head");
        AttachCC(body.transform, c.Top + ".fbx", "Top");
        AttachCC(body.transform, c.Bot + ".fbx", "Bot");
        if (!string.IsNullOrEmpty(c.Shoes))  AttachCC(body.transform, c.Shoes + ".fbx", "Shoes");
        if (!string.IsNullOrEmpty(c.Scarf))  AttachCC(body.transform, c.Scarf + ".fbx", "Scarf");
        if (!string.IsNullOrEmpty(c.Hair))   AttachHair(body.transform, c.Hair + ".fbx", "Hair");
        if (!string.IsNullOrEmpty(c.Hair2))  AttachHair(body.transform, c.Hair2 + ".fbx", "Hair_Deco");
        if (!string.IsNullOrEmpty(c.Bangs))  AttachHair(body.transform, c.Bangs + ".fbx", "Hair_Bangs");
        if (!string.IsNullOrEmpty(c.Glasses))AttachHair(body.transform, c.Glasses + ".fbx", "Glasses");

        string dir = outRoot + "/" + c.Name;
        Directory.CreateDirectory(dir);
        PrefabUtility.SaveAsPrefabAsset(root, dir + "/" + c.Name + ".prefab");
        Object.DestroyImmediate(root);
    }

    static void AttachCC(Transform baseRoot, string fbxName, string label)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ResolveMesh(fbxName));
        if (asset == null) { log.AppendLine("  [ERR] no " + fbxName); return; }
        foreach (var src in asset.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var go = new GameObject(label + "_" + src.name);
            go.transform.SetParent(baseRoot, false);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = src.sharedMesh;
            var mats = src.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var slot = mats[i] != null ? mats[i].name : "";
                var m = ResolveSlotMat(slot, asset, i);
                if (m != null) mats[i] = m;
            }
            smr.sharedMaterials = mats;
            var bones = new Transform[src.bones.Length];
            for (int i = 0; i < bones.Length; i++)
                bones[i] = src.bones[i] != null ? FindBone(baseRoot, src.bones[i].name) : null;
            smr.bones = bones;
            smr.rootBone = src.rootBone != null ? FindBone(baseRoot, src.rootBone.name) : null;
            smr.updateWhenOffscreen = true;
        }
    }

    static void AttachHair(Transform baseRoot, string fbxName, string label)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ResolveMesh(fbxName));
        if (asset == null) { log.AppendLine("  [ERR] no " + fbxName); return; }
        var head = FindBoneNC(baseRoot, "CC_Base_Head");
        if (head == null) { log.AppendLine("  [ERR] head"); return; }
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        inst.name = label;
        inst.transform.SetParent(head, false);
        foreach (var mf in inst.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mats = mf.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var slot = mats[i] != null ? mats[i].name : "";
                var m = ResolveSlotMat(slot, asset, i);
                if (m != null) mats[i] = m;
            }
            mf.sharedMaterials = mats;
        }
        foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mats = smr.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var slot = mats[i] != null ? mats[i].name : "";
                var m = ResolveSlotMat(slot, asset, i);
                if (m != null) mats[i] = m;
            }
            smr.sharedMaterials = mats;
        }
    }
}
