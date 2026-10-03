// 第4章坐姿门控（用户 2026-10-02）：
//   ① 宿舍两个座位点（第4章_坐下看资料 / 第5章_回座位）的 SitSpot.requireInteract = 自己节点上的交互点名——
//      剧情没走到那个 F 点（按 F 消费）之前，「锁住自动坐下」一律不触发（班群段人还站在书桌边就被
//      宿舍座位 auto 坐下，用户实测踩过）。
//   ② 第4章_班群通知 的 StoryInteractable.promptText 保持「点开班群通知」（用户 2026-10-02 定稿：
//      提示词不改回来，只要按 F 后不坐下；曾试改「拿起手机」被用户打回）。
// 幂等：已是目标值就跳过。报告 assets/_报告/_第4章坐姿门控.txt
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class Ch4SitGateSetup
{
    const string TRIGGER = "Assets/_ch4sitgate_trigger.txt";
    const string REPORT = "Assets/assets/_报告/_第4章坐姿门控.txt";

    [MenuItem("Tools/干预项目/第4章坐姿门控/应用到场景（幂等）")]
    public static void Apply()
    {
        var sb = new StringBuilder();
        sb.AppendLine("第4章坐姿门控  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        sb.AppendLine("背景：班群段不许提前坐下（requireInteract 门控）+ 通知交互改「拿起手机」保持站着");
        bool opened = false;
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName("Game");
        if (!scene.IsValid() || !scene.isLoaded)
        {
            scene = EditorSceneManager.OpenScene("Assets/Scenes/Game.unity", OpenSceneMode.Additive);
            opened = true;
            sb.AppendLine("Game.unity 未打开 → 叠加加载（完事关闭，不动你开着的场景）");
        }
        int changed = 0;
        changed += GateSeat("第4章_坐下看资料", sb);
        changed += GateSeat("第5章_回座位", sb);
        changed += SetPrompt("第4章_班群通知", "点开班群通知", sb);
        if (changed > 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            sb.AppendLine("已存盘 Game.unity（改动 " + changed + " 处）");
        }
        else sb.AppendLine("零改动（已是目标值）");
        if (opened) EditorSceneManager.CloseScene(scene, true);
        System.IO.File.WriteAllText(REPORT, sb.ToString());
        Debug.Log("[Ch4SitGate] 完成，报告 " + REPORT);
    }

    static int GateSeat(string seatName, StringBuilder sb)
    {
        var go = GameObject.Find(seatName);
        if (go == null) { sb.AppendLine("✗ 找不到 " + seatName); return 0; }
        var spot = go.GetComponent<SitSpot>();
        if (spot == null) { sb.AppendLine("✗ " + seatName + " 没有 SitSpot"); return 0; }
        var si = go.GetComponent<StoryInteractable>();
        if (si == null) { sb.AppendLine("✗ " + seatName + " 没有 StoryInteractable（门控目标就是它）"); return 0; }
        if (spot.requireInteract == seatName) { sb.AppendLine("= " + seatName + " 已门控"); return 0; }
        Undo.RecordObject(spot, "Ch4 SitGate");
        spot.requireInteract = seatName;
        EditorUtility.SetDirty(spot);
        sb.AppendLine("+ " + seatName + ".SitSpot.requireInteract = " + seatName
            + "（radius=" + spot.radius.ToString("F1") + "）");
        return 1;
    }

    static int SetPrompt(string nodeName, string prompt, StringBuilder sb)
    {
        var go = GameObject.Find(nodeName);
        if (go == null) { sb.AppendLine("✗ 找不到 " + nodeName); return 0; }
        var si = go.GetComponent<StoryInteractable>();
        if (si == null) { sb.AppendLine("✗ " + nodeName + " 没有 StoryInteractable"); return 0; }
        if (si.promptText == prompt) { sb.AppendLine("= " + nodeName + ".promptText 已是「" + prompt + "」"); return 0; }
        Undo.RecordObject(si, "Ch4 SitGate");
        string old = si.promptText;
        si.promptText = prompt;
        EditorUtility.SetDirty(si);
        sb.AppendLine("+ " + nodeName + ".promptText：「" + old + "」→「" + prompt + "」");
        return 1;
    }

    [InitializeOnLoadMethod]
    static void Trigger()
    {
        if (System.IO.File.Exists(TRIGGER))
        {
            System.IO.File.Delete(TRIGGER);
            EditorApplication.delayCall += Apply;
        }
    }
}
