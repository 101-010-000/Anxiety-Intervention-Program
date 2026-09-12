// 角色模型重建工具（干预项目）—— 「每个角色都不一样」
// 用法：Unity 菜单 Tools/干预项目/重建角色模型   （或命令行 -executeMethod CharRebuild.Run）
//
// 做三件事：
//   1) 用「基础身体 + 配置表里的配件」重新组装 9 个角色 prefab（Assets/assets/02_角色_Character/角色_URP/*_可动.prefab）
//   2) 按服装的实际覆盖面（离线量过网格包围盒）自动删掉被挡住的皮肤段，避免空洞/穿模
//   3) 一人一套配色：把用到的材质复制成角色专属实例（Assets/assets/02_角色_Character/角色_URP/材质/<角色>/）并染色
//
// 上衣/下装/鞋/发型的可选清单见 Assets/assets/02_角色_Character/Meshes/ 对应目录（12 种上衣、6 种下装、7 种鞋、22 种发型、4 种连体装）。

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public static class CharRebuild
{
    const string MESH = "Assets/assets/02_角色_Character/Meshes";
    const string MAT  = "Assets/assets/02_角色_Character/Materials";
    const string OUT  = "Assets/assets/02_角色_Character/角色_URP";
    const string SRC  = "Assets/assets/02_角色_Character/组合角色";

    static readonly List<string> log = new List<string>();
    static readonly Dictionary<string, Material> matCache = new Dictionary<string, Material>();

    // ------------------------------------------------------------------ 角色配置
    // 颜色写法："主色|亮部色"（十六进制，主色放在 A1，因为遮罩结果以 A1 为主）
    class Cfg
    {
        public string Name, Body, Head;
        public string Top, Bot, Outfit, Shoes;      // Outfit 有值时忽略 Top/Bot（连体装）
        public string Hair, Bangs, Deco, Hat, Glasses, Scarf;
        public string Remove;                        // 要去掉的身体段（逗号分隔，来自场景里手动删减的模板）
        public string Skin, HairC, Eye, TopC, TopB, BotC, ShoeC, AccC;
    }

    static readonly Cfg[] Chars =
    {
        // 徐夏：同系列 3 号（毛衣 + 短裙），米白/驼色系
        new Cfg{ Name="徐夏", Body="F_body", Head="F_head.001",
            Top="F_top.003_sweater", Bot="F_bot.003_shortskirt", Shoes="F_shoes.006_lowsneakers", Remove="torso.001",
            Hair="C_hair_medium.001", Bangs="C_hair_bangs.001",
            Skin="F0C9AE|FADEC9", HairC="2A1E18|4A3626",
            TopC="E8DCC6|F7F0E2", TopB="C9A882|E0C8A8", BotC="D9C6A8|F0E4CC", ShoeC="ECE7DE|B5AEA3" },

        // 林溪：同系列 2 号（夹克 + 短裤），浅蓝/白，双马尾（活泼）
        new Cfg{ Name="林溪", Body="F_body", Head="F_head.002",
            Top="F_top.002_jacket", Bot="F_bot.002_shorts", Shoes="F_shoes.002_sneakers", Remove="hips,torso.001",
            Hair="C_hair_tied.002", Bangs="C_hair_bangs.002", Deco="C_hair_tied.002_twintails",
            Skin="F2CBAF|FBE1CD", HairC="16100F|2E2320",
            TopC="8FB4DC|DCEBF7", TopB="FFFFFF|F5F5F5", BotC="5A7BA8|9CBCDC", ShoeC="E04A4A|FFFFFF" },

        // 陆宣雨：连体装 3 号（藕荷色连衣裙）+ 中长发 + 刘海 + 方框眼镜（明显女性化）
        new Cfg{ Name="陆宣雨", Body="F_body", Head="F_head.003",
            Outfit="F_outfit.003", Shoes="F_shoes.007_chelsea", Remove="hips,torso.001",
            Hair="C_hair_medium.002", Bangs="C_hair_bangs.002", Glasses="C_glasses.01_square",
            Skin="E7BFA3|F4D5C0", HairC="40261A|6A4526",
            TopC="C08AA4|E4BCCE", TopB="F2ECE2|FFFFFF", BotC="C08AA4|E4BCCE", ShoeC="4A3122|6E4A30" },

        // 王含：同系列 4 号（衬衫 + 紧身牛仔），酒红衬衫 + 深蓝牛仔 + 围巾（温柔）
        new Cfg{ Name="王含", Body="F_body", Head="F_head.001",
            Top="F_top.004_shirt", Bot="F_bot.004_skinnyjeans", Shoes="F_shoes.001_boots", Remove="hips,legs_upper,legs_lower,feet",
            Hair="C_hair_long.001", Bangs="C_hair_bangs.003", Scarf="C_acc.001_scarf",
            Skin="EFC7AC|F9DECC", HairC="191210|322420",
            TopC="8C3040|C25A66", TopB="F2E6D2|FFF6E6", BotC="2E3A5C|5B7099", ShoeC="6B4A32|A07A52",
            AccC="7A9CC6|B8CCE4" },

        // 李老师：西装 + 西裤（灰套装），低发髻 + 圆框眼镜（成熟）
        new Cfg{ Name="李老师", Body="F_body", Head="F_head.004",
            Top="F_top.011_blazer", Bot="F_bot.001_pants", Shoes="F_shoes.005_highboots", Remove="hips,legs_upper,arms_upper,arms_lower,feet",
            Hair="C_hair_tied.001", Bangs="C_hair_bangs.002", Deco="C_hair_tied.001_bun", Glasses="C_glasses.02_round",
            Skin="E4BCA2|F2D2BC", HairC="241A16|402E24",
            TopC="3E3E48|70707C", TopB="F2F2F4|FFFFFF", BotC="2A2A32|4C4C56", ShoeC="1E1E24|3A3A42" },

        // 组长（男）：白衬衫+领带 + 深灰西裤（制服套装），短寸
        new Cfg{ Name="组长", Body="M_body", Head="M_head.001",
            Top="M_top.010_shirtandtie", Bot="M_bot.001_pants", Shoes="F_shoes.006_lowsneakers", Remove="hips,shoulders",
            Hair="C_hair_short.001",
            Skin="CD9E7E|E4BC9A", HairC="120E0C|282018",
            TopC="F0F2F5|FFFFFF", TopB="24345C|465A8C", BotC="23252E|40444F", ShoeC="26262C|4A4A52" },

        // 张知远（男）：套件样例的搭配（夹克 + 紧身牛仔），短碎发
        new Cfg{ Name="张知远", Body="M_body", Head="M_head.002",
            Top="M_top.002_jacket", Bot="M_bot.004_skinnyjeans", Shoes="F_shoes.002_sneakers", Remove="hips",
            Hair="C_hair_short.004",
            Skin="D8A886|ECC6A6", HairC="2E1E12|523A22",
            TopC="33445E|5C7092", TopB="E8A03C|F5C878", BotC="2E3A5C|6B82AD", ShoeC="3A3A42|F0F0F0" },

        // 舍友A：同系列 5 号（衬衫马甲 + 大短裤），白衬衫 + 米色马甲，高马尾（时髦）
        new Cfg{ Name="舍友A", Body="F_body", Head="F_head.002",
            Top="F_top.005_shirtvest", Bot="F_bot.005_bigshorts", Shoes="F_shoes.005_highboots", Remove="hips,feet",
            Hair="C_hair_tied.001", Bangs="C_hair_bangs.001", Deco="C_hair_tied.001_ponytail",
            Skin="EAC0A4|F7DAC6", HairC="8A5A28|C4904E",
            TopC="F5F5F0|FFFFFF", TopB="C8B48C|E0D0AE", BotC="9C9484|C8C0AC", ShoeC="4A3020|6E4A32" },

        // 舍友B：同系列 6 号（开衫T + 阔腿裤），军绿开衫 + 白T + 卡其裤，卷发（随性）
        new Cfg{ Name="舍友B", Body="F_body", Head="F_head.003",
            Top="F_top.006_openshirt+T", Bot="F_bot.006_loosepants", Shoes="F_shoes.004_sandals", Remove="hips,torso.001",
            Hair="C_hair_curly.002", Bangs="C_hair_bangs.003",
            Skin="C48E6E|DCAC8C", HairC="2A1A12|4E3220",
            TopC="6B8C58|9CBB88", TopB="F5F5F2|FFFFFF", BotC="A89C84|D0C6B0", ShoeC="6B4A34|9A7350" },
    };

    // ------------------------------------------------- 配件 → 材质（工程里的 .mat 文件名）
    static readonly Dictionary<string,string> GarmentMat = new Dictionary<string,string>
    {
        // 上衣
        {"F_top.001_shirt","mat_top.001_tshirt"},{"M_top.001_shirt","mat_top.001_tshirt"},
        {"F_top.002_jacket","mat_top.002_jacket"},{"M_top.002_jacket","mat_top.002_jacket"},
        {"F_top.003_sweater","mat_top.003_sweater"},{"M_top.003_sweater","mat_top.003_sweater"},
        {"F_top.004_shirt","mat_top.004_shirt"},{"M_top.004_shirt","mat_top.004_shirt"},
        {"F_top.005_shirtvest","mat_top.005_shirt_vest"},{"M_top.005_shirtvest","mat_top.005_shirt_vest"},
        {"F_top.006_openshirt+T","mat_top.006_openshirt"},{"M_top.006_openshirt+T","mat_top.006_openshirt"},
        {"F_top.007_smalljacket_A","mat_top.007-A"},{"M_top.007_smalljacket_A","mat_top.007-A"},
        {"F_top.007_smalljacket_B","mat_top.007-B"},{"M_top.007_smalljacket_B","mat_top.007-B"},
        {"F_top.008_longsleeved","mat_top.008_longsleeved-A"},{"M_top.008_longsleeved","mat_top.008_longsleeved-A"},
        {"F_top.009_hoodie","mat_top.009_hoodie"},{"M_top.009_hoodie","mat_top.009_hoodie"},
        {"F_top.010_shirtandtie","mat_top.010_shirtntie"},{"M_top.010_shirtandtie","mat_top.010_shirtntie"},
        {"F_top.011_blazer","mat_top.011_blazer"},{"M_top.011_blazer","mat_top.011_blazer"},
        // 下装
        {"F_bot.001_pants","mat_bot.001_pants"},{"M_bot.001_pants","mat_bot.001_pants"},
        {"F_bot.002_shorts","mat_bot.002_shorts"},{"M_bot.002_shorts","mat_bot.002_shorts"},
        {"F_bot.003_shortskirt","mat_bot.003_skirt"},{"M_bot.003_shortskirt","mat_bot.003_skirt"},
        {"F_bot.004_skinnyjeans","mat_bot.004_skinnyjeans"},{"M_bot.004_skinnyjeans","mat_bot.004_skinnyjeans"},
        {"F_bot.005_bigshorts","mat_bot.005_shorts"},{"M_bot.005_bigshorts","mat_bot.005_shorts"},
        {"F_bot.006_loosepants","mat_bot.006_loosepants"},{"M_bot.006_loosepants","mat_bot.006_loosepants"},
        // 连体装
        {"F_outfit.001_overalls","mat_outfit.001_overalls"},{"M_outfit.001_overalls","mat_outfit.001_overalls"},
        {"F_outfit.002","mat_outfit.002"},{"M_outfit.002","mat_outfit.002"},
        {"F_outfit.003","mat_outfit.003"},{"M_outfit.003","mat_outfit.003"},
        {"F_outfit.004","mat_outfit.004"},{"M_outfit.004","mat_outfit.004"},
        // 鞋
        {"F_shoes.001_boots","mat_shoes.001_boots"},{"F_shoes.002_sneakers","mat_shoes.002_sneakers"},
        {"F_shoes.003_flipflop","mat_shoes.003_flipflops"},{"F_shoes.004_sandals","mat_shoes.004_sandals"},
        {"F_shoes.005_highboots","mat_shoes.005_highboots"},{"F_shoes.006_lowsneakers","mat_shoes.006_lowsneakers"},
        {"F_shoes.007_chelsea","mat_shoes.007_chelseas"},
        // 头发
        {"C_hair_medium.001","mat_hair_medium.001"},{"C_hair_medium.002","mat_hair_medium.002"},
        {"C_hair_short.001","mat_hair_short.001"},{"C_hair_short.002","mat_hair_short.002"},
        {"C_hair_short.003","mat_hair_short.003"},{"C_hair_short.004","mat_hair_short.004"},
        {"C_hair_long.001","mat_hair_long.001"},{"C_hair_buzzcut","mat_hair_buzzcut"},
        {"C_hair_curly.001","mat_hair_curly.001"},{"C_hair_curly.002","mat_hair_curly.002"},
        {"C_hair_afro.001","mat_hair_afro.001"},{"C_hair_afro.002","mat_hair_afro.002"},{"C_hair_afro.003","mat_hair_afro.003"},
        {"C_hair_tied.001","mat_hair_tied.001"},{"C_hair_tied.002","mat_hair_tied.002"},
        {"C_hair_tied.001_bun","mat_hair_tied - bun"},{"C_hair_tied.002_buns","mat_hair_tied - bun"},
        {"C_hair_tied.001_ponytail","mat_hair_tied.001 - ponytail"},
        {"C_hair_tied.002_twintails","mat_hair_tied.002 - twintails"},
        {"C_hair_bangs.001","mat_hair_bangs.001"},{"C_hair_bangs.002","mat_hair_bangs.002"},{"C_hair_bangs.003","mat_hair_bangs.003"},
        // 其它
        {"C_acc.001_scarf","mat_acc.001_scarf"},
        {"C_glasses.01_square","mat_glasses.001_square"},{"C_glasses.02_round","mat_glasses.001_round"},
        {"C_hat.001_baseballcap","mat_hat.001_baseballcap"},{"C_hat.002_beanie","mat_hat.002_beanie"},
        {"C_hat.003_racerhelmet","mat_hat.003_racerhelmet"},
    };

    // 头部/身体这类“一个网格多个材质槽”：槽名 → 材质文件
    static readonly Dictionary<string,string> SlotMat = new Dictionary<string,string>
    {
        {"mat_body_F","mat_base_F_body"},{"mat_face_F","mat_base_F_face"},
        {"mat_body_M","mat_base_M_body"},{"mat_face_M","mat_base_M_face"},
        {"mat_eyelashes","mat_eyelashes"},{"mat_eye","mat_base_eye.001"},
        {"mat_eye_highlight","mat_eye_highlight"},{"mat_mouth","mat_base_mouth"},
        {"mat_eyebrows","mat_eyebrows"},
    };

    // ------------------------------------- 服装覆盖度（离线测量，单位 cm）
    static readonly Dictionary<string,float> TopSpanX = new Dictionary<string,float>
    {
        {"F_top.001_shirt",39.5f},{"M_top.001_shirt",42.6f},
        {"F_top.002_jacket",76.2f},{"M_top.002_jacket",78.7f},
        {"F_top.003_sweater",77.2f},{"M_top.003_sweater",79.7f},
        {"F_top.004_shirt",64.5f},{"M_top.004_shirt",67.8f},
        {"F_top.005_shirtvest",64.5f},{"M_top.005_shirtvest",66.5f},
        {"F_top.006_openshirt+T",43.8f},{"M_top.006_openshirt+T",47.2f},
        {"F_top.007_smalljacket_A",73.7f},{"M_top.007_smalljacket_A",76.1f},
        {"F_top.007_smalljacket_B",73.7f},{"M_top.007_smalljacket_B",76.1f},
        {"F_top.008_longsleeved",79.5f},{"M_top.008_longsleeved",83.4f},
        {"F_top.009_hoodie",76.2f},{"M_top.009_hoodie",79.0f},
        {"F_top.010_shirtandtie",74.7f},{"M_top.010_shirtandtie",77.8f},
        {"F_top.011_blazer",74.9f},{"M_top.011_blazer",78.2f},
        // 连体装
        {"F_outfit.001_overalls",40.2f},{"M_outfit.001_overalls",43.0f},
        {"F_outfit.002",43.7f},{"M_outfit.002",47.0f},
        {"F_outfit.003",40.8f},{"M_outfit.003",44.0f},
        {"F_outfit.004",87.1f},{"M_outfit.004",90.0f},
    };
    static readonly Dictionary<string,float> BotYMin = new Dictionary<string,float>
    {
        {"F_bot.001_pants",21.8f},{"M_bot.001_pants",22.0f},
        {"F_bot.002_shorts",75.3f},{"M_bot.002_shorts",73.2f},
        {"F_bot.003_shortskirt",63.5f},{"M_bot.003_shortskirt",63.6f},
        {"F_bot.004_skinnyjeans",8.7f},{"M_bot.004_skinnyjeans",8.7f},
        {"F_bot.005_bigshorts",63.1f},{"M_bot.005_bigshorts",57.1f},
        {"F_bot.006_loosepants",26.3f},{"M_bot.006_loosepants",26.3f},
        // 连体装
        {"F_outfit.001_overalls",11.2f},{"M_outfit.001_overalls",11.0f},
        {"F_outfit.002",63.1f},{"M_outfit.002",63.0f},
        {"F_outfit.003",58.6f},{"M_outfit.003",58.6f},
        {"F_outfit.004",-0.7f},{"M_outfit.004",-0.7f},
    };
    static readonly HashSet<string> ClosedShoes = new HashSet<string>
    {
        "F_shoes.001_boots","F_shoes.002_sneakers","F_shoes.005_highboots",
        "F_shoes.006_lowsneakers","F_shoes.007_chelsea",
    };

    // ------------------------------------------------------------------ 入口
    [MenuItem("Tools/干预项目/重建角色模型")]
    public static void Run()
    {
        log.Clear();
        matCache.Clear();
        Directory.CreateDirectory(OUT);
        // 每次重建都重新生成角色专属材质，避免配置变动后留下没用的旧材质
        string matRoot = OUT + "/材质";
        if (AssetDatabase.IsValidFolder(matRoot)) AssetDatabase.DeleteAsset(matRoot);
        foreach (var c in Chars) Build(c);
        SwapTestScene();
        File.WriteAllText("Assets/assets/_报告/_重建报告.txt", string.Join("\n", log));
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("角色重建完成：\n" + string.Join("\n", log));
    }

    // 读取场景里各角色实例相对 prefab 少掉的对象（= 用户手动删掉的物件），写到报告文件
    public static void DumpSceneOverrides()
    {
        var sb = new System.Text.StringBuilder();
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        foreach (var go in scene.GetRootGameObjects())
        {
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(go)) continue;
            var src = PrefabUtility.GetCorrespondingObjectFromSource(go);
            if (src == null) continue;
            string path = AssetDatabase.GetAssetPath(src);
            if (string.IsNullOrEmpty(path) || !path.Contains("角色_URP")) continue;

            var contents = PrefabUtility.LoadPrefabContents(path);
            var full = new HashSet<string>();
            foreach (var tr in contents.GetComponentsInChildren<Transform>(true)) full.Add(PathOf(contents.transform, tr));
            var now = new HashSet<string>();
            foreach (var tr in go.GetComponentsInChildren<Transform>(true)) now.Add(PathOf(go.transform, tr));

            sb.AppendLine(go.name + "  [" + System.IO.Path.GetFileName(path) + "]");
            bool any = false;
            foreach (var k in full) if (!now.Contains(k)) { sb.AppendLine("    删: " + k); any = true; }
            if (!any) sb.AppendLine("    (没删东西)");
            PrefabUtility.UnloadPrefabContents(contents);
        }
        File.WriteAllText("Assets/assets/_报告/_场景删减清单.txt", sb.ToString());
        Debug.Log("[CharRebuild] 场景删减清单:\n" + sb);
    }

    static string PathOf(Transform root, Transform t)
    {
        string p = t.name;
        var cur = t.parent;
        while (cur != null && cur != root) { p = cur.name + "/" + p; cur = cur.parent; }
        return p;
    }

    // 把测试场景里的主角换成重建后的 prefab（场景没开就临时加载，改完保存再关）
    static void SwapTestScene()
    {
        const string scenePath = "Assets/Scenes/Test_徐夏_动画.unity";
        if (!File.Exists(scenePath)) { log.Add("（没有测试场景，跳过场景替换）"); return; }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OUT + "/徐夏_可动.prefab");
        if (prefab == null) { log.Add("（找不到新的徐夏 prefab，跳过场景替换）"); return; }

        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(scenePath);
        bool opened = false;
        if (!scene.IsValid() || !scene.isLoaded)
        {
            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            opened = true;
        }

        GameObject old = null;
        foreach (var go in scene.GetRootGameObjects())
            if (go.name.StartsWith("徐夏")) { old = go; break; }

        if (old == null) log.Add("（场景里没找到徐夏，跳过场景替换）");
        else
        {
            var tr = old.transform;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            inst.name = "徐夏";
            inst.transform.SetPositionAndRotation(tr.position, tr.rotation);
            inst.transform.localScale = tr.localScale;
            Object.DestroyImmediate(old);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.Add("测试场景里的徐夏已换成新模型");
        }
        if (opened) EditorSceneManager.CloseScene(scene, true);
    }

    // ------------------------------------------------------------------ 组装
    static void Build(Cfg c)
    {
        string bodyPath = FindFbx("1_身体_Body", c.Body);
        if (bodyPath == null) { log.Add(c.Name + ": 找不到身体 " + c.Body); return; }

        var root = new GameObject(c.Name);
        var body = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(bodyPath));
        body.name = "Base_" + c.Body;
        body.transform.SetParent(root.transform, false);

        // 1) 衣服盖住的皮肤段删掉；2) 剩下的皮肤材质
        string coverTop = string.IsNullOrEmpty(c.Outfit) ? c.Top : c.Outfit;
        string coverBot = string.IsNullOrEmpty(c.Outfit) ? c.Bot : c.Outfit;
        RemoveSkin(body, c, coverTop, coverBot);
        SkinMaterials(body, c);

        // 3) 头（面部/眼睛/嘴/眉毛/睫毛 多材质槽）
        Attach(body.transform, FindFbx("2_头部_Head", c.Head), "Head", null, c);

        // 4) 身体服装
        if (!string.IsNullOrEmpty(c.Outfit))
            Attach(body.transform, FindFbx("10_连体装_Outfit", c.Outfit), "Outfit", MatOf(c.Outfit), c);
        else
        {
            Attach(body.transform, FindFbx("7_上衣_Top", c.Top), "Top", MatOf(c.Top), c);
            Attach(body.transform, FindFbx("8_下装_Bottom", c.Bot), "Bot", MatOf(c.Bot), c);
        }
        Attach(body.transform, FindFbx("9_鞋_Shoes", c.Shoes), "Shoes", MatOf(c.Shoes), c);
        if (!string.IsNullOrEmpty(c.Scarf))
            Attach(body.transform, FindFbx("6_围巾_Scarf", c.Scarf), "Scarf", MatOf(c.Scarf), c);

        // 5) 头发/刘海/发束/帽子/眼镜：挂到 CC_Base_Head
        var headBone = FindBone(body.transform, "CC_Base_Head");
        if (headBone == null) log.Add(c.Name + ": 找不到头骨 CC_Base_Head");
        else
        {
            AttachUnderHead(headBone, FindFbx("3_发型_Hair", c.Hair), "Hair", MatOf(c.Hair), c);
            AttachUnderHead(headBone, FindFbx("3_发型_Hair", c.Bangs), "Hair_Bangs", MatOf(c.Bangs), c);
            AttachUnderHead(headBone, FindFbx("3_发型_Hair", c.Deco), "Hair_Deco", MatOf(c.Deco), c);
            AttachUnderHead(headBone, FindFbx("4_帽子_Hat", c.Hat), "Hat", MatOf(c.Hat), c);
            AttachUnderHead(headBone, FindFbx("5_眼镜_Glasses", c.Glasses), "Glasses", MatOf(c.Glasses), c);
        }

        // 6) 动画用 humanoid Avatar（沿用原角色 FBX）
        var srcFbx = SRC + "/" + c.Name + "/" + c.Name + ".fbx";
        var avatar = AssetDatabase.LoadAllAssetsAtPath(srcFbx).OfType<Avatar>().FirstOrDefault(a => a.isHuman);
        if (avatar != null)
        {
            var anim = root.AddComponent<Animator>();
            anim.avatar = avatar;
            anim.applyRootMotion = false;
        }
        else log.Add(c.Name + ": 没有找到 humanoid Avatar（动画会不可用）");

        // 7) 保存
        string outPath = OUT + "/" + c.Name + "_可动.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, outPath);
        Object.DestroyImmediate(root);
        log.Add(string.Format("  {0}: {1} / {2} / {3} / 发:{4}{5}  删:{6}  皮肤:{7}",
            c.Name,
            string.IsNullOrEmpty(c.Outfit) ? c.Top : c.Outfit,
            c.Bot, c.Shoes, c.Hair,
            string.IsNullOrEmpty(c.Deco) ? "" : "+" + c.Deco,
            string.IsNullOrEmpty(c.Remove) ? "-" : c.Remove,
            c.Skin));
    }

    // 皮肤删减清单 = 场景里手动删减的模板（每个角色一份），避免穿模。
    // 关键字按名字包含匹配：hips / torso.001 / shoulders / arms_upper / arms_lower /
    // legs_upper / legs_lower / legs_knee / feet
    static void RemoveSkin(GameObject body, Cfg c, string top, string bot)
    {
        var del = new HashSet<string>();
        if (!string.IsNullOrEmpty(c.Remove))
            foreach (var k in c.Remove.Split(','))
                if (k.Trim().Length > 0) del.Add(k.Trim());
        if (del.Count == 0) return;

        foreach (var smr in body.GetComponentsInChildren<SkinnedMeshRenderer>(true).ToArray())
        {
            string nm = smr.gameObject.name.ToLowerInvariant();
            if (!nm.Contains("_body_")) continue;
            if (del.Any(k => nm.Contains(k)))
                Object.DestroyImmediate(smr.gameObject);
        }
    }

    // 身体上剩下的皮肤（躯干以外的部分）→ 角色专属肤色材质
    static void SkinMaterials(GameObject body, Cfg c)
    {
        string skinFile = c.Body.StartsWith("M") ? "mat_base_M_body" : "mat_base_F_body";
        foreach (var r in body.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                string slot = mats[i] != null ? mats[i].name : "";
                string file = SlotMat.ContainsKey(slot) ? SlotMat[slot] : skinFile;   // 身体上剩下的都是皮肤
                mats[i] = GetMat(c, file, "skin") ?? mats[i];
            }
            r.sharedMaterials = mats;
        }
    }

    static void Attach(Transform baseRoot, string fbxPath, string label, string matName, Cfg c)
    {
        if (string.IsNullOrEmpty(fbxPath)) { log.Add("  " + c.Name + ": 缺少 " + label); return; }
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (asset == null) { log.Add("  " + c.Name + ": 无法加载 " + fbxPath); return; }

        foreach (var src in asset.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var go = new GameObject(label + "_" + src.name);
            go.transform.SetParent(baseRoot, false);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = src.sharedMesh;
            smr.sharedMaterials = ResolveMats(src, matName, label, c);
            var bones = new Transform[src.bones.Length];
            for (int i = 0; i < bones.Length; i++)
                bones[i] = src.bones[i] != null ? FindBone(baseRoot, src.bones[i].name) : null;
            smr.bones = bones;
            smr.rootBone = src.rootBone != null ? FindBone(baseRoot, src.rootBone.name) : null;
            smr.updateWhenOffscreen = true;
        }
        foreach (var mr in asset.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            var go = new GameObject(label + "_" + mr.gameObject.name);
            go.transform.SetParent(baseRoot, false);
            go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = ResolveMats(mr, matName, label, c);
        }
    }

    static void AttachUnderHead(Transform headBone, string fbxPath, string label, string matName, Cfg c)
    {
        if (string.IsNullOrEmpty(fbxPath)) return;
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (asset == null) { log.Add("  " + c.Name + ": 无法加载 " + fbxPath); return; }
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        inst.name = label;
        inst.transform.SetParent(headBone, false);
        foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            r.sharedMaterials = ResolveMats(r, matName, label, c);
    }

    static Material[] ResolveMats(Renderer r, string matName, string label, Cfg c)
    {
        var src = r.sharedMaterials;
        var outMats = new Material[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            string slot = src[i] != null ? src[i].name : "";
            string file = matName;
            string role = RoleOfLabel(label);
            if (string.IsNullOrEmpty(file) && !string.IsNullOrEmpty(slot) && SlotMat.ContainsKey(slot))
            {
                file = SlotMat[slot];
                role = RoleOfSlot(slot);
            }
            outMats[i] = GetMat(c, file, role) ?? src[i];
        }
        return outMats;
    }

    // ------------------------------------------------------------------ 材质与配色
    static string RoleOfLabel(string label)
    {
        switch (label)
        {
            case "Top": case "Outfit": return "top";
            case "Bot": return "bot";
            case "Shoes": return "shoes";
            case "Scarf": return "acc";
            case "Hair": case "Hair_Bangs": case "Hair_Deco": return "hair";
            case "Hat": return "top";
            default: return null;
        }
    }
    static string RoleOfSlot(string slot)
    {
        if (slot.Contains("eye_highlight")) return null;
        if (slot.Contains("eyebrow") || slot.Contains("eyelash")) return "brow";
        if (slot.Contains("eye")) return null;          // 眼球贴图通道含义不确定，不动，保持原材质
        if (slot.Contains("mouth")) return null;
        if (slot.Contains("body") || slot.Contains("face")) return "skin";
        return null;
    }

    static Material GetMat(Cfg c, string matFile, string role)
    {
        if (string.IsNullOrEmpty(matFile)) return null;
        var src = LoadMat(matFile);
        if (src == null) { log.Add("  " + c.Name + ": 找不到材质 " + matFile); return null; }
        if (string.IsNullOrEmpty(role)) return src;      // 不染色 → 共享材质

        string key = c.Name + "|" + role + "|" + matFile;
        Material m;
        if (matCache.TryGetValue(key, out m) && m != null) return m;

        string matRoot = OUT + "/材质";
        if (!AssetDatabase.IsValidFolder(matRoot)) AssetDatabase.CreateFolder(OUT, "材质");
        string dir = matRoot + "/" + c.Name;
        if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(matRoot, c.Name);
        string path = dir + "/" + role + "_" + matFile + ".mat";
        m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(src);
            AssetDatabase.CreateAsset(m, path);
        }
        else EditorUtility.CopySerialized(src, m);

        Tint(m, role, c, matFile);
        EditorUtility.SetDirty(m);
        matCache[key] = m;
        return m;
    }

    static void Tint(Material m, string role, Cfg c, string matFile)
    {
        string a = null, b = null;
        switch (role)
        {
            case "skin":  a = c.Skin;  break;
            case "eye":   a = c.Eye;   break;
            case "hair":  a = c.HairC; break;
            case "brow":  a = c.HairC; break;
            case "top":   a = c.TopC;  b = c.TopB;  break;
            case "bot":   a = c.BotC;  break;
            case "shoes": a = c.ShoeC; break;
            case "acc":   a = c.AccC;  break;
        }
        if (a == null) return;
        var mainP   = ParsePair(a);
        var accentP = b != null ? ParsePair(b) : mainP;

        // 有几件衣服的“主布料”在遮罩的 G 通道（实测遮罩各通道占比得出），
        // 这类材质的颜色要反过来：主色写 B 色对，点缀色写 A 色对，否则渲染出来是点缀色。
        Pair slotA = mainP, slotB = accentP, slotC = accentP;
        if (MainOnG.Contains(matFile)) { slotA = accentP; slotB = mainP; }

        if (m.HasProperty("_Color_A_1"))      // 角色 Shader（CharacterLit）
        {
            m.SetColor("_Color_A_1", slotA.c1); m.SetColor("_Color_A_2", slotA.c2);
            m.SetColor("_Color_B_1", slotB.c1); m.SetColor("_Color_B_2", slotB.c2);
            m.SetColor("_Color_C_1", slotC.c1); m.SetColor("_Color_C_2", slotC.c2);
        }
        else if (m.HasProperty("_BaseColor"))  // URP/Lit、URP/Unlit（眼镜/眉毛/睫毛等）
        {
            m.SetColor("_BaseColor", mainP.c1);
            if (m.HasProperty("_Color")) m.SetColor("_Color", mainP.c1);
        }
    }

    // 主布料在 G 通道的材质（对应贴图：G 通道占比 > R）
    static readonly HashSet<string> MainOnG = new HashSet<string>
    {
        "mat_top.002_jacket", "mat_top.006_openshirt", "mat_top.011_blazer",
        "mat_shoes.002_sneakers", "mat_outfit.002",
    };

    struct Pair { public Color c1, c2; }
    static Pair ParsePair(string s)
    {
        var parts = s.Split('|');
        Pair p; p.c1 = Hex(parts[0]); p.c2 = Hex(parts.Length > 1 ? parts[1] : parts[0]);
        return p;
    }
    static Color Hex(string hex)
    {
        hex = hex.TrimStart('#');
        int r = System.Convert.ToInt32(hex.Substring(0, 2), 16);
        int g = System.Convert.ToInt32(hex.Substring(2, 2), 16);
        int b = System.Convert.ToInt32(hex.Substring(4, 2), 16);
        return new Color(r / 255f, g / 255f, b / 255f, 1f);
    }

    static Material LoadMat(string matFile)
    {
        if (string.IsNullOrEmpty(matFile)) return null;
        foreach (var f in Directory.GetFiles(MAT, matFile + ".mat", SearchOption.AllDirectories))
            return AssetDatabase.LoadAssetAtPath<Material>(f.Replace('\\', '/'));
        return null;
    }
    static string MatOf(string garment)
    {
        return (!string.IsNullOrEmpty(garment) && GarmentMat.ContainsKey(garment)) ? GarmentMat[garment] : null;
    }
    static Transform FindBone(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }
    static string FindFbx(string category, string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        string dir = MESH + "/" + category;
        if (!Directory.Exists(dir)) return null;
        foreach (var f in Directory.GetFiles(dir, name + ".fbx", SearchOption.AllDirectories))
            return f.Replace('\\', '/');
        return null;
    }
}

