// 剧情步骤数据：每章一个 json（Assets/数据/剧情/第N章.json，TextAsset），
// 由 Chapter1StoryBuilder 绑到 StoryRunner 上，运行时 JsonUtility 解析。
//
// t 的取值：
//   nar      旁白（无名牌，灰白字）
//   dlg      对话（带名牌；s=说话人。微信台词 s 带"（微信）"后缀，直接显示在名牌上——方案一；
//            班群通知 s="班群（通知）"，同样落手机聊天 UI）
//   mon      心理独白（无名牌，浅色斜体；s=归属角色，仅数据用）
//   choice   干预选择题（title=题干；options 全选流程）
//   walk     走动段：玩家自由移动，等 Touch 触发盒（x=HUD 提示文案）
//   interact F 交互段：玩家走到目标旁按 F（x=HUD 提示文案）
//   fade     黑屏转场：黑幕淡入 → 传送玩家到 to 同名锚点（runner.fadeAnchors）→ 淡出。
//            用于"黑屏/刷新"类剧情跳转（第3章进办公室、第5章宿舍↔图书馆等）。
//            to=锚点名；时间流逝旁白请用独立的 nar 步骤（BlackFade 在最上层，黑屏期间框不可见）
//   enter    NPC 入场演出（第2章陆宣雨）：who=角色实例名（Begin 时预禁用）、from=门口起点锚点、
//            to=可选落点锚点（缺省=玩家面前1.3m）。全程真实形象（虚实渐变已移除），细节见 NpcEntrance。
//   leave    NPC 退场（2026-09-28，与 enter 对称）：who 走回【开场原位】（Begin 时快照），
//            to=可选锚点覆盖（同名解析约定同 fade）。第2章陆宣雨"回到自己的座位上"。
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
    public string to = "";                   // fade：目标锚点名（runner.fadeAnchors 里同名元素）
    public string who = "";                  // enter：角色实例名（Begin 时预禁用，enter 时启用入场）
    public string from = "";                 // enter：门口起点锚点名
    public string at = "";                   // interact：目标交互点 GameObject 名（缺省=旧逻辑取第一个可用点；
                                             // 第3章起一章多个 F 点，不点名会武装错点——2026-09-28）
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
