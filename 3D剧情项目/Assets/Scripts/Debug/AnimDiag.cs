// 运行时动画诊断 —— 进入 Play 后自动运行，不需要往场景里挂任何东西。
//
// 做法：等 1.5 秒让动画进入稳态，记录所有骨骼位置，再等 1.2 秒，比较位移。
//   位移 > 0.5mm  → 动画确实在播
//   位移 ≈ 0      → Animator 没在动（控制器/Avatar/剪辑 某一环有问题）
//
// 输出：额外文件/动画运行时诊断.txt   同时打到 Console

using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public static class AnimDiag
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("~AnimDiag");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<AnimDiagRunner>();
    }
}

public class AnimDiagRunner : MonoBehaviour
{
    const float Wait = 1.2f;

    IEnumerator Start()
    {
        yield return new WaitForSeconds(1.5f);          // 等 Animator 稳定

        var anims = Object.FindObjectsOfType<Animator>(true);
        var before = new Dictionary<Animator, Vector3[]>();

        foreach (var a in anims) if (a != null) before[a] = Sample(a);
        yield return new WaitForSeconds(Wait);

        var sb = new StringBuilder();
        sb.AppendLine("运行时动画诊断  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("场景里 Animator 数量: " + anims.Length);
        sb.AppendLine("判定：骨骼在 " + Wait + " 秒内位移 > 0.5mm 才算“在播”");
        sb.AppendLine();

        foreach (var a in anims)
        {
            if (a == null) continue;
            var st = a.GetCurrentAnimatorStateInfo(0);
            Vector3[] now = Sample(a);
            Vector3[] old;
            float moved = 0f;
            if (before.TryGetValue(a, out old) && old != null && old.Length == now.Length)
                for (int i = 0; i < now.Length; i++)
                    moved = Mathf.Max(moved, Vector3.Distance(old[i], now[i]));

            sb.AppendLine(PathOf(a.transform));
            sb.AppendLine(string.Format("    enabled={0}  层级激活={1}  骨骼数={2}",
                a.enabled, a.gameObject.activeInHierarchy, now.Length));
            sb.AppendLine(string.Format("    controller={0}",
                a.runtimeAnimatorController != null ? a.runtimeAnimatorController.name : "★ 没有"));
            if (a.avatar != null)
                sb.AppendLine(string.Format("    avatar={0}  isValid={1}  isHuman={2}",
                    a.avatar.name, a.avatar.isValid, a.avatar.isHuman));
            else
                sb.AppendLine("    avatar=★ 没有");

            // ★ 关键：Humanoid 重定向能不能找到骨骼？
            sb.AppendLine(string.Format("    isHuman={0}", a.isHuman));
            if (a.isHuman)
            {
                var hips = a.GetBoneTransform(HumanBodyBones.Hips);
                var head = a.GetBoneTransform(HumanBodyBones.Head);
                sb.AppendLine(string.Format("    GetBoneTransform(Hips)={0}", hips != null ? hips.name : "★ null（重定向找不到骨骼！）"));
                sb.AppendLine(string.Format("    GetBoneTransform(Head)={0}", head != null ? head.name : "★ null"));
                sb.AppendLine(string.Format("    bodyPosition={0}", a.bodyPosition));

                // 直接写一根骨头的旋转看能不能驱动
                if (hips != null)
                {
                    var q0 = hips.localRotation;
                    var pr = a.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                    sb.AppendLine(string.Format("    Hips 局部旋转={0}", q0.eulerAngles));
                    sb.AppendLine(string.Format("    左上臂={0}", pr != null ? pr.name : "null"));
                }
            }
            // 列出层级里前 24 个名字，看骨骼命名风格
            var nm = new List<string>();
            foreach (var t in a.GetComponentsInChildren<Transform>(true))
            {
                if (t == a.transform) continue;
                nm.Add(t.name);
                if (nm.Count >= 24) break;
            }
            sb.AppendLine("    前 24 个子对象: " + string.Join(", ", nm.ToArray()));

            sb.AppendLine(string.Format("    当前状态 fullPathHash={0}  normalizedTime={1:0.000}  speed={2:0.0}  层权重={3:0.00}",
                st.fullPathHash, st.normalizedTime, st.speed, a.GetLayerWeight(0)));
            sb.AppendLine(string.Format("    → 位移 {0:0.0000} m   {1}", moved,
                moved > 0.0005f ? "✔ 在播" : "★ 没动"));
            sb.AppendLine();
        }
        if (anims.Length == 0) sb.AppendLine("★ 场景里一个 Animator 都没有");

        // Assets/../../额外文件 = 仓库根下的 额外文件（不要在 Unity 项目内留日志）
        string dir = Path.Combine(Application.dataPath, "../../额外文件");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "动画运行时诊断.txt");
        File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
        Debug.Log("[AnimDiag] 已写 " + file + "\n" + sb.ToString());
    }

    static Vector3[] Sample(Animator a)
    {
        var list = new List<Vector3>();
        foreach (var t in a.GetComponentsInChildren<Transform>(true))
            list.Add(t.position);
        return list.ToArray();
    }

    static string PathOf(Transform t)
    {
        var s = t.name;
        while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }
}
