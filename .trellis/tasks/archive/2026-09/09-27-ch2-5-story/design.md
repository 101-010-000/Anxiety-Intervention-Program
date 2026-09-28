# 技术方案：第 2–5 章剧情制作

对应 `prd.md`。总原则：**改动集中在"数据 + 加载机制 + 场景接线"，UI 层零新建、样式零改动**。

## D1 多章加载：每章一个 StoryRunner 子节点（方案 A）

场景 `StorySystem` 下建 `第1章`~`第5章` 五个同构子节点，各挂一个 StoryRunner：

```
StorySystem
 ├ 第1章  (StoryRunner: chapterJson=第1章.json, chapterIndex=1, startAnchor=Loc_教室/StoryStart, UI引用×11)
 ├ 第2章  (chapterJson=第2章.json, chapterIndex=2, startAnchor=第2章起点, 同一套UI引用)
 ├ ...第5章
 └ StorySmokeDriver（留在 StorySystem 根上，参数化目标章）
```

- 现有 `StoryRunner.Start()` 已经是 `runOnStart && GameProgress.SelectedChapter == chapterIndex` 才 `Begin()`——**机制现成，零逻辑改动**；不匹配的 runner 只打一行 Log 静默。
- 11 个 UI 引用（dialogue/choicePanel/promptRoot/promptLabel/walkHintRoot/walkHintLabel/chapterCardGroup/cardTitle/cardSubtitle/blackFade/phoneChat）全部指向 `UI交互` 下**同一套节点**，由 Editor 工具从第 1 章 runner 复制引用值。
- 否决的备选：单 runner + json 数组。原因：startAnchor/fadeAnchors 也得跟着数组化，字段改动反而更大，且丢失"每章接线独立可查"的清晰度。
- 已知风险与对策：
  - `StoryRunner.Instance` 单例（Awake 赋值）会被后 Awake 的 runner 覆盖——检查所有 `StoryRunner.Instance` 使用点（StorySmokeDriver / PhoneChatSmoke），自检驱动改为按 chapterIndex 显式查找目标 runner。
  - `Begin()` 里 `FindObjectsOfType<StoryInteractable>()` 全量 disarm 无害（多章共存时统一收权，armed 由各章等待步骤再放开）。

## D2 黑屏转场：新步骤类型 `fade`

- `StoryStep` 已有字段够用：`t="fade"`，`x`=转场期黑屏上可显示的一句旁白（可空），`to`=目标锚点名。
- `StoryRunner` 加 `public Transform[] fadeAnchors;`（序列化数组，元素 GameObject 名 = `to` 值），`case "fade"` 进协程：
  1. BlackFade alpha→1（0.4s，unscaled，照抄 EndRoutine 写法）
  2. 若 `x` 非空，借对话框播一句 nar（打字机，等播完）——满足"黑屏刷新时补一句时间流逝旁白"的剧本需求
  3. 按名字在 fadeAnchors 找目标 → CharacterController 关 → 挪位置/朝向 → 开（照抄门口传送坑）
  4. BlackFade alpha→0（0.4s）→ `Next()`
- 新增状态 `State.Fade`：全锁（同 Card）；`DebugAdvance()` 对 Fade = 立即完成（自检用）。
- 锚点摆位：各 Loc 内放空 Transform 子节点（如 `Loc_办公室/第3章锚点/办公室内`），由 Editor 工具建并接进对应 runner 的 fadeAnchors。
- BlackFade 本身（UI交互 最上层、不透明）零改动。

## D3 交互点章节归属：`StoryInteractable.chapterTag`

- `StoryInteractable` 加 `public int chapterTag = 1;`（默认 1 → 场景里第 1 章两个点连数据都不用改）。
- `StoryRunner.FindFree(Mode)` 加过滤 `si.chapterTag == chapterIndex`。
- 第 2–5 章 interact/walk 步骤照旧：等待中 `armed=true`，WalkHint 提示文案来自 json 的 `x`。
- 各章触发点清单（摆位由工具做，位置在实现时按 Loc 实际布局定）：
  - 第2章：`interact`×1~2（拿起手机看消息 / 回复李同学前）
  - 第3章：`interact`×2~3（食堂点饭 / 坐下 / 办公室门口点击进入）＋ `fade`（进办公室、出办公室）
  - 第4章：`interact`×1（点开班群通知）＋ `fade`×1~2（宿舍→图书馆；"拉近镜头"动作按更新版剧本用黑屏替代）
  - 第5章：`fade`×3~4（回宿舍/去图书馆/回宿舍/去食堂）
- 走动段 `walk`（Touch 盒）第 2–5 章基本可省（剧本更新版把位移都改成了黑屏/交互），实现时按各章 json 实际用到的模式摆，缺哪种补哪种。

## D4 手机换联系人：`PhoneChatUI.SetContact(name)`

