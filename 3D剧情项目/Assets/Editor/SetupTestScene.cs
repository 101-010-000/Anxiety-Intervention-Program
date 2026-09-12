using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Animations;
using System.IO;
using System.Linq;

public static class SetupTestScene
{
    static void Mark(string m) { System.IO.File.AppendAllText("Assets/_step.txt", m + "\n"); }

    public static void Run()
    {
        System.IO.File.WriteAllText("Assets/_step.txt", "start\n");
        string sceneDir = "Assets/Scenes";
        string prefabDir = "Assets/Prefabs";
        string animDir = "Assets/assets/03_动作_Animation/Animators";
        Directory.CreateDirectory(sceneDir);
        Directory.CreateDirectory(prefabDir);
        Directory.CreateDirectory(animDir);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Mark("scene");

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.7f, 0.72f, 0.75f);

        // ---------- controller ----------
        string controllerPath = animDir + "/PC_徐夏_测试.controller";
        var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        var sm = controller.layers[0].stateMachine;
        AddState(sm, "待机", "待机女.fbx", true);
        AddState(sm, "行走", "行走.fbx", false);
        AddState(sm, "慢跑", "慢跑.fbx", false);
        AddState(sm, "快速跑", "快速跑.fbx", false);
        AddState(sm, "拿手机待机", "拿手机待机.fbx", false);
        Mark("controller+states");

        // ---------- animated character prefab ----------
        string charPath = "Assets/assets/02_角色_Character/组合角色/徐夏/徐夏.fbx";
        var charModel = AssetDatabase.LoadAssetAtPath<GameObject>(charPath);
        var holder = (GameObject)PrefabUtility.InstantiatePrefab(charModel);
        holder.name = "徐夏_可动";
        var anim = holder.AddComponent<Animator>();
        var avatar = AssetDatabase.LoadAllAssetsAtPath(charPath).OfType<Avatar>().FirstOrDefault(a => a.isHuman);
        Mark("avatar found " + (avatar != null) + " ctrl " + (controller != null));

        var so = new SerializedObject(anim);
        try { so.FindProperty("m_Avatar").objectReferenceValue = avatar; Mark("so avatar ok"); }
        catch (System.Exception ex) { Mark("so avatar EX: " + ex.Message); }
        try { so.FindProperty("m_Controller").objectReferenceValue = controller; Mark("so ctrl ok"); }
        catch (System.Exception ex) { Mark("so ctrl EX: " + ex.Message); }
        try { so.ApplyModifiedPropertiesWithoutUndo(); Mark("so applied"); }
        catch (System.Exception ex) { Mark("so apply EX: " + ex.Message); }

        holder.transform.rotation = Quaternion.Euler(0, 180, 0);
        string prefabPath = prefabDir + "/徐夏_可动.prefab";
        PrefabUtility.SaveAsPrefabAsset(holder, prefabPath);
        Object.DestroyImmediate(holder);
        Mark("prefab saved " + prefabPath);

        // ---------- scene ----------
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(10, 0.1f, 10);
        ground.transform.position = new Vector3(0, -0.05f, 0);
        var lg = new GameObject("Sun");
        var dl = lg.AddComponent<Light>();
        dl.type = LightType.Directional;
        dl.transform.rotation = Quaternion.Euler(50, -30, 0);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        var actor = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        actor.name = "徐夏";
        actor.transform.position = new Vector3(0, 0, 0);

        var camGO = new GameObject("Main Camera");
        var cam = camGO.AddComponent<Camera>();
        camGO.tag = "MainCamera";
        camGO.transform.position = new Vector3(0, 1.4f, 3.4f);
        camGO.transform.LookAt(new Vector3(0, 1.2f, 0));

        string scenePath = sceneDir + "/Test_徐夏_动画.unity";
        EditorSceneManager.SaveScene(scene, scenePath);
        AssetDatabase.SaveAssets();
        Mark("saved " + scenePath);
        Debug.Log("SCENE OK -> " + scenePath);
    }

    static void AddState(AnimatorStateMachine sm, string stateName, string animFbx, bool isDefault)
    {
        string p = "Assets/assets/03_动作_Animation/动画/" + animFbx;
        var clip = AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>()
                     .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        if (clip == null) { Mark("no clip " + animFbx); return; }
        var st = sm.AddState(stateName);
        st.motion = clip;
        if (isDefault) sm.defaultState = st;
    }
}
