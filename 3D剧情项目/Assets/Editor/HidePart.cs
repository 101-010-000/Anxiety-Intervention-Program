// ============================================================================
// 角色：去掉某个部件（目前用于「去掉陆宣雨的眼镜」）
//
// 为什么不是换个透明材质就完事：
//   · 带动画模型里整身是【一块合并网格 + 多个子网格】，眼镜 = 其中一个子网格；
//   · 描边（OutlineFeature）是给「对象的所有子网格」各画一层外壳的，跟材质无关
//     → 就算把眼镜材质换成全透明，黑框外壳还在；而且透明材质在 URP 里也未必真看不见。
//   所以做法是【把那个子网格的三角形清空】：拷一份网格资产出来，把眼镜子网格的三角形置空，
//   再把场景里对应实例的 SkinnedMeshRenderer.sharedMesh 换成它（其余子网格原样保留）。
//
// 顺带把源装配表（CharRebuild 的 Glasses 字段）也清掉，这样以后「重建角色模型」
// 出来的是没有眼镜的版本。
//
// 菜单：Tools/干预项目/角色：去掉部件（眼镜）
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
    const string KEYWORD = "glasses";                       // 子网格材质名里含这个就当眼镜
    const string MODEL_DIR = "Assets/assets/03_动作_Animation/带动画模型";
    const string OUT_DIR = "Assets/assets/02_角色_Character/角色_URP/去部件";
    const string REPORT = "Assets/assets/_报告/_去掉部件.txt";

    [MenuItem("Tools/干预项目/角色：去掉部件（眼镜）", false, 150)]
    public static void Run()
    {
        var log = new List<string>();
        log.Add("角色去掉部件（关键字 " + KEYWORD + "）  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("");

        Directory.CreateDirectory(OUT_DIR);
        var meshMap = new Dictionary<Mesh, Mesh>();          // 源网格 → 去掉部件后的网格

        // ---------- 1) 先把「无眼镜」网格资产准备好（每个用到的源网格一份）
        foreach (var ch in TARGETS)
        {
            foreach (var fbx in Directory.GetFiles(MODEL_DIR + "/" + ch, "*.fbx"))
            {
                string path = fbx.Replace('\\', '/');
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var src = smr.sharedMesh;
                    if (src == null || meshMap.ContainsKey(src)) continue;
                    int idx = FindSlot(smr, KEYWORD);
                    if (idx < 0) continue;
                    var dst = MakeNoPartMesh(src, idx, ch, Path.GetFileNameWithoutExtension(path));
                    meshMap[src] = dst;
                    log.Add(string.Format("  网格资产：{0}（清空第 {1} 槽「{2}」）",
                        AssetDatabase.GetAssetPath(dst), idx, smr.sharedMaterials[idx].name));
                }
            }
        }
        if (meshMap.Count == 0) { log.Add("★ 没找到含眼镜子网格的模型（可能已经去过了）"); Flush(log); return; }

        // ---------- 2) 当前场景里的实例：换成新网格
        var sc = SceneManager.GetActiveScene();
        int inst = 0, changed = 0;
        foreach (var root in sc.GetRootGameObjects())
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh rep;
                if (smr.sharedMesh == null || !meshMap.TryGetValue(smr.sharedMesh, out rep)) continue;
                // 只动目标角色的实例：靠材质槽名判断（眼镜槽在，就是戴眼镜的那个）
                if (FindSlot(smr, KEYWORD) < 0) continue;
                inst++;
                smr.sharedMesh = rep;
                changed++;
                EditorUtility.SetDirty(smr);
                log.Add("  场景实例：" + PathOf(smr.transform) + " → 已换成去部件网格");
            }
        if (changed > 0)
        {
            EditorSceneManager.MarkSceneDirty(sc);
            bool ok = EditorSceneManager.SaveScene(sc);
            log.Add("  存场景：" + (ok ? "成功 ✓" : "★ 失败"));
        }
        else log.Add("  当前场景（" + sc.name + "）里没有需要改的实例");

        // ---------- 3) 源装配表：把 Glasses 字段清掉（以后重建就没眼镜了）
        log.Add("");
        string cfg = "Assets/Editor/CharRebuild.cs";
        var txt = File.ReadAllText(cfg);
        int edits = 0;
        foreach (var ch in TARGETS)
        {
            var m = Regex.Match(txt, @"new Cfg\{ Name=""" + ch + @"""[\s\S]*?\n\s*\},", RegexOptions.Multiline);
            if (!m.Success) { log.Add("  ★ CharRebuild 里没找到 " + ch + " 的配置行"); continue; }
            string line = m.Value;
            string replaced = Regex.Replace(line, @"Glasses=""[^""]*""", "");
            if (replaced != line)
            {
                txt = txt.Replace(line, replaced);
                edits++;
                log.Add("  CharRebuild：" + ch + " 的 Glasses 字段已清空（重建角色后同样没眼镜）");
            }
        }
        if (edits > 0) { File.WriteAllText(cfg, txt); AssetDatabase.ImportAsset(cfg, ImportAssetOptions.ForceUpdate); }

        log.Add("");
        log.Add(string.Format("合计：新网格 {0} 份，场景实例改了 {1} 个", meshMap.Count, changed));
        log.Add("注意：以后若重跑「Game角色替换」换新模型，需要再跑一次本工具（网格覆盖是加在实例上的）。");
        Flush(log);
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
        var old = AssetDatabase.LoadAssetAtPath<Mesh>(dst);
        var m = Object.Instantiate(src);                  // 可写副本（原始 FBX 网格是只读的）
        m.name = src.name + "_noPart";
        var empty = new int[0];
        if (slot < m.subMeshCount) m.SetTriangles(empty, slot);
        m.RecalculateBounds();
        if (old != null) AssetDatabase.DeleteAsset(dst);
        AssetDatabase.CreateAsset(m, dst);
        AssetDatabase.SaveAssets();
        // 自检：那个子网格必须真的没三角形了（SetTriangles 在只读网格上会静默失败）
        int left = -1;
        try { left = m.GetTriangles(slot).Length; } catch { left = -2; }
        Debug.Log(string.Format("[HidePart] {0}：槽 {1} 剩余三角形 {2} {3}", dst, slot, left,
            left == 0 ? "✓" : "★ 没清干净（网格可能不可写）"));
        return m;
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

// 触发器：常驻轮询（不靠域重载/焦点）——丢 Assets/_hidepart_trigger.txt 就跑去眼镜
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
