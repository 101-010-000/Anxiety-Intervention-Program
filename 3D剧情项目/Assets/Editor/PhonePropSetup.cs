using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;   // 2026-09-28 补：SceneManager 所在命名空间（修 3×CS0103）

/// <summary>
/// 手机道具（Sketchfab CC-BY 淡蓝低模手机）：把转换好的 4 个 OBJ（按材质拆件）接进项目。
/// 做 4 件事（全程幂等）：
///   ① OBJ 导入设置：关材质导入（后面手工配 URP 材质，避免内置 Standard 在 URP 下洋红）。
///   ② 生成 4 个 URP Lit 材质（外壳淡蓝/屏幕黑/按键深灰/镜头白），颜色来自源 GLB baseColorFactor。
///   ③ 拼预置体 Assets/assets/06_道具_Props/手机/手机_淡蓝.prefab：根 + 外壳/屏幕/按键/镜头 四个子网格，
///      整棵放 Outline 层 —— 和其他角色/道具同款反向外壳描边（OutlineFeature 只画这层）。
///   ④ 渲染预览 assets/_报告/预览/道具/手机_淡蓝.png + 报告 assets/_报告/_手机道具.txt。
/// 来源与许可：Sketchfab 4ff99cf1 「Low Poly Mobile Phone」 by kimmy.k，CC-BY 4.0（★ 需在游戏致谢里署名）。
/// 几何已由 额外文件/工具脚本/glb2obj_phone.py 烘焙：0.081 × 0.016 × 0.155 m，屏幕朝 +Y 落地。
/// 用法：菜单 Tools/干预项目/手机道具/①，或丢 Assets/_phoneprop_trigger.txt。
/// </summary>
public static class PhonePropSetup
{
    const string DIR = "Assets/assets/06_道具_Props/手机";
    const string PREFAB = DIR + "/手机_淡蓝.prefab";
    const string REPORT = "Assets/assets/_报告/_手机道具.txt";
    const string PREVIEW_DIR = "Assets/assets/_报告/预览/道具";

    static readonly (string file, string mat, Color color, float smooth)[] PARTS =
    {
        ("手机_外壳", "手机_外壳", new Color(138f / 255, 173f / 255, 194f / 255), 0.35f),
        ("手机_屏幕", "手机_屏幕", new Color(0f, 0f, 0f), 0.55f),
        ("手机_按键", "手机_按键", new Color(70f / 255, 70f / 255, 70f / 255), 0.30f),
        ("手机_镜头", "手机_镜头", new Color(1f, 1f, 1f), 0.70f),
    };

    [MenuItem("Tools/干预项目/手机道具/① 导入设置+材质+预置体+预览（幂等）")]
    public static void RunMenu() { RunInternal(false); }

    [MenuItem("Tools/干预项目/手机道具/② 只诊断")]
    public static void DiagMenu() { RunInternal(true); }

    [MenuItem("Tools/干预项目/手机道具/③ 摆进宿舍+接第2/4章交互（幂等）")]
    public static void WireChapter2Menu() { WireChapter2Internal(); }

    public static void RunFromTrigger() { RunInternal(false); }
    public static void WireChapter2FromTrigger() { WireChapter2Internal(); }

