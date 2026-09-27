// 一次性场景手术：重做 MainMenu.unity 存读档页的槽位选中态。
// 旧方案 = 设计稿切片 稿_卡片_2 当选中框（不透明白卡 + 九宫格边配不上圆角）→ 又糊又错位；
// 新方案 = 程序化 页签_选中（半透明蓝洗底 + 蓝描边）做描边环，右上角 淡蓝圆底+对勾 角标，
//          SlotSelectFx 负责缩放动效与描边/角标淡入。零新增贴图，全部复用已生成的程序化贴图。
// 只动 Page_存读档 下 6 个槽位的子节点，不重建场景、不碰其他页面。改完存盘并写报告。
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class SaveSlotSelectFx
{
    const string ScenePath  = "Assets/Scenes/MainMenu.unity";
    const string ReportDir  = "Assets/assets/_报告";
    const string ReportPath = ReportDir + "/_存读档选中态.txt";

    [MenuItem("Tools/干预项目/存读档槽位选中态重做")]
    public static void Run()
    {
        var log = new List<string>();
        var ok  = false;
        try
        {
            Surgery(log);
            ok = true;
        }
        catch (System.Exception e)
        {
            log.Add("★异常：" + e.Message);
            log.Add(e.ToString());
            throw;
        }
        finally
        {
            Directory.CreateDirectory(ReportDir);
            log.Insert(0, "存读档槽位选中态重做（" + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "）结果：" + (ok ? "成功 ✓" : "失败 ★"));
            File.WriteAllText(ReportPath, string.Join("\n", log.ToArray()) + "\n");
            Debug.Log("[SaveSlotSelectFx] 报告 → " + ReportPath);
        }
    }

    static void Surgery(List<string> log)
    {
        MainMenuBuilder.PreloadScripts(log);

        var sc = EditorSceneManager.GetActiveScene();
        if (!sc.IsValid() || sc.path != ScenePath)
            sc = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var page = GameObject.Find("Page_存读档");
        if (page == null) throw new System.Exception("找不到 Page_存读档");
        var card = page.transform.Find("卡片");
        if (card == null) throw new System.Exception("Page_存读档 下没有 卡片");

        var frameSprite = MainMenuAssets.Sprite("页签_选中");
        var circleSprite = MainMenuAssets.Sprite("按钮_图标_按下");
        var checkSprite  = MainMenuAssets.Sprite("图标_对勾");
        if (frameSprite == null) throw new System.Exception("缺贴图：页签_选中");
        if (circleSprite == null) throw new System.Exception("缺贴图：按钮_图标_按下");
        if (checkSprite == null) throw new System.Exception("缺贴图：图标_对勾");

        for (int i = 1; i <= 6; i++)
        {
            var slot = card.Find("槽位_" + i);
            if (slot == null) throw new System.Exception("缺 槽位_" + i);
            bool bake = i == 1;   // 槽位 1 在场景里烘成“选中”状态，预览图能直接看到效果；运行时 Awake 会刷新掉

            // 1) 选中框：稿_卡片_2 → 页签_选中，放到 372×212（比槽位大一圈，描边环套在虚线框外面）
            var selT = slot.Find("选中框");
            if (selT == null) throw new System.Exception("槽位_" + i + " 缺 选中框");
            var selImg = selT.GetComponent<Image>();
            selImg.sprite = frameSprite;
            selImg.type = Image.Type.Sliced;
            selImg.color = Color.white;
            selImg.enabled = true;
            selImg.raycastTarget = false;
            var selRt = (RectTransform)selT;
            selRt.anchoredPosition = Vector2.zero;
            selRt.sizeDelta = new Vector2(372f, 212f);
            var frameCg = selT.GetComponent<CanvasGroup>();
            if (frameCg == null) frameCg = selT.gameObject.AddComponent<CanvasGroup>();
            frameCg.alpha = bake ? 1f : 0f;
            frameCg.interactable = false;
            frameCg.blocksRaycasts = false;

            // 2) 右上角对勾角标（淡蓝圆底 + 蓝对勾），淡入由动效驱动
            var badgeT = slot.Find("选中角标");
            if (badgeT == null)
            {
                var badgeGo = new GameObject("选中角标", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
                badgeGo.transform.SetParent(slot, false);
                badgeT = badgeGo.transform;
            }
            var badgeImg = badgeT.GetComponent<Image>();
            badgeImg.sprite = circleSprite;
            badgeImg.type = Image.Type.Sliced;
            badgeImg.color = Color.white;
            badgeImg.raycastTarget = false;
            var badgeRt = (RectTransform)badgeT;
            badgeRt.anchoredPosition = new Vector2(150f, -80f);
            badgeRt.sizeDelta = new Vector2(36f, 36f);
            badgeRt.localScale = Vector3.one;
            var checkT = badgeT.Find("对勾");
            if (checkT == null)
            {
                var checkGo = new GameObject("对勾", typeof(RectTransform), typeof(Image));
                checkGo.transform.SetParent(badgeT, false);
                checkT = checkGo.transform;
            }
            var checkImg = checkT.GetComponent<Image>();
            checkImg.sprite = checkSprite;
            checkImg.type = Image.Type.Simple;
            checkImg.color = MainMenuAssets.ACCENT;
            checkImg.raycastTarget = false;
            var checkRt = (RectTransform)checkT;
            checkRt.anchoredPosition = Vector2.zero;
            checkRt.sizeDelta = new Vector2(20f, 20f);
            var badgeCg = badgeT.GetComponent<CanvasGroup>();
            badgeCg.alpha = bake ? 1f : 0f;
            badgeCg.interactable = false;
            badgeCg.blocksRaycasts = false;

            // 3) 缩放归 SlotSelectFx 管并存进 SaveSlotUI
            var hs = slot.GetComponent<UIHoverScale>();
            if (hs != null) Object.DestroyImmediate(hs);
            var fx = slot.GetComponent<SlotSelectFx>();
            if (fx == null) fx = slot.gameObject.AddComponent<SlotSelectFx>();
            fx.frameCg = frameCg;
            fx.badgeCg = badgeCg;

            var slotUi = slot.GetComponent<SaveSlotUI>();
            if (slotUi != null)
            {
                slotUi.selectFrame = selImg;
                slotUi.selectFx = fx;
            }
            log.Add("槽位_" + i + "：描边=页签_选中 372×212，角标@(150,-80)，动效已接" + (bake ? "（场景烘成选中态供预览）" : ""));
        }

        EditorSceneManager.MarkSceneDirty(sc);
        if (!EditorSceneManager.SaveScene(sc)) throw new System.Exception("场景保存失败");
        log.Add("场景已保存 ✓");
        log.Add("后续：渲染主界面预览 + 主界面运行自检");
    }
}

// 工程根存在 Assets/_slotsel_trigger.txt 时，编辑器下次刷新/重编译后自动跑一次手术，
// 接着渲染主界面预览并跑运行自检（一次性任务，一次聚焦全部完成）。
[InitializeOnLoad]
public static class SaveSlotSelectFxTrigger
{
    const string TriggerFile = "Assets/_slotsel_trigger.txt";
    const string ErrFile     = "../额外文件/错误_存读档选中态.txt";

    static SaveSlotSelectFxTrigger()
    {
        if (!File.Exists(TriggerFile)) return;
        File.Delete(TriggerFile);
        EditorApplication.delayCall += delegate
        {
            try
            {
                SaveSlotSelectFx.Run();
                MainMenuBuilder.RenderPreviews();
                MainMenuBuilder.SmokeTest();
                if (File.Exists(ErrFile)) File.Delete(ErrFile);
                Debug.Log("[SaveSlotSelectFx] 自动手术+预览+自检 已触发完成");
            }
            catch (System.Exception e)
            {
                Directory.CreateDirectory("../额外文件");
                File.WriteAllText(ErrFile, e.ToString());
                Debug.LogError("[SaveSlotSelectFx] 自动手术失败：" + e);
            }
        };
    }
}
