// 给主角（玩家）挂上可用的动画：
//   1) 把循环用的动画剪辑设为 Loop（否则播一遍就停）
//   2) 生成 AnimatorController：Locomotion（待机/行走/慢跑/快速跑 1D 混合）+ 拿手机待机 + 拿手机
//   3) 参数：Speed(Float) / Phone(Bool) / TakePhone(Trigger)，挂到 Assets/assets/角色_URP/徐夏_可动.prefab
// 用法：菜单 Tools/干预项目/给主角挂动画   （或命令行 -executeMethod PlayerAnimSetup.Run，或放 Assets/_anim_trigger.txt 自动跑）

using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public static class PlayerAnimSetup
{
    const string AnimDir    = "Assets/assets/03_动作_Animation/动画";
    const string CtrlPath   = "Assets/assets/03_动作_Animation/Animators/PC_徐夏_测试.controller";
    const string PlayerPref = "Assets/assets/02_角色_Character/角色_URP/徐夏_可动.prefab";

    [MenuItem("Tools/干预项目/给主角挂动画")]
    public static void Run()
    {
        var log = new List<string>();

        // ---------- 1) 循环设置 ----------
        string[] loopClips = { "待机女", "待机男", "行走", "行走男", "慢跑", "快速跑", "拿手机待机" };
        foreach (var n in loopClips)
        {
            string p = AnimDir + "/" + n + ".fbx";
            var imp = AssetImporter.GetAtPath(p) as ModelImporter;
            if (imp == null) { log.Add("缺动画: " + p); continue; }
            var clips = imp.clipAnimations;
            if (clips == null || clips.Length == 0) clips = imp.defaultClipAnimations;
            if (clips == null || clips.Length == 0) { log.Add("没有剪辑: " + n); continue; }
            bool dirty = false;
            foreach (var c in clips) { if (!c.loopTime) { c.loopTime = true; dirty = true; } }
            if (dirty) { imp.clipAnimations = clips; imp.SaveAndReimport(); }
        }

        // ---------- 2) 控制器 ----------
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(CtrlPath) != null)
            AssetDatabase.DeleteAsset(CtrlPath);
        Directory.CreateDirectory(Path.GetDirectoryName(CtrlPath));
        AssetDatabase.Refresh();

        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(CtrlPath);
        ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
        ctrl.AddParameter("Phone", AnimatorControllerParameterType.Bool);
        ctrl.AddParameter("TakePhone", AnimatorControllerParameterType.Trigger);

        var sm = ctrl.layers[0].stateMachine;

        var tree = new BlendTree
        {
            name = "Locomotion",
            blendType = BlendTreeType.Simple1D,
            blendParameter = "Speed",
            useAutomaticThresholds = false      // 用固定阈值：0=待机 0.5=行走 1=慢跑 2=快速跑
        };
        tree.AddChild(Clip("待机女"), 0f);
        tree.AddChild(Clip("行走"),   0.5f);
        tree.AddChild(Clip("慢跑"),   1.0f);
        tree.AddChild(Clip("快速跑"), 2.0f);

        var loco = sm.AddState("Locomotion");
        loco.motion = tree;
        loco.writeDefaultValues = false;
        AssetDatabase.AddObjectToAsset(tree, ctrl);
        sm.defaultState = loco;

        var phoneIdle = sm.AddState("拿手机待机");
        phoneIdle.motion = Clip("拿手机待机");
        var phone = sm.AddState("拿手机");
        phone.motion = Clip("拿手机");

        var t1 = loco.AddTransition(phoneIdle);
        t1.hasExitTime = false; t1.duration = 0.2f; t1.AddCondition(AnimatorConditionMode.If, 0, "Phone");
        var t2 = phoneIdle.AddTransition(loco);
        t2.hasExitTime = false; t2.duration = 0.2f; t2.AddCondition(AnimatorConditionMode.IfNot, 0, "Phone");
        var t3 = sm.AddAnyStateTransition(phone);
        t3.hasExitTime = false; t3.duration = 0.15f; t3.canTransitionToSelf = false;
        t3.AddCondition(AnimatorConditionMode.If, 0, "TakePhone");
        var t4 = phone.AddTransition(phoneIdle);
        t4.hasExitTime = true; t4.exitTime = 0.9f; t4.duration = 0.2f;

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        log.Add("控制器: " + CtrlPath + "（Locomotion 混合树 + 拿手机待机 + 拿手机）");

        // ---------- 3) 挂到主角 prefab ----------
        var contents = PrefabUtility.LoadPrefabContents(PlayerPref);
        var anim = contents.GetComponent<Animator>();
        if (anim == null) { anim = contents.AddComponent<Animator>(); log.Add("（原 prefab 没有 Animator，已添加）"); }
        string avatarName = anim.avatar != null ? anim.avatar.name : "无";
        anim.runtimeAnimatorController = ctrl;
        anim.applyRootMotion = false;
        anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        PrefabUtility.SaveAsPrefabAsset(contents, PlayerPref);
        PrefabUtility.UnloadPrefabContents(contents);
        log.Add("已挂到: " + PlayerPref + "（avatar=" + avatarName + "）");

        AssetDatabase.SaveAssets();
        File.WriteAllText("Assets/assets/_报告/_动画接入报告.txt", string.Join("\n", log));
        Debug.Log("[PlayerAnimSetup]\n" + string.Join("\n", log));
    }

    static AnimationClip Clip(string name)
    {
        string p = AnimDir + "/" + name + ".fbx";
        var c = AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>()
                    .FirstOrDefault(x => !x.name.StartsWith("__preview__"));
        if (c == null) Debug.LogWarning("[PlayerAnimSetup] 找不到剪辑: " + p);
        return c;
    }
}

// 一次性自动执行（放 Assets/_anim_trigger.txt 后，编辑器下次刷新就自动跑）
[InitializeOnLoad]
public static class PlayerAnimAutoRun
{
    const string Trigger = "Assets/_anim_trigger.txt";
    static PlayerAnimAutoRun()
    {
        if (!File.Exists(Trigger)) return;
        File.Delete(Trigger);
        EditorApplication.delayCall += () =>
        {
            try { PlayerAnimSetup.Run(); Debug.Log("[PlayerAnimSetup] 自动执行完成"); }
            catch (System.Exception e) { File.WriteAllText("Assets/_动画接入错误.txt", e.ToString()); Debug.LogError(e); }
        };
    }
}
