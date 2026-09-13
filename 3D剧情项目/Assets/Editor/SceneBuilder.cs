// 搭建游戏主场景：把 6 个剧情地点做成并排的"片场"，并生成触发点骨架。
// 用法：菜单 Tools/干预项目/搭建游戏场景 / 列出场景模型尺寸 / 渲染场景总览
// 输出：Assets/Scenes/Game.unity + Assets/assets/_报告/_场景搭建.txt
//
// 设计要点：
//   · 房间尺寸按剧本需要定（12~24m），给玩家探索空间
//   · 墙体用 走廊套件 的模块化墙板（墙/门框墙/窗墙，每块 4×4m）拼，不用封死的等距房间盒子
//   · 不做天花板（方便在 Scene 视图里俯瞰编辑）
//   · 家具用 寝室套件1（真实尺寸：床 1.87m、桌 2.15m），不需要缩放
//   · 所有模型自动落地 + 自动居中（FBX 原点各不相同）

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public static class SceneBuilder
{
    const string OUT_SCENE = "Assets/Scenes/Game.unity";
    const string OUT_DIR   = "Assets/Scenes";
    const string REPORT    = "Assets/assets/_报告/_场景搭建.txt";
    const float  GAP       = 40f;      // 地点之间的间距
    const float  PANEL     = 4f;       // 墙板尺寸（墙.fbx 是 4×4m）
    const string FIX_DIR   = "Assets/assets/01_场景_Scene/_修正网格";

    // 墙板
    const string W_WALL = "墙.fbx";
    const string W_DOOR = "门框墙.fbx";
    const string W_WIN  = "窗墙.fbx";

    // 各墙板的"正脸"朝向不一致，需要补一个 yaw 偏移：
    //   墙.fbx / 窗墙.fbx  面积加权法线 +Z（正脸朝 +Z）
    //   门框墙.fbx         面积加权法线 -Z（根节点自带 180° 转向，是反的）
    static float PanelYawFix(string file)
    {
        return file == W_DOOR ? 180f : 0f;
    }

    // ------------------------------------------------------------------ 数据结构
    // Pos = 房间中心为原点的局部坐标；Pos.y = 模型底部对准的高度
    class Piece { public string File; public Vector3 Pos; public float Yaw; }

    class Loc
    {
        public string Name, Title, Chapter;
        public float W = 16f, D = 12f, H = 3.6f;     // 房间净尺寸（W×D×H）
        public List<Piece> Pieces = new List<Piece>();
        public List<string> Triggers = new List<string>();
        public int DoorIdx = -1;                      // 南墙第几块换成门框墙（-1=不要门）
        public int[] WinIdx = new int[0];             // 北墙哪几块换成窗墙
        public string Note = "";
    }

    // ------------------------------------------------------------------ 配置
    static Loc[] BuildLocs()
    {
        var locs = new List<Loc>();

        // ① 教室 16×12m（3 排 × 3 座）
        var room = new Loc { Name = "Loc_教室", Title = "大学教室", Chapter = "第1章", W = 16, D = 12, H = 3.6f, DoorIdx = 1, WinIdx = new[]{ 1, 2 } };
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
            {
                float x = -3.6f + c * 3.6f;
                float z = -2.6f + r * 3.2f;
                room.Pieces.Add(new Piece { File = "simple_desk_A.fbx", Pos = new Vector3(x, 0, z),        Yaw = 90 });
                room.Pieces.Add(new Piece { File = "simple_chair.fbx",  Pos = new Vector3(x, 0, z - 1.7f), Yaw = 90 });
            }
        room.Pieces.Add(new Piece { File = "shelf.fbx",            Pos = new Vector3(-6.6f, 0, -5.0f), Yaw = 0 });
        room.Pieces.Add(new Piece { File = "simple_library_A.fbx", Pos = new Vector3( 6.4f, 0, -5.0f), Yaw = 0 });
        room.Triggers.AddRange(new[] { "1-01_自己的座位", "1-02_组长", "1-03_手机", "1-04_教室门" });
        locs.Add(room);

        // ② 走廊 24×4m
        var hall = new Loc { Name = "Loc_走廊", Title = "教室外走廊", Chapter = "第1章", W = 24, D = 4, H = 3.6f, DoorIdx = 2, WinIdx = new[]{ 0, 1, 4, 5 } };
        hall.Pieces.Add(new Piece { File = "书架.fbx",       Pos = new Vector3(-7f, 0, -1.4f), Yaw = 0 });
        hall.Pieces.Add(new Piece { File = "书架.fbx",       Pos = new Vector3(-4f, 0, -1.4f), Yaw = 0 });
        hall.Pieces.Add(new Piece { File = "储物箱单个.fbx", Pos = new Vector3( 8f, 0, -1.4f), Yaw = 0 });
        hall.Pieces.Add(new Piece { File = "纸箱闭合.fbx",   Pos = new Vector3( 9.5f, 0, -1.3f), Yaw = 0 });
        hall.Pieces.Add(new Piece { File = "相框横.fbx",     Pos = new Vector3(-9f, 1.6f, -1.90f), Yaw = 0 });
        hall.Pieces.Add(new Piece { File = "相框竖.fbx",     Pos = new Vector3( 3f, 1.7f, -1.90f), Yaw = 0 });
        hall.Triggers.AddRange(new[] { "1-05_张知远", "1-06_走廊尽头" });
        locs.Add(hall);

        // ③ 宿舍 16×12m（4 人间：4 床 + 4 桌 + 4 椅）
        var dorm = new Loc { Name = "Loc_宿舍", Title = "女生宿舍（4人间）", Chapter = "第2/4/5章", W = 16, D = 12, H = 3.2f, DoorIdx = 2, WinIdx = new[]{ 1, 2 } };
        for (int i = 0; i < 4; i++)
        {
            float x = -5.4f + i * 3.6f;
            dorm.Pieces.Add(new Piece { File = "simple_single_bed.fbx", Pos = new Vector3(x, 0, -4.0f), Yaw = 0 });
            dorm.Pieces.Add(new Piece { File = "simple_desk_A.fbx",     Pos = new Vector3(x, 0,  3.4f), Yaw = 180 });
            dorm.Pieces.Add(new Piece { File = "simple_chair.fbx",      Pos = new Vector3(x, 0,  1.6f), Yaw = 180 });
        }
        dorm.Pieces.Add(new Piece { File = "simple_library_A.fbx", Pos = new Vector3(-6.6f, 0, -0.6f), Yaw = 90 });
        dorm.Pieces.Add(new Piece { File = "simple_library_B.fbx", Pos = new Vector3( 6.6f, 0, -0.6f), Yaw = 90 });
        dorm.Pieces.Add(new Piece { File = "lamp.fbx",             Pos = new Vector3(-1.8f, 0.88f, 3.4f), Yaw = 0 });
        dorm.Pieces.Add(new Piece { File = "carpet.fbx",           Pos = new Vector3( 0.0f, 0.01f, -0.6f), Yaw = 0 });
        dorm.Triggers.AddRange(new[] {
            "2-01_书桌", "2-02_手机", "2-03_手机", "2-04_陆宣雨", "2-05_手机", "2-06_床",
            "4-01_书桌", "4-02_手机", "4-03_林溪",
            "5-01_自己的座位", "5-03_陆宣雨", "5-04_陆宣雨", "5-06_宿舍门"
        });
        locs.Add(dorm);

        // ④ 食堂 24×16m（靠窗用餐区 + 邻桌，项目里没有食堂素材，先用通用桌椅拼）
        var can = new Loc { Name = "Loc_食堂", Title = "食堂（★无专用素材，占位）", Chapter = "第3/5章", W = 24, D = 16, H = 4.0f, DoorIdx = 2, WinIdx = new[]{ 0, 1, 2, 3, 4 } };
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
            {
                float x = -6.5f + c * 6.5f;
                float z = -4.0f + r * 4.0f;
                can.Pieces.Add(new Piece { File = "simple_square_table_B.fbx", Pos = new Vector3(x, 0, z), Yaw = 0 });
                can.Pieces.Add(new Piece { File = "simple_chair.fbx", Pos = new Vector3(x - 1.2f, 0, z), Yaw = 90 });
                can.Pieces.Add(new Piece { File = "simple_chair.fbx", Pos = new Vector3(x + 1.2f, 0, z), Yaw = 270 });
            }
        can.Triggers.AddRange(new[] { "3-01_靠窗座位", "3-02_林溪王含" });
        can.Note = "★ 项目没有食堂专用素材，桌椅是拿寝室套件凑的；档口/饭碗/筷子都缺";
        locs.Add(can);

        // ⑤ 咨询办公室 12×8m
        var off = new Loc { Name = "Loc_办公室", Title = "咨询办公室", Chapter = "第3章", W = 12, D = 8, H = 3.2f, DoorIdx = 1, WinIdx = new[]{ 1 } };
        off.Pieces.Add(new Piece { File = "couch.fbx",             Pos = new Vector3(-3.2f, 0, -1.0f), Yaw = 0 });
        off.Pieces.Add(new Piece { File = "armchair.fbx",          Pos = new Vector3(-0.6f, 0, -1.0f), Yaw = 270 });
        off.Pieces.Add(new Piece { File = "coffee_table.fbx",      Pos = new Vector3(-2.0f, 0,  0.6f), Yaw = 0 });
        off.Pieces.Add(new Piece { File = "simple_desk_B.fbx",     Pos = new Vector3( 4.0f, 0, -2.6f), Yaw = 180 });
        off.Pieces.Add(new Piece { File = "simple_chair.fbx",      Pos = new Vector3( 4.0f, 0, -1.2f), Yaw = 180 });
        off.Pieces.Add(new Piece { File = "simple_library_A.fbx",  Pos = new Vector3(-5.4f, 0,  2.6f), Yaw = 90 });
        off.Pieces.Add(new Piece { File = "simple_library_C.fbx",  Pos = new Vector3( 5.4f, 0,  2.6f), Yaw = 270 });
        off.Pieces.Add(new Piece { File = "plant.fbx",             Pos = new Vector3( 5.0f, 0, -3.2f), Yaw = 0 });
        off.Triggers.AddRange(new[] { "3-04_沙发", "3-05_李老师" });
        locs.Add(off);

        // ⑥ 图书馆 20×16m
        var lib = new Loc { Name = "Loc_图书馆", Title = "图书馆 / 自习区", Chapter = "第4/5章", W = 20, D = 16, H = 4.0f, DoorIdx = 2, WinIdx = new[]{ 1, 2, 3 } };
        for (int i = 0; i < 5; i++)
        {
            float x = -6.0f + i * 3.0f;
            lib.Pieces.Add(new Piece { File = "书架.fbx", Pos = new Vector3(x, 0, -5.0f), Yaw = 0 });
        }
        for (int i = 0; i < 4; i++)
        {
            float x = -4.5f + i * 3.0f;
            lib.Pieces.Add(new Piece { File = "simple_rect_table_B.fbx", Pos = new Vector3(x, 0, 1.0f), Yaw = 0 });
            lib.Pieces.Add(new Piece { File = "simple_chair.fbx",        Pos = new Vector3(x - 1.1f, 0, 1.0f), Yaw = 90 });
            lib.Pieces.Add(new Piece { File = "simple_chair.fbx",        Pos = new Vector3(x + 1.1f, 0, 1.0f), Yaw = 270 });
        }
        lib.Pieces.Add(new Piece { File = "simple_rect_table_A.fbx", Pos = new Vector3( 0.0f, 0, 5.0f), Yaw = 90 });
        lib.Pieces.Add(new Piece { File = "simple_chair.fbx",        Pos = new Vector3(-1.2f, 0, 5.0f), Yaw = 90 });
        lib.Pieces.Add(new Piece { File = "simple_chair.fbx",        Pos = new Vector3( 1.2f, 0, 5.0f), Yaw = 270 });
        lib.Triggers.AddRange(new[] { "4-04_靠窗自习位", "4-05_复习计划纸" });
        locs.Add(lib);

        return locs.ToArray();
    }

    // ------------------------------------------------------------------ 主入口
    [MenuItem("Tools/干预项目/搭建游戏场景")]
    public static void Run()
    {
        var log = new List<string>();
        log.Add("场景搭建报告  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("");

        var locs = BuildLocs();
        float[] xs = new float[locs.Length];
        for (int i = 0; i < locs.Length; i++) xs[i] = i * GAP;

        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.Log("[SceneBuilder] 用户取消，已中止");
            return;
        }
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 环境光：给个柔和底光，避免背光面全黑
        RenderSettings.ambientMode  = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.20f, 0.21f, 0.25f);

        // 主光（带阴影）+ 反向补光（不带阴影）—— 单盏定向光会让背光墙全黑
        var sunGO = new GameObject("Sun_主光");
        var sun = sunGO.AddComponent<Light>();
        sun.type = LightType.Directional; sun.intensity = 1.05f;
        sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.75f;
        sunGO.transform.rotation = Quaternion.Euler(48, 35, 0);

        var fillGO = new GameObject("Sun_补光");
        var fill = fillGO.AddComponent<Light>();
        fill.type = LightType.Directional; fill.intensity = 0.32f;
        fill.shadows = LightShadows.None;
        fillGO.transform.rotation = Quaternion.Euler(35, 215, 0);

        var root = new GameObject("GameRoot");
        var locsRoot = new GameObject("Locations");
        locsRoot.transform.SetParent(root.transform, false);

        for (int i = 0; i < locs.Length; i++)
            BuildLocation(locs[i], xs[i], locsRoot.transform, log);

        // 玩家（第一人称）+ 相机，放在教室
        string playerPrefab = AssetLocator.FileNamed("徐夏_可动.prefab");
        var locClassroom = GameObject.Find("Loc_教室");
        Vector3 spawnWorld = Vector3.zero;
        if (locClassroom != null)
        {
            var sp = locClassroom.transform.Find("Spawn");
            if (sp != null) spawnWorld = sp.position;
        }

        if (playerPrefab != null)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(playerPrefab);
            var player = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
            player.name = "Player_徐夏";
            player.transform.position = spawnWorld;
            player.transform.SetParent(root.transform, true);

            int hidden = 0;
            foreach (var smr in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.gameObject.name.StartsWith("Head_")) { smr.enabled = false; hidden++; }
            log.Add("[玩家] 徐夏_可动   已隐藏头部 renderer ×" + hidden + "（第一人称）");

            var camGO = new GameObject("FP_相机");
            camGO.transform.SetParent(player.transform, false);
            camGO.transform.localPosition = new Vector3(0f, 1.62f, 0.12f);
            camGO.tag = "MainCamera";
            var cam = camGO.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f; cam.fieldOfView = 60f;
            camGO.AddComponent<AudioListener>();
            log.Add("[玩家] FP_相机 挂载于 徐夏，眼高 1.62m");
        }
        else log.Add("[玩家] ★ 找不到 徐夏_可动.prefab");
        log.Add("");

        Directory.CreateDirectory(OUT_DIR);
        AssetDatabase.Refresh();
        bool ok = EditorSceneManager.SaveScene(scene, OUT_SCENE);
        AssetDatabase.Refresh();
        log.Add("保存场景：" + OUT_SCENE + "  " + (ok ? "成功" : "★失败"));

        var list = EditorBuildSettings.scenes.ToList();
        if (!list.Any(s => s.path == OUT_SCENE))
        {
            list.Insert(0, new EditorBuildSettingsScene(OUT_SCENE, true));
            EditorBuildSettings.scenes = list.ToArray();
            log.Add("已加入 Build Settings（第 0 号）");
        }
        log.Add("");
        log.Add("说明：");
        log.Add("  · 没有天花板，方便在 Scene 视图俯瞰编辑");
        log.Add("  · 墙用 走廊套件 的 4×4m 墙板（墙/门框墙/窗墙）拼，改尺寸就改 W/D/H");
        log.Add("  · 家具用 寝室套件1，真实尺寸，未缩放");
        log.Add("  · 触发点是带图标的空节点，位置只是占位");

        Directory.CreateDirectory(Path.GetDirectoryName(REPORT).Replace('/', Path.DirectorySeparatorChar));
        File.WriteAllText(REPORT, string.Join("\n", log.ToArray()));
        Debug.Log("[SceneBuilder] 完成，报告：" + REPORT);
    }

    // ------------------------------------------------------------------ 单个地点
    static void BuildLocation(Loc loc, float x, Transform parent, List<string> log)
    {
        var locRoot = new GameObject(loc.Name);
        locRoot.transform.SetParent(parent, false);
        locRoot.transform.position = new Vector3(x, 0, 0);

        // --- 地板（唯一保留的"壳"，名字固定 Shell_地板，预览时会保留它）---
        var shell = new GameObject("Shell");
        shell.transform.SetParent(locRoot.transform, false);
        Box(shell.transform, "Shell_地板", Vector3.zero, new Vector3(loc.W, 0.2f, loc.D), LoadMat("Surface_Floor_A.mat"), -0.1f);

        // --- 四面墙：用 4×4m 墙板拼 ---
        var walls = new GameObject("Shell_墙");
        walls.transform.SetParent(locRoot.transform, false);
        int nW = Mathf.Max(1, Mathf.RoundToInt(loc.W / PANEL));
        int nD = Mathf.Max(1, Mathf.RoundToInt(loc.D / PANEL));
        Wall(walls.transform, "南", new Vector3(0, 0, -loc.D * 0.5f), nW, loc.W, loc.H, 0f,   loc.DoorIdx, new int[0], locRoot);
        Wall(walls.transform, "北", new Vector3(0, 0,  loc.D * 0.5f), nW, loc.W, loc.H, 180f, -1,          loc.WinIdx, locRoot);
        Wall(walls.transform, "西", new Vector3(-loc.W * 0.5f, 0, 0), nD, loc.D, loc.H, 90f,  -1,          new int[0], locRoot);
        Wall(walls.transform, "东", new Vector3( loc.W * 0.5f, 0, 0), nD, loc.D, loc.H, 270f, -1,          new int[0], locRoot);

        // --- 家具 ---
        var content = new GameObject("Content");
        content.transform.SetParent(locRoot.transform, false);
        for (int k = 0; k < loc.Pieces.Count; k++)
        {
            var p = loc.Pieces[k];
            string path = AssetLocator.FileNamed(p.File);
            if (path == null) { log.Add("  ★ 找不到模型 " + p.File); continue; }
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) { log.Add("  ★ 加载失败 " + p.File); continue; }

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset, locRoot.scene);
            inst.name = Path.GetFileNameWithoutExtension(p.File) + "_" + k;

            // ★ 模型根节点自带 旋转(270,0,0) + 缩放(100)，这些绝不能改（改了模型会躺倒/缩到看不见）。
            //   所以 yaw 交给一个 pivot 空节点，模型原封不动。
            var pivot = new GameObject("P_" + inst.name);
            pivot.transform.SetParent(content.transform, false);
            pivot.transform.localRotation = Quaternion.Euler(0f, p.Yaw, 0f);
            inst.transform.SetParent(pivot.transform, false);
            inst.transform.localPosition = Vector3.zero;      // 只清掉 FBX 根自带位移
            PlaceAt(pivot, locRoot.transform.position, p.Pos);
        }

        // --- 灯组（只留空节点，房间灯后续自己加）---
        var lights = new GameObject("Area_灯组");
        lights.transform.SetParent(locRoot.transform, false);

        // --- 出生点 ---
        var spawn = new GameObject("Spawn");
        spawn.transform.SetParent(locRoot.transform, false);
        spawn.transform.localPosition = new Vector3(0f, 0f, -loc.D * 0.25f);

        // --- 触发点骨架 ---
        var trigRoot = new GameObject("Triggers");
        trigRoot.transform.SetParent(locRoot.transform, false);
        int n = loc.Triggers.Count;
        int cols = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(n)));
        int rows = Mathf.Max(1, Mathf.CeilToInt((float)n / cols));
        for (int i = 0; i < n; i++)
        {
            int col = i % cols, row = i / cols;
            float tx = Mathf.Lerp(-loc.W * 0.3f, loc.W * 0.3f, (col + 0.5f) / cols);
            float tz = Mathf.Lerp(-loc.D * 0.3f, loc.D * 0.3f, (row + 0.5f) / rows);
            var t = new GameObject(loc.Triggers[i]);
            t.transform.SetParent(trigRoot.transform, false);
            t.transform.localPosition = new Vector3(tx, 1.1f, tz);
            SetIcon(t);
        }

        log.Add(string.Format("{0}（{1}）  x={2}m", loc.Name, loc.Chapter, x));
        log.Add(string.Format("    房间：{0:0.#} × {1:0.#} × {2:0.#} m（{3:0} m²）   门在南墙第{4}块   窗 {5} 扇",
            loc.W, loc.D, loc.H, loc.W * loc.D, loc.DoorIdx, loc.WinIdx.Length));
        log.Add(string.Format("    摆件：{0} 件", loc.Pieces.Count));

        // 外壳实测：确认墙板尺寸/厚度对（墙板根节点带 缩放100，算错就会变成平铺的薄片）
        var wb = BoundsOf(walls);
        if (wb.HasValue)
            log.Add(string.Format("    墙体实测：X {0:0.0} ~ {1:0.0}   Y {2:0.00} ~ {3:0.0}   Z {4:0.0} ~ {5:0.0}   （应≈ ±{6:0.0} / 高{7:0.#} / ±{8:0.0}）",
                wb.Value.min.x - locRoot.transform.position.x, wb.Value.max.x - locRoot.transform.position.x,
                wb.Value.min.y, wb.Value.max.y,
                wb.Value.min.z - locRoot.transform.position.z, wb.Value.max.z - locRoot.transform.position.z,
                loc.W * 0.5f, loc.H, loc.D * 0.5f));

        // 越界检查：家具是否穿出墙/地板
        var cb = BoundsOf(content);
        if (cb.HasValue)
        {
            float lx0 = cb.Value.min.x - locRoot.transform.position.x, lx1 = cb.Value.max.x - locRoot.transform.position.x;
            float lz0 = cb.Value.min.z - locRoot.transform.position.z, lz1 = cb.Value.max.z - locRoot.transform.position.z;
            float hy = cb.Value.min.y - locRoot.transform.position.y;
            var bad = new List<string>();
            if (lx0 < -loc.W * 0.5f) bad.Add(string.Format("西侧超出 {0:0.0}m", -loc.W * 0.5f - lx0));
            if (lx1 >  loc.W * 0.5f) bad.Add(string.Format("东侧超出 {0:0.0}m", lx1 - loc.W * 0.5f));
            if (lz0 < -loc.D * 0.5f) bad.Add(string.Format("南侧超出 {0:0.0}m", -loc.D * 0.5f - lz0));
            if (lz1 >  loc.D * 0.5f) bad.Add(string.Format("北侧超出 {0:0.0}m", lz1 - loc.D * 0.5f));
            if (hy < -0.05f) bad.Add(string.Format("沉入地下 {0:0.0}m", -hy));
            log.Add(string.Format("    内容实测：X {0:0.0} ~ {1:0.0}    Z {2:0.0} ~ {3:0.0}    （房间半宽 {4:0.0} / {5:0.0}）",
                lx0, lx1, lz0, lz1, loc.W * 0.5f, loc.D * 0.5f));
            log.Add(bad.Count == 0 ? "    越界：无 ✓" : "    ★ 越界：" + string.Join("；", bad.ToArray()));
        }

        log.Add(string.Format("    触发点 {0} 个：{1}", n, string.Join("  ", loc.Triggers.ToArray())));
        if (loc.Note != "") log.Add("    " + loc.Note);
        log.Add("");
    }

    // 铺一面墙。yaw: 南=0 北=180 西=90 东=270（面板长边沿墙面）
    static void Wall(Transform parent, string side, Vector3 centerLocal, int count, float len, float h,
                     float yaw, int doorIdx, int[] winIdx, GameObject locRoot)
    {
        float step = len / count;
        float sy = h / PANEL, sx = step / PANEL;
        for (int i = 0; i < count; i++)
        {
            string file = W_WALL;
            if (i == doorIdx) file = W_DOOR;
            else if (winIdx.Contains(i)) file = W_WIN;

            string path = AssetLocator.FileNamed(file);
            if (path == null) continue;
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) continue;

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset, locRoot.scene);
            inst.name = Path.GetFileNameWithoutExtension(file) + "_" + i;

            // 同上：墙板根节点也带 旋转(270,0,0)+缩放(100)，必须靠 pivot 加 yaw、缩放在 pivot 上
            // （pivot 只能绕 Y 转，所以 pivot 的局部 X/Y/Z 就是墙的 长/高/厚）
            var pivot = new GameObject(side + "_" + inst.name);
            pivot.transform.SetParent(parent, false);
            pivot.transform.localRotation = Quaternion.Euler(0f, yaw + PanelYawFix(file), 0f);
            pivot.transform.localScale = new Vector3(sx, sy, 1f);
            inst.transform.SetParent(pivot.transform, false);
            inst.transform.localPosition = Vector3.zero;

            // 沿墙面方向排布（面板绕 yaw 后的"右"方向）
            float off = -len * 0.5f + step * (i + 0.5f);
            Vector3 along = Quaternion.Euler(0f, yaw, 0f) * new Vector3(off, 0f, 0f);
            PlaceAt(pivot, locRoot.transform.position,
                    new Vector3(centerLocal.x + along.x, 0f, centerLocal.z + along.z));
        }
    }

    // 绕序诊断：有向体积（散度定理）。
    // 闭合网格绕序正确 → 体积为正；绕序反了（里外翻转）→ 体积为负。
    // 另算一个"法线外向度"：顶点法线是否指向外面（正=正常，负=内向）。
    static string WindingInfo(GameObject go)
    {
        var mf = go.GetComponentInChildren<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return "绕序: 无网格";
        var m = mf.sharedMesh;
        var vs = m.vertices; var ts = m.triangles; var ns = m.normals;
        if (vs.Length == 0 || ts.Length < 3) return "绕序: 数据不足";

        float vol = 0f;
        for (int i = 0; i + 2 < ts.Length; i += 3)
        {
            var a = vs[ts[i]]; var b = vs[ts[i + 1]]; var c = vs[ts[i + 2]];
            vol += Vector3.Dot(a, Vector3.Cross(b, c)) / 6f;
        }
        string wind = Mathf.Abs(vol) < 1e-9f ? "开放面（体积≈0）"
                    : (vol > 0f ? "闭合·外向 ✓" : "★ 闭合但绕序翻转（里外颠倒）");

        string outward = "-";
        if (ns.Length == vs.Length && ns.Length > 0)
        {
            Vector3 c0 = Vector3.zero;
            foreach (var v in vs) c0 += v;
            c0 /= vs.Length;
            double s = 0;
            for (int i = 0; i < vs.Length; i++)
            {
                var d = vs[i] - c0;
                if (d.sqrMagnitude < 1e-12f) continue;
                s += Vector3.Dot(ns[i], d.normalized);
            }
            double r = s / vs.Length;
            outward = string.Format("法线外向度 {0:0.000}{1}", r, r < -0.05 ? "  ★ 内向（法线反了）" : "");
        }
        return string.Format("绕序: 体积 {0:0.0000}  {1}   {2}", vol, wind, outward);
    }

    // 面积加权平均法线 —— 判断模型"正脸"朝哪边（单面墙板能看出内外，双面会互相抵消）
    static string FacingInfo(GameObject go)
    {
        var mf = go.GetComponentInChildren<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return "朝向: 无网格";
        var m = mf.sharedMesh;
        var vs = m.vertices; var ts = m.triangles;
        if (vs.Length == 0 || ts.Length < 3) return "朝向: 数据不足";
        var M = mf.transform.localToWorldMatrix;
        Vector3 sum = Vector3.zero; float total = 0f;
        for (int i = 0; i + 2 < ts.Length; i += 3)
        {
            var a = M.MultiplyPoint3x4(vs[ts[i]]);
            var b = M.MultiplyPoint3x4(vs[ts[i + 1]]);
            var c = M.MultiplyPoint3x4(vs[ts[i + 2]]);
            var n = Vector3.Cross(b - a, c - a);
            float area = n.magnitude * 0.5f;
            if (area < 1e-9f) continue;
            sum += n * 0.5f;
            total += area;
        }
        if (total < 1e-9f) return "朝向: 无面积";
        var avg = sum / total;
        return string.Format("朝向: 面积加权法线 ({0:0.00},{1:0.00},{2:0.00})  法线残留 {3:0.00}  面积 {4:0.0}",
            avg.x, avg.y, avg.z, avg.magnitude, total);
    }

    // 把模型 XZ 中心对准 target、底部对准 target.y（抵消 FBX 原点差异）
    static void PlaceAt(GameObject inst, Vector3 locRootPos, Vector3 targetLocal)
    {
        var b = BoundsOf(inst);
        if (!b.HasValue) return;
        Vector3 want = locRootPos + new Vector3(targetLocal.x, targetLocal.y, targetLocal.z);
        var d = inst.transform.position;
        d.x += want.x - b.Value.center.x;
        d.z += want.z - b.Value.center.z;
        d.y += want.y - b.Value.min.y;
        inst.transform.position = d;
    }

    // ------------------------------------------------------------------ 工具
    static Bounds? BoundsOf(GameObject go)
    {
        bool any = false;
        var total = new Bounds();
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            Bounds lb;
            var mf = r.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) lb = mf.sharedMesh.bounds;
            else
            {
                var smr = r as SkinnedMeshRenderer;
                if (smr != null && smr.sharedMesh != null) lb = smr.sharedMesh.bounds;
                else continue;
            }
            var m = r.transform.localToWorldMatrix;
            var c  = m.MultiplyPoint3x4(lb.center);
            var ex = m.MultiplyVector(new Vector3(lb.extents.x, 0, 0));
            var ey = m.MultiplyVector(new Vector3(0, lb.extents.y, 0));
            var ez = m.MultiplyVector(new Vector3(0, 0, lb.extents.z));
            var wb = new Bounds(c, new Vector3(
                Mathf.Abs(ex.x) + Mathf.Abs(ey.x) + Mathf.Abs(ez.x),
                Mathf.Abs(ex.y) + Mathf.Abs(ey.y) + Mathf.Abs(ez.y),
                Mathf.Abs(ex.z) + Mathf.Abs(ey.z) + Mathf.Abs(ez.z)) * 2f);
            if (!any) { total = wb; any = true; } else total.Encapsulate(wb);
        }
        return any ? (Bounds?)total : null;
    }

    static GameObject Box(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat, float yOffset = 0f)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos + new Vector3(0f, yOffset, 0f);
        go.transform.localScale = size;
        if (mat != null) go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    static Material LoadMat(string fileName)
    {
        string p = AssetLocator.FileNamed(fileName);
        return p == null ? null : AssetDatabase.LoadAssetAtPath<Material>(p);
    }

    static void SetIcon(GameObject go)
    {
        try
        {
            var icon = EditorGUIUtility.IconContent("sv_label_1").image as Texture2D;
            if (icon != null) EditorGUIUtility.SetIconForObject(go, icon);
        }
        catch { }
    }

    // ------------------------------------------------------------------ 优化场景阴影
    // URP 默认阴影配置对这个室内 demo 很不友好：
    //   m_ShadowDistance=50 + m_ShadowCascadeCount=1 → 50m 范围共用一张 2048 图，近处锯齿明显
    //   m_SoftShadowsSupported=0 → 灯的 Soft Shadows 设置被忽略，边缘是硬的
    //   m_MSAA=1 → 无抗锯齿
    [MenuItem("Tools/干预项目/优化场景阴影")]
    public static void TuneShadows()
    {
        var log = new List<string>();
        log.Add("阴影/抗锯齿设置  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("");

        string path = null;
        foreach (var g in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
        {
            path = AssetDatabase.GUIDToAssetPath(g);
            if (!string.IsNullOrEmpty(path)) break;
        }
        if (path == null) { Debug.LogError("[SceneBuilder] 找不到 URP 资产"); return; }
        var rp = AssetDatabase.LoadAssetAtPath<Object>(path);
        log.Add("URP 资产：" + path);
        log.Add("");

        var so = new SerializedObject(rp);
        Report(so, log, "m_ShadowDistance", 30f);            // 50 → 30（室内 demo 够用，纹素密度翻倍）
        Report(so, log, "m_ShadowCascadeCount", 4);          // 1 → 4（近处单独一张图）
        Report(so, log, "m_SoftShadowsSupported", true);     // 开软阴影（否则灯的 Soft 设置被忽略）
        so.ApplyModifiedPropertiesWithoutUndo();

        // MSAA / 阴影贴图分辨率走的是 per-quality 内部数组，改顶层字段会被覆盖，
        // 必须用公开属性（用反射调，避开编译期对 URP 类型的依赖）
        SetProp(rp, log, "msaaSampleCount", 4);
        SetProp(rp, log, "mainLightShadowmapResolution", 4096);
        SetProp(rp, log, "shadowDepthBias", 0.6f);
        SetProp(rp, log, "shadowNormalBias", 1.2f);

        EditorUtility.SetDirty(rp);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        log.Add("");
        log.Add("若阴影仍有细条纹（acne）就把 m_ShadowDepthBias 再调大一点；");
        log.Add("若阴影边缘跟物体脱开（peter-panning）就把 NormalBias 调小。");
        Directory.CreateDirectory("Assets/assets/_报告");
        File.WriteAllText("Assets/assets/_报告/_阴影设置.txt", string.Join("\n", log.ToArray()));
        Debug.Log("[SceneBuilder] 阴影设置完成，报告：Assets/assets/_报告/_阴影设置.txt");
    }

    static void Report(SerializedObject so, List<string> log, string prop, object want)
    {
        var p = so.FindProperty(prop);
        if (p == null) { log.Add(string.Format("  ★ 找不到属性 {0}", prop)); return; }
        string old = p.propertyType == SerializedPropertyType.Integer ? p.intValue.ToString()
                   : p.propertyType == SerializedPropertyType.Boolean ? p.boolValue.ToString()
                   : p.floatValue.ToString("0.###");
        if (p.propertyType == SerializedPropertyType.Integer) p.intValue = System.Convert.ToInt32(want);
        else if (p.propertyType == SerializedPropertyType.Boolean) p.boolValue = System.Convert.ToBoolean(want);
        else p.floatValue = System.Convert.ToSingle(want);
        log.Add(string.Format("  {0,-34} {1}  →  {2}", prop, old, want));
    }

    // 通过公开属性改（URP 的 per-quality 项只能这么改），反射避免编译耦合
    static void SetProp(object obj, List<string> log, string name, object want)
    {
        var pi = obj.GetType().GetProperty(name);
        if (pi == null || !pi.CanWrite) { log.Add(string.Format("  ★ 无属性 {0}（版本不同，手动在 Inspector 改）", name)); return; }
        object old = pi.GetValue(obj);
        try
        {
            pi.SetValue(obj, System.Convert.ChangeType(want, pi.PropertyType));
            log.Add(string.Format("  {0,-34} {1}  →  {2}", name, old, want));
        }
        catch (System.Exception e)
        {
            log.Add(string.Format("  ★ {0} 设置失败: {1}", name, e.Message));
        }
    }

    // ------------------------------------------------------------------ 列出场景模型尺寸
    [MenuItem("Tools/干预项目/列出场景模型尺寸")]
    public static void DumpModelSizes()
    {
        string dir = "Assets/assets/01_场景_Scene";
        if (!Directory.Exists(dir)) { Debug.LogError("[SceneBuilder] 找不到 " + dir); return; }

        var log = new List<string>();
        log.Add("场景模型尺寸清单（单位：米）");
        log.Add("");

        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var files = Directory.GetFiles(dir, "*.fbx", SearchOption.AllDirectories).OrderBy(x => x).ToArray();
        foreach (var f in files)
        {
            string path = f.Replace('\\', '/');
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) { log.Add("★ 加载失败 " + path); continue; }

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
            var mb = BoundsOf(inst);
            string s = "★ 无网格";
            if (mb.HasValue)
                s = string.Format("{0,7:0.00} × {1,6:0.00} × {2,7:0.00}   Y:{3,6:0.00}~{4,6:0.00}",
                    mb.Value.size.x, mb.Value.size.y, mb.Value.size.z, mb.Value.min.y, mb.Value.max.y);
            log.Add(string.Format("{0,-30} {1}", Path.GetFileName(path), s));
            log.Add("      " + RoofInfo(inst));
            Object.DestroyImmediate(inst);
        }

        Directory.CreateDirectory("Assets/assets/_报告");
        File.WriteAllText("Assets/assets/_报告/_场景模型尺寸.txt", string.Join("\n", log.ToArray()));
        Debug.Log("[SceneBuilder] 尺寸清单已写 Assets/assets/_报告/_场景模型尺寸.txt");
    }

    // 探测模型顶部是否封顶（封顶的话从上方改场景会被挡住）
    static string RoofInfo(GameObject go)
    {
        var mf = go.GetComponentInChildren<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return "顶面：无网格";
        var vs = mf.sharedMesh.vertices;
        if (vs.Length < 8) return "顶面：顶点太少";

        float minY = float.MaxValue, maxY = float.MinValue;
        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var v in vs)
        {
            if (v.y < minY) minY = v.y; if (v.y > maxY) maxY = v.y;
            if (v.x < minX) minX = v.x; if (v.x > maxX) maxX = v.x;
            if (v.z < minZ) minZ = v.z; if (v.z > maxZ) maxZ = v.z;
        }
        float h = maxY - minY;
        if (h < 0.01f) return "顶面：模型没高度";
        float thr = maxY - h * 0.04f;
        int topN = 0;
        float tx0 = float.MaxValue, tx1 = float.MinValue, tz0 = float.MaxValue, tz1 = float.MinValue;
        foreach (var v in vs)
            if (v.y >= thr)
            {
                topN++;
                if (v.x < tx0) tx0 = v.x; if (v.x > tx1) tx1 = v.x;
                if (v.z < tz0) tz0 = v.z; if (v.z > tz1) tz1 = v.z;
            }
        if (topN == 0) return "顶面：★ 没有（上盖敞开 ✓）";
        float covX = (maxX - minX) > 0.001f ? (tx1 - tx0) / (maxX - minX) : 0f;
        float covZ = (maxZ - minZ) > 0.001f ? (tz1 - tz0) / (maxZ - minZ) : 0f;
        string verdict = (covX > 0.85f && covZ > 0.85f) ? "★ 有天花板（封顶，删不掉）" : "上盖敞开 ✓";
        return string.Format("顶面：顶点 {0}/{1}  覆盖 X {2:P0} Z {3:P0}  → {4}", topN, vs.Length, covX, covZ, verdict);
    }

    // ------------------------------------------------------------------ 列出变换异常
    // 查出根节点旋转/缩放异常、以及子节点负缩放的模型（负缩放 = 法线翻转、只看到背面）。
    [MenuItem("Tools/干预项目/列出变换异常")]
    public static void DumpTransforms()
    {
        string dir = "Assets/assets/01_场景_Scene";
        var log = new List<string>();
        log.Add("模型变换诊断（根旋转 / 缩放 / 负缩放）");
        log.Add("");

        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var files = Directory.GetFiles(dir, "*.fbx", SearchOption.AllDirectories).OrderBy(x => x).ToArray();
        foreach (var f in files)
        {
            string path = f.Replace('\\', '/');
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) continue;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);

            var r = inst.transform.localRotation.eulerAngles;
            var s = inst.transform.localScale;

            int negTotal = 0, uniqTotal = 0;
            var badScales = new List<string>();
            foreach (var t in inst.GetComponentsInChildren<Transform>(true))
            {
                uniqTotal++;
                var ls = t.localScale;
                if (ls.x < 0f || ls.y < 0f || ls.z < 0f)
                {
                    negTotal++;
                    if (badScales.Count < 3)
                        badScales.Add(t.name + "(" + ls.x.ToString("0.##") + "," + ls.y.ToString("0.##") + "," + ls.z.ToString("0.##") + ")");
                }
            }
            bool rootRot = Mathf.Abs(Mathf.DeltaAngle(r.x, 0f)) > 0.5f || Mathf.Abs(Mathf.DeltaAngle(r.y, 0f)) > 0.5f || Mathf.Abs(Mathf.DeltaAngle(r.z, 0f)) > 0.5f;
            bool rootScale = Mathf.Abs(s.x - 1f) > 0.001f || Mathf.Abs(s.y - 1f) > 0.001f || Mathf.Abs(s.z - 1f) > 0.001f;
            if (!rootRot && !rootScale && negTotal == 0) { Object.DestroyImmediate(inst); continue; }

            log.Add(string.Format("{0,-30} 根旋转({1:0.#},{2:0.#},{3:0.#})  根缩放({4:0.###},{5:0.###},{6:0.###})  节点{7}  负缩放{8}",
                Path.GetFileName(path), r.x, r.y, r.z, s.x, s.y, s.z, uniqTotal, negTotal));
            if (badScales.Count > 0) log.Add("       ! " + string.Join("  ", badScales.ToArray()));
            log.Add("       " + FacingInfo(inst));
            log.Add("       " + WindingInfo(inst));
            Object.DestroyImmediate(inst);
        }

        Directory.CreateDirectory("Assets/assets/_报告");
        File.WriteAllText("Assets/assets/_报告/_变换诊断.txt", string.Join("\n", log.ToArray()));
        Debug.Log("[SceneBuilder] 变换诊断已写 Assets/assets/_报告/_变换诊断.txt");
    }

    // ------------------------------------------------------------------ 修正反转模型
    // 本地解析 FBX 二进制（额外文件/工具脚本/fbx_check.py）查出的缺陷件。
    // 两类问题：
    //   A. 绕序翻转（有向体积为负）——面片里外颠倒，背面剔除后从外面看是空的
    //   B. 绕序正常但法线向内  ——面看得到，但光照从内侧算，显黑/颜色不对
    // 修法：生成修正后的 mesh 资产（反转绕序 + 翻转法线），再换掉场景里的 MeshFilter。
    // 不动 .fbx / .meta。
    static readonly string[] BAD_WINDING = {          // A 类：绕序翻转
        "double_bed.fbx", "single_bed.fbx", "simple_square_table_A.fbx",
        "simple_footstool.fbx", "simple_rect_table_A.fbx",
    };
    static readonly string[] BAD_NORMALS = {          // B 类：仅法线内向
        "lamp.fbx", "simple_rect_table_B.fbx", "simple_square_table_B.fbx",
        // "Interiors_A.fbx"（楼梯间）也是法线内向，但它一个文件里有 375 个子网格，
        // 会生成 375 个副本资产；目前没任何场景用它，所以先不开。要用时把它加回来。
    };

    [MenuItem("Tools/干预项目/修正反转模型")]
    public static void FixInvertedMeshes()
    {
        var log = new List<string>();
        log.Add("反转模型修正报告  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("");

        // 下面会逐个打开场景，先让用户保存未存的改动，避免丢数据
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            log.Add("用户取消（未保存当前场景），已中止");
            Debug.Log("[SceneBuilder] 修正反转模型：用户取消");
            return;
        }

        string fixDir = FIX_DIR;
        Directory.CreateDirectory(fixDir);
        AssetDatabase.Refresh();

        var map = new Dictionary<string, Mesh>();   // 原 mesh 名 -> 修正后 mesh
        var all = new List<string>();
        all.AddRange(BAD_WINDING); all.AddRange(BAD_NORMALS);

        foreach (var file in all)
        {
            string path = AssetLocator.FileNamed(file);
            if (path == null) { log.Add("★ 找不到 " + file); continue; }
            var assets = AssetDatabase.LoadAllAssetsAtPath(path);
            var meshes = assets.OfType<Mesh>().ToArray();
            if (meshes.Length == 0) { log.Add("★ " + file + " 没有 Mesh"); continue; }

            bool fixWind = BAD_WINDING.Contains(file);
            string stem = file.Replace(".fbx", "");
            for (int mi = 0; mi < meshes.Length; mi++)
            {
                var src = meshes[mi];
                string nm = meshes.Length == 1 ? stem : stem + "_" + mi;
                var m = FixMesh(src, fixWind, nm);
                string outPath = fixDir + "/" + m.name + ".asset";
                AssetDatabase.DeleteAsset(outPath);
                AssetDatabase.CreateAsset(m, outPath);
                map[src.name] = m;
            }

            log.Add(string.Format("{0,-28} {1}  （Mesh ×{2}）  →  {3}/…", file,
                fixWind ? "绕序翻转+法线内向" : "仅法线内向", meshes.Length, fixDir));
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        log.Add("");

        // 换掉场景里的 MeshFilter
        foreach (var scPath in new[] { OUT_SCENE, "Assets/Scenes/MainMenu.unity", "Assets/Scenes/Test_徐夏_动画.unity" })
        {
            if (!File.Exists(scPath)) continue;
            var scene = EditorSceneManager.OpenScene(scPath, OpenSceneMode.Single);
            int n = 0;
            foreach (var mf in Object.FindObjectsOfType<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Mesh fixedMesh;
                string key = mf.sharedMesh.name.Replace("_fixed", "");
                if (!map.TryGetValue(key, out fixedMesh)) continue;
                mf.sharedMesh = fixedMesh;
                EditorUtility.SetDirty(mf);
                n++;
            }
            if (n > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            log.Add("场景 " + scPath + "：替换 " + n + " 个 MeshFilter");
        }

        log.Add("");
        log.Add("说明：修正后是独立 mesh 资产，与 FBX 脱钩；重导 FBX 不会影响它。");
        Directory.CreateDirectory("Assets/assets/_报告");
        File.WriteAllText("Assets/assets/_报告/_反转模型修正.txt", string.Join("\n", log.ToArray()));
        Debug.Log("[SceneBuilder] 修正完成，报告：Assets/assets/_报告/_反转模型修正.txt");
    }

    static Mesh FixMesh(Mesh src, bool fixWinding, string newName)
    {
        var m = Object.Instantiate(src);
        m.name = newName + "_fixed";
        if (fixWinding)
        {
            for (int s = 0; s < m.subMeshCount; s++)
            {
                var t = m.GetTriangles(s);
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    int tmp = t[i]; t[i] = t[i + 2]; t[i + 2] = tmp;
                }
                m.SetTriangles(t, s);
            }
        }

        // 两类缺陷都要把法线转过来
        var ns = m.normals;
        if (ns != null && ns.Length > 0)
        {
            for (int i = 0; i < ns.Length; i++) ns[i] = -ns[i];
            m.normals = ns;
        }
        if (m.tangents != null && m.tangents.Length > 0) m.RecalculateTangents();
        m.RecalculateBounds();
        return m;
    }

    // ------------------------------------------------------------------ 回滚反转模型修正
    // 我的判据（有向体积 + 法线外向度）后来被证实不可靠：
    //   · 体积数值在部分文件里荒谬（5967、7e6）→ 三角化还原本身有问题，符号也不可信
    //   · 外向度是连续分布（-0.43 ~ +0.70），拿阈值卡是拍脑袋，会误判
    // 所以提供一个回滚：把场景里的 _fixed 网格全部换回 FBX 原始网格。
    [MenuItem("Tools/干预项目/回滚反转模型修正")]
    public static void RevertFixedMeshes()
    {
        var log = new List<string>();
        log.Add("回滚反转模型修正  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("");

        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        foreach (var scPath in new[] { OUT_SCENE, "Assets/Scenes/MainMenu.unity", "Assets/Scenes/Test_徐夏_动画.unity" })
        {
            if (!File.Exists(scPath)) continue;
            var scene = EditorSceneManager.OpenScene(scPath, OpenSceneMode.Single);
            int n = 0, miss = 0;
            foreach (var mf in Object.FindObjectsOfType<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                string nm = mf.sharedMesh.name;
                if (!nm.EndsWith("_fixed")) continue;

                string stem = nm.Substring(0, nm.Length - "_fixed".Length);
                // "Interiors_A_0" -> "Interiors_A"
                var mm = System.Text.RegularExpressions.Regex.Match(stem, @"^(.*)_\d+$");
                if (mm.Success) stem = mm.Groups[1].Value;

                string fbx = AssetLocator.FileNamed(stem + ".fbx");
                var orig = fbx == null ? null
                         : AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Mesh>().FirstOrDefault(m => m.name == stem);
                if (orig == null) { miss++; continue; }
                mf.sharedMesh = orig;
                EditorUtility.SetDirty(mf);
                n++;
            }
            if (n > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            log.Add("场景 " + scPath + "：还原 " + n + " 个" + (miss > 0 ? "，★ 找不到原网格 " + miss + " 个" : ""));
        }

        // 删掉那批未经证实的修正网格
        int del = 0;
        if (Directory.Exists(FIX_DIR))
        {
            foreach (var f in Directory.GetFiles(FIX_DIR, "*.asset"))
            {
                AssetDatabase.DeleteAsset(f.Replace('\\', '/'));
                del++;
            }
            AssetDatabase.Refresh();
            if (Directory.GetFileSystemEntries(FIX_DIR).Length == 0) AssetDatabase.DeleteAsset(FIX_DIR);
        }
        log.Add("已删除修正网格资产：" + del + " 个");
        log.Add("");
        log.Add("回滚后场景引用的是 FBX 原始网格，与未修改时一致。");

        Directory.CreateDirectory("Assets/assets/_报告");
        File.WriteAllText("Assets/assets/_报告/_回滚反转模型修正.txt", string.Join("\n", log.ToArray()));
        Debug.Log("[SceneBuilder] 回滚完成，报告：Assets/assets/_报告/_回滚反转模型修正.txt");
    }

    // ------------------------------------------------------------------ 渲染场景总览
    [MenuItem("Tools/干预项目/渲染场景总览")]
    public static void RenderOverview()
    {
        if (!File.Exists(OUT_SCENE)) { Debug.LogError("[SceneBuilder] 先跑『搭建游戏场景』"); return; }
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene(OUT_SCENE, OpenSceneMode.Single);

        string outDir = "Assets/assets/_报告/预览/场景";
        Directory.CreateDirectory(outDir.Replace('/', Path.DirectorySeparatorChar));

        var camGO = new GameObject("ov_cam");
        var cam = camGO.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.30f, 0.33f, 0.38f);
        cam.fieldOfView = 50f;
        cam.nearClipPlane = 0.1f; cam.farClipPlane = 800f;
        var rt = new RenderTexture(1100, 700, 24);
        cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;   // 预热 shader

        var locsRoot = GameObject.Find("Locations");
        if (locsRoot == null) { Debug.LogError("[SceneBuilder] 场景里没有 Locations"); return; }

        foreach (Transform loc in locsRoot.transform)
        {
            var b = BoundsOf(loc.gameObject);
            if (!b.HasValue) continue;
            var bb = b.Value;
            float span = Mathf.Max(bb.size.x, bb.size.z, 8f);
            Vector3 dir = new Vector3(-1f, 1.05f, -1f).normalized;
            cam.transform.position = bb.center + dir * span * 0.95f;
            cam.transform.LookAt(new Vector3(bb.center.x, bb.center.y * 0.5f, bb.center.z), Vector3.up);

            cam.targetTexture = rt; cam.Render(); cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1100, 700, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1100, 700), 0, 0); tex.Apply();
            RenderTexture.active = null; cam.targetTexture = null;
            File.WriteAllBytes(outDir + "/" + loc.name + ".png", tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
        Object.DestroyImmediate(rt);
        Debug.Log("[SceneBuilder] 场景预览已写 " + outDir);
    }
}

// 工程根存在 Assets/_scene_trigger.txt 时，编辑器下次刷新/重编译后自动跑一次搭建。
// 工程根存在 Assets/_fixmesh_trigger.txt 时，同理自动跑一次模型修正。
[InitializeOnLoad]
public static class SceneBuilderTrigger
{
    const string Trigger     = "Assets/_scene_trigger.txt";
    const string FixTrigger  = "Assets/_fixmesh_trigger.txt";
    const string RevertTrigger = "Assets/_revertmesh_trigger.txt";
    const string ErrFile = "../额外文件/错误_场景搭建.txt";

    static SceneBuilderTrigger()
    {
        if (File.Exists(RevertTrigger))
        {
            EditorApplication.delayCall += () =>
            {
                if (File.Exists(RevertTrigger)) File.Delete(RevertTrigger);
                try { SceneBuilder.RevertFixedMeshes(); Debug.Log("[SceneBuilder] 自动回滚完成"); }
                catch (System.Exception e)
                {
                    Directory.CreateDirectory("../额外文件");
                    File.WriteAllText("../额外文件/错误_回滚反转模型.txt", e.ToString());
                    Debug.LogError("[SceneBuilder] 自动回滚失败: " + e);
                }
            };
            return;
        }

        if (File.Exists(FixTrigger))
        {
            EditorApplication.delayCall += () =>
            {
                if (File.Exists(FixTrigger)) File.Delete(FixTrigger);
                try
                {
                    SceneBuilder.FixInvertedMeshes();
                    Debug.Log("[SceneBuilder] 自动修正反转模型完成");
                }
                catch (System.Exception e)
                {
                    Directory.CreateDirectory("../额外文件");
                    File.WriteAllText("../额外文件/错误_修正反转模型.txt", e.ToString());
                    Debug.LogError("[SceneBuilder] 自动修正失败: " + e);
                }
            };
            return;
        }

        if (!File.Exists(Trigger)) return;
        // ★ 不能在排队前就删触发器：域重载会把排队的动作吃掉，触发器却已经消失。
        //    改成动作真正开始时再删。
        EditorApplication.delayCall += () =>
        {
            try
            {
                if (File.Exists(Trigger)) File.Delete(Trigger);
                SceneBuilder.Run();
                if (File.Exists(ErrFile)) File.Delete(ErrFile);
                Debug.Log("[SceneBuilder] 自动搭建完成");
            }
            catch (System.Exception e)
            {
                Directory.CreateDirectory("../额外文件");
                File.WriteAllText(ErrFile, e.ToString());
                Debug.LogError("[SceneBuilder] 自动搭建失败: " + e);
            }
        };
    }
}
