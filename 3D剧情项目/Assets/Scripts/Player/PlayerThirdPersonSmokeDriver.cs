// 第三人称自检驱动（Play 模式跑）。
//
// 为什么是运行时脚本、而不是编辑器脚本里 tick：
//   · EditorApplication.update 的 tick ≠ 游戏帧（后台 Play 帧率低，注入输入会跑不动）；
//   · 进/退 Play 会域重载，编辑器侧静态状态、订阅全被冲掉（踩过：自检写到一半就没了）。
//   放在场景里用协程跑，用的就是真游戏帧，而且不受域重载影响（这是仓库里 DoorSmokeDriver 的同一套路）。
//
// 用法：由 Assets/Editor/PlayerThirdPerson.cs 的「② 运行自检」临时挂到场景里 → 进 Play → 跑完写报告 → 编辑器收尾。
// 报告：Assets/assets/_报告/_主角第三人称自检.txt

using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class PlayerThirdPersonSmokeDriver : MonoBehaviour
{
    public const string REPORT = "Assets/assets/_报告/_主角第三人称自检.txt";
    public const string DONE_FLAG = "../额外文件/_player3p_done";
    public const string RUN_FLAG = "../额外文件/_player3p_run";
    public static bool Finished;

    readonly List<string> log = new List<string>();
    float _t0Real; int _f0;
    FirstPersonController fpc;
    Transform player;
    Camera cam;
    Behaviour story;

    /// <summary>
    /// 自举：看到 ../额外文件/_player3p_run 就自己建一个驱动对象。
    /// ★ 为什么不在编辑器里往场景挂：场景被标脏时进 Play 会弹「保存场景?」模态框，把自动化卡死（踩过）。
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (!Application.isEditor) return;
        if (!File.Exists(RUN_FLAG)) return;
        File.Delete(RUN_FLAG);          // ★ 立刻删掉：保证一次自检只有一个驱动（多驱动会互相干扰测量）
        var go = new GameObject("~PlayerThirdPersonSmoke");
        go.AddComponent<PlayerThirdPersonSmokeDriver>();
        Debug.Log("[Player3P自检] 自举创建驱动对象（未动场景）");
    }

    void Start()
    {
        // 只留一个（多驱动会用虚拟输入互相打架，测出来的数全废）
        // ★ 判据必须是「有没有 instanceID 比我小的」：同一帧里建出多个时，
        //   用 all.Length > 1 会让每个实例都以为“有别人”而全部自毁（踩过：结果一个都不剩）。
        foreach (var d in FindObjectsOfType<PlayerThirdPersonSmokeDriver>())
            if (d != this && d.GetInstanceID() < GetInstanceID())
            {
                Debug.Log("[Player3P自检] 已有驱动(" + d.GetInstanceID() + ")，本实例(" + GetInstanceID() + ")自毁");
                Destroy(gameObject);
                return;
            }
        Debug.Log("[Player3P自检] 驱动已启动：" + name);
        StartCoroutine(Run());
    }

    void Say(string s)
    {
        log.Add(s);
        Debug.Log("[Player3P自检] " + s);
    }

    const string PROBE_ONLY = "../额外文件/_camprobe_only";
    const string START_PROBE = "../额外文件/_startprobe_only";
    const string IDLE_PROBE = "../额外文件/_idleprobe_only";

    System.Collections.IEnumerator Run()
    {
        yield return new WaitForSeconds(0.4f);      // 等场景起来
        if (File.Exists(PROBE_ONLY)) { yield return StartCoroutine(ProbeOnly()); yield break; }
        if (File.Exists(START_PROBE)) { yield return StartCoroutine(StartProbe()); yield break; }
        if (File.Exists(IDLE_PROBE)) { yield return StartCoroutine(IdleProbe()); yield break; }

        _t0Real = Time.realtimeSinceStartup; _f0 = Time.frameCount;
        fpc = FindObjectOfType<FirstPersonController>();
        if (fpc == null) { Say("★ 场景里没有 FirstPersonController"); Finish(); yield break; }
        player = fpc.transform;
        cam = fpc.cameraPivot != null ? fpc.cameraPivot.GetComponent<Camera>() : null;
        if (cam == null) { Say("★ cameraPivot 上没有 Camera"); Finish(); yield break; }

        Say("主角第三人称自检  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        Say("");

        // ---- 清残留状态（上一轮卡住会留下虚拟输入 → 角色一直在走，测什么都不准）
        fpc.useInputOverride = false;
        fpc.inputOverride = Vector2.zero;
        fpc.camYaw = player.eulerAngles.y;

        // ---- 剧情开场会锁住玩家，自检期间先停掉
        story = FindObjectOfType<StoryRunner>();
        bool storyWasOn = story != null && story.enabled;
        if (story != null) story.enabled = false;
        bool lockWas = fpc.locked, moveLockWas = fpc.moveLocked;
        fpc.locked = false; fpc.moveLocked = false;

        Say("玩家：" + Path(player) + "   第三人称=" + fpc.thirdPerson);
        Say("驱动的 Animator：" + (fpc.animator != null
            ? fpc.animator.gameObject.name + "  controller=" +
              (fpc.animator.runtimeAnimatorController != null ? fpc.animator.runtimeAnimatorController.name : "无")
            : "★ 没接"));
        int vis = 0, hidden = 0;
        foreach (var r in player.GetComponentsInChildren<Renderer>(true))
        {
            if (fpc.cameraPivot != null && r.transform.IsChildOf(fpc.cameraPivot)) continue;
            if (r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) hidden++;
            else if (r.enabled) vis++;
        }
        Say("主角部件：正常显示 " + vis + "，只投影(隐藏) " + hidden + (vis > 0 ? " ✓" : " ★"));
        Say("剧情锁：StoryRunner=" + (story == null ? "场景里没有" : (storyWasOn ? "开着（自检期间临时停用）" : "本来就关着"))
            + "；玩家 locked=" + lockWas + " moveLocked=" + moveLockWas + "（自检期间临时解开）");

        float behind = Vector3.Dot((cam.transform.position - player.position).normalized, player.forward);
        Say(string.Format("开局：相机在角色身后 dot={0:0.00}（负=背后）{1}", behind, behind < -0.3f ? " ✓" : " ★"));

        // 动画基线
        float t0 = 0, bone = 0;
        Transform probe = null; Vector3 bone0 = Vector3.zero;
        if (fpc.animator != null)
        {
            t0 = fpc.animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            var smr = fpc.animator.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr != null && smr.bones != null && smr.bones.Length > 0 && smr.bones[0] != null)
            { probe = smr.bones[0]; bone0 = probe.position; }
        }

        // ================= ① 只转镜头：相机要绕，角色不能动
        Quaternion rot0 = player.rotation;
        float az0 = Azimuth(player.position, cam.transform.position);
        fpc.camYaw += 60f;
        yield return null; yield return null;
        float dAz = Mathf.DeltaAngle(az0, Azimuth(player.position, cam.transform.position));
        float dRot = Quaternion.Angle(rot0, player.rotation);
        Say(string.Format("① 只转镜头 60°：相机绕了 {0:0.0}° {1}；角色朝向变化 {2:0.0}° {3}",
            dAz, Mathf.Abs(Mathf.Abs(dAz) - 60f) < 10f ? "✓" : "★",
            dRot, dRot < 2f ? "✓（角色没跟着转）" : "★ 角色跟着转了"));

        // ================= ② 只转角色：相机不能被带着走
        float az1 = Azimuth(player.position, cam.transform.position);
        player.Rotate(0f, 90f, 0f, Space.World);
        yield return null; yield return null;
        float dAz2 = Mathf.DeltaAngle(az1, Azimuth(player.position, cam.transform.position));
        Say(string.Format("② 只转角色 90°：相机方位变化 {0:0.0}° {1}", dAz2,
            Mathf.Abs(dAz2) < 5f ? "✓（镜头不跟角色朝向）" : "★ 镜头跟着转了"));

        // ================= ③ 按“W”：相对相机方向走，角色自己转过去
        fpc.camYaw = Azimuth(player.position, cam.transform.position) + 180f;   // 相机在角色正后方 → “W” = 远离相机
        player.rotation = Quaternion.Euler(0f, 180f, 0f);                       // 故意背对，看它会不会自己转
        Vector3 camFwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
        Vector3 p0 = player.position;
        fpc.useInputOverride = true;
        fpc.inputOverride = new Vector2(0f, 1f);                                // W
        for (int i = 0; i < 40; i++) yield return null;
        Vector3 d = player.position - p0; d.y = 0f;
        float alongCam = d.sqrMagnitude > 1e-6f ? Vector3.Dot(d.normalized, camFwd) : -1f;
        float faceCam = Vector3.Dot(player.forward, camFwd);
        Say(string.Format("③ 按“W”（相机前方）走了 {0:0.00}m：位移·相机前方 = {1:0.00} {2}；角色朝向·相机前方 = {3:0.00} {4}",
            d.magnitude, alongCam, alongCam > 0.9f ? "✓ 跟着镜头走" : "★",
            faceCam, faceCam > 0.9f ? "✓ 自己转向移动方向" : "★ 没转"));

        // ================= ④ 按“D”：横着走（仍是相机坐标系）
        Vector3 camRight = Vector3.ProjectOnPlane(cam.transform.right, Vector3.up).normalized;
        Vector3 p1 = player.position;
        fpc.inputOverride = new Vector2(1f, 0f);                                // D
        for (int i = 0; i < 40; i++) yield return null;
        Vector3 d2 = player.position - p1; d2.y = 0f;
        float alongRight = d2.sqrMagnitude > 1e-6f ? Vector3.Dot(d2.normalized, camRight) : -1f;
        Say(string.Format("④ 按“D”走了 {0:0.00}m：位移·相机右方 = {1:0.00} {2}",
            d2.magnitude, alongRight, alongRight > 0.9f ? "✓ 相对镜头" : "★"));

        // ================= ⑤ 动画 / 可见性
        if (probe != null)
        {
            bone = Vector3.Distance(bone0, probe.position);
            Say(string.Format("⑤ 动画：normalizedTime {0:0.00}→{1:0.00}，骨头位移 {2:0.0000}m {3}",
                t0, fpc.animator.GetCurrentAnimatorStateInfo(0).normalizedTime, bone,
                bone > 0.0005f ? "✓ 在播" : "★ 没动"));
        }
        var planes = GeometryUtility.CalculateFrustumPlanes(cam);
        bool inView = false;
        foreach (var smr in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (GeometryUtility.TestPlanesAABB(planes, smr.bounds)) { inView = true; break; }
        Say("主角模型在相机视锥内：" + (inView ? "✓" : "★"));

        // ================= ⑥ 镜头稳定性：走一段，量「相机相对角色根」的偏移有没有抖
        {
            Vector3 root0 = player.position;
            Vector3 camOff0 = cam.transform.position - player.position;
            var smrH = fpc.animator != null ? fpc.animator.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
            Transform hips = null;
            if (smrH != null && smrH.bones != null)
                foreach (var b in smrH.bones)
                    if (b != null && b.name.ToLower().Contains("hips")) { hips = b; break; }
            Vector3 hipsOff0 = hips != null ? hips.position - player.position : Vector3.zero;

            float camMax = 0f, camSum = 0f, rootYmin = float.MaxValue, rootYmax = float.MinValue;
            float hipsMax = 0f, modelLocalMax = 0f;
            var modelRoot = fpc.animator != null ? fpc.animator.transform : null;
            Vector3 modelLocal0 = modelRoot != null ? modelRoot.localPosition : Vector3.zero;
            fpc.useInputOverride = true;
            fpc.inputOverride = new Vector2(0f, 1f);          // 走起来
            const int N = 40;
            for (int i = 0; i < N; i++)
            {
                yield return null;
                Vector3 camOff = cam.transform.position - player.position;
                float dc = (camOff - camOff0).magnitude;
                camSum += dc; if (dc > camMax) camMax = dc;
                rootYmin = Mathf.Min(rootYmin, player.position.y);
                rootYmax = Mathf.Max(rootYmax, player.position.y);
                if (hips != null)
                {
                    float dh = ((hips.position - player.position) - hipsOff0).magnitude;
                    if (dh > hipsMax) hipsMax = dh;
                }
                if (modelRoot != null)
                {
                    float dm = (modelRoot.localPosition - modelLocal0).magnitude;
                    if (dm > modelLocalMax) modelLocalMax = dm;
                }
            }
            fpc.useInputOverride = false; fpc.inputOverride = Vector2.zero;

            Say("⑥ 走路 90 帧的抖动测量：");
            Say(string.Format("   相机相对角色根的偏移：平均变化 {0:0.000}m，最大 {1:0.000}m  {2}",
                camSum / N, camMax, camMax > 0.05f ? "★ 相机自己在动" : "✓ 相机相对稳定"));
            Say(string.Format("   角色根 Y 波动 {0:0.000}m（重力/落地）", rootYmax - rootYmin));
            Say(string.Format("   模型 Hips 相对角色根的位移（动画起伏）：最大 {0:0.000}m {1}",
                hipsMax, hipsMax > 0.02f ? "（走路的正常起伏）" : ""));
            Say(string.Format("   模型根 localPosition 变化（根位移有没有漏出来）：最大 {0:0.000}m {1}",
                modelLocalMax, modelLocalMax > 0.001f ? "★ 有根位移" : "✓ 无"));
        }

        // ---- 还原
        fpc.useInputOverride = false;
        fpc.inputOverride = Vector2.zero;
        fpc.locked = lockWas; fpc.moveLocked = moveLockWas;
        if (story != null && storyWasOn) story.enabled = true;

        Finish();
    }

    // —— 只跑探针：量「相机 / 角色根 / 模型 Hips」各自的世界位移幅度，回答“到底谁在晃”
    System.Collections.IEnumerator ProbeOnly()
    {
        fpc = FindObjectOfType<FirstPersonController>();
        if (fpc == null) { Say("★ 没有 FirstPersonController"); Finish(); yield break; }
        player = fpc.transform;
        cam = fpc.cameraPivot != null ? fpc.cameraPivot.GetComponent<Camera>() : null;
        var all = FindObjectsOfType<PlayerThirdPersonSmokeDriver>();
        Say("镜头抖动探针  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        Say("驱动对象数：" + all.Length + (all.Length > 1 ? "  ★ 多个驱动在跑（会互相干扰）" : " ✓"));
        Say("第三人称=" + fpc.thirdPerson + "  tpFollowSmooth=" + fpc.tpFollowSmooth +
            "  tpDistance=" + fpc.tpDistance + "  相机父节点=" + (cam != null && cam.transform.parent != null ? cam.transform.parent.name : "?"));
        var smr = fpc.animator != null ? fpc.animator.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
        Transform hips = null;
        if (smr != null && smr.bones != null)
            foreach (var b in smr.bones) if (b != null && b.name.ToLower().Contains("hips")) { hips = b; break; }
        Transform modelRoot = fpc.animator != null ? fpc.animator.transform : null;

        fpc.useInputOverride = false; fpc.inputOverride = Vector2.zero;

        yield return Probe("站着不动（Idle）", 30, fpc, hips, modelRoot, false);
        yield return Probe("往前走（Walk）", 40, fpc, hips, modelRoot, true);
        yield return ProbeTurn(fpc);

        Finish();
    }

    System.Collections.IEnumerator Probe(string label, int n, FirstPersonController f, Transform hips, Transform modelRoot, bool walk)
    {
        f.useInputOverride = walk;
        f.inputOverride = walk ? new Vector2(0f, 1f) : Vector2.zero;
        yield return null;
        Vector3 c0 = cam.transform.position, r0 = player.position;
        Vector3 h0 = hips != null ? hips.position : Vector3.zero;
        Vector3 m0 = modelRoot != null ? modelRoot.localPosition : Vector3.zero;
        float camMax = 0, hipsMax = 0, rootMax = 0, modelMax = 0;
        float offMax = 0, offSum = 0; Vector3 off0 = cam.transform.position - player.position;
        float t0 = Time.realtimeSinceStartup; int f0 = Time.frameCount;
        for (int i = 0; i < n; i++)
        {
            yield return null;
            camMax = Mathf.Max(camMax, (cam.transform.position - c0).magnitude);
            Vector3 off = (cam.transform.position - player.position) - off0;   // 相机相对角色的偏移变化 = 真正会看出来的“晃”
            offSum += off.magnitude; offMax = Mathf.Max(offMax, off.magnitude);
            rootMax = Mathf.Max(rootMax, (player.position - r0).magnitude);
            if (hips != null) hipsMax = Mathf.Max(hipsMax, ((hips.position - r0) - (h0 - r0)).magnitude);
            if (modelRoot != null) modelMax = Mathf.Max(modelMax, (modelRoot.localPosition - m0).magnitude);
        }
        float dt = Time.realtimeSinceStartup - t0; int df = Time.frameCount - f0;
        f.useInputOverride = false; f.inputOverride = Vector2.zero;
        Say(string.Format("[{0}] {1} 帧（{2:0.0}s，{3:0.0}fps）", label, n, dt, df / Mathf.Max(0.01f, dt)));
        Say(string.Format("    相机世界位移 最大 {0:0.000}m | 角色根 {1:0.000}m | 模型Hips相对根 {2:0.000}m | 模型根localPos {3:0.000}m",
            camMax, rootMax, hipsMax, modelMax));
        Say(string.Format("    相机相对角色的偏移：平均 {0:0.000}m 最大 {1:0.000}m {2}",
            offSum / n, offMax, offMax > 0.06f ? "★ 看得出来在晃" : "✓ 稳定"));
        Say(string.Format("    相机到角色根的距离：起点 {0:0.00}m 现在 {1:0.00}m",
            off0.magnitude, (cam.transform.position - player.position).magnitude));
    }

    // 横移（角色要转身 90°）时，镜头朝向/位置不能跟着转
    System.Collections.IEnumerator ProbeTurn(FirstPersonController f)
    {
        f.useInputOverride = false; f.inputOverride = Vector2.zero;
        yield return null;
        Vector3 f0 = cam.transform.forward;
        Vector3 off0 = cam.transform.position - player.position;
        float rot0 = player.eulerAngles.y;
        float maxStep = 0f, maxOff = 0f;
        f.useInputOverride = true; f.inputOverride = new Vector2(1f, 0f);   // D：往右横移 → 角色转向 90°
        for (int i = 0; i < 40; i++)
        {
            Vector3 prev = cam.transform.forward;
            yield return null;
            maxStep = Mathf.Max(maxStep, Vector3.Angle(prev, cam.transform.forward));
            maxOff = Mathf.Max(maxOff, ((cam.transform.position - player.position) - off0).magnitude);
        }
        f.useInputOverride = false; f.inputOverride = Vector2.zero;
        float total = Vector3.Angle(f0, cam.transform.forward);
        float turned = Mathf.Abs(Mathf.DeltaAngle(rot0, player.eulerAngles.y));
        Say(string.Format("[横移+转身] 角色转了 {0:0.0}°；镜头朝向总变化 {1:0.00}°（单帧最大 {2:0.00}°）{3}；镜头相对角色的偏移变化最大 {4:0.000}m {5}",
            turned, total, maxStep,
            (maxStep < 0.5f && total < 2f) ? "✓ 镜头没跟着转" : "★ 镜头跟着转了",
            maxOff, maxOff < 0.06f ? "✓" : "★"));
    }

    // —— 开局探针：从第一帧开始逐帧看「角色根」有没有动，以及当时黑幕黑不黑
    System.Collections.IEnumerator StartProbe()
    {
        fpc = FindObjectOfType<FirstPersonController>();
        if (fpc == null) { Say("★ 没有 FirstPersonController"); Finish(); yield break; }
        player = fpc.transform;
        var cc = player.GetComponent<CharacterController>();
        var smr = fpc.animator != null ? fpc.animator.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
        Transform hips = null;
        if (smr != null && smr.bones != null)
            foreach (var b in smr.bones) if (b != null && b.name.ToLower().Contains("hips")) { hips = b; break; }
        CanvasRenderer fade = null;
        foreach (var cr in FindObjectsOfType<CanvasRenderer>())
            if (cr.gameObject.name.ToLower().Contains("blackfade")) { fade = cr; break; }

        Say("开局探针  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        Say("（逐帧看角色根位置；只看前 150 帧）");
        Vector3 p0 = player.position;
        float hips0 = hips != null ? hips.position.y : 0f;
        Vector3 prev = p0;
        float total = 0f, maxStep = 0f;
        int stepFrame = -1;
        // ★ 前 12 帧逐帧记黑幕 alpha（看开场到底黑没黑）
        for (int i = 0; i < 150; i++)
        {
            yield return null;
            Vector3 p = player.position;
            float d = (p - prev).magnitude;
            total += d;
            if (d > maxStep) { maxStep = d; stepFrame = i + 1; }
            if (i < 12 && fade != null)
                Say(string.Format("   帧 {0,3}: BlackFade(active={1}) alpha={2:0.00}  角色位置 ({3:0.00},{4:0.00},{5:0.00})",
                    i + 1, fade.gameObject.activeInHierarchy, fade.GetAlpha(), p.x, p.y, p.z));
            if (d > 0.005f)   // 超过 5mm 就记一笔
            {
                Say(string.Format("   帧 {0,3}: 移动 {1:0.000}m  → ({2:0.00},{3:0.00},{4:0.00})  黑幕 alpha={5:0.00}  grounded={6}  累计 {7:0.00}m",
                    i + 1, d, p.x, p.y, p.z, fade != null ? fade.GetAlpha() : -1f,
                    cc != null ? cc.isGrounded.ToString() : "?", total));
            }
            prev = p;
        }
        Say("");
        Say(string.Format("总计：150 帧里移动了 {0:0.000}m（单帧最大 {1:0.000}m，出现在第 {2} 帧）", total, maxStep, stepFrame));
        Say(string.Format("落点：起点 ({0:0.00},{1:0.00},{2:0.00}) → 现在 ({3:0.00},{4:0.00},{5:0.00})",
            p0.x, p0.y, p0.z, player.position.x, player.position.y, player.position.z));
        Say(string.Format("模型 Hips 高度：第 0 帧 {0:0.000} → 现在 {1:0.000}（差 {2:0.000}m，纯动画姿势）",
            hips0, hips != null ? hips.position.y : 0f, hips != null ? hips.position.y - hips0 : 0f));
        Say("黑幕对象：" + (fade != null ? fade.gameObject.name + "  alpha=" + fade.GetAlpha().ToString("0.00") : "★ 没找到 BlackFade（那就盖不住瞬移）"));
        Finish();
    }

    // —— 待机重播探针：走一段 → 停下 → 看待机是否从第 0 帧起
    System.Collections.IEnumerator IdleProbe()
    {
        fpc = FindObjectOfType<FirstPersonController>();
        player = fpc.transform;
        var an = fpc.animator;
        Say("待机重播探针  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        Say("（check：停下后 Locomotion 状态的 normalizedTime 应该回到 ≈0）");
        if (an == null) { Say("★ 没有 animator"); Finish(); yield break; }

        // 1) 先走起来
        fpc.useInputOverride = true; fpc.inputOverride = new Vector2(0f, 1f);
        for (int i = 0; i < 40; i++) yield return null;
        var st = an.GetCurrentAnimatorStateInfo(0);
        Say(string.Format("走动中：状态 {0}  normalizedTime={1:0.000}", st.shortNameHash, st.normalizedTime));

        // 2) 停下（松开输入）
        fpc.inputOverride = Vector2.zero;
        for (int i = 0; i < 4; i++) yield return null;
        var s0 = an.GetCurrentAnimatorStateInfo(0);
        float t0 = s0.normalizedTime % 1f;
        Say(string.Format("刚停下（4 帧后）：normalizedTime={0:0.000}  {1}", t0,
            t0 < 0.05f ? "✓ 从第 0 帧起" : (t0 > 0.9f ? "（≈1，等价于刚绕回第 0 帧）" : "★ 接在中间相位")));

        // 3) 再走再停，重复两次确认"每一次"
        for (int round = 2; round <= 3; round++)
        {
            fpc.inputOverride = new Vector2(0f, 1f);
            for (int i = 0; i < 30; i++) yield return null;
            fpc.inputOverride = Vector2.zero;
            for (int i = 0; i < 4; i++) yield return null;
            float tt = an.GetCurrentAnimatorStateInfo(0).normalizedTime % 1f;
            Say(string.Format("第 {0} 次走→停：normalizedTime={1:0.000}  {2}", round, tt,
                tt < 0.05f ? "✓ 从第 0 帧起" : (tt > 0.9f ? "（≈1）" : "★ 接在中间相位")));
        }
        fpc.useInputOverride = false; fpc.inputOverride = Vector2.zero;
        Finish();
    }

    static float Azimuth(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from; d.y = 0f;
        return d.sqrMagnitude < 1e-6f ? 0f : Quaternion.LookRotation(d, Vector3.up).eulerAngles.y;
    }

    static string Path(Transform t)
    {
        var l = new List<string>();
        while (t != null) { l.Add(t.name); t = t.parent; }
        l.Reverse();
        return string.Join("/", l.ToArray());
    }

    void Finish()
    {
        float dtReal = Time.realtimeSinceStartup - _t0Real;
        int dFrames = Time.frameCount - _f0;
        log.Add(string.Format("⑥ 本次自检实测帧率：{0} 帧 / {1:0.0}s ≈ {2:0.0} fps{3}",
            dFrames, dtReal, dFrames / Mathf.Max(0.01f, dtReal),
            dFrames / Mathf.Max(0.01f, dtReal) < 20f ? "（★ 后台 Play 被限速：编辑器窗口没在前台/没在渲染时，游戏帧率会掉到个位数 —— 自检的数字要按“帧”看，别按秒）" : ""));
        log.Add("");
        log.Add("（自检结束，编辑器会自动退出 Play）");
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(REPORT));
            File.WriteAllText(REPORT, string.Join("\n", log.ToArray()));
        }
        catch (System.Exception e) { Debug.LogError("[Player3P自检] 写报告失败：" + e); }
        try { File.WriteAllText(DONE_FLAG, System.DateTime.Now.ToString("o")); } catch { }
        try { if (File.Exists(RUN_FLAG)) File.Delete(RUN_FLAG); } catch { }
        Finished = true;
    }
}
