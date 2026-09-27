# 执行计划：第 2–5 章剧情制作

> **进度（2026-09-27）**：阶段 0–4 的代码与数据全部完成并提交（baa2396）——
> 框架脚本 6 个 + 自检参数化 + ChapterStoriesSetup 工具 + 第2~5章.json（78/104/79/92 步）+ AGENTS 更新。
> 程序集已编译通过（Library/ScriptAssemblies 08:36 更新，无 error CS）。
> **当前停在人工卡点**：① 在 Unity 跑 `Tools/干预项目/多章剧情/一键搭建第2-5章（幂等）`；
> ② Scene 里手调锚点/触发点位置（工具默认坐标按 Loc 中心估，重跑不覆盖手调）；
> ③ 跑 `Tools/干预项目/第N章剧情运行自检`（N=1..5，先第1章回归）→ 报告齐后继续阶段 5 收尾。

按「框架先行、逐章可验收」分 6 个阶段。每阶段末有验证点；阶段 2–5 每章独立可玩、可回归。
所有 Unity 场景/资产变更走 `Assets/Editor/ChapterStoriesSetup.cs` 菜单项（**不用自动触发器**）；
Unity 侧验证需在编辑器里人工触发菜单/自检（agent 无法直接驱动 Unity，卡点处汇报并等用户操作）。

## 阶段 0：框架代码（Story/ 五个脚本的小改）

- [ ] `StoryInteractable.cs`：加 `public int chapterTag = 1;`
- [ ] `StoryRunner.cs`：
  - [ ] `FindFree()` 过滤 `si.chapterTag == chapterIndex`
  - [ ] 新增 `State.Fade` + `case "fade"` 协程（黑幕 0.4s → 可选 nar 黑屏旁白 → CC 关/挪/开 → 淡出 → Next）
  - [ ] `public Transform[] fadeAnchors;` + 按名字查找
  - [ ] 拿/放手机关键词数组化（`拿起手机/拿过手机/拿出手机`、`放下手机/肩膀…放松/长长地舒了一口气`）
  - [ ] wechat 分支：非徐夏 speaker → 推导联系人名 → `phoneChat.SetContact()`
  - [ ] `DoChoice` 把 `chapterIndex` 传给 ChoicePanel
  - [ ] `DebugAdvance()` 覆盖 Fade 状态（自检推进）
- [ ] `PhoneChatUI.cs`：`SetContact(name)`（更新 `headerName` + `标题` Text）
- [ ] `ChoicePanel.cs`：记录键改 `story.choice.ch<N>.<idx>`
- [ ] `StorySmokeDriver.cs` / `Assets/Editor/PhoneChatSmoke.cs`：目标章参数化（菜单 `Tools/干预项目/第N章剧情运行自检`，N=1..5）
- [ ] 编译通过（无新警告）
- **验证**：Unity 里跑 `第1章剧情运行自检` → `_第一章剧情运行自检.txt` PASS（回归门 1）

## 阶段 1：第 2 章数据 + 搭建

- [ ] 写 `Assets/数据/剧情/第2章.json`（≈95 步；**正文以 剧本更新_extract.txt 为准**；含 4 道 choice；微信段：李同学→林溪 SetContact；更新版注记落点：拿起手机看消息 interact、点开和李同学聊天框回复前 interact、问林溪前不插步骤（注记写"自由决定"）；剧本 `献花_720.png`→`[鲜花]`）
- [ ] `ChapterStoriesSetup.cs` 工具：菜单 ①（runner 节点）+ ②（锚点/触发点）成形，先只做第 2 章：
  - [ ] `StorySystem/第2章`（绑 json、chapterIndex=2、复制第 1 章 UI 引用、fadeAnchors）
  - [ ] `Loc_宿舍` 内：第 2 章 StoryStart（书桌旁）、interact 触发点（chapterTag=2）
- [ ] 跑 ①② → Unity 编译 + `第2章剧情运行自检` → `_第2章剧情运行自检.txt` PASS
- [ ] 人工过一遍 Play：手机 UI 联系人切换（李同学↔林溪）标题正确、贴纸正常
- **验证**：第 2 章自检 PASS + 第 1 章自检仍 PASS

## 阶段 2：第 3 章（食堂 + 办公室，首个 fade 章）