    // ------------------------------------------------------------------ 主流程
    static void RunInternal(bool diagOnly)
    {
        var log = new List<string>();
        log.Add("手机道具导入" + (diagOnly ? "（只诊断，未改动）" : "") + "  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("");
        try
        {
            if (EditorApplication.isPlaying) { log.Add("★ 正在 Play 模式，退出后再跑。"); WriteReport(log); return; }

            // ① OBJ 就位检查 + 导入设置
            var missing = new List<string>();
            foreach (var (file, _, _, _) in PARTS)
                if (!File.Exists(DIR + "/" + file + ".obj")) missing.Add(file + ".obj");
            if (missing.Count > 0)
            {
                log.Add("★ 缺少 OBJ：" + string.Join("、", missing));
                log.Add("  先跑 额外文件/工具脚本/glb2obj_phone.py 生成。");
                WriteReport(log); return;
            }
            if (!diagOnly)
            {
                foreach (var (file, _, _, _) in PARTS)
                {
                    string path = DIR + "/" + file + ".obj";
                    var imp = (ModelImporter)AssetImporter.GetAtPath(path);
                    if (imp == null) { AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate); imp = (ModelImporter)AssetImporter.GetAtPath(path); }
                    if (imp.materialImportMode != ModelImporterMaterialImportMode.None)
                    {
                        imp.materialImportMode = ModelImporterMaterialImportMode.None;
                        imp.SaveAndReimport();
                    }
                }
                AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            }

            // ② 材质（URP Lit；已有就只校正颜色/光滑度）
            string matDir = DIR + "/材质";
            if (!diagOnly) Directory.CreateDirectory(matDir.Replace('/', Path.DirectorySeparatorChar));
            var mats = new Dictionary<string, Material>();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            foreach (var (file, matName, color, smooth) in PARTS)
            {
                string path = matDir + "/" + matName + ".mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!diagOnly)
                {
                    // CreateAsset 覆盖同路径 = 原地替换内容、GUID 不变；删了重建会换 GUID，
                    // 已保存的预制体/场景引用会断链（AGENTS.md 踩过的坑），别改回 DeleteAsset。
                    m = new Material(shader);
                    m.color = color;
                    if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
                    if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
                    AssetDatabase.CreateAsset(m, path);
                }
                m = m != null ? m : AssetDatabase.LoadAssetAtPath<Material>(path);
                mats[matName] = m;
                log.Add($"材质 {matName}: {(m != null ? m.shader.name : "缺失")}  颜色 #{ColorUtility.ToHtmlStringRGB(color)}");
            }

            // ③ 预置体（根 + 4 子网格，整棵 Outline 层）
            int outline = LayerMask.NameToLayer("Outline");
            if (outline < 0 && !diagOnly) outline = EnsureOutlineLayer();
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB);
            if (!diagOnly)
            {
                var go = new GameObject("手机_淡蓝");
                if (outline >= 0) go.layer = outline;          // 根也在 Outline：与其他道具「整棵子树」约定一致
                int tri = 0;
                var bounds = new Bounds();
                bool first = true;
                foreach (var (file, matName, _, _) in PARTS)
                {
                    var mesh = LoadMesh(file);
                    if (mesh == null) { log.Add($"★ {file}.obj 里没取到网格"); continue; }
                    var part = new GameObject(file);
                    part.transform.SetParent(go.transform, false);
                    var mf = part.AddComponent<MeshFilter>(); mf.sharedMesh = mesh;
                    var mr = part.AddComponent<MeshRenderer>(); mr.sharedMaterials = new[] { mats[matName] };
                    if (outline >= 0) part.layer = outline;
                    tri += mesh.triangles.Length / 3;
                    foreach (var v in mesh.vertices)
                    {
                        if (first) { bounds = new Bounds(v, Vector3.zero); first = false; }
                        else bounds.Encapsulate(v);
                    }
                }
                log.Add($"几何：{bounds.size.x:0.000} × {bounds.size.y:0.000} × {bounds.size.z:0.000} m，{tri} 三角，{PARTS.Length} 件");
                // SaveAsPrefabAsset 覆盖同路径 = 原地替换、GUID 不变；用户场景里可能已摆放实例，
                // DeleteAsset 重建会把实例断成 missing prefab —— 别改回 DeleteAsset。
                root = PrefabUtility.SaveAsPrefabAsset(go, PREFAB);
                Object.DestroyImmediate(go);
                log.Add("预置体：" + PREFAB + (root != null ? " ✓" : " ✗ 保存失败"));
            }
            log.Add($"Outline 层：{(outline >= 0 ? LayerMask.LayerToName(outline) + " (" + outline + ")" : "缺失（描边不生效）")}");

            // ④ 预览
            if (!diagOnly && root != null) RenderPreview(root, log);
            else if (diagOnly) log.Add("（诊断模式不渲染预览）");

            // 自检
            log.Add("");
            log.Add("自检：");
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB);
            if (pf == null) log.Add("  预置体：缺失 ✗");
            else
            {
                var rs = pf.GetComponentsInChildren<MeshRenderer>();
                var bad = rs.Where(r => r.sharedMaterials == null || r.sharedMaterials.Length == 0 || r.sharedMaterials.Any(mm => mm == null)).Count();
                int off = 0;
                foreach (var t in pf.GetComponentsInChildren<Transform>()) if (t.gameObject.layer != outline) off++;
                log.Add($"  渲染器 {rs.Length} 个，空材质 {bad} 个 {(bad == 0 ? "✓" : "✗")}");
                log.Add($"  层级：非 Outline 的节点 {off} 个 {(off == 0 && outline >= 0 ? "✓" : "✗")}");
                var meshCount = pf.GetComponentsInChildren<MeshFilter>().Count(mf => mf.sharedMesh != null);
                log.Add($"  网格 {meshCount}/4 {(meshCount == 4 ? "✓" : "✗")}");
            }
            log.Add("  许可：CC-BY 4.0（kimmy.k）★ 发布时需在致谢中署名");
        }
        catch (System.Exception e)
        {
            log.Add("★ 异常：" + e);
        }
        WriteReport(log);
    }

    // ============================================== ③ 手机 × 第2/4章「拿手机/点通知」交互接线
    // 用户需求（2026-09-28）：交互以场景里的 手机_淡蓝 为中心、半径收小要靠近才能按 F；
    // 按下后手机整棵隐藏（拿起的反馈），第2章第二次拿手机步骤到达时再回到桌上。
    // 判定中心/隐藏/重现都在 StoryInteractable.propObjectName（按名解析，手挪手机自动跟随）；
    // 本菜单只负责把场景接线写盘：半径 1.2m + 联动名 + 存场景。幂等。
    // 用户需求（2026-09-29）：第4章「点开通知看看吧」（第4章_班群通知）也改成同样机制——
    // 判定跟着同一部手机走（换章会重载场景，手机回到桌上，互不干扰）。
    const string PROP_NAME = "手机_淡蓝";
    const string CH2_LOC = "Loc_宿舍";
    const string CH2_ANCHOR_PATH = "多章锚点/第2章_手机";
    const string CH4_ANCHOR = "第4章_班群通知";
    const float CH2_RADIUS = 1.2f;
    const string GAME_SCENE = "Assets/Scenes/Game.unity";
    const string WIRE_REPORT = "Assets/assets/_报告/_手机交互.txt";

    static void WireChapter2Internal()
    {
        var log = new List<string>();
        log.Add("手机 × 第2/4章交互接线  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("");
        try
        {
            if (EditorApplication.isPlaying) { log.Add("★ 正在 Play 模式，退出后再跑。"); WriteWireReport(log); return; }

            // 场景：优先活动场景（用户手摆的手机多半还没存盘，先在打开的场景里找），其次已打开的 Game，最后才从盘上开
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != GAME_SCENE)
            {
                // 2022.3 的 EditorSceneManager 没有 GetOpenScenes——用 SceneManager.sceneCount/GetSceneAt 枚举已打开场景
                var opened = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).FirstOrDefault(s => s.path == GAME_SCENE);
                scene = opened.IsValid() ? opened : EditorSceneManager.OpenScene(GAME_SCENE, OpenSceneMode.Single);
            }
            log.Add("场景：" + scene.path + (scene.isDirty ? "（带未保存手改——会一起存盘）" : ""));

            var loc = FindInScene(scene, CH2_LOC);
            var anchor = loc != null ? loc.transform.Find(CH2_ANCHOR_PATH) : null;
            if (anchor == null)
            {
                log.Add("★ " + CH2_LOC + "/" + CH2_ANCHOR_PATH + " 不存在——先跑 Tools/干预项目/多章剧情/一键搭建第2-5章 再回来。");
                WriteWireReport(log); return;
            }

            // 手机实例：用户手摆的优先（整棵场景树按名找，含未激活）；没有才用预置体兜底摆在锚点处
            var phone = FindInScene(scene, PROP_NAME);
            if (phone == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB);
                if (prefab == null) { log.Add("★ 场景里没有「" + PROP_NAME + "」，预置体也缺失：" + PREFAB + "——先跑菜单①。"); WriteWireReport(log); return; }
                phone = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                phone.transform.SetParent(loc.transform, true);
                phone.transform.position = anchor.position;
                log.Add("★ 场景里没有「" + PROP_NAME + "」，已用预置体兜底摆在锚点处——请在 Scene 里手挪到桌上（工具不猜桌位）。");
            }
            else
                log.Add("手机实例：" + GetPath(phone.transform));
            if (!phone.activeSelf) { phone.SetActive(true); log.Add("  手机原本是隐藏的，已重新激活（首次交互前必须在桌上）。"); }

            var si = anchor.GetComponent<StoryInteractable>();
            if (si == null)
            {
                si = anchor.gameObject.AddComponent<StoryInteractable>();
                log.Add("  交互点缺 StoryInteractable，已补挂");
            }
            si.mode = StoryInteractable.Mode.InteractF;
            si.chapterTag = 2;
            si.oneShot = false;                                   // 第2章两次拿手机复用同一个点
            if (string.IsNullOrEmpty(si.promptText)) si.promptText = "拿起手机";
            si.radius = CH2_RADIUS;                               // 要靠近才能交互
            si.propObjectName = PROP_NAME;                        // 判定中心=手机位置；F 后隐藏；下次交互前重现
            EditorUtility.SetDirty(si);

            log.Add("");
            log.Add("接线：" + CH2_LOC + "/" + CH2_ANCHOR_PATH + "  半径=" + CH2_RADIUS.ToString("0.0") + "m  prompt=" + si.promptText + "  联动道具=" + PROP_NAME);
            log.Add("行为：走近手机 ≤1.2m 出提示 → 按 F 拿起（拿手机动画 + 手机隐藏）→ 第2章第二次拿手机步骤到达时手机回到桌上。");
            log.Add("提示文案/位置若被手调过：文案不动，半径与联动名按设计值覆盖（重跑本菜单即可恢复）。");

            // ---- 第4章「点开通知看看吧」（第4章_班群通知）：同一部手机、同一套机制（用户 2026-09-29）----
            var ch4Go = FindInScene(scene, CH4_ANCHOR);
            if (ch4Go == null)
            {
                log.Add("");
                log.Add("★ 找不到「" + CH4_ANCHOR + "」——先跑 Tools/干预项目/多章剧情/一键搭建第2-5章、第4/5章门口引导 再回来重跑本菜单。");
            }
            else
            {
                var si4 = ch4Go.GetComponent<StoryInteractable>();
                if (si4 == null)
                {
                    si4 = ch4Go.AddComponent<StoryInteractable>();
                    log.Add("  第4章交互点缺 StoryInteractable，已补挂");
                }
                si4.mode = StoryInteractable.Mode.InteractF;
                si4.chapterTag = 4;
                si4.oneShot = true;                               // 第4章只点开一次
                if (string.IsNullOrEmpty(si4.promptText)) si4.promptText = "点开通知";
                si4.radius = CH2_RADIUS;                          // 与第2章一致：以手机为中心 1.2m
                si4.propObjectName = PROP_NAME;                   // 判定中心=手机位置；F 后隐藏；重新武装时重现
                EditorUtility.SetDirty(si4);

                log.Add("");
                log.Add("接线：" + GetPath(ch4Go.transform) + "  半径=" + CH2_RADIUS.ToString("0.0") + "m  prompt=" + si4.promptText + "  联动道具=" + PROP_NAME);
                log.Add("行为：走近手机 ≤1.2m 出提示 → 按 F 点开通知（手机隐藏，后续班群台词走手机 UI、动画有自动补拿兜底）。");
                log.Add("位置说明：节点自身位置只是兜底——判定中心=手机实际位置（与第2章共用同一部 手机_淡蓝）。");
            }

            if (EditorSceneManager.SaveOpenScenes()) log.Add("场景已保存 ✓");
            else log.Add("★ 场景保存失败（手动 Ctrl+S 兜底）");
        }
        catch (System.Exception e) { log.Add("★ 异常：" + e); }
        WriteWireReport(log);
    }

    static GameObject FindInScene(Scene scene, string name)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var hit = FindRecursive(root.transform, name);
            if (hit != null) return hit.gameObject;
        }
        return null;
    }

    static Transform FindRecursive(Transform t, string name)
    {
        if (t.name == name) return t;
        for (int i = 0; i < t.childCount; i++)
        {
            var hit = FindRecursive(t.GetChild(i), name);
            if (hit != null) return hit;
        }
        return null;
    }

    static string GetPath(Transform t)
    {
        var sb = new StringBuilder(t.name);
        while (t.parent != null) { t = t.parent; sb.Insert(0, t.name + "/"); }
        return sb.ToString();
    }

    static void WriteWireReport(List<string> log)
    {
        var sb = new StringBuilder();
        sb.AppendLine("手机 × 第2章交互接线");
        sb.AppendLine(new string('=', 46));
        sb.AppendLine();
        foreach (var l in log) sb.AppendLine(l);
        Directory.CreateDirectory("Assets/assets/_报告".Replace('/', Path.DirectorySeparatorChar));
        File.WriteAllText(WIRE_REPORT.Replace('/', Path.DirectorySeparatorChar), sb.ToString(), new UTF8Encoding(false));
        Debug.Log("[PhonePropSetup] 接线报告 → " + WIRE_REPORT);
    }

    // --------------------------------------------------------------- 工具
    // OBJ 的 Mesh 是子资产，主资产是 GameObject —— 必须按类型枚举子资产取
    static Mesh LoadMesh(string file)
    {
        return AssetDatabase.LoadAllAssetsAtPath(DIR + "/" + file + ".obj").OfType<Mesh>().FirstOrDefault();
    }

    // --------------------------------------------------------------- Outline 层兜底
    static int EnsureOutlineLayer()
    {
        var assets = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = assets.FindProperty("layers");
        for (int i = 8; i < 32; i++)
        {
            var sp = layers.GetArrayElementAtIndex(i);
            if (sp.stringValue == "Outline") return i;
            if (string.IsNullOrEmpty(sp.stringValue)) { sp.stringValue = "Outline"; assets.ApplyModifiedProperties(); return i; }
        }
        return -1;
    }

    // ------------------------------------------------------------------ 预览渲染
    // ⚠ 编辑器的 cam.Render() 剔除是「全局按层」的，不管相机在哪个场景 —— 首版用 Outline+Default 层
    //   直接把打开着的 Game 场景（角色脚、地板）拍进了预览图。学 CharPreview：用隔离层 31 只画道具。
    const int PREVIEW_LAYER = 31;

    static void RenderPreview(GameObject prefab, List<string> log)
    {
        Directory.CreateDirectory(PREVIEW_DIR.Replace('/', Path.DirectorySeparatorChar));
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            var camGO = new GameObject("prev_cam"); SceneManager.MoveGameObjectToScene(camGO, scene);
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.87f, 0.90f, 0.93f);
            cam.fieldOfView = 30f;
            cam.cullingMask = 1 << PREVIEW_LAYER;

            var keyGO = new GameObject("prev_key"); SceneManager.MoveGameObjectToScene(keyGO, scene);
            var key = keyGO.AddComponent<Light>(); key.type = LightType.Directional; key.intensity = 1.1f;
            keyGO.transform.rotation = Quaternion.Euler(40, 210, 0);
            key.cullingMask = 1 << PREVIEW_LAYER;
            var fillGO = new GameObject("prev_fill"); SceneManager.MoveGameObjectToScene(fillGO, scene);
            var fill = fillGO.AddComponent<Light>(); fill.type = LightType.Directional; fill.intensity = 0.4f;
            fillGO.transform.rotation = Quaternion.Euler(20, 30, 0);
            fill.cullingMask = 1 << PREVIEW_LAYER;

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            foreach (var t in inst.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = PREVIEW_LAYER;
            var bounds = new Bounds(inst.transform.position, Vector3.zero);
            bool has = false;
            foreach (var r in inst.GetComponentsInChildren<MeshRenderer>())
            {
                if (!has) { bounds = r.bounds; has = true; } else bounds.Encapsulate(r.bounds);
            }

            var rt = new RenderTexture(800, 800, 24);
            cam.targetTexture = rt;
            Vector3 dir = new Vector3(0.9f, 0.75f, 1.2f).normalized;   // 3/4 俯视
            float radius = Mathf.Max(bounds.size.magnitude * 0.6f, 0.05f);
            float dist = radius / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) + radius;
            cam.transform.position = bounds.center + dir * dist;
            cam.transform.LookAt(bounds.center);
            cam.Render(); cam.Render();                                 // 首轮 warm shader（描边/shader 编译）
            RenderTexture.active = rt;
            var tex = new Texture2D(800, 800, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 800, 800), 0, 0);
            tex.Apply();
            RenderTexture.active = null; cam.targetTexture = null;
            string png = PREVIEW_DIR + "/手机_淡蓝.png";
            File.WriteAllBytes(png.Replace('/', Path.DirectorySeparatorChar), tex.EncodeToPNG());
            Object.DestroyImmediate(tex); Object.DestroyImmediate(rt);
            Object.DestroyImmediate(inst);
            log.Add("预览：" + png);
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    // ------------------------------------------------------------------ 报告
    static void WriteReport(List<string> log)
    {
        var sb = new StringBuilder();
        sb.AppendLine("手机道具（Sketchfab CC-BY 淡蓝低模手机）");
        sb.AppendLine(new string('=', 46));
        sb.AppendLine();
        foreach (var l in log) sb.AppendLine(l);
        sb.AppendLine();
        sb.AppendLine("来源：Sketchfab 4ff99cf17b164e7b9790638c5d2ef4ce「Low Poly Mobile Phone」by kimmy.k");
        sb.AppendLine("许可：CC-BY 4.0（★ 发布/商用需署名作者 kimmy.k）");
        sb.AppendLine("转换：额外文件/工具脚本/glb2obj_phone.py（GLB sha256 49dc219c…）");
        sb.AppendLine("约定：与其他道具一致 —— 整棵 Outline 层吃全局描边；URP Lit；无碰撞体（桌面道具不需要）");
        Directory.CreateDirectory("Assets/assets/_报告".Replace('/', Path.DirectorySeparatorChar));
        File.WriteAllText(REPORT.Replace('/', Path.DirectorySeparatorChar), sb.ToString(), new UTF8Encoding(false));
        Debug.Log("[PhonePropSetup] 报告 → " + REPORT);
    }
}

