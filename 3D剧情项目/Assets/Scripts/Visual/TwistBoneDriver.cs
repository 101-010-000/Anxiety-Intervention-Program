// ⛔⛔⛔ 不要使用这个脚本 ⛔⛔⛔
// 2026-09-28：本脚本作为"脚部变形"的修复方案试过，**实测让情况更糟** ——
//            加上之后「所有脚都弯曲了」。已从预览场景全部移除。
//            保留文件只为留个记录，不要再往角色上挂。
//            排查请看：Assets/assets/_报告/_脚部变形_诊断报告.md
// ---------------------------------------------------------------------------
// 给 Character Creator（CC_Base）骨架的"扭转骨"补上驱动。
//
// 【为什么需要】
// 套件（衣服/鞋/袜）把脚踝、大腿根附近的权重压在了 CC_Base 的扭转骨上：
//     CC_BaseThighTwist01/02、CC_BaseCalfTwist01/02
// 这些骨是【小腿/大腿的兄弟骨】——它们不驱动脚，只负责把关节处的形变"分摊"开。
// CC_Base 的原生动画里它们跟随关节做部分旋转；但 mixamo 动画里【没有对应的扭转骨】，
// Unity 的人形重定向不会去驱动它们 → 它们永远停在绑定姿势。
//
// 后果：脚踝一圈的顶点跟着"不转的小腿骨"，鞋/袜的其余部分跟着"转 100°+ 的脚骨"
//       → 蒙皮被从脚踝撕开，看起来就是个"肿包"。绑定姿势下两骨重合，所以静止时完全正常，
//         只有走路/跑步（脚一弯）才炸 —— 而且【每一双鞋都有】，因为都带扭转骨权重。
//
// 【这个脚本做什么】
// 每帧（LateUpdate，即 Animator 之后）把扭转骨的朝向设成"两端关节朝向的插值"：
//     大腿扭转 1/2 → 大腿↔小腿 插值 0.5，大腿扭转 2/2 → 插值 0.75
//     小腿扭转 1/2 → 小腿↔脚   插值 0.5，小腿扭转 2/2 → 插值 0.8
// 绑定姿势下两端朝向相同 → 插值结果 == 原值 → 【静止时一个像素都不变】，
// 只有关节弯了才起作用。这就把关节形变重新摊回三根骨上。
//
// ⚠ 扭转骨是关节的【兄弟】，不是父级，所以改它们不会带动脚/脚趾。
//
// 挂法：挂在角色根节点（或任意祖先）上即可，会自己找骨头。挂上后再打开 Inspector 看 _twists。
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(10000)]   // 排在 Animator 之后
public class TwistBoneDriver : MonoBehaviour
{
    [System.Serializable]
    public class Twist
    {
        [Tooltip("要驱动的扭转骨")] public Transform bone;
        [Tooltip("插值的上端（靠近身体的一端）")] public Transform from;
        [Tooltip("插值的下端（靠近末端的一端）")] public Transform to;
        [Range(0f, 1f), Tooltip("0=完全跟 from，1=完全跟 to")] public float t = 0.5f;
    }

    [Tooltip("留空就让脚本自己按名字找（CC_Base 套件）")]
    public List<Twist> twists = new List<Twist>();

    [Header("自动匹配（名字规则）")]
    [Tooltip("大腿扭转：thigh↔calf 的插值；小腿扭转：calf↔foot 的插值")]
    public bool autoBind = true;

    void Awake()
    {
        if (autoBind) Bind();
    }

    /// 按 CC_Base 的命名去找扭转骨和它的两端
    public void Bind()
    {
        twists.Clear();
        var map = new Dictionary<string, Transform>();
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (!map.ContainsKey(t.name)) map[t.name] = t;

        foreach (var side in new[] { "L", "R" })
        {
            var thigh = Find(map, "CC_BaseThigh." + side, "CC_Base_Thigh." + side);
            var calf = Find(map, "CC_BaseCalf." + side, "CC_Base_Calf." + side);
            var foot = Find(map, "CC_BaseFoot." + side, "CC_Base_Foot." + side);

            Add(TwistDeep(map, side, "CC_BaseThighTwist01", "CC_Base_ThighTwist01"), thigh, calf, 0.50f);
            Add(TwistDeep(map, side, "CC_BaseThighTwist02", "CC_Base_ThighTwist02"), thigh, calf, 0.75f);
            Add(TwistDeep(map, side, "CC_BaseCalfTwist01", "CC_Base_CalfTwist01"), calf, foot, 0.50f);
            Add(TwistDeep(map, side, "CC_BaseCalfTwist02", "CC_Base_CalfTwist02"), calf, foot, 0.80f);
        }
    }

    void Add(Transform bone, Transform from, Transform to, float t)
    {
        if (bone == null || from == null || to == null) return;
        twists.Add(new Twist { bone = bone, from = from, to = to, t = t });
    }

    static Transform Find(Dictionary<string, Transform> map, string a, string b)
    {
        Transform t;
        if (map.TryGetValue(a, out t)) return t;
        if (map.TryGetValue(b, out t)) return t;
        return null;
    }

    /// 扭转骨在套件里有 "CC_BaseCalfTwist01.L" 和 "CC_BaseCalfTwist01_L" 两种写法，都试一遍
    static Transform TwistDeep(Dictionary<string, Transform> map, string side, string a, string b)
    {
        Transform t;
        if (map.TryGetValue(a + "." + side, out t)) return t;
        if (map.TryGetValue(b + "." + side, out t)) return t;
        if (map.TryGetValue(a + "_" + side, out t)) return t;
        if (map.TryGetValue(b + "_" + side, out t)) return t;
        return null;
    }

    void LateUpdate()
    {
        for (int i = 0; i < twists.Count; i++)
        {
            var tw = twists[i];
            if (tw == null || tw.bone == null || tw.from == null || tw.to == null) continue;
            // 世界朝向插值（套件骨骼缩放是均匀的，直接赋 world rotation 是安全的）
            tw.bone.rotation = Quaternion.Slerp(tw.from.rotation, tw.to.rotation, tw.t);
        }
    }
}
