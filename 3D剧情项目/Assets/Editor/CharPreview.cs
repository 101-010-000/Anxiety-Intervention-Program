// 把 9 个角色 prefab 各渲染一张预览图，方便检查材质（有没有洋红/缺材质）。
// 用法：菜单 Tools/干预项目/渲染角色预览；或放 Assets/_preview_trigger.txt 自动执行。
// 输出：Assets/assets/_报告/预览/<角色>.png

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public static class CharPreview
{
    const int LAYER = 31;

    [MenuItem("Tools/干预项目/渲染角色预览")]
    public static void Run()
    {
        string dir = AssetLocator.Dir("角色_URP");
        string outDir = (AssetLocator.Dir("_报告") ?? "Assets") + "/预览";
        Directory.CreateDirectory(outDir.Replace('/', Path.DirectorySeparatorChar));
        var log = new List<string>();

        var prefabs = Directory.GetFiles(dir, "*_可动.prefab", SearchOption.TopDirectoryOnly).OrderBy(x => x).ToArray();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

        var camGO = new GameObject("prev_cam"); SceneManager.MoveGameObjectToScene(camGO, scene);
        var cam = camGO.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.18f, 0.20f, 0.24f);
        cam.cullingMask = 1 << LAYER;
        cam.fieldOfView = 32f;
        var rt = new RenderTexture(512, 640, 24);

        var lightGO = new GameObject("prev_light"); SceneManager.MoveGameObjectToScene(lightGO, scene);
        var lt = lightGO.AddComponent<Light>();
        lt.type = LightType.Directional;
        lt.intensity = 1.1f;
        lt.cullingMask = 1 << LAYER;
        lightGO.transform.rotation = Quaternion.Euler(35, 200, 0);

        // --- 第一轮：把每个角色都渲染一遍，触发 shader 变体编译（否则会拍到洋红） ---
        var warm = new List<GameObject>();
        foreach (var pf in prefabs)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(pf.Replace('\\', '/'));
            if (asset == null) continue;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
            SetLayer(inst, LAYER);
            FrameCamera(cam, inst);
            cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
            Object.DestroyImmediate(inst);
        }
        while (UnityEditor.ShaderUtil.anythingCompiling) System.Threading.Thread.Sleep(50);
        AssetDatabase.Refresh();

        foreach (var pf in prefabs)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(pf.Replace('\\', '/'));
            if (asset == null) { log.Add("无法加载 " + pf); continue; }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
            SetLayer(inst, LAYER);
            float h = FrameCamera(cam, inst);

            cam.targetTexture = rt;
            cam.Render();
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(512, 640, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 512, 640), 0, 0);
            tex.Apply();
            RenderTexture.active = null; cam.targetTexture = null;

            string name = Path.GetFileNameWithoutExtension(pf);
            File.WriteAllBytes(outDir + "/" + name + ".png", tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(inst);
            log.Add(name + " 渲染完成 (包围盒高度 " + h.ToString("0.00") + ")");
        }

        EditorSceneManager.CloseScene(scene, true);
        File.WriteAllText(outDir + "/_渲染日志.txt", string.Join("\n", log));
        Debug.Log("[CharPreview]\n" + string.Join("\n", log));
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

    // 强制重导所有角色专属材质（材质引用存在但 Unity 返回 null 时使用，否则角色会渲染成洋红）
    [MenuItem("Tools/干预项目/强制重导角色材质")]
    public static void ReimportMats()
    {
        string root = AssetLocator.Dir("角色_URP");
        int n = 0;
        foreach (var f in Directory.GetFiles(root, "*.mat", SearchOption.AllDirectories))
        {
            AssetDatabase.ImportAsset(f.Replace('\\', '/'), ImportAssetOptions.ForceUpdate);
            n++;
        }
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        string rep = (AssetLocator.Dir("_报告") ?? "Assets") + "/_重导材质.txt";
        File.WriteAllText(rep, "重导材质 " + n + " 个\n目录: " + root);
        Debug.Log("[CharPreview] 重导材质 " + n + " 个");
    }

    // 全量强制重导（相当于 Assets → Reimport All）：用于修复会话内材质引用变成 null 的状态
    [MenuItem("Tools/干预项目/全量强制重导")]
    public static void FullReimport()
    {
        var paths = AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets")).ToArray();
        AssetDatabase.StartAssetEditing();
        int n = 0;
        foreach (var p in paths)
        {
            AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
            n++;
        }
        AssetDatabase.StopAssetEditing();
        AssetDatabase.Refresh();
        string rep = (AssetLocator.Dir("_报告") ?? "Assets") + "/_全量重导.txt";
        File.WriteAllText(rep, "全量强制重导 " + n + " 个资产");
        Debug.Log("[CharPreview] 全量重导 " + n);
    }

    // 副产物统一写到仓库根的 额外文件/（项目外，不进版本库）
    public static void WriteAgentError(System.Exception e)
    {
        try
        {
            string dir = "../额外文件";
            Directory.CreateDirectory(dir);
            File.WriteAllText(dir + "/错误_角色预览.txt", e.ToString());
        }
        catch { }
    }

    static void SetLayer(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
    }

    // 诊断：逐个角色/渲染器报告 网格、材质、shader 是否可用（洋红=shader 不可用）
    [MenuItem("Tools/干预项目/诊断角色材质")]
    public static void Diag()
    {
        string dir = AssetLocator.Dir("角色_URP");
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("### 直接加载测试（角色专属材质）");
        int nullCnt = 0, okCnt = 0;
        foreach (var f in Directory.GetFiles(dir, "*.mat", SearchOption.AllDirectories))
        {
            string p = f.Replace('\\', '/');
            var mat = AssetDatabase.LoadAssetAtPath<Material>(p);
            var t = AssetDatabase.GetMainAssetTypeAtPath(p);
            if (mat == null) { nullCnt++; if (nullCnt <= 12) sb.AppendLine("    NULL  " + p + "  (mainType=" + (t == null ? "null" : t.Name) + ")"); }
            else okCnt++;
        }
        sb.AppendLine("    合计: 可加载=" + okCnt + "  加载失败=" + nullCnt);
        sb.AppendLine();
        foreach (var pf in Directory.GetFiles(dir, "*_可动.prefab", SearchOption.TopDirectoryOnly).OrderBy(x => x))
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(pf.Replace('\\', '/'));
            if (asset == null) continue;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            sb.AppendLine("===== " + asset.name);
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                string mesh = "-";
                var smr = r as SkinnedMeshRenderer;
                var mf = r.GetComponent<MeshFilter>();
                if (smr != null) mesh = smr.sharedMesh == null ? "MESH_NULL" : smr.sharedMesh.name;
                else if (mf != null) mesh = mf.sharedMesh == null ? "MESH_NULL" : mf.sharedMesh.name;
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) { sb.AppendLine("    " + r.name + " | " + mesh + " | MATERIAL_NULL"); continue; }
                    var sh = m.shader;
                    bool ok = sh != null && sh.isSupported;
                    var msgs = sh != null ? ShaderUtil.GetShaderMessages(sh) : null;
                    string err = "";
                    if (msgs != null) foreach (var msg in msgs) err += "[" + msg.severity + "] " + msg.message + " | ";
                    sb.AppendLine("    " + r.name + " | " + mesh + " | " + m.name + " | shader=" + (sh == null ? "NULL" : sh.name) + " | supported=" + ok + (err.Length > 0 ? " | ERR: " + err : ""));
                }
            }
            Object.DestroyImmediate(inst);
        }
        File.WriteAllText((AssetLocator.Dir("_报告") ?? "Assets") + "/_材质诊断.txt", sb.ToString());
        Debug.Log("[CharPreview] 材质诊断完成");
    }
}

