// 剧情步骤数据：每章一个 json（Assets/数据/剧情/第N章.json，TextAsset），
// 由 Chapter1StoryBuilder 绑到 StoryRunner 上，运行时 JsonUtility 解析。
//
// t 的取值：
//   nar      旁白（无名牌，灰白字）
//   dlg      对话（带名牌；s=说话人。微信台词 s 带"（微信）"后缀，直接显示在名牌上——方案一）
//   mon      心理独白（无名牌，浅色斜体；s=归属角色，仅数据用）
//   choice   干预选择题（title=题干；options 全选流程）
//   walk     走动段：玩家自由移动，等 Touch 触发盒（x=HUD 提示文案）
//   interact F 交互段：玩家走到目标旁按 F（x=HUD 提示文案）
//   card     入场淡入（黑幕淡出，不显示标题卡——用户反馈定稿：不要开场黑屏）
//   end      章节结束卡（返回主界面 + 固定提示句）
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class StoryStep
{
    public string t = "nar";
    public string s = "";
    public string x = "";
    public string title = "";
    public List<StoryOption> options = new List<StoryOption>();
}

[Serializable]
public class StoryOption
{
    public string head = "";
    public string body = "";
}

[Serializable]
public class StoryChapter
{
    public string chapter = "";
    public List<StoryStep> steps = new List<StoryStep>();

    public static StoryChapter FromTextAsset(TextAsset asset)
    {
        if (asset == null) return null;
        try { return JsonUtility.FromJson<StoryChapter>(asset.text); }
        catch (Exception e) { Debug.LogError("[StoryData] json 解析失败：" + e.Message); return null; }
    }
}
