// ============================================================================
// 角色：去掉某个部件（目前用于「去掉陆宣雨的眼镜」）
//
// 为什么不是换个透明材质就完事：
//   · 带动画模型里整身是【一块合并网格 + 多个子网格】，眼镜 = 其中一个子网格；
//   · 描边（OutlineFeature）是给「对象的所有子网格」各画一层外壳的，跟材质无关
//     → 就算把眼镜材质换成全透明，黑框外壳还在。
//   所以主做法是【把那个子网格的三角形清空】：拷一份网格资产出来，把眼镜子网格置空，
//   再把场景实例的 SkinnedMeshRenderer.sharedMesh 换成它（其余子网格原样保留）。
//
// 另外还会把 FBX 导入器里那个材质映射成「隐藏部件_不渲染」（全透明、不写深度）：
//   这样【新拖进来的实例】、工程窗口缩略图、带动画模型预览渲染也都不再显示眼镜
//   （清空网格只管已经摆在场景里的那些实例）。
//
// 还会把源装配表（CharRebuild 的 Glasses 字段）清掉，以后「重建角色模型」出来也没眼镜。
//
// 菜单：Tools/干预项目/角色：去掉部件（眼镜）        触发器：Assets/_hidepart_trigger.txt
// 报告：Assets/assets/_报告/_去掉部件.txt
// ============================================================================
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class HidePart
{
    // 要去掉眼镜的角色（想加别人就往这里加名字）
    static readonly string[] TARGETS = { "陆宣雨" };
    const string KEYWORD = "glasses";                       // 材质名里含这个就当眼镜
    const string MODEL_DIR = "Assets/assets/03_动作_Animation/带动画模型";
    const string OUT_DIR = "Assets/assets/02_角色_Character/角色_URP/去部件";
    const string HIDE_MAT = OUT_DIR + "/隐藏部件_不渲染.mat";
    const string REPORT = "Assets/assets/_报告/_去掉部件.txt";

    [MenuItem("Tools/干预项目/角色：去掉部件（眼镜）", false, 150)]
    public static void Run()
    {
        var log = new List<string>();
        log.Add("角色去掉部件（关键字 " + KEYWORD + "）  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("");

        Directory.CreateDirectory(OUT_DIR);
        var hideMat = EnsureHideMaterial();
        log.Add("隐形材质：" + HIDE_MAT);
        log.Add("");
        log.Add("── 1) 模型：清空子网格 + 改 FBX 导入器材质映射");
        var meshMap = new Dictionary<Mesh, Mesh>();          // 源网格 → 去掉部件后的网格

        foreach (var ch in TARGETS)
        {
            string dir = MODEL_DIR + "/" + ch;
            bool exists = Directory.Exists(dir);
            var files = exists ? Directory.GetFiles(dir, "*.fbx").OrderBy(p => p).ToArray() : new string[0];
            log.Add(string.Format("  诊断：目录「{0}」存在={1}，FBX {2} 个", dir, exists, files.Length));
            if (!exists) continue;
            foreach (var fbx in files)
            {
                string path = fbx.Replace('\\', '/');
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) { log.Add("  ★ 读不到 " + path); continue; }
                bool didMesh = false;
                // ★ 槽位用【FBX 文件里的材质名顺序】定位：导入器里的材质可能已经被映射成隐形了，
                //   再按材质名去找会找不到（踩过）。FBX 里材质对象顺序 = 子网格顺序。
                var innerNames = FbxMaterialNames(path);
                int slotByFile = innerNames.FindIndex(n => n.ToLower().Contains(KEYWORD));
                log.Add(string.Format("  诊断：{0} 解析出材质名 {1} 个，眼镜槽 = {2}",
                    Path.GetFileName(path), innerNames.Count, slotByFile));
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var src = smr.sharedMesh;
                    if (src == null || meshMap.ContainsKey(src)) continue;
                    int idx = slotByFile;
                    if (idx < 0 || idx >= src.subMeshCount) idx = FindSlot(smr, KEYWORD);   // 兜底
                    if (idx < 0 || idx >= src.subMeshCount) continue;
                    var dst = MakeNoPartMesh(src, idx, ch, Path.GetFileNameWithoutExtension(path));
                    meshMap[src] = dst;
                    log.Add(string.Format("  {0}：清空第 {1} 槽「{2}」 → {3}",
                        Path.GetFileName(path), idx,
                        idx < innerNames.Count ? innerNames[idx] : "?",
                        Path.GetFileName(AssetDatabase.GetAssetPath(dst))));
                    didMesh = true;
                }
                if (!didMesh) continue;
                // ★ FBX 导入器映射（新实例/缩略图/预览渲染都靠它）
                foreach (var inner in innerNames.Where(n => n.ToLower().Contains(KEYWORD)))
                {
                    if (RemapOne(path, inner, hideMat)) log.Add("      FBX 导入器映射：" + inner + " → 隐藏部件_不渲染 ✓");
                    else log.Add("      ★ FBX 导入器映射没落盘：" + inner);
                }
            }
        }
        if (meshMap.Count == 0) { log.Add("  没找到含眼镜子网格的模型（可能已经去过了）"); }

        // ---------- 2) 所有场景里的实例：换成清空后的网格
        log.Add("");
        log.Add("── 2) 场景实例换网格");
        int changed = 0;
        var scenes = new List<string>();
        var active = SceneManager.GetActiveScene();
        if (!string.IsNullOrEmpty(active.path)) scenes.Add(active.path);
        foreach (var f in Directory.GetFiles("Assets/Scenes", "*.unity").OrderBy(p => p))
        {
            string p = f.Replace('\\', '/');
            if (!scenes.Contains(p)) scenes.Add(p);
        }
        foreach (var scenePath in scenes)
        {
            if (SceneManager.GetActiveScene().path != scenePath)
            {
                var cur = SceneManager.GetActiveScene();
                if (cur.isDirty) { EditorSceneManager.SaveScene(cur); log.Add("  （先存盘当前场景 " + cur.name + "）"); }
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            }
            var sc = SceneManager.GetActiveScene();
            int n = 0;
            foreach (var root in sc.GetRootGameObjects())
                foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    Mesh rep;
                    if (smr.sharedMesh == null || !meshMap.TryGetValue(smr.sharedMesh, out rep)) continue;
                    // ★ 靠【网格】认，不靠材质名：上面第一步已经把眼镜材质映射成隐形了，
                    //   再用 “材质名里有没有 glasses” 去认会全部认不出来（踩过）。
                    smr.sharedMesh = rep;
                    n++; changed++;
                    EditorUtility.SetDirty(smr);
                    log.Add("  " + sc.name + "：" + PathOf(smr.transform));
                }
            if (n > 0)
            {
                EditorSceneManager.MarkSceneDirty(sc);
                bool ok = EditorSceneManager.SaveScene(sc);
                log.Add("  存场景 " + sc.name + "：" + (ok ? "成功 ✓" : "★ 失败") + "（改了 " + n + " 个实例）");
            }
            else log.Add("  场景 " + sc.name + "：没有需要改的实例");
        }

        // ---------- 3) 源装配表：清掉 Glasses 字段
        log.Add("");
        log.Add("── 3) CharRebuild 装配表");
        try
        {
            string cfg = "Assets/Editor/CharRebuild.cs";
            var txt = File.ReadAllText(cfg);
            int edits = 0;
            foreach (var ch in TARGETS)
            {
                foreach (Match m in Regex.Matches(txt, @"Glasses=""[^""]*"""))
                {
                    // 只处理属于该角色那一行块：往前找最近的 Name="角色"
                    int lineStart = txt.LastIndexOf("new Cfg{", m.Index);
                    if (lineStart < 0) continue;
                    int blockEnd = txt.IndexOf("},", lineStart);
                    if (blockEnd < 0 || m.Index > blockEnd) continue;
                    if (!txt.Substring(lineStart, blockEnd - lineStart).Contains("Name=\"" + ch + "\"")) continue;
                    txt = txt.Remove(m.Index, m.Length);
                    edits++;
                    break;
                }
            }
            if (edits > 0)
            {
                File.WriteAllText(cfg, txt);
                AssetDatabase.ImportAsset(cfg, ImportAssetOptions.ForceUpdate);
                log.Add("  已清掉 " + edits + " 处 Glasses 字段（" + string.Join("、", TARGETS) + "）");
            }
            else log.Add("  装配表里已经没有目标角色的 Glasses 字段了 ✓");
        }
        catch (System.Exception e) { log.Add("  ★ 改装配表失败：" + e.Message); }

        log.Add("");
        log.Add(string.Format("合计：新网格 {0} 份，场景实例改了 {1} 个", meshMap.Count, changed));
        log.Add("注意：网格覆盖是加在实例上的 —— 以后若重跑「Game角色替换」换新模型，要再跑一次本工具");
        log.Add("      （不过 FBX 导入器映射是永久的，新拖进来的实例至少不会再显示眼镜本体）");
        Flush(log);
    }

    // ------------------------------------------------------------------ 工具函数
    static Material EnsureHideMaterial()
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(HIDE_MAT);
        if (m != null) return m;
        var sh = Shader.Find("Universal Render Pipeline/Unlit");
        m = new Material(sh);
        m.name = "隐藏部件_不渲染";
        m.SetFloat("_Surface", 1f);                                       // Transparent
        m.SetFloat("_Blend", 0f);                                         // Alpha
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.SetColor("_BaseColor", new Color(0f, 0f, 0f, 0f));
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        AssetDatabase.CreateAsset(m, HIDE_MAT);
        AssetDatabase.SaveAssets();
        return m;
    }

    static int FindSlot(Renderer r, string keyword)
    {
        var mats = r.sharedMaterials;
        for (int i = 0; i < mats.Length; i++)
            if (mats[i] != null && mats[i].name.ToLower().Contains(keyword)) return i;
        return -1;
    }

    static Mesh MakeNoPartMesh(Mesh src, int slot, string ch, string tag)
    {
        string dst = OUT_DIR + "/" + ch + "_" + tag + "_无" + KEYWORD + ".asset";
        // ★ 已有资产就【原地只清那个子网格】：删掉重建会换 GUID，把场景里已挂的引用弄断
        var m = AssetDatabase.LoadAssetAtPath<Mesh>(dst);
        if (m == null)
        {
            m = Object.Instantiate(src);                  // 可写副本（FBX 原始网格是只读的）
            m.name = src.name + "_noPart";
            if (slot < m.subMeshCount) m.SetTriangles(new int[0], slot);
            m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, dst);
        }
        else if (slot < m.subMeshCount)
        {
            int now = 0;
            try { now = m.GetTriangles(slot).Length; } catch { now = -1; }
            if (now != 0) { m.SetTriangles(new int[0], slot); m.RecalculateBounds(); }
        }
        EditorUtility.SetDirty(m);
        AssetDatabase.SaveAssets();
        // 自检：那个子网格必须真的没三角形了
        int left = -2;
        try { left = m.GetTriangles(slot).Length; } catch { left = -2; }
        Debug.Log(string.Format("[HidePart] {0}：槽 {1} 剩余三角形 {2} {3}", dst, slot, left,
            left == 0 ? "✓" : "★ 没清干净"));
        return m;
    }

    // 读二进制 FBX 里的材质对象名
    static List<string> FbxMaterialNames(string path)
    {
        var res = new List<string>();
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (!(bytes.Length > 20 && System.Text.Encoding.ASCII.GetString(bytes, 0, 18) == "Kaydara FBX Binary"))
                return res;
            var pat = System.Text.Encoding.ASCII.GetBytes("\u0000\u0001Material");
            for (int i = 0; i + pat.Length <= bytes.Length; i++)
            {
                if (bytes[i] != 0) continue;
                bool ok = true;
                for (int q = 1; q < pat.Length; q++) if (bytes[i + q] != pat[q]) { ok = false; break; }
                if (!ok) continue;
                int st = Mathf.Max(0, i - 200);
                string seg = System.Text.Encoding.UTF8.GetString(bytes, st, i - st);
                var parts = Regex.Split(seg, "[\x00-\x1f]+");
                for (int q = parts.Length - 1; q >= 0; q--) if (parts[q].Length >= 3) { res.Add(parts[q]); break; }
            }
        }
        catch (System.Exception e) { Debug.LogWarning("[HidePart] 解析 " + path + " 失败：" + e.Message); }
        return res.Distinct().ToList();
    }

    // 改 FBX 导入器的材质映射（2022.3 没有公开 API：反射 AddRemap，不行就直接改 .meta）
    static bool RemapOne(string fbxPath, string innerName, Material mat)
    {
        var imp = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (imp == null) return false;
        var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), innerName);

        // ① 反射内部 AddRemap
        try
        {
            var mi = typeof(AssetImporter)
                .GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic |
                            System.Reflection.BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "AddRemap" && m.GetParameters().Length == 2);
            if (mi != null)
            {
                mi.Invoke(imp, new object[] { id, mat });
                imp.SaveAndReimport();
                if (MetaHas(fbxPath + ".meta", innerName)) return true;
            }
        }
        catch (System.Exception e) { Debug.LogWarning("[HidePart] AddRemap 调用失败：" + e.Message); }

        // ② 直接改 .meta 的 externalObjects 块
        try
        {
            string meta = fbxPath + ".meta";
            var lines = File.ReadAllLines(meta).ToList();
            int start = lines.FindIndex(l => l.StartsWith("  externalObjects:"));
            if (start < 0) return false;
            int end = start + 1;
            while (end < lines.Count && (lines[end].StartsWith("  -") || lines[end].StartsWith("    "))) end++;
            var block = new List<string> { "  externalObjects:" };
            for (int i = start + 1; i < end; i++) block.Add(lines[i]);        // 保留已有映射

            // 已经映射过同一名字就不重复加
            if (!block.Any(l => l.Contains("name: " + innerName)))
            {
                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(mat));
                block.Add("  - first:");
                block.Add("      type: UnityEngine:Material");
                block.Add("      assembly: UnityEngine.CoreModule");
                block.Add("      name: " + innerName);
                block.Add("    second: {fileID: 2100000, guid: " + guid + ", type: 2}");
                lines.RemoveRange(start, end - start);
                lines.InsertRange(start, block);
                File.WriteAllText(meta, string.Join("\n", lines.ToArray()) + "\n", new System.Text.UTF8Encoding(false));
            }
            AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceUpdate);
            return MetaHas(meta, innerName);
        }
        catch (System.Exception e) { Debug.LogWarning("[HidePart] 改 .meta 失败：" + e.Message); return false; }
    }

    static bool MetaHas(string meta, string name)
    {
        try { return File.Exists(meta) && File.ReadAllText(meta).Contains("name: " + name); }
        catch { return false; }
    }

    static string PathOf(Transform t)
    {
        var l = new List<string>();
        while (t != null) { l.Add(t.name); t = t.parent; }
        l.Reverse();
        return string.Join("/", l.ToArray());
    }

    static void Flush(List<string> log)
    {
        Directory.CreateDirectory("Assets/assets/_报告");
        File.WriteAllText(REPORT, string.Join("\n", log.ToArray()));
        AssetDatabase.Refresh();
        Debug.Log("[HidePart] 报告：" + REPORT);
    }
}

// 触发器：常驻轮询（不靠域重载/焦点）——丢 Assets/_hidepart_trigger.txt 就跑
[InitializeOnLoad]
static class HidePartTrigger
{
    const string T = "Assets/_hidepart_trigger.txt";
    static double _next;

    static HidePartTrigger()
    {
        EditorApplication.update -= Poll;
        EditorApplication.update += Poll;
    }

    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < _next) return;
        _next = EditorApplication.timeSinceStartup + 0.5;
        if (Application.isPlaying) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (!File.Exists(T)) return;
        try
        {
            File.Delete(T);
            HidePart.Run();
        }
        catch (System.Exception e)
        {
            Debug.LogError("[HidePart] " + e);
            Directory.CreateDirectory("../额外文件");
            File.WriteAllText("../额外文件/错误_去掉部件.txt", e.ToString());
        }
    }
}
