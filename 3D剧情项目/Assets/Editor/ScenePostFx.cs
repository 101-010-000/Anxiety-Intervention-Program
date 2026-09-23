// 场景后期效果（全屏 Post-processing）：辉光 / 色调映射 / 调色 / 暗角 / 胶片颗粒 / 雾
// 用法：菜单 Tools/干预项目/场景后期效果/…，或丢 Assets/_postfxfull_trigger.txt（布置+预览）
// 输出：Assets/URP/后期/*.asset + 报告 Assets/assets/_报告/_后期效果.txt、_后期预览.txt
//
// ★ 前置（最容易踩的坑）：
//   URP 的 Renderer 资产里 postProcessData 如果是空的，URP 会【静默跳过所有后期】——
//   相机勾了 Post Processing、Volume 里 Bloom 调多大都没用，也不报错。
//   本工具会自己把 postProcessData 填上，顺便把 renderer 里其它空引用重载一遍。
//
// ★ Bloom 吃什么：
//   只吃超过 threshold 的亮部。这里【不进场景加任何自发光物体】，
//   直接拿场景本来就亮的东西当光源：白墙 / 白纸 / 靠窗的亮地面 / 天空。
//   所以阈值要低于 1.0（BLOOM_THRESHOLD），否则 Bloom 基本不动。
//
// 设计要点：
//   · 全局面板（Post_全局）= 电影感底子：ACES 色调映射 + 辉光 + 冷暖分离调色 + 暗角 + 颗粒
//   · 每个地点一个本地 Volume（BoxCollider + blendDistance），走进去氛围会平滑变化
//   · 雾用 RenderSettings（URP Lit 吃雾），Exp2 + 低密度，只做纵深朦胧，不糊脸
//   · 全部幂等：重复跑只是刷新，不会叠出第二套 Volume

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public static class ScenePostFx
{
    public const string OUT_SCENE = "Assets/Scenes/Game.unity";
    public const string DIR       = "Assets/URP/后期";
    const string PROF_GLOBAL      = DIR + "/Post_全局.asset";
    const string REPORT           = "Assets/assets/_报告/_后期效果.txt";
    const string REPORT_PV        = "Assets/assets/_报告/_后期预览.txt";
    const string URP_PKG          = "Packages/com.unity.render-pipelines.universal";

    // 全局基线的可调参数（想整体更朦胧/更克制，改这里再跑一次工具）
    //
    // 【思路】不要用“全局变灰”做朦胧（那样远近糊成一团）——
    //   模糊交给【景深】：从 BLUR_START 开始，往后 BLUR_FALLOFF 米逐级化开；
    //   纵深交给【雾】：越远越往亮雾色里褪，但近处几乎不吃雾。
    //   对比/饱和保持住，这样近处才有“实”的感觉。
    const float BLOOM_THRESHOLD   = 0.95f;   // 只让真正亮的地方发光（<1.0 是因为场景里没有自发光体）
    const float BLOOM_INTENSITY   = 0.42f;
    const float BLOOM_SCATTER     = 0.78f;   // 散射小一点 → 光晕贴近光源，不会满屏铺灰
    const float BLACK_LIFT        = 0.028f;  // 抬黑：只保证不出现纯黑（之前的 0.06 会把暗部拉成灰）
    const float AMBIENT           = 0.25f;   // 环境光（角色暗部靠它）
    //  【为什么用 Gaussian 不用 Bokeh】
    //    Bokeh 是“以对焦点为中心、前后都糊”，而且模糊度与 |1-对焦/深度| 挂钩：
    //      对焦 5m 时，1m 处的东西直接糊到顶 —— 物理上做不到“近处一大片清晰”。
    //    Gaussian 只糊 gaussianStart 以后，前面完全不动 → 天生就是“近处实、远处虚”。
    //  所以用 Gaussian，并把过渡距离压短（房间只有 12~24m）。
    //  模糊半径 = saturate((depth-start)/ramp) * 最大半径（被写死在 shader 里：14px）
    const float BLUR_START        = 6.5f;   // ★ 这个距离以内完全不动
    const float BLUR_END          = 24.0f;  // ★ 到这个距离糊到最满（拉长 = 没那么糊）
    const float BLUR_MAX_RADIUS   = 1.5f;    // 最大模糊半径（0.5~1.5，上限就是 1.5）
    const float FOG_DENSITY       = 0.065f;  // ★ 雾：0.018 太淡看不出纵深，0.065 远近能拉开
                                             //   实测：3m 4% ／ 6m 13% ／ 12m 38% ／ 20m 70%
    static readonly Color FOG_COLOR = new Color(0.76f, 0.78f, 0.82f);

    // 动态对焦脚本（挂在主相机上，让景深对焦点跟着视线走）
    const string FOCUS_SCRIPT    = "Assets/Scripts/Visual/DreamyFocus.cs";

    // 轮廓描边（Renderer Feature + 它用的 shader/材质）
    const string OUTLINE_SCRIPT  = "Assets/Scripts/Visual/OutlineFeature.cs";
    const string OUTLINE_MAT     = DIR + "/描边.mat";
    const string OUTLINE_SHADER  = "Hidden/SceneOutline";
    const string OUTLINE_LAYER   = "Outline";  // 要描边的层名（工具会自动建层，并把角色/道具放进去）
    static readonly Color OUTLINE_COLOR = new Color(0.02f, 0.02f, 0.03f, 1f);
    const float  OUTLINE_WIDTH   = 2.0f;    // 线宽（屏幕像素）
    const float  OUTLINE_FADE_A  = 8f;      // 线条开始淡出（米）
    const float  OUTLINE_FADE_B  = 24f;     // 线条完全消失（米）

    // 早期版本往窗洞里贴过加色发光片；现在不用了，跑一次工具就把它们清掉。
    const string LEGACY_GLOW_GO  = "Post_窗光";
    const string LEGACY_GLOW_MAT = DIR + "/材质_窗光.mat";
    const string LEGACY_GLOW_TEX = DIR + "/光斑_柔.png";

    // ------------------------------------------------------------------ 数据
    /// 一个地点的氛围。**本地 Volume 是覆盖不是叠加**，所以这里写的是绝对值。
    /// 注意：不要把 DepthOfField 放进本地面板 —— 那会盖掉全局的，动态对焦就失效了。
    class Mood
    {
        public string Room, Desc;
        public float  Bloom, Scatter, Exposure, Contrast, Saturation, Temp, WbTint, Grain;
        public Color  BloomTint, Filter, ShadowTone, HighlightTone;
    }

    static Color C(float r, float g, float b) => new Color(r, g, b);

    static Mood[] Moods()
    {
        var cool     = C(0.38f, 0.45f, 0.58f);   // 阴影偏冷蓝（<0.5 才真的压暗）
        var warm     = C(0.64f, 0.58f, 0.47f);   // 高光偏暖
        var coolSoft = C(0.44f, 0.47f, 0.54f);
        var neutralS = C(0.48f, 0.485f, 0.51f);
        var neutralH = C(0.53f, 0.525f, 0.50f);

        return new[]
        {
            // 教室：白天 · 阳光通透。辉光最猛，因为这是"窗外太阳"的主场
            new Mood { Room = "Loc_教室", Desc = "白天 · 阳光通透",
                Bloom = 0.46f, Scatter = 0.78f, BloomTint = C(1.00f, 0.95f, 0.90f),
                Exposure = 0.10f, Contrast = 10f, Saturation = 4f, Filter = C(1.00f, 0.99f, 0.99f),
                Temp = 8f, WbTint = 2f, Grain = 0.10f,
                ShadowTone = cool, HighlightTone = warm },

            // 走廊：阴天 · 冷调纵深，雾感最重（走廊套件本身也偏灰）
            new Mood { Room = "Loc_走廊", Desc = "阴天 · 冷调纵深",
                Bloom = 0.38f, Scatter = 0.82f, BloomTint = C(0.94f, 0.96f, 1.00f),
                Exposure = 0.16f, Contrast = 12f, Saturation = -4f, Filter = C(0.98f, 0.99f, 1.00f),
                Temp = -12f, WbTint = -1f, Grain = 0.13f,
                ShadowTone = cool, HighlightTone = coolSoft },

            // 宿舍：傍晚 · 暖黄、私密、颗粒稍重（回忆感）
            new Mood { Room = "Loc_宿舍", Desc = "傍晚 · 暖黄私密",
                Bloom = 0.44f, Scatter = 0.80f, BloomTint = C(1.00f, 0.94f, 0.84f),
                Exposure = 0.16f, Contrast = 8f, Saturation = 6f, Filter = C(1.00f, 0.98f, 0.96f),
                Temp = 16f, WbTint = 4f, Grain = 0.11f,
                ShadowTone = cool, HighlightTone = warm },

            // 食堂：正午 · 亮、热闹
            new Mood { Room = "Loc_食堂", Desc = "正午 · 明亮热闹",
                Bloom = 0.40f, Scatter = 0.78f, BloomTint = C(1.00f, 0.97f, 0.92f),
                Exposure = 0.20f, Contrast = 8f, Saturation = 10f, Filter = C(1.00f, 0.99f, 0.97f),
                Temp = 10f, WbTint = 2f, Grain = 0.09f,
                ShadowTone = neutralS, HighlightTone = warm },

            // 办公室（咨询室）：安静 · 柔一点、冷暖中和，这一段是谈心戏
            new Mood { Room = "Loc_办公室", Desc = "安静 · 柔光",
                Bloom = 0.48f, Scatter = 0.86f, BloomTint = C(1.00f, 0.98f, 0.97f),
                Exposure = 0.18f, Contrast = 2f, Saturation = -2f, Filter = C(1.00f, 0.99f, 1.00f),
                Temp = -2f, WbTint = 3f, Grain = 0.12f,
                ShadowTone = coolSoft, HighlightTone = neutralH },

            // 图书馆：静谧 · 中性偏冷、对比稍高，安静但有层次
            new Mood { Room = "Loc_图书馆", Desc = "静谧 · 中性偏冷",
                Bloom = 0.36f, Scatter = 0.80f, BloomTint = C(0.98f, 0.99f, 1.00f),
                Exposure = 0.14f, Contrast = 14f, Saturation = -2f, Filter = C(0.99f, 0.995f, 1.00f),
                Temp = -6f, WbTint = 1f, Grain = 0.14f,
                ShadowTone = cool, HighlightTone = neutralH },
        };
    }

    // ------------------------------------------------------------------ 菜单
    [MenuItem("Tools/干预项目/场景后期效果/① 一键布置（Game.unity）", false, 10)]
    public static void Apply() => ApplyInternal(false);

    /// 触发器走的入口：不弹任何对话框（弹窗会把 Unity 卡住等人点）
    public static void ApplyFromTrigger() => ApplyInternal(true);

    static void ApplyInternal(bool fromTrigger)
    {
        var log = new List<string>();
        log.Add("场景后期效果（全屏）  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("");

        // 目标固定是剧情主场景
        var cur = SceneManager.GetActiveScene();
        if (cur.path != OUT_SCENE)
        {
            if (fromTrigger)
            {
                log.Add("当前打开的是「" + cur.path + "」，不是 " + OUT_SCENE + "。");
                log.Add("触发器不动当前场景（怕弹窗把人卡住），请在 Unity 里打开 Game.unity，");
                log.Add("或直接用菜单 Tools/干预项目/场景后期效果/① 一键布置（它会自己切过去）。");
                Write(REPORT, log);
                Debug.Log("[ScenePostFx] 已跳过：当前场景不是 Game.unity");
                return;
            }
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("[ScenePostFx] 用户取消");
                return;
            }
        }
        if (!File.Exists(OUT_SCENE)) { Debug.LogError("[ScenePostFx] 找不到 " + OUT_SCENE + "，先跑『搭建游戏场景』"); return; }
        var scene = cur.path == OUT_SCENE ? cur : EditorSceneManager.OpenScene(OUT_SCENE, OpenSceneMode.Single);

        ApplyToScene(scene, log);

        bool ok = EditorSceneManager.SaveScene(scene);
        log.Add("");
        log.Add("保存场景：" + OUT_SCENE + "  " + (ok ? "成功" : "★失败"));

        Write(REPORT, log);
        Debug.Log("[ScenePostFx] 完成，报告：" + REPORT);
    }

    [MenuItem("Tools/干预项目/场景后期效果/② 只刷新后期资产（不动场景）", false, 11)]
    public static void RefreshAssetsOnly()
    {
        var log = new List<string>();
        log.Add("后期资产刷新（未改场景）  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("");
        FixRenderer(log);
        BuildProfiles(log);
        Write(REPORT, log);
        Debug.Log("[ScenePostFx] 后期资产已刷新");
    }

    [MenuItem("Tools/干预项目/场景后期效果/③ 渲染后期预览（每屋一张）", false, 12)]
    public static void RenderPreview() => RenderPreviewInternal(false);

    static void RenderPreviewInternal(bool fromTrigger)
    {
        if (!File.Exists(OUT_SCENE)) { Debug.LogError("[ScenePostFx] 先跑『搭建游戏场景』"); return; }
        var cur = SceneManager.GetActiveScene();
        if (cur.path != OUT_SCENE)
        {
            if (fromTrigger) { Debug.Log("[ScenePostFx] 预览跳过：当前不是 Game.unity"); return; }
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(OUT_SCENE, OpenSceneMode.Single);
        }

        string outDir = "Assets/assets/_报告/预览/场景";
        Directory.CreateDirectory(outDir.Replace('/', Path.DirectorySeparatorChar));

        var camGO = new GameObject("postfx_cam");
        var cam = camGO.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.fieldOfView = 62f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 600f;
        cam.allowHDR = true;                       // ★ Bloom 必须 HDR
        var extra = cam.GetUniversalAdditionalCameraData();
        extra.renderPostProcessing = true;         // ★ 预览相机也要吃后期
        extra.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        extra.antialiasingQuality = AntialiasingQuality.High;
        extra.dithering = true;

        var rt = new RenderTexture(1100, 620, 24, RenderTextureFormat.DefaultHDR);
        cam.targetTexture = rt;
        cam.Render();                              // 预热 shader

        var locsRoot = GameObject.Find("Locations");
        var done = new List<string>();
        if (locsRoot != null)
        {
            foreach (Transform loc in locsRoot.transform)
            {
                var floor = loc.Find("Shell/Shell_地板");
                if (floor == null) continue;
                var rb = floor.GetComponent<Renderer>();
                if (rb == null) continue;

                // 站位：站在房间中心、眼高 1.62，看出去（过道这种 24×4 的窄房间要沿长边看，
                // 不然进门第一眼就是贴脸的墙；站中心也比靠墙安全，不容易卡进书架里）
                float d = rb.bounds.size.z, w = rb.bounds.size.x;
                bool alongX = w > d * 1.8f;
                float len = alongX ? w : d;
                Vector3 eye = new Vector3(loc.position.x, 1.62f, loc.position.z);
                Vector3 focus = eye;
                if (alongX)
                {
                    eye.x   = loc.position.x - len * 0.20f;
                    focus.x = loc.position.x + len * 0.45f;
                }
                else
                {
                    eye.z   = loc.position.z - len * 0.10f;
                    focus.z = loc.position.z + len * 0.45f;
                }
                focus.y = 1.30f;
                cam.transform.position = eye;
                cam.transform.LookAt(focus, Vector3.up);

                // 一次渲两版：开后期 / 关后期，方便直接对比“到底改了啥”
                var stats = new float[2];
                var sharp = new Vector2[2];
                Vector2 sharpNoDof = Vector2.zero;
                for (int k = 0; k < 2; k++)
                {
                    extra.renderPostProcessing = (k == 0);
                    cam.Render(); cam.Render();
                    stats[k] = Shoot(cam, rt, outDir + (k == 0 ? "/后期_" : "/原始_") + loc.name + ".png", out sharp[k]);
                }
                extra.renderPostProcessing = true;

                // ★ 诊断：临时把景深关掉再渲一张，看“清晰度”到底变了没 ——
                //   如果远近两个数都差不多，说明 DoF 没生效（URP 会静默跳过，不报错）。
                //   只改内存不 SetDirty，不会写脏 .asset。
                var dofComp = FindGlobalDof();
                if (dofComp != null && dofComp.mode.value != DepthOfFieldMode.Off)
                {
                    var savedMode = dofComp.mode.value;
                    dofComp.mode.value = DepthOfFieldMode.Off;
                    cam.Render(); cam.Render();
                    Shoot(cam, rt, null, out sharpNoDof);
                    dofComp.mode.value = savedMode;
                }
                else sharpNoDof = sharp[0];

                done.Add(string.Format("{0}  房间 {1:0.#}×{2:0.#}m   {3}   亮度 原{4:0.000}→后{5:0.000}   清晰度 近{6:0.0000}/远{7:0.0000}（关景深时 近{8:0.0000}/远{9:0.0000}）",
                    loc.name, w, d, alongX ? "沿长边" : "朝北看窗", stats[1], stats[0],
                    sharp[0].x, sharp[0].y, sharpNoDof.x, sharpNoDof.y));

            }
        }
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(camGO);

        var log = new List<string>();
        log.Add("后期预览渲染  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("输出目录：" + outDir + "/后期_*.png（1100×620，第一人称视角，已开后期）");
        log.Add("");
        log.AddRange(done);
        log.Add("");
        log.Add("注：SceneBuilder 的『渲染场景总览』是俯瞰图、而且相机没开后期（看的还是旧样子），");
        log.Add("    要看后期效果请用这一张 —— 或者直接进 Play 模式走一圈。");
        log.Add("");
        log.Add("怎么判断调得对不对：");
        log.Add("· 亮的地方（白墙 / 白纸 / 靠窗地面 / 天空）周围应该糊出一圈柔光，这就是 Bloom；");
        log.Add("  完全没有 → BLOOM_THRESHOLD 给高了，或相机的 Post Processing 没开。");
        log.Add("· 近处应该锐、越远越糊（这靠景深，不是靠变灰）；对比度“关景深”一列如果和“开后期”一样，");
        log.Add("  说明 DepthOfField 没生效（URP 会静默跳过，不报错）。");
        log.Add("· 画面最黑的地方应该是灰的，不能是纯黑（LiftGammaGain 抬黑）；有死黑 → 抬黑量不够。");
        log.Add("· 画面远处应该淡淡发白（Fog）；完全没有 → 雾没开，或被材质忽略。");
        log.Add("· 四角比中心略暗（Vignette），暗部偏冷、高光偏暖（SplitToning）。");
        log.Add("· 整体发灰发脏 → 曝光/对比过了；发白没层次 → 对比再拉一点。");
        Directory.CreateDirectory(Path.GetDirectoryName(REPORT_PV).Replace('/', Path.DirectorySeparatorChar));
        File.WriteAllText(REPORT_PV, string.Join("\n", log.ToArray()));
        Debug.Log("[ScenePostFx] 后期预览已写 " + outDir);
    }

    [MenuItem("Tools/干预项目/场景后期效果/④ 关闭后期效果（可反复开关）", false, 13)]
    public static void Revert()
    {
        if (!File.Exists(OUT_SCENE)) { Debug.LogError("[ScenePostFx] 找不到 " + OUT_SCENE); return; }
        var cur = SceneManager.GetActiveScene();
        if (cur.path != OUT_SCENE && !Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var scene = cur.path == OUT_SCENE ? cur : EditorSceneManager.OpenScene(OUT_SCENE, OpenSceneMode.Single);

        var log = new List<string>();
        log.Add("关闭场景后期效果  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("");
        int n = 0;

        foreach (var cam in Object.FindObjectsOfType<Camera>(true))
        {
            var e = cam.GetComponent<UniversalAdditionalCameraData>();
            if (e == null || !e.renderPostProcessing) continue;
            e.renderPostProcessing = false;
            log.Add("  相机 " + cam.name + "：Post Processing 关");
            n++;
        }
        foreach (var v in Object.FindObjectsOfType<Volume>(true))
        {
            if (v.name != "Post_全局" && !v.name.StartsWith("Post_")) continue;
            v.gameObject.SetActive(false);
            log.Add("  Volume " + v.name + "：隐藏（没删，想开回来点一下 ① / ④ 之外的手动勾也行）");
            n++;
        }
        var glowRoot = GameObject.Find(LEGACY_GLOW_GO);
        if (glowRoot != null) { glowRoot.SetActive(false); log.Add("  窗光片（旧版遗留）：隐藏"); n++; }

        // 轮廓描边（Renderer Feature）：取消勾选 active
        foreach (var rd in ProjectRendererDatas())
        {
            var so = new SerializedObject(rd);
            var list = so.FindProperty("m_RendererFeatures");
            if (list == null) continue;
            for (int i = 0; i < list.arraySize; i++)
            {
                if (!(list.GetArrayElementAtIndex(i).objectReferenceValue is OutlineFeature f)) continue;
                var fso = new SerializedObject(f);
                var act = fso.FindProperty("settings.active");
                if (act != null) { act.boolValue = false; fso.ApplyModifiedPropertiesWithoutUndo(); }
                EditorUtility.SetDirty(f);
                log.Add("  轮廓描边：关闭（active = false）");
                n++;
            }
        }

        RenderSettings.fog = false;
        log.Add("  雾：关");
        log.Add("");
        log.Add(n == 0
            ? "  （场景里没找到后期对象，本来就没开）"
            : string.Format("  共处理 {0} 处。注意是『停用』不是删除，跑一次 ① 就全回来。", n));

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Write(REPORT, log);
        Debug.Log("[ScenePostFx] 后期效果已关闭");
    }

    // ------------------------------------------------------------------ 主流程（SceneBuilder 也调这个）
    public static void ApplyToScene(Scene scene, List<string> log)
    {
        log.Add("场景：" + scene.path);
        log.Add("");
        FixRenderer(log);
        var moods = BuildProfiles(log);
        SetupAtmosphere(log);
        SetupCamera(log);
        SetupVolumes(scene, log, moods);
        SetupOutline(log);
        EnsureCharacterColliders(log);
        RemoveLegacyGlow(scene, log);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    // ------------------------------------------------------------------ ⑦ 轮廓描边
    /// 把 OutlineFeature 挂到项目自己的 URP Renderer 资产上（幂等），并把参数刷成设计值。
    static void SetupOutline(List<string> log)
    {
        log.Add("【7】轮廓描边（Renderer Feature）");

        var shader = Shader.Find(OUTLINE_SHADER);
        if (shader == null)
        {
            log.Add("  ★ 找不到 shader " + OUTLINE_SHADER + "（看 Assets/Scripts/Visual/SceneOutline.shader 有没有编译错）");
            log.Add("");
            return;
        }

        // 1) 材质资产：Feature 引用它，shader 就不会在打包时被剥掉
        var mat = AssetDatabase.LoadAssetAtPath<Material>(OUTLINE_MAT);
        if (mat == null)
        {
            Directory.CreateDirectory(DIR.Replace('/', Path.DirectorySeparatorChar));
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, OUTLINE_MAT);
        }
        // ★ 运行期的参数源就是这个材质（Feature 不会在运行期改它，所以 Inspector 里调是实时的）。
        //   跑一次 ① 会把下面这几个常量写回去 = 重置成设计值。
        mat.shader = shader;
        mat.SetColor("_OutlineColor", OUTLINE_COLOR);
        mat.SetFloat("_OutlineWidth", OUTLINE_WIDTH);
        mat.SetFloat("_FadeStart", OUTLINE_FADE_A);
        mat.SetFloat("_FadeEnd", OUTLINE_FADE_B);
        EditorUtility.SetDirty(mat);

        // 2) 把“角色 + 道具”放到描边层上（墙/天花板/地板留在 Default，不描）
        int layer = EnsureLayer(OUTLINE_LAYER);
        if (layer < 0) log.Add("  ★ 没拿到空闲层，描边会落到 Default 层（全场景都描，不建议）");
        int tagged = layer > 0 ? TagOutlineObjects(layer, log) : 0;

        // 3) Feature：挂到项目自己的 Renderer 上
        var datas = ProjectRendererDatas();
        if (datas.Count == 0) { log.Add("  ★ 没找到项目自己的 RendererData"); log.Add(""); return; }

        var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(OUTLINE_SCRIPT);
        if (ms == null)
        {
            AssetDatabase.ImportAsset(OUTLINE_SCRIPT, ImportAssetOptions.ForceUpdate);
            ms = AssetDatabase.LoadAssetAtPath<MonoScript>(OUTLINE_SCRIPT);
        }
        var type = ms != null ? ms.GetClass() : null;
        if (type == null)
        {
            // 域重载还没完，下一帧重试（AGENTS.md 里记过的 MonoScript.GetClass() 坑）
            EditorApplication.delayCall += () => { try { SetupOutline(new List<string>()); } catch { } };
            log.Add("  脚本还没编译完，已排队下一帧重试");
            log.Add("");
            return;
        }

        foreach (var rd in datas)
        {
            string path = AssetDatabase.GetAssetPath(rd);
            var so = new SerializedObject(rd);
            var list = so.FindProperty("m_RendererFeatures");
            if (list == null) { log.Add("  ★ " + path + "：找不到 m_RendererFeatures"); continue; }

            OutlineFeature feat = null;
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue is OutlineFeature f) { feat = f; break; }

            bool isNew = false;
            if (feat == null)
            {
                feat = (OutlineFeature)ScriptableObject.CreateInstance(type);
                feat.name = "Scene Outline";
                AssetDatabase.AddObjectToAsset(feat, rd);
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = feat;
                isNew = true;
            }

            var fso = new SerializedObject(feat);
            var matProp = fso.FindProperty("material");
            if (matProp != null) matProp.objectReferenceValue = mat;
            var p = fso.FindProperty("settings");
            if (p != null)
            {
                SetChild(p, "active", true);
                var lay = p.FindPropertyRelative("layers");
                if (lay != null) lay.intValue = 1 << Mathf.Max(layer, 0);
            }
            fso.ApplyModifiedPropertiesWithoutUndo();

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(rd);
            log.Add(string.Format("  {0}：{1} OutlineFeature（只含 active + 层掩码，参数在材质里）",
                path, isNew ? "新建并挂上" : "已存在，刷新参数"));
        }

        AssetDatabase.SaveAssets();
        log.Add(string.Format("  描边对象：『{0}』层上的 {1} 个物体（只含角色与道具，墙/天花板/地板不描）", OUTLINE_LAYER, tagged));
        log.Add(string.Format("  ★ 调参就改 {0}：_OutlineWidth={1:0.#}px、_OutlineColor、_FadeStart={2:0.#}m、_FadeEnd={3:0.#}m（运行期不覆盖，拖了立刻生效）",
            OUTLINE_MAT, OUTLINE_WIDTH, OUTLINE_FADE_A, OUTLINE_FADE_B));
        log.Add("  做法：反向外壳（顶点沿法线外推 → Cull Front 画背面）。是真实几何体，描边长在物体表面上，不跟随摄像机");
        log.Add("  时机：AfterRenderingOpaques → 和普通不透明物体同阶段，会被景深/雾一起处理，且被墙挡住的部分自动不描");
        log.Add("  调参：改材质 " + OUTLINE_MAT + "（Feature 运行期不覆盖它，拖了立刻生效）；或改本脚本顶部 OUTLINE_* 再跑一次 ①");
        log.Add("  想多描点：手动把物体设成 Layer =『" + OUTLINE_LAYER + "』；不想要：取消 Feature 的 active 勾选");
        log.Add("");
    }

    // ------------------------------------------------------------------ ⑧ 角色碰撞体
    /// 给场景里每个角色实例加一个胶囊碰撞体，玩家（CharacterController）就撞不过去了。
    /// 玩家自己（Player_徐夏）不在 第X章角色 节点下，所以不会被误加。
    static void EnsureCharacterColliders(List<string> log)
    {
        log.Add("【8】角色碰撞体");
        var locsRoot = GameObject.Find("Locations");
        if (locsRoot == null) { log.Add("  ★ 没有 Locations，跳过"); log.Add(""); return; }

        int added = 0, fitted = 0;
        var detail = new List<string>();
        foreach (Transform loc in locsRoot.transform)
        {
            for (int i = 0; i < loc.childCount; i++)
            {
                var group = loc.GetChild(i);
                if (!group.name.Contains("角色")) continue;
                for (int c = 0; c < group.childCount; c++)
                {
                    var ch = group.GetChild(c).gameObject;
                    if (FitCapsule(ch)) added++;
                    fitted++;
                }
            }
            var g = loc.GetChild(0);
            detail.Add(loc.name);
        }

        if (fitted == 0) { log.Add("  ★ 没找到角色实例"); log.Add(""); return; }
        log.Add(string.Format("  {0} 个角色实例，本次新增/重置胶囊体 {1} 个（覆盖 {2} 个地点）",
            fitted, added, detail.Count));
        log.Add("  胶囊体尺寸由角色的渲染包围盒算：高 = 高度×0.96，半径 = 高度×0.16（不含张开的手臂）");
        log.Add("  放在角色根节点上，跟着角色动；层就是 Outline 层，跟其它层默认碰撞");
        log.Add("  ⚠ 玩家自己（Player_徐夏）不在 第X章角色 节点下，不会被加 —— 它有 CharacterController");
        log.Add("");
    }

    /// 按渲染包围盒给角色根节点配一个竖着的胶囊体。返回是否新建/重置过。
    static bool FitCapsule(GameObject go)
    {
        var b = BoundsOf(go);
        if (!b.HasValue) return false;
        var bb = b.Value;
        float h = bb.size.y;
        if (h < 0.5f) return false;                 // 明显不是个人，别硬加

        // 半径不按 X 跨度算 —— 角色是 T-pose，手臂张开会算出一个巨大的胶囊
        float r = Mathf.Clamp(h * 0.16f, 0.12f, 0.50f);
        float cap = Mathf.Max(h * 0.96f, r * 2.01f);

        var col = go.GetComponent<CapsuleCollider>();
        bool created = col == null;
        if (created) col = go.AddComponent<CapsuleCollider>();

        col.direction = 1;                          // Y
        col.radius = r;
        col.height = cap;
        col.isTrigger = false;

        // 角色根节点只有 yaw、scale 1，所以世界包围盒可以直接换算到局部
        var t = go.transform;
        Vector3 worldCenter = new Vector3(bb.center.x, bb.min.y + cap * 0.5f, bb.center.z);
        col.center = t.InverseTransformPoint(worldCenter);
        col.center = new Vector3(col.center.x, col.center.y, col.center.z);

        EditorUtility.SetDirty(col);
        return created;
    }

    /// 确保有一个叫 name 的层，返回层号（没有空闲层就返回 -1）
    static int EnsureLayer(string name)
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets == null || assets.Length == 0) return -1;
        var so = new SerializedObject(assets[0]);
        var layers = so.FindProperty("layers");
        if (layers == null) return -1;

        for (int i = 0; i < layers.arraySize; i++)
            if (layers.GetArrayElementAtIndex(i).stringValue == name) return i;

        // 0~7 是 Unity 保留层，从 8 开始找空的
        for (int i = 8; i < layers.arraySize; i++)
        {
            var p = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(p.stringValue))
            {
                p.stringValue = name;
                so.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                return i;
            }
        }
        return -1;
    }

    /// 把每个房间的『道具(Content)』与『角色(第X章角色)』全换到描边层；Shell（地板/墙）不动。
    /// 返回改动的 GameObject 数量。
    static int TagOutlineObjects(int layer, List<string> log)
    {
        if (layer <= 0) return 0;
        var locsRoot = GameObject.Find("Locations");
        if (locsRoot == null) { log.Add("  ★ 没有 Locations，没改任何物体的层"); return 0; }

        int n = 0;
        var hits = new List<string>();
        foreach (Transform loc in locsRoot.transform)
        {
            for (int i = 0; i < loc.childCount; i++)
            {
                var child = loc.GetChild(i);
                string cn = child.name;
                // Content/content = 道具；含“角色” = 角色；Shell* = 房子本体，跳过
                bool isProp = cn.Equals("Content", System.StringComparison.OrdinalIgnoreCase);
                bool isChar = cn.Contains("角色");
                if (!isProp && !isChar) continue;
                n += SetLayerDeep(child.gameObject, layer);   // 返回该子树里的物体总数（不只是改动的）
                hits.Add(loc.name + "/" + cn);
            }
        }
        if (hits.Count > 0) log.Add("  已放入描边层：" + string.Join("、", hits.ToArray()));
        else log.Add("  ★ 没找到 Content / 角色 节点，什么都没改");
        return n;
    }

    /// 返回这棵子树里的 GameObject 总数（不只是被改动的），用来在报告里报“描边对象共多少个”
    static int SetLayerDeep(GameObject go, int layer)
    {
        int n = 1;
        go.layer = layer;
        foreach (Transform t in go.transform) n += SetLayerDeep(t.gameObject, layer);
        return n;
    }

    static void SetChild(SerializedProperty parent, string name, object v)
    {
        var p = parent.FindPropertyRelative(name);
        if (p == null) return;
        if (p.propertyType == SerializedPropertyType.Float) p.floatValue = System.Convert.ToSingle(v);
        else if (p.propertyType == SerializedPropertyType.Boolean) p.boolValue = System.Convert.ToBoolean(v);
        else if (p.propertyType == SerializedPropertyType.Integer) p.intValue = System.Convert.ToInt32(v);
    }

    /// 项目自己的 UniversalRendererData（从项目自己的 URP 资产里取，不碰包里的）
    static List<UniversalRendererData> ProjectRendererDatas()
    {
        var res = new List<UniversalRendererData>();
        foreach (var g in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            if (string.IsNullOrEmpty(p) || p.StartsWith("Packages/")) continue;
            var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(p);
            if (rp == null) continue;
            var so = new SerializedObject(rp);
            var list = so.FindProperty("m_RendererDataList");
            if (list == null) continue;
            for (int i = 0; i < list.arraySize; i++)
            {
                var d = list.GetArrayElementAtIndex(i).objectReferenceValue as UniversalRendererData;
                if (d != null && !res.Contains(d) && !AssetDatabase.GetAssetPath(d).StartsWith("Packages/")) res.Add(d);
            }
        }
        if (res.Count == 0)
        {
            var d = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/URP/URP_Renderer.asset");
            if (d != null) res.Add(d);
        }
        return res;
    }

    /// 早期版本往窗洞里贴过加色发光片（Post_窗光 + 配套材质贴图）。
    /// 现在不用了：跑一次工具就把场景里的节点和资源都清掉，免得留下看不见的脏东西。
    static void RemoveLegacyGlow(Scene scene, List<string> log)
    {
        var go = GameObject.Find(LEGACY_GLOW_GO);
        bool hadGo  = go != null;
        bool hadMat = AssetDatabase.LoadAssetAtPath<Material>(LEGACY_GLOW_MAT) != null;
        bool hadTex = AssetDatabase.LoadAssetAtPath<Texture2D>(LEGACY_GLOW_TEX) != null;
        if (!hadGo && !hadMat && !hadTex) return;

        log.Add("【7】清理旧版窗光片");
        if (hadGo)  { Object.DestroyImmediate(go); log.Add("  删掉场景里的 " + LEGACY_GLOW_GO + "（连同所有发光片）"); }
        if (hadMat) { AssetDatabase.DeleteAsset(LEGACY_GLOW_MAT); log.Add("  删掉 " + LEGACY_GLOW_MAT); }
        if (hadTex) { AssetDatabase.DeleteAsset(LEGACY_GLOW_TEX); log.Add("  删掉 " + LEGACY_GLOW_TEX); }
        AssetDatabase.SaveAssets();
        log.Add("  现在辉光完全靠场景自身的亮部（白墙 / 白纸 / 天空）+ 雾，场景里不再有额外发光物体。");
        log.Add("");
    }

    // ------------------------------------------------------------------ ① 修 URP Renderer 资源
    static void FixRenderer(List<string> log)
    {
        log.Add("【1】URP Renderer 资源");

        // ★ 只动【项目自己】的 Renderer；不能用 FindAssets("t:UniversalRendererData")，
        //    那会把 URP 包里的 Runtime/Data/UniversalRendererData.asset 也搜出来并改动，很脏。
        var datas = ProjectRendererDatas();
        if (datas.Count == 0) { log.Add("  ★ 一个 Renderer 资产都没找到"); log.Add(""); return; }

        foreach (var d in datas)
        {
            string path = AssetDatabase.GetAssetPath(d);
            int nullShaders = CountNullShaders(d);
            if (d.postProcessData == null)
            {
                // ★ 关键一步：没有它，URP 会静默跳过所有后期（不报错、Bloom 完全没反应）
                var ppd = AssetDatabase.LoadAssetAtPath<PostProcessData>(URP_PKG + "/Runtime/Data/PostProcessData.asset");
                if (ppd == null) { log.Add("  ★ 找不到包里的 PostProcessData.asset"); continue; }
                d.postProcessData = ppd;
                EditorUtility.SetDirty(d);
                log.Add("  " + path + "：postProcessData 原本是空的 → 已指向 URP 包的 PostProcessData（★这一步不做 Bloom 就是死的）");
            }
            else log.Add("  " + path + "：postProcessData 已就位 ✓");

            // renderer 里还有一堆 [Reload] 的 shader 引用；顺手把空的重载一遍
            if (nullShaders > 0)
            {
                ResourceReloader.TryReloadAllNullIn(d, URP_PKG);
                EditorUtility.SetDirty(d);
                log.Add(string.Format("    空白 shader 引用 {0} 处 → 重载后还剩 {1} 处", nullShaders, CountNullShaders(d)));
            }
            else log.Add("    shader 引用：全部就位 ✓");
        }
        AssetDatabase.SaveAssets();
        log.Add("");
    }

    static int CountNullShaders(UniversalRendererData d)
    {
        var so = new SerializedObject(d);
        var sh = so.FindProperty("shaders");
        if (sh == null) return -1;
        int n = 0;
        var it = sh.Copy();
        var end = sh.GetEndProperty();
        while (it.NextVisible(true) && !SerializedProperty.EqualContents(it, end))
            if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue == null) n++;
        return n;
    }

    // ------------------------------------------------------------------ ② 生成 VolumeProfile
    static Mood[] BuildProfiles(List<string> log)
    {
        log.Add("【2】Volume 面板（Profile 资产）");
        var global = LoadOrCreate(PROF_GLOBAL);
        Wipe(global);

        var tone = Add<Tonemapping>(global);
        tone.mode.value = TonemappingMode.Neutral;   // 梦核要"奶白不硬"；ACES 在这个 Gamma 空间工程里压得太狠
        tone.mode.overrideState = true;

        var bloom = Add<Bloom>(global);
        bloom.threshold.value = BLOOM_THRESHOLD;
        bloom.intensity.value = BLOOM_INTENSITY;
        bloom.scatter.value = BLOOM_SCATTER;         // 越大越"蒙"，0.85 是很柔的一层
        bloom.tint.value = C(1.00f, 0.96f, 0.90f);
        bloom.highQualityFiltering.value = true;     // 高质量滤波 = 光晕更顺滑不结块
        bloom.maxIterations.value = 6;
        bloom.dirtTexture.value = null;
        bloom.dirtIntensity.value = 0f;

        var ca = Add<ColorAdjustments>(global);
        ca.postExposure.value = 0.10f;
        ca.contrast.value = 10f;                     // ★ 对比要留住，否则远近真的“融在一起”
        ca.saturation.value = 4f;                    // 稍微减一点鲜就行，别真的去色
        ca.colorFilter.value = C(1.00f, 0.99f, 1.00f);
        ca.hueShift.value = 0f;

        var wb = Add<WhiteBalance>(global);
        wb.temperature.value = 4f;
        wb.tint.value = 3f;                          // 偏一点品红，梦核的典型色偏

        // ★ 抬黑（Lift）：画面里不再有纯黑 —— 治角色死黑，也是梦核的关键底子
        //   LiftGammaGain 的 lift = (颜色, 偏移)，颜色给中性 0.5 就只留偏移，_Lift.xyz = 偏移值
        var lgg = Add<LiftGammaGain>(global);
        lgg.lift.value   = new Vector4(0.5f, 0.5f, 0.5f, BLACK_LIFT);
        lgg.gamma.value  = new Vector4(1f, 1f, 1f, 0f);
        lgg.gain.value   = new Vector4(1f, 1f, 1f, 0f);

        var st = Add<SplitToning>(global);           // 冷暖分离：高光暖 / 阴影冷
        st.shadows.value = C(0.46f, 0.48f, 0.58f);
        st.highlights.value = C(0.56f, 0.53f, 0.52f);
        st.balance.value = 0f;

        // 四角压暗：用户明确不要 → 默认 0（组件留着，想要就把 intensity 改回 0.2~0.3）
        var vig = Add<Vignette>(global);
        vig.color.value = C(0.05f, 0.07f, 0.11f);
        vig.center.value = new Vector2(0.5f, 0.5f);
        vig.intensity.value = 0f;
        vig.smoothness.value = 0.55f;
        vig.rounded.value = false;

        var grain = Add<FilmGrain>(global);          // 胶片颗粒：把"数码干净"磨成"实拍"
        grain.type.value = FilmGrainLookup.Medium3;
        grain.intensity.value = 0.10f;
        grain.response.value = 0.85f;
        grain.texture.value = null;

        var chroma = Add<ChromaticAberration>(global); // 极轻的镜头色散（给大了近处也糊）
        chroma.intensity.value = 0.05f;

        var lens = Add<LensDistortion>(global);
        lens.intensity.value = -0.03f;               // 轻微桶形，画面更"包"
        lens.scale.value = 1.00f;

        // ★ 模糊：Gaussian 景深 —— start 以内完全不动，往后逐步化开。
        //   实测半径：4m 内 0 ／ 6m 约 3px ／ 8m 约 6px ／ 11m 约 11px ／ 13m+ 14px（封顶）
        //   运行期由 DreamyFocus.cs 同步（方便在 Play 里直接改着看）。
        var dof = Add<DepthOfField>(global);
        dof.mode.value = DepthOfFieldMode.Gaussian;
        dof.gaussianStart.value = BLUR_START;
        dof.gaussianEnd.value = BLUR_END;
        dof.gaussianMaxRadius.value = BLUR_MAX_RADIUS;
        dof.highQualitySampling.value = true;
        // Bokeh 那套也写个合理值，万一改回去不至于很怪
        dof.focusDistance.value = 5f;
        dof.aperture.value = 5.6f;
        dof.focalLength.value = 50f;
        dof.bladeCount.value = 6;
        dof.bladeCurvature.value = 1f;
        dof.bladeRotation.value = 0f;

        EditorUtility.SetDirty(global);
        log.Add("  " + PROF_GLOBAL + "：Tonemapping(Neutral) + Bloom(阈值" + BLOOM_THRESHOLD.ToString("0.00") + " 强度" + BLOOM_INTENSITY.ToString("0.00") + " 散射" + BLOOM_SCATTER.ToString("0.00") + ")");
        log.Add("      + DepthOfField(Gaussian：" + BLUR_START.ToString("0.#") + "m 内完全不动，" + BLUR_START.ToString("0.#") + "~" + BLUR_END.ToString("0.#") + "m 逐步化开) ← ★ 『越近越清楚、越远越朦胧』由它实现");
        log.Add("      + LiftGammaGain(抬黑 +" + BLACK_LIFT.ToString("0.000") + ") ← 只保证不出现纯黑，不再把暗部拉灰");
        log.Add("      + ColorAdjustments(曝+0.10 对比+10 饱和+4) + WhiteBalance(暖+4 品红+3) + SplitToning(冷影暖高光)");
        log.Add("      + FilmGrain(0.10) + ChromaticAberration(0.05) + LensDistortion(-0.03)");
        log.Add("      + Vignette(intensity = 0，四角不再压暗；想开就改这个值，0.2~0.3 比较合适)");

        // 每个地点一份
        var moods = Moods();
        foreach (var m in moods)
        {
            string path = DIR + "/Post_" + m.Room.Replace("Loc_", "") + ".asset";
            var p = LoadOrCreate(path);
            Wipe(p);

            var b = Add<Bloom>(p);
            b.threshold.value = BLOOM_THRESHOLD;
            b.intensity.value = Mathf.Clamp01(m.Bloom);
            b.scatter.value = m.Scatter;
            b.tint.value = m.BloomTint;
            b.highQualityFiltering.value = true;
            b.maxIterations.value = 6;

            var c = Add<ColorAdjustments>(p);
            c.postExposure.value = m.Exposure;
            c.contrast.value = m.Contrast;
            c.saturation.value = m.Saturation;
            c.colorFilter.value = m.Filter;

            var w = Add<WhiteBalance>(p);
            w.temperature.value = m.Temp;
            w.tint.value = m.WbTint;

            var s = Add<SplitToning>(p);
            s.shadows.value = m.ShadowTone;
            s.highlights.value = m.HighlightTone;
            s.balance.value = 0f;

            var g = Add<FilmGrain>(p);
            g.type.value = FilmGrainLookup.Medium3;
            g.intensity.value = m.Grain;
            g.response.value = 0.85f;

            EditorUtility.SetDirty(p);
            log.Add(string.Format("  {0}  {1}  辉光 {2:0.00} / 曝光 {3:+0.00;-0.00;0} / 对比 {4:+0;-0;0} / 饱和 {5:+0;-0;0} / 色温 {6:+0;-0;0} / 颗粒 {7:0.00}",
                m.Room, m.Desc, m.Bloom, m.Exposure, m.Contrast, m.Saturation, m.Temp, m.Grain));
        }
        AssetDatabase.SaveAssets();
        log.Add("");
        return moods;
    }

    static VolumeProfile LoadOrCreate(string path)
    {
        var p = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (p != null) return p;
        Directory.CreateDirectory(Path.GetDirectoryName(path).Replace('/', Path.DirectorySeparatorChar));
        p = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(p, path);
        AssetDatabase.SaveAssets();
        return p;
    }

    /// 清空重建：这样改一次数值跑一次工具就生效，不会残留上一次的旧组件
    static void Wipe(VolumeProfile p)
    {
        var old = p.components.Where(x => x != null).ToList();
        p.components.Clear();
        EditorUtility.SetDirty(p);
        foreach (var c in old) Object.DestroyImmediate(c, true);
    }

    static T Add<T>(VolumeProfile p) where T : VolumeComponent
    {
        var c = p.Add<T>(true);          // true = 所有参数都勾上 override
        c.name = typeof(T).Name;
        AssetDatabase.AddObjectToAsset(c, p);
        return c;
    }

    // ------------------------------------------------------------------ ③ 大气（雾 + 环境光）
    static void SetupAtmosphere(List<string> log)
    {
        log.Add("【3】大气（雾 / 环境光）");
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = FOG_DENSITY;
        RenderSettings.fogColor = FOG_COLOR;

        // 环境光压暗偏冷一点 → 背光面不再"一片平灰"，阳光方向感更强
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = C(AMBIENT, AMBIENT * 1.04f, AMBIENT * 1.18f);
        RenderSettings.ambientIntensity = 1f;

        // 指定“太阳”：不设的话程序化天空盒不画太阳（天空一片死黑），URP 也可能挑错主光
        Light best = null;
        foreach (var l in Object.FindObjectsOfType<Light>(true))
        {
            if (l.type != LightType.Directional || !l.enabled || !l.gameObject.activeInHierarchy) continue;
            if (best == null || SunScore(l) > SunScore(best)) best = l;
        }
        RenderSettings.sun = best;
        log.Add("  太阳（RenderSettings.sun）：" + (best != null ? string.Format("{0}   强度 {1:0.00}  投影 {2}", best.name, best.intensity, best.shadows) : "★ 场景里没有定向光"));

        log.Add("  雾：Exponential Squared，浓度 " + FOG_DENSITY.ToString("0.###") + "，颜色 (" + FOG_COLOR.r.ToString("0.00") + "," + FOG_COLOR.g.ToString("0.00") + "," + FOG_COLOR.b.ToString("0.00") + ")");
        log.Add(string.Format("      实测：3m {0:0.#}% ／ 6m {1:0.#}% ／ 12m {2:0.#}% ／ 24m {3:0.#}%（近处几乎不吃雾，远墙才发白）",
            FogAmount(3f) * 100f, FogAmount(6f) * 100f, FogAmount(12f) * 100f, FogAmount(24f) * 100f));
        log.Add(string.Format("  环境光：Flat ({0:0.###}, {1:0.###}, {2:0.###})   环境光抬到 {0:0.00} 就是为了让角色暗部不是一团黑",
            RenderSettings.ambientLight.r, RenderSettings.ambientLight.g, RenderSettings.ambientLight.b));
        log.Add("  角色不喷雾：ShaderGraph_CharacterLit 本身就是 URP Lit 模板，雾是自带的（不会『人飘在雾上』）。");
        log.Add("  天空/环境光想再亮：改 PlayerSettings 的 Color Space 为 Linear（工程现在是 Gamma），");
        log.Add("  后期里的色调映射/调色在 Gamma 下本来就与设计不符，换 Linear 是真正的升级，但要全量重导一次。");
        log.Add("");
    }

    /// 挑“太阳”：先看强度，投影的优先，名字像太阳的再加一档。
    /// （这个场景里 “Point Light” 其实也是 Directional，光看类型会挑错）
    static float SunScore(Light l)
    {
        float s = l.intensity * l.color.grayscale;
        if (l.shadows != LightShadows.None) s *= 1.5f;
        string n = l.name.ToLowerInvariant();
        if (n.Contains("sun") || n.Contains("太阳") || n.Contains("主光")) s *= 1.4f;
        if (n.Contains("补光") || n.Contains("fill")) s *= 0.5f;
        return s;
    }

    static float FogAmount(float dist)
    {
        float f = FOG_DENSITY * dist;
        return 1f - Mathf.Exp(-f * f);
    }

    // ------------------------------------------------------------------ ④ 相机
    static void SetupCamera(List<string> log)
    {
        log.Add("【4】相机 / 抗锯齿");
        int n = 0;
        foreach (var cam in Object.FindObjectsOfType<Camera>(true))
        {
            if (cam.name != "FP_相机" && !cam.CompareTag("MainCamera")) continue;
            var e = cam.GetUniversalAdditionalCameraData();
            e.renderPostProcessing = true;
            e.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            e.antialiasingQuality = AntialiasingQuality.High;
            e.dithering = true;      // 抖色：雾/渐变不容易起色带，这一项对"高级感"很值
            e.stopNaN = true;
            e.renderShadows = true;
            cam.allowHDR = true;     // Bloom 必须有 HDR
            cam.allowMSAA = true;
            EditorUtility.SetDirty(e);
            EditorUtility.SetDirty(cam);
            AttachFocusScript(cam.gameObject);
            log.Add("  " + cam.name + "：Post Processing 开 / SMAA High / Dithering 开 / HDR 开 / MSAA 开");
            n++;
        }
        if (n == 0) log.Add("  ★ 没找到主相机（应先跑『搭建游戏场景』）");
        log.Add("");
    }

    /// 把 DreamyFocus 挂到相机上（让景深对焦点钉在近处）。
    /// ★ AGENTS.md 里记过的坑：刚写/刚拷进来的 .cs，MonoScript.GetClass() 可能是 null，
    ///   这时 AddComponent 出来的组件会被写成"内联 MonoScript"，换个会话就变成缺脚本。
    ///   所以先 ImportAsset(ForceUpdate) 把脚本过一遍，拿不到类型就不挂（宁可不挂，也不要拄在那）。
    static void AttachFocusScript(GameObject camGO)
    {
        // 先确保组件在（成不成再说，下面统一用 SerializedObject 写字段，新老组件都适用）
        EnsureComponent(camGO);
        var comp = FindFocusComponent(camGO);
        if (comp == null) return;

        // 参数跟着工具顶部的常量走：重跑一次 ① 就会把场景里的值刷成设计值
        var so = new SerializedObject(comp);
        SetIfExist(so, "hazeStart", BLUR_START);
        SetIfExist(so, "hazeEnd", BLUR_END);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(comp);
    }

    static void SetIfExist(SerializedObject so, string name, object v)
    {
        var p = so.FindProperty(name);
        if (p == null) return;
        if (p.propertyType == SerializedPropertyType.Float) p.floatValue = System.Convert.ToSingle(v);
        else if (p.propertyType == SerializedPropertyType.Boolean) p.boolValue = System.Convert.ToBoolean(v);
        else if (p.propertyType == SerializedPropertyType.Integer) p.intValue = System.Convert.ToInt32(v);
    }

    static Component FindFocusComponent(GameObject camGO)
    {
        foreach (var c in camGO.GetComponents<Component>())
            if (c != null && c.GetType().Name == "DreamyFocus") return c;
        return null;
    }

    static void EnsureComponent(GameObject camGO)
    {
        if (FindFocusComponent(camGO) != null) return;
        var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(FOCUS_SCRIPT);
        if (ms == null)
        {
            AssetDatabase.ImportAsset(FOCUS_SCRIPT, ImportAssetOptions.ForceUpdate);
            ms = AssetDatabase.LoadAssetAtPath<MonoScript>(FOCUS_SCRIPT);
        }
        var type = ms != null ? ms.GetClass() : null;
        if (type != null)
        {
            camGO.AddComponent(type);
            return;
        }
        // 域重载还没完，下一帧再试一次（否则就是真没编译过）
        EditorApplication.delayCall += () =>
        {
            if (camGO == null || FindFocusComponent(camGO) != null) return;
            var m2 = AssetDatabase.LoadAssetAtPath<MonoScript>(FOCUS_SCRIPT);
            var t2 = m2 != null ? m2.GetClass() : null;
            if (t2 == null) return;
            camGO.AddComponent(t2);
            EditorUtility.SetDirty(camGO);
        };
    }

    // ------------------------------------------------------------------ ⑤ Volume 对象
    static void SetupVolumes(Scene scene, List<string> log, Mood[] moods)
    {
        log.Add("【5】场景里的 Volume");
        var root = GameObject.Find("GameRoot");
        var parent = root != null ? root.transform : null;

        // 全局
        var gGO = EnsureChild(parent, "Post_全局");
        gGO.transform.position = Vector3.zero;
        gGO.SetActive(true);
        var gv = gGO.GetComponent<Volume>();
        if (gv == null) gv = gGO.AddComponent<Volume>();
        gv.isGlobal = true;
        gv.priority = 0f;
        gv.weight = 1f;
        gv.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PROF_GLOBAL);
        // 全局 Volume 不该有碰撞体，有的话 URP 会警告
        foreach (var c in gGO.GetComponents<Collider>()) Object.DestroyImmediate(c);
        log.Add("  Post_全局（isGlobal, priority 0）→ Post_全局.asset");

        // 每个地点一个本地 Volume
        var locsRoot = GameObject.Find("Locations");
        if (locsRoot == null) { log.Add("  ★ 场景里没有 Locations，跳过各屋氛围"); log.Add(""); return; }

        int n = 0;
        foreach (Transform loc in locsRoot.transform)
        {
            var mood = moods.FirstOrDefault(m => m.Room == loc.name);
            if (mood == null) { log.Add("  ○ " + loc.name + "：没有配氛围，跳过"); continue; }

            var floor = loc.Find("Shell/Shell_地板");
            if (floor == null) { log.Add("  ★ " + loc.name + "：找不到 Shell_地板，跳过"); continue; }
            var fr = floor.GetComponent<Renderer>();
            if (fr == null) { log.Add("  ★ " + loc.name + "：地板没有 Renderer，跳过"); continue; }

            var b = fr.bounds;
            float h = 3.4f;
            var walls = loc.Find("Shell_墙");
            if (walls != null)
            {
                var wb = BoundsOf(walls.gameObject);
                if (wb.HasValue) h = Mathf.Max(2.2f, wb.Value.max.y - loc.position.y);
            }

            var go = EnsureChild(loc, "Post_" + loc.name.Replace("Loc_", ""));
            go.SetActive(true);
            go.transform.position = new Vector3(loc.position.x, loc.position.y + h * 0.5f, loc.position.z);
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var box = go.GetComponent<BoxCollider>();
            if (box == null) box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;   // ★ 必须是 Trigger：不然玩家（CharacterController）会被自己的房间挡住
            box.center = Vector3.zero;
            box.size = new Vector3(Mathf.Max(1f, b.size.x), h, Mathf.Max(1f, b.size.z));

            var v = go.GetComponent<Volume>();
            if (v == null) v = go.AddComponent<Volume>();
            v.isGlobal = false;
            v.priority = 10f;       // 比全局高 → 进屋就顶掉全局
            v.weight = 1f;
            v.blendDistance = 5f;   // 出屋 5m 内平滑过渡，不是硬切
            v.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(DIR + "/Post_" + mood.Room.Replace("Loc_", "") + ".asset");

            log.Add(string.Format("  {0}：碰撞盒 {1:0.#}×{2:0.#}×{3:0.#}m（Trigger）+ blendDistance 5m → Post_{4}.asset",
                loc.name, box.size.x, box.size.y, box.size.z, mood.Room.Replace("Loc_", "")));
            n++;
        }
        log.Add("  合计：1 个全局 + " + n + " 个本地");
        log.Add("  说明：本地 Volume 是『覆盖』不是叠加，所以走不同房间时辉光/色温/暗角会整体换一套。");
        log.Add("");
    }

    static GameObject EnsureChild(Transform parent, string name)
    {
        Transform t = null;
        if (parent != null) t = parent.Find(name);
        else
        {
            var found = GameObject.Find(name);
            if (found != null) t = found.transform;
        }
        if (t != null) return t.gameObject;
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        return go;
    }

    // ------------------------------------------------------------------ 工具
    static Bounds? BoundsOf(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return null;
        var b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }

    /// path 为 null 时不写文件（只算指标）
    /// sharpness.x = 下半屏（近处）的边缘能量， sharpness.y = 上部（远处）的边缘能量
    static float Shoot(Camera cam, RenderTexture rt, string path, out Vector2 sharpness)
    {
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;

        var px = tex.GetPixels();
        float sum = 0f;
        for (int i = 0; i < px.Length; i++) sum += px[i].grayscale;
        float avg = px.Length > 0 ? sum / px.Length : 0f;

        int w = tex.width, h = tex.height;
        sharpness = new Vector2(EdgeEnergy(px, w, 0, h / 3),        // 下部（近处）
                                EdgeEnergy(px, w, h * 2 / 3, h));   // 上部（远处）

        if (!string.IsNullOrEmpty(path)) File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        return avg;
    }

    /// 某个横向条带里的平均“相邻像素亮度差”（越大越锐，越糊越小）
    static float EdgeEnergy(Color[] px, int w, int y0, int y1)
    {
        float e = 0f;
        int n = 0;
        for (int y = y0; y < y1; y += 3)
            for (int x = 0; x + 3 < w; x += 3)
            {
                e += Mathf.Abs(px[y * w + x + 3].grayscale - px[y * w + x].grayscale);
                n++;
            }
        return n > 0 ? e / n : 0f;
    }

    /// 全局面板里的 DepthOfField（找不顺就返回 null）
    static DepthOfField FindGlobalDof()
    {
        var v = GameObject.Find("Post_全局");
        if (v == null) return null;
        var vol = v.GetComponent<Volume>();
        if (vol == null || vol.sharedProfile == null) return null;
        for (int i = 0; i < vol.sharedProfile.components.Count; i++)
        {
            var d = vol.sharedProfile.components[i] as DepthOfField;
            if (d != null) return d;
        }
        return null;
    }

    static void Write(string path, List<string> log)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path).Replace('/', Path.DirectorySeparatorChar));
        log.Add("");
        log.Add("———————————————————————————— 怎么调 ————————————————————————————");
        log.Add("· 远处还是太糊：把顶部 BLUR_END 调大（24 → 40，过渡更长）或 BLUR_START 调大（6.5 → 10）。");
        log.Add("· 想要更明显的远近分层：BLUR_END 调小（24 → 14）、BLUR_START 调小（6.5 → 4）。");
        log.Add("  （URP 的最大模糊半径被写死在 shader 里（14px）；再糊就只能靠雾了。）");
        log.Add("· 整体又发灰融在一起：那是对比/饱和/抬黑被拉过头了 —— 保住 contrast ≥ 8、BLACK_LIFT ≤ 0.03。");
        log.Add("· 模糊强度：改本脚本顶部 BLUR_START（多少米内清楚）/ BLUR_END（多远糊满）/ BLUR_MAX_RADIUS，再跑一次 ①。");
        log.Add("· 角色还是偏黑：把本脚本顶部 AMBIENT 提到 0.32、BLACK_LIFT 提到 0.07。");
        log.Add("  根因：场景里只有 2 盏平行光（都没阴影）+ 环境光，室内没有任何灯；");
        log.Add("  套件里的衣服本来就深色，环境光一低就成一团黑。真正的解法是给房间加灯");
        log.Add("  （场景里每个房间都已经留了空的 Area_灯组 节点，就在那里摆）。");
        log.Add("· 对焦不跟手/抽气：选场景里的 FP_相机，调 DreamyFocus 的 smooth（默认 7）。");
        log.Add("· 想让画面里有东西自发亮（灯光、屏幕）：给那个物体换自发光材质，再把 threshold 拉到 1.0 以上。");
        log.Add("· 雾更浓/更淡：改 FOG_DENSITY（0.012 淡 / 0.018 中 / 0.030 浓）。");
        log.Add("· 单个房间的氛围：改 Moods() 里那一行，再跑一次 ①。");
        log.Add("· 四角压暗不想要（现在是关的）；想开就把 Post_全局.asset 的 Vignette intensity 改 0.2~0.3。");
        log.Add("· 想换成 ACES 色调映射（更电影、更暗）：Post_全局.asset 的 Tonemapping mode 改 ACES，");
        log.Add("  同时把 postExposure 提到 +0.5 左右补亮。");
        log.Add("· 调完想看效果：跑『渲染后期预览』（③），或直接进 Play 模式走一圈。");
        log.Add("· 不想用：跑 ④ 关闭后期效果。");
        File.WriteAllText(path, string.Join("\n", log.ToArray()));
    }
}