- [ ] `第3章.json`（≈115 步；**以更新版为准，含新增台词**：林溪「你先去点饭，我找个座位！」/ 徐夏「好」/ 独白「去看看有什么饭吧......」；两步玩家行为（去窗口点饭 / 回头看到林溪找的座位坐下）落 interact/walk；隔壁桌黄亮对话占位（旁人甲/乙，内容待用户补）；办公室门口 interact「进入」+ fade 刷进办公室（落点含"坐到座位上"）；出办公室 fade + "主视角切换回徐夏"旁白）
- [ ] 工具扩展第 3 章摆位：食堂起点/点饭窗口/座位触发点、办公室门 interact、`fadeAnchors`（办公室内座位旁、办公室外走廊）
- [ ] 自检 + 人工过 Play（黑屏转场落点正确、不穿模）
- **验证**：`_第3章剧情运行自检.txt` PASS

## 阶段 3：第 4 章（宿舍→图书馆，班群 + 学姐）

- [ ] `第4章.json`（≈105 步；**以更新版为准**：新增引导句「点开通知看看吧。」落 interact（班群通知，手机 UI `班群（通知）`左气泡）；「得赶紧看看复习资料了......资料就在桌子上。」落 interact（坐下看资料，"拉近屏幕固定视角"做不了按注记黑屏替代）；删了林溪进场揣测句（更新版已删，照录）；舍友抱怨黄亮对话占位（旁人甲/乙，待用户补）；学姐微信段 SetContact("学姐")；并肩去图书馆 fade；③题干用"环境带来的新认知"；`加油_720.png`→`[加油]`）
- [ ] 工具扩展第 4 章摆位：宿舍起点、班群通知 interact 点、坐下 interact 点、图书馆 fade 落点
- [ ] 自检 + 人工过 Play
- **验证**：`_第4章剧情运行自检.txt` PASS

## 阶段 4：第 5 章（宿舍两段 + 图书馆 + 食堂，fade 最多）

- [ ] `第5章.json`（≈125 步；**以更新版为准**：开场黑屏 fade 落宿舍；「回到自己的位置上吧」玩家行动 walk/interact；「还是去图书馆好了」+ 门口"出去"黑屏 → walk 到门口 + fade；「让角色自己走回座位」fade 传送；蹲身用替代词「她看着椅子上的徐夏」；拍肩动作删（注记"拍不了就删"）；结尾"并肩往食堂走去"已删、收在收拾东西+欢声笑语；陆宣雨/舍友A/舍友B 实体对话）
- [ ] 工具扩展第 5 章摆位：宿舍起点（第五章锚点与第二章区分）、门口 walk 触发、fade 落点（图书馆、宿舍座位、食堂）
- [ ] 自检 + 人工过 Play
- **验证**：`_第5章剧情运行自检.txt` PASS

## 阶段 5：全量回归 + 收尾

- [ ] 5 章自检全部重跑一遍（顺序 1→5）→ 全 PASS
- [ ] 主菜单流程验证：通关第 N 章 → 解锁第 N+1 章（GameProgress 现有逻辑，抽查即可）
- [ ] 剧本台词抽查：每章 ≥10 句对照 `剧本更新_extract.txt`（更新版为准）；4 道干预题 title/options 对照更新版原文
- [ ] git diff 复查：`UI交互` 子树零样式改动；无新增 UI 贴图/画布/prefab 素材
- [ ] 报告落位：`assets/_报告/_多章剧情搭建.txt`（层级/接线清单）+ 各章自检报告
- [ ] AGENTS.md 第五节补「多章剧情」小节（工具用法 + fade/SetContact 约定）
- [ ] 更新 spec（trellis-update-spec：多章 runner 模式、fade 步骤约定）
- [ ] 提交（中文标题+要点）

## 验证命令汇总

| 目的 | 命令/操作 |
|---|---|
| 编译检查 | Unity Console 无错误（agent 侧：`python 额外文件/工具脚本/检查CSharp.py` 如适用） |
| 第 N 章自检 | Unity 菜单 `Tools/干预项目/第N章剧情运行自检` → `assets/_报告/_第N章剧情运行自检.txt` |
| 回归 | 第 1 章自检 PASS + git diff `UI交互` 子树无样式变化 |
| 台词抽查 | 人工对照 `额外文件/剧本更新_extract.txt`（更新版为唯一正文权威源） |

## 回滚点

- 每阶段一个 git commit（阶段号写进提交信息）；json/场景问题可整章回退不影响框架。
- 框架阶段（0）若引发第 1 章回归失败：`git checkout` Story/ 脚本目录即可回到现状（场景未动）。

## 人工卡点（需要用户在 Unity 里做的）

1. 每阶段工具菜单执行（①②③）与自检触发——agent 写好工具与说明，用户点菜单。
2. Play 模式人工体验确认（黑屏转场落点、手机联系人切换的观感）。
3. 阶段验收签字后进入下一章。