[InitializeOnLoad]
public static class CharPreviewAutoRun
{
    const string Trigger = "Assets/_preview_trigger.txt";
    const string DiagTrigger = "Assets/_diagmat_trigger.txt";
    const string ReimportTrigger = "Assets/_reimport_trigger.txt";
    const string FullTrigger = "Assets/_fullreimport_trigger.txt";
    static CharPreviewAutoRun()
    {
        if (File.Exists(FullTrigger))
        {
            File.Delete(FullTrigger);
            EditorApplication.delayCall += () => { try { CharPreview.FullReimport(); } catch (System.Exception e) { WriteAgentError(e); } };
        }
        if (File.Exists(ReimportTrigger))
        {
            File.Delete(ReimportTrigger);
            EditorApplication.delayCall += () => { try { CharPreview.ReimportMats(); } catch (System.Exception e) { WriteAgentError(e); } };
        }
        if (File.Exists(DiagTrigger))
        {
            File.Delete(DiagTrigger);
            EditorApplication.delayCall += () => { try { CharPreview.Diag(); } catch (System.Exception e) { WriteAgentError(e); } };
        }
        if (!File.Exists(Trigger)) return;
        File.Delete(Trigger);
        EditorApplication.delayCall += () =>
        {
            try { CharPreview.Run(); Debug.Log("[CharPreview] 完成"); }
            catch (System.Exception e) { WriteAgentError(e); Debug.LogError(e); }
        };
    }
}

// touch 213914

// touch 214116

// touch 214436

// touch 214623
