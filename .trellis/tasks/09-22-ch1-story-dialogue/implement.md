# 实施计划：第一章剧情演出系统（v2）

前置：设计 v2 已评审（F 交互动线 / uGUI 文本 / 手机聊天 UI 占位 / 章节结尾提示句均已定）。场景改动全部经 Editor 工具，报告进 `assets/_报告/`。

## 阶段清单（每阶段末 Play 模式验收）

### P1 演出内核（验收 S01–S51：开场旁白→F 交互→组长段→①）
- [ ] `Scripts/Story/StoryData.cs`：JSON 数据类（含 interact/cam:phone_in|out）。
- [ ] `Assets/数据/剧情/第1章.json`：先录 S01–S51。
- [ ] `FirstPersonController.cs` 加 `moveLocked` 中间档（锚 117–134，3 行）。
- [ ] `DialogueUI.cs`：Dialog 接线；name/neirong 由工具换 uGUI Text；三语态；打字机；逐句/框体/名牌淡入淡出；▼；GameSettings 文字速度/自动播放。
- [ ] `StoryRunner.cs`：状态机+权限表（§3）+输入+开场旁白自动连播+DoorTravelSystem 抑制。
- [ ] `StoryInteractable.cs`：组长 F 交互（"按 F 与组长交谈"提示，复用门口提示样式）。
- [ ] `Editor/Chapter1StoryBuilder.cs` + `_story1_trigger.txt`：name/neirong 换 uGUI Text、CanvasGroup/▼、布局重排（§8）、DialogBack 换 Image 九宫格（聊天背景.png 设 border）、组长摆位+交互触发、起点锚点 → 报告 `_第一章剧情搭建.txt`。
- [ ] 验收：开场旁白自动播且不进对话；F 后进对话；间隙可转不可走；节点结束恢复移动。

### P2 干预面板（验收 ①，含全选流程）
- [ ] `ChoicePanel.cs`：全选流程、选项展开动画、收档写 PlayerPrefs。
- [ ] Chapter1StoryBuilder 增加 ChoicePanel 子树（主菜单九宫格素材）。
- [ ] json 补 ① 完整数据。
- [ ] 验收：未全选不可关；每选一项展开解释；全选后收档回剧情。

### P3 手机微信聊天 UI（验收 S52–S79）
- [ ] `PhoneChatUI.cs`：气泡逐条推进、"正在输入…"、表情包占位块、旁白/独白字幕条、②收档后发首个选中项、phone_out 淡出。
- [ ] Chapter1StoryBuilder 增加 `UI_手机聊天` 子树（程序化占位贴图：外框/气泡绿白/输入栏，MainMenuAssets 工艺；参数集中常量）。
- [ ] TakePhone→Phone 动画接线（S52）。
- [ ] json 补 ② 数据与 S53–S79。
- [ ] 验收：气泡方向正确、视角=林溪（聊天头"徐夏"）、②消息发出、回切正常。

### P4 走动段与收尾（验收 S80–S118）
- [ ] 撞张知远触发盒 + 张知远摆位（Editor 工具）；HUD 走动提示。
- [ ] card/end 章节卡：主标题+副文"查看本章核心内容...请前往填写实验者发的问卷链接~"+点击返回 MainMenu。
- [ ] json 录入 S80–S118 全量。
- [ ] 验收：撞人一次即消费；③④正常；结束卡文案正确并回主菜单。

### P5 自检与打磨
- [ ] `StorySmokeDriver`（参照 DoorSmokeDriver 协程等真帧法）+ `_story1smoke_trigger.txt` → `assets/_报告/_第一章剧情自检.txt`：119 步顺序、间隙权限断言、F 交互、4 题全选、手机 UI 进出、结束卡全走查。
- [ ] 自动播放/文字速度联调；黑幕/章节卡/气泡动画时长终调。

## 验证方式

- `Tools/干预项目/搭建第一章剧情`（或触发器）→ Play 自检 → `_第一章剧情自检.txt` 全 ✓。
- 回归：跑一次门口传送自检，确认抑制逻辑未破坏门系统。

## 风险文件与回滚

- `FirstPersonController.cs`：唯一动到的既有运行时代码（3 行），可单独还原。
- `Scenes/Game.unity`：仅经幂等 Editor 工具改；git 兜底。
- 用户手搭 Dialog：变更仅限（换 uGUI Text/加 CanvasGroup/布局重排/DialogBack 换 Image），参数集中常量，已获同意。
- 新增 `Scripts/Story/`、`Editor/Chapter1StoryBuilder.cs`、`数据/剧情/第1章.json`：整目录删除即回滚。

## task.py start 前检查

- [x] 林溪段方案定稿：手机微信聊天 UI（程序化占位）。
- [x] Dialog 布局重排/DialogBack 九宫格/换 uGUI Text：已同意（模糊疑虑已解答）。
- [x] 章节结尾提示句：已定稿。
- [ ] 台词表 5 处 ★（S49/S50/S69/S93/S113）默认归类确认（不阻塞，可在实现中改 json）。
- [ ] 开场"旁白自动播放（不进对话模式）"与张知远"撞人触发"两处解释复核。