- 联系人推导**零数据格式变更**：微信台词 `s` 本身带名字。规则（放 StoryRunner.PlayText 的 wechat 分支）：
  - `s = "徐夏（微信）"` → 发出方，不换联系人；
  - 其它（`林溪（微信）/李同学（微信）/学姐（微信）/班群（通知）`）→ 取去掉括号后缀的名字，若 ≠ 当前联系人则 `phoneChat.SetContact(名字)`。
- `SetContact(string name)`：记 `headerName`、更新 `标题` Text 的 text（EnsureRefs 补拿 `标题` 引用）。`Show()` 现有的 `Clear()` 保证换段必新开聊天流。
- **头像占位（用户许可方案）**：不换头像贴图，左右沿用现有两张（读作通用头像）；不生成、不修改任何 png。`avatarLeft/avatarRight` 本就是序列化字段，将来用户给了素材直接在 Inspector 换即可——但同一章内多联系人会共享一套头像，这是占位阶段的已知限制，写进报告。
- 气泡左右判定（speaker 含"徐夏"→右蓝泡）不动；贴纸标签机制不动（剧本 `献花_720.png`→`[鲜花]`）。
- 班群通知（第4章）：落成 `dlg s="班群（通知）"` 的左气泡 + 标题"班群"；"玩家点开"由前置 interact 步骤承载。

## D5 干预题记录键按章隔离

- `ChoicePanel` 的 PlayerPrefs 键从 `story.choice.<idx>` 改为 `story.choice.ch<chapter>.<idx>`；章号由 `StoryRunner.DoChoice` 调 `Open()` 时传入（加一个参数或设公共属性）。
- 主菜单概览页当前未读取这些键（已核实 grep 无命中，AGENTS 注明"供二期联动"），改名无联动风险；第 1 章旧键数据作废（本地 PlayerPrefs，可接受，报告注明）。

## D6 拿手机动画钩子：关键词数组化

- 现判定 `step.x.Contains("拿起手机")` / `Contains("紧绷的肩膀也慢慢放松")` 是第 1 章逐字硬编码；第 2–5 章旁白原文（"把手机从桌角拿过来"）不命中。
- 改为两个 `static readonly string[]`（拿起：`拿起手机/拿过手机/拿出手机`；放下：`放下手机/肩膀…放松/长长地舒了一口气`），Contains 任一命中即触发。文本零改写，代码只扩数组。

## D7 Editor 工具与数据生产

- 新工具 `Assets/Editor/ChapterStoriesSetup.cs`，菜单 `Tools/干预项目/多章剧情/`：
  1. `① 搭建第2-5章运行节点`：建 4 个 runner 子节点、绑 json、startAnchor、复制第 1 章 UI 引用、接 fadeAnchors；**幂等**（已存在则只补缺字段）；照抄 `MainMenuBuilder.PreloadScripts()` 先 ForceUpdate 导入脚本（AGENTS 第七节 MonoScript 缓存坑）。
  2. `② 摆各章锚点与触发点`：StoryStart、interact/walk 触发盒（chapterTag=N）、fade 落点；幂等、不碰已有第 1 章节点。
  3. `③ 第N章剧情运行自检（N=1..5）`：参数化现有 PhoneChatSmoke 入口，报告 `_第N章剧情运行自检.txt`。
- json 数据文件（`第2章.json`~`第5章.json`）是新建 TextAsset，无引用链，Unity 生成 .meta 即可（不存在断链风险——断链坑只针对"移动已有资产"）。
- 工具不用自动触发器（AGENTS 第一章教训定稿）；一次性跑完后工具本身保留在 `Assets/Editor/`（长期工具，与 WalkHintRestyle 同级）。
- 各章 json 步骤数预估：第2章 ≈95、第3章 ≈115、第4章 ≈105、第5章 ≈125（第1章 121 参照）。

## D8 剧本→步骤映射规则（录数据时统一执行）

**正文权威源 = `剧本更新.docx`（`剧本更新_extract.txt`）**；旧版 `剧本_extract.txt` 仅对照。更新版独有的正文增删（第3章新增林溪点饭台词、第4/5章新增引导句、删走位动作句）照更新版录入。

