using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;
using System.Linq;
using System.Text;

public static class FinalVerify
{
    public static void Run()
    {
        var sb = new StringBuilder();
        string dir = "Assets/assets/角色_URP";
        foreach (var f in Directory.GetFiles(dir, "*_可动.prefab").OrderBy(x => x))
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(f.Replace('\\', '/'));
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(go);
            int mats = 0, nullBase = 0;
            string firstBase = "-";
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    mats++;
                    if (!m.HasProperty("_BaseMap") || m.GetTexture("_BaseMap") == null)
                    {
                        nullBase++;
                        if (firstBase == "-") firstBase = m.name + ":" + (m.HasProperty("_BaseMap"));
                    }
                    else if (firstBase == "-") firstBase = m.GetTexture("_BaseMap").name;
                }
            }
            sb.AppendLine(Path.GetFileNameWithoutExtension(f) + ": mats=" + mats + " 缺BaseMap=" + nullBase + " 示例=" + firstBase);
            Object.DestroyImmediate(inst);
        }
        // render 徐夏 preview
        var pipe = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>("Assets/URP/URP_Pipeline.asset");
        UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = pipe;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.6f, 0.62f, 0.66f);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(dir + "/徐夏_可动.prefab");
        var act = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        act.transform.rotation = Quaternion.Euler(0, 45, 0);
        var camGO = new GameObject("C");
        var cam = camGO.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.55f, 0.63f, 0.72f);
        cam.transform.position = new Vector3(0, 1.2f, 3.2f);
        cam.transform.LookAt(new Vector3(0, 1.1f, 0));
        var lg = new GameObject("L");
        var dl = lg.AddComponent<Light>();
        dl.type = LightType.Directional;
        var rt = new RenderTexture(512, 640, 24);
        cam.targetTexture = rt;
        for (int i = 0; i < 5; i++) cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(512, 640, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 512, 640), 0, 0);
        tex.Apply();
        File.WriteAllBytes("Assets/assets/角色_URP/徐夏_贴图预览.png", tex.EncodeToPNG());
        int mag = 0;
        for (int y = 0; y < 640; y += 2)
            for (int x = 0; x < 512; x += 2)
            {
                var c = tex.GetPixel(x, y);
                if (c.r > 0.8f && c.g < 0.3f && c.b > 0.8f) mag++;
            }
        sb.AppendLine("徐夏渲染: 洋红像素=" + mag + " center=" + tex.GetPixel(256, 200));
        File.WriteAllText("Assets/_final.txt", sb.ToString());
        Debug.Log("FINAL DONE");
    }
}
