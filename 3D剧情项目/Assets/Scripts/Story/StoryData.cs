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
//   door     门口传送导流（2026-09-29，第4章）：to=要去的 locationId（如 Loc_图书馆）、
//            anchor=到达后的落点锚点名（缺省=门自己的 arrivePoint）、x=左上角目标卡文案。
//            剧情不开门自己跳，而是亮目标卡引导玩家【自己走到门口按 F】，面板里只有本章目的地可点；
//            到达后 DoorTravelSystem 回调继续剧情。门系统平时被剧情整体关掉，只有这一步临时打开。
//   cut      切视角（2026-09-29）：who=角色实例名 → 镜头切成 TA 的第三人称跟拍（CutawayCamera），
//            主角原地不动；who 空 = 切回主角相机。可选 camH/lookH 覆盖机位/视线高度（坐姿用低值）。
//   enter    NPC 入场演出（第2章陆宣雨）：who=角色实例名（Begin 时预禁用）、from=门口起点锚点、
//            to=可选落点锚点（缺省=玩家面前1.3m）。全程真实形象（虚实渐变已移除），细节见 NpcEntrance。
//   leave    NPC 退场（2026-09-28，与 enter 对称）：who 走回【开场原位】（Begin 时快照），
//            to=可选锚点覆盖（同名解析约定同 fade）。第2章陆宣雨"回到自己的座位上"。
//            可带 via=途经锚点名列表分段走（绕开桌椅等家具；解析同 to，缺锚点只警告跳过）。
//            hide=true = 到位直接隐藏（不转身不待机，"走出门了"；第4章林溪与玩家一起去图书馆）
//   stage    舞台道具（2026-10-01，第3章老师视角）：showNames=亮出的物体名列表、hideNames=隐藏的物体名
//            （按 fade 锚点同名解析）；hidePlayer/showPlayer=藏/恢复玩家自身模型——注意是玩家根下
//            【全部】渲染器（HideWholePlayer，光藏 standingModel 挡不住 徐夏_坐姿 等历史模型子物体），
//            顺带挂起/恢复 SitSpot；standAt=玩家传到该锚点原地站好（同 fade 传送）。
//            老师跟拍段玩家模型只是被隐藏，沙发上的坐姿徐夏是舞台道具；
//            showNames 里的物体 Begin 时预藏（同 enter 的 seat 惯例），续播快进按步骤终态同步。
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
                                             // door：要去的 locationId（门外那片地点的 id，如 Loc_图书馆）
    public bool showChars = false;           // fade：落地时点亮【落点所在 Loc】下的本章角色容器
                                             // （第5章食堂第五章角色：Begin 全地点显示满足不了"剧中才出现"，
                                             //   先预藏、黑屏期间点亮 → 淡出时人已在座，不穿帮）
    public string anchor = "";               // door：到达后的落点锚点名（缺省 = 用门自己的 arrivePoint）
    public string who = "";                  // enter：角色实例名（Begin 时预禁用，enter 时启用入场）
    public string seat = "";                 // enter：到位后亮出的坐姿模型实例名（走位模型整棵隐藏）。
                                             // 第3章王含：从食堂门走到 凳子2(9) 旁 → 切成用户摆好的坐姿（2026-09-30）；
                                             // seat 实例 Begin 时同样预藏，续播快进按终态同步（见 StoryRunner）
    public string from = "";                 // enter：门口起点锚点名
    public List<string> via = new List<string>();  // enter/leave：途经锚点名列表（同名解析同 to/from；
                                                   // 缺锚点=警告并跳过该点，不报错）。第2章陆宣雨退场绕开桌椅
    public bool hide = false;                 // leave：到位直接整棵隐藏（「走出门」效果——不转身、不待机；
                                             // 第4章林溪「一起去图书馆」，用户 2026-09-29）
    public List<string> showNames = new List<string>();  // stage：要亮出的物体名列表（解析同 fade 锚点；
                                                         // Begin 预藏，走到这一步才亮出——第3章办公室坐姿徐夏）
    public List<string> hideNames = new List<string>();  // stage：要隐藏的物体名列表（不叫 hide：与 leave.hide 撞名）
    public bool hidePlayer = false;          // stage：藏玩家自身模型（老师跟拍段第一人称模型只投影，镜头里不能没徐夏）
    public bool showPlayer = false;          // stage：恢复玩家自身模型
    public string standAt = "";              // stage：把玩家传到该锚点原地站好（第3章「第3章_办公室起身」）
    public bool auto = false;                // nar：语音播完自动推进不等点击（第3章老师走廊边走边播的旁白，
                                             // 复用 State.Gap 的 AutoPlay 通道：定时 + !DialogueVoicePlayer.IsPlaying 门）
    public bool bg = false;                  // leave：走位放后台，立即推进下一步（台词/旁白在走位期间继续播；
                                             // 后续 cut 会等 _leaveRt 走完再切镜头）
    public bool alreadyIn = false;           // enter：who 开场已在场（不预禁用）——从座位起身走到玩家面前的
                                             // 入场（第5章陆宣雨：三人组开场就坐在宿舍，到她的对话节点才起身）
    public bool seated = false;              // enter(alreadyIn)：开场就是坐姿（Begin 置 Sitting=true）。
                                             // ⭐ 代码驱动，场景 SitHere 组件被并发会话 stomping 也不影响
    public float delay = 0f;                 // leave：起步延迟秒——多人错峰竖排离场（第5章三人去食堂：
                                             // 前一个走出几秒后下一个才动，避免挤在一起；SilentApply/快进不复刻）
    public string at = "";                   // interact：目标交互点 GameObject 名（缺省=旧逻辑取第一个可用点；
                                             // 第3章起一章多个 F 点，不点名会武装错点——2026-09-28）
    public float camH = -1f;                 // cut：机位高度（≤0 = 用 CutawayCamera 内置默认 1.55）。
                                             // 第1章林溪坐姿用 1.15（站着的高度看坐着的人会高高在上）——2026-09-30
    public float lookH = -1f;                // cut：LookAt 的“头”高（≤0 = 内置默认 1.35；林溪坐姿 0.95）
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
