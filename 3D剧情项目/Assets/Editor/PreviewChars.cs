using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;
using System.Linq;
using System.Text;

public static class PreviewChars
{
    public static void Run()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.6f, 0.62f, 0.66f);
        QualitySettings.shadows = ShadowQuality.Disable;
        string root = "Assets/assets/角色_URP";
        string outDir = root + "/预览";
        Directory.CreateDirectory(outDir);
        var sb = new StringBuilder();

        foreach (var p in Directory.GetFiles(root, "*_可动.prefab").OrderBy(x => x))
        {
            string name = Path.GetFileNameWithoutExtension(p).Replace("_可动", "");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p.Replace('\\', '/'));
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            // audit materials
            int matsN = 0, noBase = 0, badShader = 0;
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    matsN++;
                    if (m.shader == null || !m.shader.isSupported) badShader++;
                    if (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") == null) noBase++;
                }
            }
            sb.AppendLine($"{name}: renderer材质{matsN} 无BaseMap{noBase} 坏shader{badShader}");

            // render
            var camGO = new GameObject("Cam");
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.78f, 0.85f, 0.92f);
            cam.fieldOfView = 28;
            camGO.transform.position = new Vector3(2.6f, 1.1f, 2.6f);
            camGO.transform.LookAt(new Vector3(0, 0.85f, 0));
            inst.transform.rotation = Quaternion.Euler(0, 45, 0);
            var lg = new GameObject("L");
            var dl = lg.AddComponent<Light>();
            dl.type = LightType.Directional;
            lg.transform.rotation = Quaternion.Euler(45, -35, 0);
            var rt = new RenderTexture(512, 640, 24);
            cam.targetTexture = rt;
            for (int i=0;i<3;i++) cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(512, 640, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 512, 640), 0, 0);
            tex.Apply();
            File.WriteAllBytes(outDir + "/" + name + "_预览.png", tex.EncodeToPNG());
            Object.DestroyImmediate(tex); RenderTexture.active = null; Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGO); Object.DestroyImmediate(lg); Object.DestroyImmediate(inst);
        }
        File.WriteAllText(root + "/_预览审计.txt", sb.ToString());
        Debug.Log("PREVIEW DONE");
    }
}
