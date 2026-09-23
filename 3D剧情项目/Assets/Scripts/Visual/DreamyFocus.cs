// 朦胧（远近分层）：把「多远以内清楚 / 多远开始全糊」这套值同步到全局面板的景深上。
// 挂在第一人称相机上（菜单 Tools/干预项目/场景后期效果/① 会自动挂）。
//
// 【为什么用 Gaussian 景深，不用 Bokeh】
//   Bokeh 是「以对焦点为中心、前后都糊」，模糊度和 |1-对焦/深度| 挂钩：
//   对焦 5m 时，1m 处的东西直接糊到顶 —— 物理上做不到"近处一大片清晰"。
//   Gaussian 只糊 gaussianStart 以后，前面完全不动 → 天生就是"近处实、远处虚"。
//
// 这个脚本本身不做射线检测。它只是：
//   ① 把 hazeStart / hazeEnd 写到运行期的 Volume 上（改的是 volume.profile 实例，
//      不会污染 .asset），这样在 Play 模式里拖滑块就能实时看效果；
//   ② 给剧情/对话系统留一个口子：SetHaze(start, end) 可以让「正在说话的人」变清楚。
//
// 调参：
//   hazeStart  这个距离（米）以内完全不动。想近处更清楚就调大
//   hazeEnd    到这个距离糊到最满。想过渡更急就调小
//   liveEdit   关掉就不再每帧同步（你在 Volume 资产里改的值说了算）

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[RequireComponent(typeof(Camera))]
public class DreamyFocus : MonoBehaviour
{
    [Header("远近朦胧（Gaussian 景深）")]
    [Tooltip("多少米以内完全不动（越大，近处越清楚）")]
    public float hazeStart = 6.5f;
    [Tooltip("到多少米糊到最满（越小，远近对比越强）")]
    public float hazeEnd = 24f;
    [Tooltip("关掉就不再每帧写入（由 Volume 资产里的值决定）")]
    public bool liveEdit = true;

    DepthOfField dof;

    void Awake() { Bind(); }

    /// 找全局面板里的 DepthOfField，拿它的【运行期实例】。
    /// ★ 必须用 volume.profile（实例），不能用 sharedProfile —— 后者是磁盘资产，
    ///   运行期改它会把工程里的 .asset 写脏。
    void Bind()
    {
        foreach (var v in FindObjectsOfType<Volume>())
        {
            if (v == null || !v.isGlobal || v.sharedProfile == null) continue;
            var p = v.profile;
            if (p == null) continue;
            for (int i = 0; i < p.components.Count; i++)
            {
                var d = p.components[i] as DepthOfField;
                if (d != null) { dof = d; break; }
            }
            if (dof != null) return;
        }
    }

    void LateUpdate()
    {
        if (!liveEdit) return;
        if (dof == null)
        {
            Bind();
            if (dof == null) return;
        }
        if (dof.mode.value != DepthOfFieldMode.Gaussian) return;

        dof.gaussianStart.value = hazeStart;
        dof.gaussianEnd.value = Mathf.Max(hazeEnd, hazeStart + 0.1f);
    }

    /// 给剧情/对话系统用：临时把某段距离拉进清晰区。
    /// 例：跟 NPC 说话时 SetHaze(8, 20) —— 8m 内全清楚，后面的背景才化开。
    public void SetHaze(float start, float end)
    {
        hazeStart = start;
        hazeEnd = end;
    }
}