// ------------------------------------------------------------- 一次性自动执行
// 工程根目录存在 Assets/_rebuild_trigger.txt 时，编辑器下次刷新/重编译后自动跑一遍重建，
// 跑完自己删掉触发器；出错则把异常写到 Assets/_角色重建错误.txt
[InitializeOnLoad]
public static class CharRebuildAutoRun
{
    const string Trigger = "Assets/_rebuild_trigger.txt";
    const string SaveTrigger = "Assets/_save_scene_trigger.txt";
    const string DumpTrigger = "Assets/_dump_trigger.txt";
    const string ErrFile = "Assets/_角色重建错误.txt";

    static CharRebuildAutoRun()
    {
        Debug.Log("[CharRebuild] ctor: save=" + File.Exists(SaveTrigger) + " dump=" + File.Exists(DumpTrigger) + " run=" + File.Exists(Trigger) + " cwd=" + Directory.GetCurrentDirectory());
        if (File.Exists(SaveTrigger))
        {
            File.Delete(SaveTrigger);
            EditorApplication.delayCall += () =>
            {
                UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
                Debug.Log("[CharRebuild] 已保存当前打开的场景");
            };
        }
        if (File.Exists(DumpTrigger))
        {
            File.Delete(DumpTrigger);
            EditorApplication.delayCall += () => CharRebuild.DumpSceneOverrides();
        }
        if (!File.Exists(Trigger)) return;
        File.Delete(Trigger);
        EditorApplication.delayCall += () =>
        {
            try
            {
                CharRebuild.Run();
                if (File.Exists(ErrFile)) File.Delete(ErrFile);
                Debug.Log("[CharRebuild] 自动重建完成");
            }
            catch (System.Exception e)
            {
                File.WriteAllText(ErrFile, e.ToString());
                Debug.LogError("[CharRebuild] 自动重建失败: " + e);
            }
        };
    }
}

// touch 214150