| 剧本形态（更新版） | 步骤 |
|---|---|
| 场景描述/动作描述 | `nar`（原文照录） |
| 无名牌内心戏（如"完了完了…"） | `mon s=徐夏`（或所属角色） |
| 【角色】台词（当面） | `dlg s=角色`（张知远→"张同学"沿用第1章名牌） |
| 【角色】微信台词 | `dlg s=角色（微信）`（自动进手机 UI + SetContact） |
| 班群/通知 | `dlg s=班群（通知）` |
| 隔壁桌/舍友背景人声（两处黄亮待补） | `dlg s=旁人甲/旁人乙`（走对话框名牌，无实体；内容待用户提供，先占位不阻塞） |
| 贴纸行（比心_720.png 等） | `dlg s=角色（微信） x=[比心]`（贴纸标签整句） |
| 干预题块 | `choice`（title=题干连选项引导；options=head/body 原文，剧本多行 body 合并成段） |
| （插入玩家行为/玩家行动）注记 | `interact`（x=目标卡文案，可直接用更新版引导句如「点开通知看看吧」）或 `walk` |
| （黑屏/刷新/做不了就黑屏）注记 | `fade`（to=锚点名，x=黑屏期旁白，可空） |
| 章首/章尾 | `card` / `end` |
| 剧本笔误 | 录修正版（如"越在意结果"），报告注明（照第1章先例） |

各章更新版注记落点（录 json 时逐一核对）：
- **第2章**：拿起手机看消息（interact）；问林溪前（注记写"自由决定"，默认不插步骤、旁白带过）；点开和李同学的聊天框回复前（interact）；陆宣雨进场（无需玩家行为）。
- **第3章**：林溪「你先去点饭」→ interact 去窗口；「点完饭，回头看到林溪找的座位」→ walk/interact 落座；嘈杂人声背景音（氛围注记，不落步骤，记录在报告）；「走廊走到办公室门口，点击进入，黑屏刷进办公室」→ interact（办公室门）+ fade；「从门口坐到座位上」（角色自动/玩家操纵皆可）→ fade 落点直接放座位旁，用 fade 顺带完成；「主视角切换回徐夏」→ 出办公室 fade + 旁白。
- **第4章**：「点开通知看看吧」→ interact（班群通知，手机 UI）；「坐下看资料、拉近屏幕固定视角」→ interact（坐下）+ 借 fade/固定视角简化（做不了按注记黑屏）；「背景人声渐弱 BGM 加大」（音频注记，记录待办不落步骤）；「好难实现的动作黑屏吧」→ fade。
- **第5章**：开场「（可以黑屏）」→ fade 进宿舍；「回到自己的位置上吧（玩家行动）」→ walk/interact；「离开宿舍，到门口'出去'后黑屏」→ walk 到门口 + fade 图书馆；「让角色自己走回座位」→ fade/传送回座位；蹲身做不了 → 用注记给的替代词「她看着椅子上的徐夏」；拍肩「拍不了就删动作」→ 录正文时去掉该动作短语。

特例：
- 第3章开头"宿舍躺了一小时"→ fade 进食堂前的黑屏旁白即可，不回宿舍场景（本章直接从食堂开场旁白起步，起点锚点放食堂）。
- 第3章"（此下主视角暂用李老师）"段：更新版已改为"走走廊→点击进入→黑屏刷新"，即 interact+fade，无需镜头切换系统。
- 第4章「靠窗的（←建模没有就删了这个形容词）」：按图书馆 Loc 实际有无窗边位决定保留/删"靠窗的"。
- 第5章开头考场：跳过（nar 带过"考完试走回宿舍"，fade 落宿舍）。

## D9 兼容与回归

- 第 1 章零行为回归风险点：FindFree 过滤（chapterTag 默认 1 + chapterIndex=1 恒匹配）、choice 键改名（记录层面，不影响流程）、Instance 单例（第 1 章 runner 是唯一 Begin 者，无并发）。
- 回归门：`_第一章剧情运行自检.txt` PASS + git diff 确认 `UI交互` 子树零样式改动（Scene YAML 中该子树无 diff 或仅引用无关变化）。
- `DoorTravelSystem` 剧情期禁用逻辑已全局（Begin 关 / Done 不重开——维持现状，多章同样处理；章末 ToMainMenu 回主菜单，无泄漏）。

## D10 交付物清单

| 文件 | 类型 |
|---|---|
| `Assets/数据/剧情/第2章.json` ~ `第5章.json` | 数据（新） |
| `Assets/Scripts/Story/StoryRunner.cs` | fade 状态 + FindFree 过滤 + 关键词数组 + SetContact 驱动 + choice 章号 |
| `Assets/Scripts/Story/StoryData.cs` | 注释更新（t 新增 fade/to 说明） |
| `Assets/Scripts/Story/StoryInteractable.cs` | chapterTag 字段 |
| `Assets/Scripts/Story/PhoneChatUI.cs` | SetContact + 标题引用 |
| `Assets/Scripts/Story/ChoicePanel.cs` | 记录键按章隔离 |
| `Assets/Scripts/Story/StorySmokeDriver.cs` / `Assets/Editor/PhoneChatSmoke.cs` | 参数化章号 |
| `Assets/Editor/ChapterStoriesSetup.cs` | 工具（新） |
| `Game.unity` | runner×4、锚点、触发点（工具写入） |
| `assets/_报告/_第N章剧情运行自检.txt` ×5、`_多章剧情搭建.txt` | 报告 |
