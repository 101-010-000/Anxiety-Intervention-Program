// 场景视图（编辑器摄像机）复位：视角歪了 / 斜着 / 乱转 / 跑飞了，一键恢复默认状态。
// 用法：菜单 Tools/干预项目/场景视图相机/…（4 个选项）
//       或放 Assets/_camera_trigger.txt → 编辑器下次刷新（Ctrl+R 或切回 Unity）自动跑"完全复位"并写报告
// 输出：Assets/assets/_报告/_场景视图相机.txt（每个视图复位前后的 pivot / 角度 / 距离 + 倾斜角）
//
// 为什么要写脚本：Scene 视图的相机状态由 Unity 存在 UserSettings/Layouts/*.dwlt 里，
// 手改那个文件没用（Unity 运行时读内存、退出时覆盖），只能在编辑器里改运行中的 SceneView。
//
// 关键点：Quaternion 里带 z 分量 = 画面"歪"（roll），俯仰/水平转动不算歪。
//        所以"只摆正"= 保留"朝哪看"，把 roll 丢掉。

using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public static class SceneViewReset
{
    const string REPORT = "Assets/assets/_报告/_场景视图相机.txt";

    // 编辑器默认的 3/4 视角（roll = 0，所以画面是"平"的）
    const float DEFAULT_PITCH = 30f;    // 向下俯 30°
    const float DEFAULT_YAW   = -45f;   // 水平朝 -45°

    enum Mode { Full, UprightOnly, Origin, Selection }

    // ------------------------------------------------------------------ 菜单（数字=优先级，按顺序排）
    [MenuItem("Tools/干预项目/场景视图相机/1 完全复位（摆正 + 看全场景）", false, 100)]
    public static void ResetFull() { Apply(Mode.Full); }

    [MenuItem("Tools/干预项目/场景视图相机/2 只摆正（位置和朝向不变）", false, 101)]
    public static void ResetUprightOnly() { Apply(Mode.UprightOnly); }

    [MenuItem("Tools/干预项目/场景视图相机/3 回到原点正视角", false, 102)]
    public static void ResetOrigin() { Apply(Mode.Origin); }

    [MenuItem("Tools/干预项目/场景视图相机/4 聚焦选中物体（角度摆正）", false, 103)]
    public static void FocusSelection() { Apply(Mode.Selection); }

    // ------------------------------------------------------------------ 主流程
    static void Apply(Mode mode)
    {
        var log = new StringBuilder();
        log.AppendLine("场景视图相机复位  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        log.AppendLine("模式：" + ModeName(mode));
        log.AppendLine(new string('-', 64));

        var views = new List<SceneView>();
        foreach (var o in SceneView.sceneViews)
        {
            var sv = o as SceneView;
            if (sv != null) views.Add(sv);
        }

        if (views.Count == 0)
        {
            log.AppendLine("× 没有打开的 Scene 视图（按 Ctrl+1，或 Window ▸ General ▸ Scene 打开）");
            Flush(log);
            Debug.LogWarning("[SceneViewReset] 没有打开的 Scene 视图");
            return;
        }

        // 复位后要看哪儿：优先"选中物体"（方便就在手边），否则看整个场景内容
        Bounds? target = null;
        string what = "（没动位置）";
        if (mode == Mode.Selection)
        {
            var sel = BoundsOfSelection();
            target = sel;
            what = sel.HasValue ? "选中物体" + Desc(sel.Value) : "有选中但都没模型（已略）";
        }
        else if (mode == Mode.Full)
        {
            // 完全复位 = 看全场景：优先看"够大的选中"（你正在弄的那个房间/楼层），
            // 选中只是个道具（半径 < 2m）时不要，否则会贴脸；没有选中就看整个场景。
            var sel = BoundsOfSelection();
            if (sel.HasValue && sel.Value.extents.magnitude >= 2f)
            {
                target = sel;
                what = "选中的那组物体" + Desc(sel.Value) + "（想全看请先取消选中）";
            }
            else
            {
                var sc = BoundsOfScene();
                target = sc;
                what = sc.HasValue ? "整个场景内容" + Desc(sc.Value) : "场景里没有带模型的物体 → 按原点处理";
            }
        }
        if (mode == Mode.Selection && !target.HasValue)
            log.AppendLine("（提示：当前没有选中任何带模型的物体，这次只摆正角度，不动位置）");
        log.AppendLine("看的目标：" + what);

        int n = 0;
        foreach (var sv in views)
        {
            Vector3 p0 = sv.pivot, e0 = sv.rotation.eulerAngles;
            float s0 = sv.size;
            bool o0 = sv.orthographic, d0 = sv.in2DMode;
            float roll0 = RollOf(sv.rotation);
            string tag = sv == SceneView.lastActiveSceneView ? "（当前活动）" : "";

            // ① 拆出"朝哪看"：pitch 俯仰 + yaw 水平，扔掉 roll（画面歪的元凶）
            Vector3 fwd = sv.rotation * Vector3.forward;
            float pitch = Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;
            float yaw   = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;

            switch (mode)
            {
                case Mode.UprightOnly:                      // 只摆正
                    sv.rotation = Quaternion.Euler(pitch, yaw, 0f);
                    break;

                case Mode.Origin:                           // 原点 + 默认 3/4 角度
                    sv.pivot    = Vector3.zero;
                    sv.size     = 10f;
                    sv.rotation = Quaternion.Euler(DEFAULT_PITCH, DEFAULT_YAW, 0f);
                    break;

                default:                                    // Full / Selection：默认 3/4 角度
                    sv.rotation = Quaternion.Euler(DEFAULT_PITCH, DEFAULT_YAW, 0f);
                    if (target.HasValue)
                    {
                        sv.pivot = target.Value.center;
                        sv.size  = FitSize(target.Value);
                    }
                    else if (mode == Mode.Full)             // 空场景：回原点看
                    {
                        sv.pivot = Vector3.zero;
                        sv.size  = 10f;
                    }
                    break;
            }

            // ② 核心的"默认状态"（这几项没副作用）
            sv.orthographic     = false;      // 透视（不是正交）
            sv.in2DMode         = false;      // 退出 2D 模式
            sv.isRotationLocked = false;      // 别把视角锁住

            // ③ 显示相关（装饰性，万一某项不支持也不该挡住复位）
            string note = "";
            try
            {
                sv.sceneLighting = true;      // 场景光照开
                sv.drawGizmos    = true;      // Gizmo 显示
                sv.showGrid      = true;      // 网格显示
                var st = sv.sceneViewState;
                if (st != null)
                {
                    st.showSkybox          = true;
                    st.showFog             = true;
                    st.showFlares          = true;
                    st.showImageEffects    = true;
                    st.showParticleSystems = true;
                }
                note = SetShaded(sv);
            }
            catch (Exception e)
            {
                note = "显示设置有一项没改成（" + e.GetType().Name + ": " + e.Message + "）";
            }
            sv.Repaint();

            log.AppendLine("视图 " + (++n) + tag);
            log.AppendLine("  复位前: pivot=" + Fmt(p0) + "  角度=" + Fmt(e0) + "  距离=" + s0.ToString("0.##")
                           + "  正交=" + (o0 ? "是" : "否") + "  2D模式=" + (d0 ? "是" : "否"));
            log.AppendLine("  复位后: pivot=" + Fmt(sv.pivot) + "  角度=" + Fmt(sv.rotation.eulerAngles) + "  距离=" + sv.size.ToString("0.##")
                           + "  正交=" + (sv.orthographic ? "是" : "否") + "  2D模式=" + (sv.in2DMode ? "是" : "否"));
            log.AppendLine("  画面倾斜(roll): " + roll0.ToString("0.##") + "° → 0°" + (Mathf.Abs(roll0) > 0.05f ? "   ← 这就是画面斜着的原因" : ""));
            if (!string.IsNullOrEmpty(note)) log.AppendLine("  " + note);
        }

        log.AppendLine(new string('-', 64));
        log.AppendLine("其它选项（菜单 Tools/干预项目/场景视图相机）：");
        log.AppendLine("  2 只摆正 —— 清掉倾斜但不动你的位置和朝向（想留在原地就点这个）");
        log.AppendLine("  3 回到原点正视角 —— pivot 回 (0,0,0)，距离 10");
        log.AppendLine("  4 聚焦选中物体 —— 先选中场景里的物体再点，视角居中到它");
        log.AppendLine("手动操作速查：Scene 视图里 F = 聚焦选中物体，Shift+F = 跟随锁定，Alt+左键拖 = 绕圈，右键拖 = 转头");
        Flush(log);

        Debug.Log("[SceneViewReset] 场景视图相机已复位（" + ModeName(mode) + "），详见 " + REPORT);
    }

    // ------------------------------------------------------------------ 工具方法
    static string ModeName(Mode m)
    {
        switch (m)
        {
            case Mode.UprightOnly: return "只摆正（位置朝向不变）";
            case Mode.Origin:      return "回到原点正视角";
            case Mode.Selection:   return "聚焦选中物体";
            default:               return "完全复位（摆正 + 看全场景）";
        }
    }

    // 画面倾斜角：相机 up 与"无 roll 时的 up"的夹角（绕视线轴，正值=画面往一边歪）
    static float RollOf(Quaternion rot)
    {
        Vector3 fwd = rot * Vector3.forward;
        Vector3 up  = rot * Vector3.up;
        Vector3 refUp = Vector3.ProjectOnPlane(Vector3.up, fwd);
        Vector3 curUp = Vector3.ProjectOnPlane(up, fwd);
        if (refUp.sqrMagnitude < 1e-6f || curUp.sqrMagnitude < 1e-6f) return 0f;   // 正上/正下看，没有 roll 概念
        return Vector3.SignedAngle(refUp.normalized, curUp.normalized, fwd);
    }

    // 让包围盒刚好填满视口（Scene 视图默认 FOV ≈ 60°）
    static float FitSize(Bounds b)
    {
        float r = Mathf.Max(b.extents.magnitude, 0.5f);
        return Mathf.Clamp(r / Mathf.Sin(30f * Mathf.Deg2Rad) * 1.15f, 3f, 2000f);
    }

    static Bounds? BoundsOfSelection()
    {
        var sel = Selection.gameObjects;
        if (sel == null || sel.Length == 0) return null;
        Bounds? b = null;
        foreach (var go in sel)
        {
            var bb = BoundsOf(go);
            if (!bb.HasValue) bb = new Bounds(go.transform.position, Vector3.one);
            if (!b.HasValue) b = bb;
            else { var t = b.Value; t.Encapsulate(bb.Value); b = t; }
        }
        return b;
    }

    static Bounds? BoundsOfScene()
    {
        Bounds? b = null;
        foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            var bb = BoundsOf(go);
            if (!bb.HasValue) continue;
            if (!b.HasValue) b = bb;
            else { var t = b.Value; t.Encapsulate(bb.Value); b = t; }
        }
        return b;
    }

    static Bounds? BoundsOf(GameObject go)
    {
        if (go == null) return null;
        Bounds? b = null;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null) continue;
            if (!b.HasValue) b = r.bounds;
            else { var t = b.Value; t.Encapsulate(r.bounds); b = t; }
        }
        foreach (var c in go.GetComponentsInChildren<Collider>(true))
        {
            if (c == null) continue;
            if (!b.HasValue) b = c.bounds;
            else { var t = b.Value; t.Encapsulate(c.bounds); b = t; }
        }
        return b;
    }

    // 着色模式回到 Shaded。
    // ⚠ 坑：SceneView.cameraMode 的 setter 只接受"已注册"的模式，手搓 struct 会被拒
    //    （The provided camera mode ... is not registered!），必须用反射拿 Unity 内置注册的那一份。
    static string SetShaded(SceneView sv)
    {
        try
        {
            var mi = typeof(SceneView).GetMethod("GetBuiltinCameraMode",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                null, new[] { typeof(DrawCameraMode) }, null);
            if (mi == null) return "着色模式：跳过（Unity 没暴露内置 CameraMode）";
            var cm = (SceneView.CameraMode)mi.Invoke(null, new object[] { DrawCameraMode.Normal });
            sv.cameraMode = cm;
            return "着色模式：Shaded";
        }
        catch (Exception e)
        {
            return "着色模式：跳过（" + e.GetType().Name + "）";
        }
    }

    static string Fmt(Vector3 v)
    {
        return "(" + v.x.ToString("0.##") + ", " + v.y.ToString("0.##") + ", " + v.z.ToString("0.##") + ")";
    }

    static string Desc(Bounds b)
    {
        return " 中心=" + Fmt(b.center) + " 半径=" + b.extents.magnitude.ToString("0.##") + "m";
    }

    static void Flush(StringBuilder log)
    {
        var dir = Path.GetDirectoryName(REPORT);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(REPORT, log.ToString(), new UTF8Encoding(false));
        AssetDatabase.ImportAsset(REPORT);       // 让 .meta / 资源库跟上
    }
}