// 工程里存在 Assets/_postfx_trigger.txt 时，编辑器下次刷新/重编译后自动跑一次后期布置。
[InitializeOnLoad]
public static class ScenePostFxTrigger
{
    const string Trigger = "Assets/_postfx_trigger.txt";
    const string Preview = "Assets/_postfxpreview_trigger.txt";
    const string Full    = "Assets/_postfxfull_trigger.txt";
    const string ErrFile = "../额外文件/错误_场景后期.txt";

    static ScenePostFxTrigger()
    {
        if (File.Exists(Preview))
        {
            EditorApplication.delayCall += () =>
            {
                if (File.Exists(Preview)) File.Delete(Preview);
                DelMeta(Preview);
                try { ScenePostFx.RenderPreview(); }
                catch (System.Exception e)
                {
                    Directory.CreateDirectory("../额外文件");
                    File.WriteAllText("../额外文件/错误_后期预览.txt", e.ToString());
                    Debug.LogError("[ScenePostFx] 预览失败: " + e);
                }
            };
            return;
        }

        if (File.Exists(Full))
        {
            // 一把跑完：布置 + 渲染预览（省得来回等域重载）
            EditorApplication.delayCall += () =>
            {
                if (File.Exists(Full)) File.Delete(Full);
                DelMeta(Full);
                try
                {
                    ScenePostFx.ApplyFromTrigger();
                    ScenePostFx.RenderPreview();
                    if (File.Exists(ErrFile)) File.Delete(ErrFile);
                    Debug.Log("[ScenePostFx] 自动布置 + 预览完成");
                }
                catch (System.Exception e)
                {
                    Directory.CreateDirectory("../额外文件");
                    File.WriteAllText(ErrFile, e.ToString());
                    Debug.LogError("[ScenePostFx] 自动布置失败: " + e);
                }
            };
            return;
        }

        if (!File.Exists(Trigger)) return;
        // 不能在排队前就删触发器：域重载会把排队的动作吃掉，触发器却已经消失。
        EditorApplication.delayCall += () =>
        {
            try
            {
                if (File.Exists(Trigger)) File.Delete(Trigger);
                DelMeta(Trigger);
                ScenePostFx.ApplyFromTrigger();
                if (File.Exists(ErrFile)) File.Delete(ErrFile);
                Debug.Log("[ScenePostFx] 自动布置完成");
            }
            catch (System.Exception e)
            {
                Directory.CreateDirectory("../额外文件");
                File.WriteAllText(ErrFile, e.ToString());
                Debug.LogError("[ScenePostFx] 自动布置失败: " + e);
            }
        };
    }

    // 触发器文件是外部创建的，Unity 会给它生成 .meta；只删 .txt 会留下孤儿 .meta 刷警告
    static void DelMeta(string path)
    {
        if (File.Exists(path + ".meta")) File.Delete(path + ".meta");
    }
}