// 工程里存在 Assets/_phoneprop_trigger.txt 时，编辑器下次刷新/重编译后自动跑一次（同 SceneColliders 的路子）。
[InitializeOnLoad]
public static class PhonePropSetupTrigger
{
    const string Trigger = "Assets/_phoneprop_trigger.txt";
    const string ErrFile = "../额外文件/错误_手机道具.txt";

    static PhonePropSetupTrigger()
    {
        if (!File.Exists(Trigger)) return;
        EditorApplication.delayCall += () =>
        {
            try
            {
                if (File.Exists(Trigger)) File.Delete(Trigger);
                if (File.Exists(Trigger + ".meta")) File.Delete(Trigger + ".meta");
                PhonePropSetup.RunFromTrigger();
            }
            catch (System.Exception e)
            {
                Directory.CreateDirectory("../额外文件");
                File.WriteAllText(ErrFile, e.ToString());
                Debug.LogError("[PhonePropSetup] 触发失败: " + e);
            }
        };
    }
}

// 工程里存在 Assets/_phonech2_trigger.txt 时，编辑器下次刷新/重编译后自动跑一次「手机×第2章交互接线」。
[InitializeOnLoad]
public static class PhoneCh2WireTrigger
{
    const string Trigger = "Assets/_phonech2_trigger.txt";
    const string ErrFile = "../额外文件/错误_手机交互接线.txt";

    static PhoneCh2WireTrigger()
    {
        if (!File.Exists(Trigger)) return;
        EditorApplication.delayCall += () =>
        {
            try
            {
                if (File.Exists(Trigger)) File.Delete(Trigger);
                if (File.Exists(Trigger + ".meta")) File.Delete(Trigger + ".meta");
                PhonePropSetup.WireChapter2FromTrigger();
            }
            catch (System.Exception e)
            {
                Directory.CreateDirectory("../额外文件");
                File.WriteAllText(ErrFile, e.ToString());
                Debug.LogError("[PhonePropSetup] 手机交互接线触发失败: " + e);
            }
        };
    }
}