// 放 Assets/_camera_trigger.txt → 编辑器下次刷新/重编译后自动跑一次"完全复位"。
[InitializeOnLoad]
public static class SceneViewResetTrigger
{
    const string Trigger = "Assets/_camera_trigger.txt";
    const string ErrFile = "../额外文件/错误_场景视图相机.txt";

    static int tick;
    static bool running;

    static SceneViewResetTrigger()
    {
        if (!File.Exists(Trigger)) return;
        // ★ 不能在排队前就删触发器：域重载会把排队的动作吃掉，触发器却已经消失。
        //    改成动作真正开始时再删。
        // ★ 域重载刚结束时 Scene 视图可能还没建好，所以先等几帧再动手。
        tick = 0;
        EditorApplication.update += WaitThenRun;
    }

    static void WaitThenRun()
    {
        if (running) return;
        tick++;
        if (SceneView.sceneViews.Count == 0 && tick < 120) return;   // 最多等 ~120 帧
        running = true;
        EditorApplication.update -= WaitThenRun;

        try
        {
            if (File.Exists(Trigger)) File.Delete(Trigger);
            SceneViewReset.ResetFull();
            if (File.Exists(ErrFile)) File.Delete(ErrFile);
            Debug.Log("[SceneViewReset] 触发器：场景视图相机已复位");
        }
        catch (Exception e)
        {
            Directory.CreateDirectory("../额外文件");
            File.WriteAllText(ErrFile, e.ToString());
            Debug.LogError("[SceneViewReset] 触发器复位失败: " + e);
        }
    }
}
