// ============================================================================
// 带动画模型：把角色材质「贴回去」
//
// 背景：Mixamo 重新绑定后导回的 FBX 只带材质【名字】（skin_mat_base_F_body 这类），
//       材质本身是嵌在模型里的空壳（没贴图、没走 ShaderGraph_CharacterLit）→ 白模。
//       角色真正在用的材质在 02_角色_Character/角色_URP/材质/<角色>/（一人一套，RGB 遮罩 + 角色 Shader）。
//
// 做法：把 FBX 导入器的「材质重映射」（externalObjects）写上去：
//          FBX 内材质名  →  角色材质资产
//       这样以后任何地方实例化这个 FBX，材质都是对的，不用逐个场景手接。
//       （不复制材质 —— 和 角色_URP/*_可动.prefab 共用同一份，一人一色仍只有一处真源）
//
// ⚠ Unity 2022.3 没有 SetExternalObjectMap / AddRemappedAsset 这类公开 API，
//   改 GetExternalObjectMap() 返回的字典再 SaveAndReimport 【不会】落盘（实测 .meta 里还是 0 条）。
//   所以这里用两条路：
//     ① 反射调用内部的 AssetImporter.AddRemap（原生绑定，存在就用它）
//     ② 直接改 .meta 里的 externalObjects 块 + AssetDatabase.ImportAsset(ForceUpdate)
//   两条都做完会读回 .meta 复核条数，报告里写明走的是哪条。
//
// 名字怎么对上的：Mixamo 保留了导出时的材质名（Unity 导出时把 . 空格 - 都换成 _），
//       所以「清洗(FBX 内材质名) == 清洗(.mat 文件名)」即可对上；
//       角色自己的实例材质优先，其次是套件共享材质（眼睛/嘴/高光/眼镜）。
//
// 菜单：Tools/干预项目/带动画模型：贴回角色材质
// 报告：Assets/assets/_报告/_带动画模型材质.txt
// ============================================================================
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class AnimModelMats
{
    const string ROOT = "Assets/assets/03_动作_Animation/带动画模型";
    const string CHAR_ROOT = "Assets/assets/02_角色_Character";
    const string INST_MATS = CHAR_ROOT + "/角色_URP/材质/";     // 角色实例材质
    const string KIT_MATS = CHAR_ROOT + "/Materials/";          // 套件共享材质（眼睛/嘴/眼镜…）
    const string REPORT = "Assets/assets/_报告/_带动画模型材质.txt";

    static MethodInfo _addRemap;
    static string _remapProbe = "（未探测）";

    [MenuItem("Tools/干预项目/带动画模型：贴回角色材质", false, 111)]
    public static void Run()
    {
        var log = new List<string>();
        log.Add("带动画模型：贴回角色材质  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("做法：写 FBX 导入器的 Material Remap（externalObjects），不是改场景实例");
        log.Add("根目录：" + ROOT);
        log.Add("");

        if (!Directory.Exists(ROOT)) { log.Add("★ 找不到 " + ROOT); Flush(log); return; }

        var idx = BuildMaterialIndex();
        log.Add("材质索引：清洗后名字 " + idx.Count + " 组（角色实例 + 套件共享）");
        log.Add("");

        int nFbx = 0, nSlot = 0, nMapped = 0, nNull = 0, nUnresolved = 0, nRewritten = 0;
        var unresolved = new List<string>();
        var badSlots = new List<string>();
        var texMissing = new List<string>();

        foreach (var dir in Directory.GetDirectories(ROOT).OrderBy(p => p))
        {
            string ch = Path.GetFileName(dir);
            log.Add("════ " + ch);
            var files = Directory.GetFiles(dir, "*.fbx").OrderBy(p => p).ToList();
            if (files.Count == 0) log.Add("   （空目录）");

            foreach (var f in files)
            {
                string path = f.Replace('\\', '/');
                string fn = Path.GetFileName(path);
                nFbx++;
                var mi = AssetImporter.GetAtPath(path) as ModelImporter;
                if (mi == null) { log.Add("   ★ " + fn + "：拿不到 ModelImporter（还没导入？）"); continue; }

                log.Add("   ── " + fn);

                // 1) FBX 里的材质名：优先解析文件本体（顺序=文件里的顺序），解析不出来才用 Unity 侧名字兜底
                var names = FbxMaterialNames(path);
                string src = "文件";
                if (names.Count == 0)
                {
                    names = RendererMaterialNames(path);
                    src = "Unity 侧兜底";
                }
                if (names.Count == 0) { log.Add("      ★ 这个 FBX 里没有材质"); continue; }

                // 2) 逐个材质名找目标材质（角色实例优先）
                var pairs = new List<KeyValuePair<string, string>>();      // FBX 内名字 → .mat 资产路径
                var plan = new List<string>();
                foreach (var raw in names)
                {
                    nSlot++;
                    string target, how;
                    // ★ 已经「去掉部件」过的角色（陆宣雨的眼镜）：这个槽保持【隐形材质】，
                    //   别再贴回真眼镜材质 —— 否则眼镜又冒出来（踩过：HidePart 先跑、本工具后跑）
                    if (HidePart.IsHiddenPart(ch, raw))
                    {
                        target = HidePart.HIDE_MAT;
                        how = "隐藏";
                    }
                    else Resolve(ch, raw, idx, out target, out how);
                    if (target == null)
                    {
                        nUnresolved++;
                        unresolved.Add(ch + " / " + fn + " / " + raw);
                        plan.Add(string.Format("      ★ {0,-38} → 找不到对应材质", raw));
                        continue;
                    }
                    pairs.Add(new KeyValuePair<string, string>(raw, target));
                    nMapped++;
                    plan.Add(string.Format("      {0,-38} → {1,-6} {2}", raw, how, Path.GetFileName(target)));
                }

                // 3) 该不该改：.meta 里的条数对不上就得重写
                int before = MetaMapCount(path);
                bool changed = before != pairs.Count;

                if (changed)
                {
                    string used = ApplyMap(mi, path, pairs);
                    nRewritten++;
                    log.Add("      材质名来源：" + src + "（" + names.Count + " 个）");
                    log.Add("      → 写入方式：" + used);
                }
                else
                {
                    log.Add("      材质名来源：" + src + "（" + names.Count + " 个）");
                    log.Add("      → .meta 里已有 " + before + " 条映射，跳过");
                }
                log.Add("      .meta 复核：externalObjects 里 " + MetaMapCount(path) + " 条");
                foreach (var line in plan) log.Add(line);

                // 4) 自检：模型上每个材质槽到底接了什么 + 贴图有没有断
                var check = Verify(path, ch, pairs, texMissing);
                nNull += check.nullSlots;
                foreach (var b in check.bad) badSlots.Add(ch + " / " + fn + " / " + b);
                log.Add("      " + check.line);

                // 5) 顺手报一下导入设置（材质之外的现状，方便下一步）
                var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                                .Where(c => !c.name.StartsWith("__preview__")).ToList();
                log.Add(string.Format("      导入设置：animationType={0}  avatarSetup={1}  importAnimation={2}  剪辑 {3} 条{4}",
                    mi.animationType, mi.avatarSetup, mi.importAnimation, clips.Count,
                    clips.Count > 0 ? "（" + string.Join("、", clips.Select(c => c.name + " " + c.length.ToString("0.0") + "s")) + "）" : ""));
            }
            log.Add("");
        }

        log.Add("════ 汇总");
        log.Add(string.Format("  FBX {0} 个；材质槽 {1} 个；已映射 {2} 个；重写过映射的 FBX {3} 个", nFbx, nSlot, nMapped, nRewritten));
        log.Add("  找不到对应材质的：" + nUnresolved + (nUnresolved == 0 ? " ✓" : ""));
        foreach (var u in unresolved) log.Add("      ★ " + u);
        log.Add("  空材质槽 / 洋红：" + nNull + (nNull == 0 ? " ✓" : ""));
        foreach (var b in badSlots) log.Add("      ★ " + b);
        log.Add("  材质贴图（_BaseMap 遮罩）缺失：" + texMissing.Count + (texMissing.Count == 0 ? " ✓" : ""));
        foreach (var t in texMissing.Distinct()) log.Add("      ★ " + t);
        log.Add("");
        log.Add("机制自检：" + _remapProbe);
        log.Add("");
        log.Add("备注：");
        log.Add("  · 材质是【共用】的（和 角色_URP/*_可动.prefab 同一份）—— 改颜色两边一起变，这是故意的。");
        log.Add("  · 只改导入器的材质重映射，不动网格 / 骨骼 / 动画 / 场景。");
        log.Add("  · FBX 里材质名 = 导出时的 Unity 材质名（. 空格 - 都变成 _），所以能一一对上。");
        Flush(log);
    }

    // ============================================================ 写映射
    // 返回实际生效的那条路（报告里要写）
    static string ApplyMap(ModelImporter mi, string path, List<KeyValuePair<string, string>> pairs)
    {
        // ① 反射调用内部的 AssetImporter.AddRemap（原生绑定）
        bool viaReflect = false;
        if (FindAddRemap())
        {
            foreach (var kv in pairs)
            {
                var mb = AssetDatabase.LoadAssetAtPath<Material>(kv.Value);
                if (mb == null) continue;
                try
                {
                    var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key);
                    _addRemap.Invoke(mi, new object[] { id, mb });
                    viaReflect = true;
                }
                catch (System.Exception e) { _remapProbe = "AddRemap 调用失败：" + e.Message; viaReflect = false; break; }
            }
            if (viaReflect)
            {
                mi.SaveAndReimport();
                if (MetaMapCount(path) == pairs.Count)
                {
                    _remapProbe = "走内部 API AssetImporter.AddRemap(反射)：可用 ✓";
                    return "内部 API AddRemap（反射）+ 重导";
                }
                _remapProbe = "AddRemap 能调，但 .meta 没落盘（" + MetaMapCount(path) + " 条）→ 改走写 .meta";
            }
        }
        else
        {
            _remapProbe = "2022.3 里没有可用的 AddRemap：" + _remapProbe;
        }

        // ② 直接改 .meta 的 externalObjects 块，再强制重导
        bool ok = WriteMetaExternalObjects(path, pairs);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        int after = MetaMapCount(path);
        return (ok ? "写 .meta 的 externalObjects" : "★ 写 .meta 失败") + " + 强制重导 → 复核 " + after + " 条"
             + (after == pairs.Count ? " ✓" : " ★");
    }

    static bool FindAddRemap()
    {
        if (_addRemap != null) return true;
        var t = typeof(AssetImporter);
        var cands = t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                     .Where(m => m.Name == "AddRemap" || m.Name == "AddRemappedAsset").ToList();
        _addRemap = cands.OrderByDescending(m => m.GetParameters().Length == 2).FirstOrDefault();
        if (_addRemap == null)
        {
            var all = t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                       .Where(m => m.Name.Contains("Remap") || m.Name.Contains("External"))
                       .Select(m => m.Name + "(" + string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name).ToArray()) + ")");
            _remapProbe = "AssetImporter 上带 Remap/External 的方法：" + string.Join("、", all.Distinct().ToArray());
        }
        return _addRemap != null;
    }

    // 把 .meta 里的 externalObjects 块整块替换成我们的映射
    static bool WriteMetaExternalObjects(string assetPath, List<KeyValuePair<string, string>> pairs)
    {
        try
        {
            string meta = assetPath + ".meta";
            if (!File.Exists(meta)) return false;
            var lines = File.ReadAllLines(meta).ToList();
            int start = lines.FindIndex(l => l.StartsWith("  externalObjects:"));
            if (start < 0) return false;

            // 块的范围：从 externalObjects 起，直到下一个"2 空格缩进的键"（- first: / 4 空格缩进都算块内）
            int end = start + 1;
            while (end < lines.Count)
            {
                string l = lines[end];
                bool inBlock = l.StartsWith("  -") || l.StartsWith("    ");
                if (!inBlock) break;
                end++;
            }

            var block = new List<string>();
            if (pairs.Count == 0) block.Add("  externalObjects: {}");
            else
            {
                block.Add("  externalObjects:");
                foreach (var kv in pairs)
                {
                    string guid = AssetDatabase.AssetPathToGUID(kv.Value);
                    if (string.IsNullOrEmpty(guid)) continue;
                    block.Add("  - first:");
                    block.Add("      type: UnityEngine:Material");
                    block.Add("      assembly: UnityEngine.CoreModule");
                    block.Add("      name: " + kv.Key);
                    block.Add("    second: {fileID: 2100000, guid: " + guid + ", type: 2}");
                }
            }
            lines.RemoveRange(start, end - start);
            lines.InsertRange(start, block);
            File.WriteAllText(meta, string.Join("\n", lines.ToArray()) + "\n", new UTF8Encoding(false));
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[AnimModelMats] 写 .meta 失败：" + e.Message);
            return false;
        }
    }

    // ============================================================ 名字解析
    static Dictionary<string, List<string>> BuildMaterialIndex()
    {
        var idx = new Dictionary<string, List<string>>();
        foreach (var g in AssetDatabase.FindAssets("t:Material", new[] { CHAR_ROOT }))
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            if (string.IsNullOrEmpty(p) || !p.EndsWith(".mat")) continue;
            string key = San(Path.GetFileNameWithoutExtension(p));
            List<string> l;
            if (!idx.TryGetValue(key, out l)) { l = new List<string>(); idx[key] = l; }
            l.Add(p);
        }
        return idx;
    }

    // Unity 的资产名清洗：. 空格 - 都变下划线（Mixamo 往返之后名字就长这样）
    static string San(string s) { return Regex.Replace(s, "[. \\-]", "_"); }

    static void Resolve(string ch, string fbxName, Dictionary<string, List<string>> idx,
                        out string target, out string how)
    {
        target = null; how = null;
        List<string> cands;
        if (!idx.TryGetValue(San(fbxName), out cands) || cands.Count == 0) return;

        string own = INST_MATS + ch + "/";
        var hit = cands.FirstOrDefault(p => p.StartsWith(own));
        if (hit != null) { target = hit; how = "角色实例"; return; }

        var kit = cands.FirstOrDefault(p => p.StartsWith(KIT_MATS));
        if (kit != null) { target = kit; how = "套件共享"; return; }

        target = cands.OrderBy(p => p.Length).First();     // 同名跨角色（理论上不会走到）
        how = "★同名歧义";
    }

    // ============================================================ 读 FBX 里的材质名
    static List<string> FbxMaterialNames(string path)
    {
        var res = new List<string>();
        try
        {
            var bytes = File.ReadAllBytes(path);
            // 二进制 FBX 头就是 20 字节 "Kaydara FBX Binary  "（两个空格）
            bool binary = bytes.Length > 20 && Encoding.ASCII.GetString(bytes, 0, 18) == "Kaydara FBX Binary";
            if (binary)
            {
                var pat = Encoding.ASCII.GetBytes("\u0000\u0001Material");
                for (int i = 0; i + pat.Length <= bytes.Length; i++)
                {
                    if (bytes[i] != 0) continue;
                    bool ok = true;
                    for (int q = 1; q < pat.Length; q++) if (bytes[i + q] != pat[q]) { ok = false; break; }
                    if (!ok) continue;
                    // 名字在它前面，按不可见字符切，取最后一段长度 ≥3 的
                    int s = Mathf.Max(0, i - 200);
                    string seg = Encoding.UTF8.GetString(bytes, s, i - s);
                    var parts = Regex.Split(seg, "[\\x00-\\x1f]+");
                    for (int q = parts.Length - 1; q >= 0; q--)
                        if (parts[q].Length >= 3) { res.Add(parts[q]); break; }
                }
            }
            else
            {
                var txt = Encoding.UTF8.GetString(bytes);
                foreach (Match m in Regex.Matches(txt, "Material:\\s*\\d+,\\s*\"Material::([^\"]+)\""))
                    res.Add(m.Groups[1].Value);
            }
        }
        catch (System.Exception e) { Debug.LogWarning("[AnimModelMats] 解析 " + path + " 失败：" + e.Message); }
        return res.Distinct().ToList();
    }

    // Unity 侧看到的材质名（兜底用）
    static List<string> RendererMaterialNames(string path)
    {
        var res = new List<string>();
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (go == null) return res;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            foreach (var m in r.sharedMaterials)
                if (m != null && !res.Contains(m.name)) res.Add(m.name);
        return res;
    }

    // ============================================================ 自检
    // 直接读 .meta，数一下 externalObjects 到底写进去几条（和 Unity 内部状态无关的硬证据）
    static int MetaMapCount(string assetPath)
    {
        try
        {
            string meta = assetPath + ".meta";
            if (!File.Exists(meta)) return -1;
            return Regex.Matches(File.ReadAllText(meta), "    second:").Count;
        }
        catch { return -1; }
    }

    struct Check { public int nullSlots; public List<string> bad; public string line; }

    static Check Verify(string path, string ch, List<KeyValuePair<string, string>> pairs, List<string> texMissing)
    {
        var c = new Check { nullSlots = 0, bad = new List<string>(), line = "" };
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (go == null) { c.line = "★ 自检：读不到模型资产"; return c; }

        var expect = new HashSet<Material>();
        foreach (var kv in pairs)
        {
            var mb = AssetDatabase.LoadAssetAtPath<Material>(kv.Value);
            if (mb == null) { texMissing.Add("材质读不到：" + kv.Value); continue; }
            expect.Add(mb);
        }
        // 角色材质靠 _BaseMap 遮罩出颜色，遮罩丢了就是一片纯色
        // （眉毛/睫毛/眼睛/高光这几类本来就是纯色材质，没有遮罩是正常的）
        string[] needMask = { "skin_", "top_", "bot_", "hair_", "shoes_", "acc_", "outfit_" };
        foreach (var mb in expect)
        {
            if (!needMask.Any(p => mb.name.StartsWith(p))) continue;
            var tex = mb.HasProperty("_BaseMap") ? mb.GetTexture("_BaseMap") : null;
            if (tex == null) texMissing.Add(mb.name + "（" + AssetDatabase.GetAssetPath(mb) + "）的 _BaseMap 为空");
        }

        int slots = 0, okc = 0, renderers = 0, submeshes = 0;
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            renderers++;
            if (smr.sharedMesh != null) submeshes += smr.sharedMesh.subMeshCount;
            foreach (var m in smr.sharedMaterials)
            {
                slots++;
                if (m == null) { c.nullSlots++; c.bad.Add("空材质槽（renderer " + smr.name + "）"); continue; }
                if (m.shader == null || m.shader.name.Contains("Error") || m.shader.name.Contains("InternalErrorShader"))
                { c.nullSlots++; c.bad.Add("材质没有可用 Shader：" + m.name); continue; }
                if (expect.Contains(m)) okc++;
                else c.bad.Add("槽上是意料之外的材质：" + m.name);
            }
        }
        string tail = (c.nullSlots == 0 && c.bad.Count == 0) ? "✓" : "★ 见汇总";
        c.line = string.Format("自检：渲染器 {0} 个 / 子网格 {1} / 材质槽 {2}，已接上角色材质 {3}，空槽 {4}  {5}",
            renderers, submeshes, slots, okc, c.nullSlots, tail);
        return c;
    }

    // ============================================================ 预览渲染（看材质对不对）
    // 和 CharPreview 同一套机位/打光，出的图和 assets/_报告/预览/<角色>_可动.png 可以并排比
    const string PREVIEW_DIR = "Assets/assets/_报告/预览/带动画模型";
    const string PREVIEW_REPORT = "Assets/assets/_报告/_带动画模型预览.txt";
    const int LAYER = 31;

    [MenuItem("Tools/干预项目/带动画模型：渲染预览（看材质）", false, 112)]
    public static void RenderPreview()
    {
        var log = new List<string>();
        log.Add("带动画模型预览  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("每个角色渲染 已绑定.fbx（rest 姿势，材质走导入器重映射） → " + PREVIEW_DIR);
        log.Add("对照：assets/_报告/预览/<角色>_可动.png（原模型同一机位）");
        log.Add("");
        Directory.CreateDirectory(PREVIEW_DIR);

        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var camGO = new GameObject("prev_cam"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGO, scene);
        var cam = camGO.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.18f, 0.20f, 0.24f);
        cam.cullingMask = 1 << LAYER;
        cam.fieldOfView = 32f;
        var rt = new RenderTexture(512, 640, 24);

        var lightGO = new GameObject("prev_light"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightGO, scene);
        var lt = lightGO.AddComponent<Light>();
        lt.type = LightType.Directional; lt.intensity = 1.1f; lt.cullingMask = 1 << LAYER;
        lightGO.transform.rotation = Quaternion.Euler(35, 200, 0);

        var jobs = new List<KeyValuePair<string, string>>();      // 角色 → fbx 路径
        foreach (var dir in Directory.GetDirectories(ROOT).OrderBy(p => p))
        {
            string ch = Path.GetFileName(dir);
            var fs = Directory.GetFiles(dir, "*.fbx");
            string pick = fs.FirstOrDefault(p => Path.GetFileName(p) == "已绑定.fbx");
            if (pick == null) pick = fs.OrderBy(p => p).FirstOrDefault();
            if (pick == null) { log.Add("★ " + ch + "：没有 FBX"); continue; }
            jobs.Add(new KeyValuePair<string, string>(ch, pick.Replace('\\', '/')));
        }

        // 第一轮：先把每个模型渲一遍，触发 shader 变体编译（不然会拍到洋红）
        foreach (var j in jobs)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(j.Value);
            if (asset == null) continue;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
            SetLayer(inst, LAYER);
            FrameCamera(cam, inst);
            cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
            Object.DestroyImmediate(inst);
        }
        while (ShaderUtil.anythingCompiling) System.Threading.Thread.Sleep(50);
        AssetDatabase.Refresh();

        foreach (var j in jobs)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(j.Value);
            if (asset == null) { log.Add("★ " + j.Key + "：读不到 " + j.Value); continue; }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
            inst.name = "~prev";
            SetLayer(inst, LAYER);
            float h = FrameCamera(cam, inst);

            cam.targetTexture = rt; cam.Render(); cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(512, 640, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 512, 640), 0, 0);
            tex.Apply();
            RenderTexture.active = null; cam.targetTexture = null;

            // 顺手记一下这个实例上到底挂了哪些材质（图 + 数据双证）
            var mats = new List<string>();
            foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                foreach (var m in smr.sharedMaterials)
                    mats.Add(m != null ? m.name : "（空）");

            string outPath = PREVIEW_DIR + "/" + j.Key + "_已绑定.png";
            File.WriteAllBytes(outPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(inst);
            log.Add(string.Format("  {0,-8} 高 {1:0.00}m  材质 {2} 个：{3}", j.Key, h, mats.Count, string.Join("、", mats)));
            log.Add("        → " + outPath);
        }

        UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
        Directory.CreateDirectory("Assets/assets/_报告");
        File.WriteAllText(PREVIEW_REPORT, string.Join("\n", log.ToArray()));
        AssetDatabase.Refresh();
        Debug.Log("[AnimModelMats] 预览报告：" + PREVIEW_REPORT);
    }

    static void SetLayer(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
    }

    static float FrameCamera(Camera cam, GameObject inst)
    {
        var rends = inst.GetComponentsInChildren<Renderer>(true);
        Bounds b = new Bounds(inst.transform.position + Vector3.up * 1.0f, Vector3.one * 0.2f);
        bool first = true;
        foreach (var r in rends)
        {
            if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
        }
        float h = Mathf.Max(b.size.y, 1.6f);
        cam.transform.position = b.center + new Vector3(0f, 0.08f * h, -h * 1.75f);
        cam.transform.LookAt(b.center + Vector3.up * 0.03f * h);
        return h;
    }

    static void Flush(List<string> log)
    {
        Directory.CreateDirectory("Assets/assets/_报告");
        File.WriteAllText(REPORT, string.Join("\n", log.ToArray()));
        AssetDatabase.Refresh();
        Debug.Log("[AnimModelMats] 报告：" + REPORT);
    }
}

[InitializeOnLoad]
static class AnimModelMatsTrigger
{
    const string T = "Assets/_animmodelmats_trigger.txt";
    static AnimModelMatsTrigger()
    {
        if (!File.Exists(T)) return;
        EditorApplication.delayCall += () =>
        {
            try
            {
                if (File.Exists(T)) File.Delete(T);
                AnimModelMats.Run();
                AnimModelMats.RenderPreview();
            }
            catch (System.Exception e)
            {
                Debug.LogError("[AnimModelMats] " + e);
                Directory.CreateDirectory("../额外文件");
                File.WriteAllText("../额外文件/错误_带动画模型材质.txt", e.ToString());
            }
        };
    }
}
