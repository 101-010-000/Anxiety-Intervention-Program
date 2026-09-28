// 角色动画预览的 Play 模式自检：确认每个角色【真的在动】。
// 只在 AnimPreviewSetup 的菜单/触发器要求时才跑（Requested = true）。
//
// 为什么不能只看"挂上了没"：Animator 上挂了 controller 也可能因为
//   · 没有 Avatar（人形重定向失效）
//   · Avatar 不是 Humanoid
//   · controller 里 state 的 Motion 为空
// 而完全不播。所以这里进 Play 采样两帧，比较 normalizedTime 有没有推进。
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class AnimPreviewSmokeDriver : MonoBehaviour
{
    public static bool Requested;
    public static bool Finished;
    public static readonly List<string> Lines = new List<string>();
    public static readonly List<string> Errors = new List<string>();

    static bool _hooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (!Requested) return;
        var go = new GameObject("~AnimPreviewSmokeDriver");
        DontDestroyOnLoad(go);
        go.AddComponent<AnimPreviewSmokeDriver>();
    }

    void Awake()
    {
        if (!_hooked) { Application.logMessageReceived += OnLog; _hooked = true; }
        StartCoroutine(Run());
    }

    static void OnLog(string msg, string stack, LogType type)
    {
        if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
            Errors.Add(type + "：" + msg);
    }

    IEnumerator Run()
    {
        Finished = false;
        Lines.Clear(); Errors.Clear();

        for (int i = 0; i < 3; i++) yield return null;

        // 只看"真正驱动蒙皮"的 Animator（从蒙皮网格往上找最近的 Animator）
        var drivers = new HashSet<Animator>();
        foreach (var smr in Object.FindObjectsOfType<SkinnedMeshRenderer>())
        {
            var an = smr != null ? smr.GetComponentInParent<Animator>() : null;
            if (an != null) drivers.Add(an);
        }
        Lines.Add("驱动蒙皮的 Animator：" + drivers.Count + " 个");

        var list = drivers.OrderBy(a => a.transform.root.name).ToList();
        var t0 = new Dictionary<Animator, float>();
        foreach (var a in list)
            t0[a] = a.GetCurrentAnimatorStateInfo(0).normalizedTime;

        // 等约 0.4 秒，看时间有没有推进
        float until = Time.time + 0.4f;
        while (Time.time < until) yield return null;

        int ok = 0;
        foreach (var a in list)
        {
            var st = a.GetCurrentAnimatorStateInfo(0);
            float d = st.normalizedTime - t0[a];
            bool hasAvatar = a.avatar != null;
            bool isHuman = hasAvatar && a.isHuman;
            bool hasCtrl = a.runtimeAnimatorController != null;
            float loop = hasCtrl && a.runtimeAnimatorController.animationClips.Length > 0
                       ? a.runtimeAnimatorController.animationClips[0].length : 0f;
            bool playing = Mathf.Abs(d) > 1e-4f || (loop > 0f && st.normalizedTime > 0f);

            string clipName = hasCtrl ? a.runtimeAnimatorController.name : "(无)";
            Lines.Add(string.Format("  {0}  {1,-9} controller={2,-9} avatar={3} humanoid={4} 片段长={5:0.00}s Δ={6:+0.0000;-0.0000;0}",
                playing ? "✓" : "★", a.transform.root.name, clipName,
                hasAvatar ? "有" : "无", isHuman ? "是" : "否", loop, d));
            if (playing && hasAvatar && isHuman && hasCtrl) ok++;
        }

        Lines.Add("");
        Lines.Add("在动的角色：" + ok + " / " + list.Count);
        Lines.Add("（Δ 是 0.4 秒里 normalizedTime 的推进量；为 0 说明这个角色没在播）");
        Lines.Add("游戏跑了 " + Time.frameCount + " 帧");
        Finished = true;
    }
}
