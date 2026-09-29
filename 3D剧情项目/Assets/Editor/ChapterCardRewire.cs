// 章节卡灰罩引用接回（幂等）：章节选择页 3/4/5 卡的 ChapterCardUI.lockOverlay/cardImage 断链成 {fileID: 0}
// （a73eff2d 贴图换新时「底 / 锁定遮罩」子节点被删掉重建，fileID 变了，引用没接回），
// 而「锁定遮罩」节点在场景里还存成激活 → 运行时 SetState 想关也找不到对象 → 已完成的章也蒙一层灰。
// 按【子节点名字】把断掉的字段接回，只填空引用、绝不动已有引用和任何布局数值。
// 菜单：Tools/干预项目/章节卡/接回灰罩引用（幂等） → 报告 assets/_报告/_章节卡灰罩.txt
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class ChapterCardRewire
{
    const string ReportPath = "Assets/assets/_报告/_章节卡灰罩.txt";

    [MenuItem("Tools/干预项目/章节卡/接回灰罩引用（幂等）")]
    public static void Run()
    {
        var log = new StringBuilder();
        log.AppendLine("== 章节卡灰罩引用接回 ==");
        log.AppendLine("时间：" + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        if (Application.isPlaying)
        {
            Debug.LogWarning("[章节卡灰罩] Play 模式下不跑，退出 Play 再执行。");
            return;
        }

        int fixedCount = 0, checkedCount = 0;
        foreach (var ui in Object.FindObjectsOfType<ChapterCardUI>(true))
        {
            checkedCount++;
            string card = ui.gameObject.name;
            var changes = new List<string>();

            if (ui.lockOverlay == null)
            {
                var t = ui.transform.Find("锁定遮罩");
                if (t != null) { ui.lockOverlay = t.gameObject; changes.Add("lockOverlay←锁定遮罩"); }
                else log.AppendLine("  ⚠ " + card + "：找不到子节点「锁定遮罩」，lockOverlay 仍为空");
            }
            if (ui.cardImage == null)
            {
                var t = ui.transform.Find("底");
                if (t != null)
                {
                    var img = t.GetComponent<Image>();
                    if (img != null) { ui.cardImage = img; changes.Add("cardImage←底"); }
                }
                if (ui.cardImage == null) log.AppendLine("  ⚠ " + card + "：找不到子节点「底」(Image)，cardImage 仍为空");
            }
            if (ui.button == null) ui.button = ui.GetComponent<Button>();
            if (ui.numText == null)   { var t = ui.transform.Find("编号");     if (t != null) ui.numText   = t.GetComponent<Text>(); }
            if (ui.titleText == null) { var t = ui.transform.Find("标题");     if (t != null) ui.titleText = t.GetComponent<Text>(); }
            if (ui.descText == null)  { var t = ui.transform.Find("描述");     if (t != null) ui.descText  = t.GetComponent<Text>(); }
            if (ui.stateText == null) { var t = ui.transform.Find("状态");     if (t != null) ui.stateText = t.GetComponent<Text>(); }
            if (ui.stateIcon == null) { var t = ui.transform.Find("状态图标"); if (t != null) ui.stateIcon = t.GetComponent<Image>(); }

            if (changes.Count > 0)
            {
                fixedCount++;
                EditorUtility.SetDirty(ui);
                log.AppendLine("  ✔ " + card + "：" + string.Join(", ", changes.ToArray()));
            }
            else
            {
                log.AppendLine("  · " + card + "：引用完好，不动");
            }
        }

        bool sceneDirty = false;
        if (fixedCount > 0 && !Application.isPlaying)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            sceneDirty = scene.isDirty;
            EditorSceneManager_SaveOpenScenes();
        }

        log.AppendLine("结果：检查 " + checkedCount + " 张卡，接回 " + fixedCount + " 张" + (fixedCount > 0 ? "（场景已保存）" : "（无需保存）"));

        File.WriteAllText(ReportPath, log.ToString(), new UTF8Encoding(false));
        Debug.Log("[章节卡灰罩] " + (fixedCount > 0 ? ("接回 " + fixedCount + " 张卡，场景已保存") : "引用都完好，无需修改") + " → 报告 " + ReportPath);
    }

    static void EditorSceneManager_SaveOpenScenes()
    {
        UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
    }
}

// 丢 Assets/_cardrewire_trigger.txt → 下次刷新/重编译后自动跑一次（用完自删）
[InitializeOnLoad]
public static class ChapterCardRewireTrigger
{
    const string Trigger = "Assets/_cardrewire_trigger.txt";
    const string ErrFile = "../额外文件/错误_章节卡灰罩.txt";

    static ChapterCardRewireTrigger()
    {
        if (!File.Exists(Trigger)) return;
        if (EditorApplication.isPlaying) return;    // Play 模式不消费触发器，退出 Play 后下次刷新再跑
        EditorApplication.delayCall += () =>
        {
            try
            {
                if (File.Exists(Trigger)) File.Delete(Trigger);
                if (File.Exists(Trigger + ".meta")) File.Delete(Trigger + ".meta");
                ChapterCardRewire.Run();
            }
            catch (System.Exception e)
            {
                Directory.CreateDirectory("../额外文件");
                File.WriteAllText(ErrFile, e.ToString());
                Debug.LogError("[章节卡灰罩] 失败: " + e);
            }
        };
    }
}
