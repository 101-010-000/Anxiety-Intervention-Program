# 主按钮悬停改纯变色提亮

## Goal

把主菜单 4 个主按钮（开始游戏 / 应用 / 读取 / 确定）的悬停/按下反馈从「SpriteSwap 换旧发光贴图」改成与白底按钮一致的 **ColorTint 变色 + UIHoverScale 缩放** 方案；深蓝底允许真正用 >1 提亮（与白底按钮的"相对提亮"形成约定内的小差异）。用户已自行更换了按钮素材（`按钮_1.png` / `按钮_2.png`），悬停时不能再切回旧的程序化发光图。

## Background / 确认的事实（2026-09-15 场景勘察）

- 4 个主按钮现状（`Assets/Scenes/MainMenu.unity`）：
  - `Btn_开始游戏`、`Btn_应用`：普通图已被用户手改为新素材 `assets/05_UI/按钮_Button/按钮_1.png`；`Btn_读取`、`Btn_确定`：普通图仍指向**已被用户删除**的 `按钮_主_普通.png`（guid `20149ef7…`，丢失中，运行时渲染为无图空钮）。
  - 4 个按钮 `transition = SpriteSwap`，`highlightedSprite = 按钮_主_悬停.png`（旧发光图）、`pressedSprite = 按钮_主_按下.png` —— 悬浮/点击时切回旧素材，正是用户要解决的问题。
  - 4 个按钮都已挂 `UIHoverScale`（hoverScale=1.035 / pressScale=0.975），缩放反馈已存在。
- 白底按钮（次按钮/图标钮/页签，28 个）：上一任务已改 `ColorTint` 相对提亮（normal `0.96,0.97,0.98` → hover 白），运行正常，本任务不动其配色。
- 用户手改进行中（**不在本任务范围**）：删除了 `按钮_主_普通`、`稿_列表_默认`、`搞_面板`、`稿_卡片_1`、`稿_面板_弹窗` 五张图；`Btn_取消/删除/返回` 与部分 `卡片/实底/底` 对象的 sprite 引用尚丢失，用户新增了 `按钮_2.png`、`章节选择.png`、`面板背景.png` 但尚未全部指完。
- 旧方案来源：`MainMenuBuilder.cs` 的 `Btn()` primary 分支（SpriteSwap + `按钮_主_悬停/按下/禁用`）。
- 补丁工具 `MainMenuBuilder.PatchBtnHover()` 目前**按贴图名识别主按钮**（`按钮_主_*` 前缀）；用户换图后主按钮贴图名已变成 `按钮_1` 等，该识别方式已失效，必须改为按对象名识别，否则重跑会错分。
- 整场景重建（`搭建主界面UI场景`）会冲掉用户手改（已删 Logo 徽标、换图、布局微调），本任务仍走定向补丁。

## Requirements

1. `MainMenuBuilder.Btn()` primary 分支：`SpriteSwap → ColorTint`，不设 `spriteState`；配色用新的 `PrimaryTint()`：normal `(1,1,1)`、hovered `(1.06,1.08,1.12)`（微蓝真提亮）、pressed `(0.88,0.93,1)`、selected=normal（点过不常亮）、disabled 沿用 `(0.8,0.85,0.89,0.6)`。
2. `PatchBtnHover()` 升级：主按钮改按对象名识别（`Btn_开始游戏/应用/读取/确定`），套用 `PrimaryTint()`；白底按钮维持现配色逻辑；继续补挂 `UIButtonPolish`、清理缺脚本空壳。
3. 场景补丁执行时，把 `Btn_读取`、`Btn_确定` 丢失的普通图补为 `按钮_1.png`（与另两个主按钮一致），否则这两个按钮无图可亮。
4. 触发器 `_btnhover_trigger.txt` 重跑补丁；场景手改（换图、布局、已删 Logo）不得被冲掉。
5. AGENTS.md「按钮悬停」条目更新为主按钮变色方案。

## Acceptance Criteria

- [ ] 场景中 4 个主按钮 `m_Transition: 1`（ColorTint），`m_SpriteState` 全空，颜色块符合 `PrimaryTint()` 数值。
- [ ] `Btn_读取`、`Btn_确定` 的 `Image.sprite` 指向 `按钮_1.png`。
- [ ] 运行时：主按钮悬浮 = 微蓝提亮 + 放大 1.035 + 手型光标；按下 = 压蓝 + 缩小 0.975；点击后不再保持高亮；全程不出现旧发光贴图。
- [ ] 白底按钮行为与配色不变；场景手改（Logo 已删、用户的换图与布局）完好。
- [ ] `assets/_报告/_按钮悬停改色.txt` 报告更新且无错误；`额外文件/错误_按钮悬停补丁.txt` 不存在。

## Out of Scope

- 用户进行中的其它换图（取消/删除/返回、卡片/面板底等丢失引用）——等用户完成或另行指示。
- `按钮_主_*` 旧贴图资产本身的清理（注意：`MainMenuAssets` 全量重生会把删掉的 `按钮_主_普通.png` 再生成，属既有机制）。
- 概览页签的持久选中表达（运行时换图 + 字色，现状保留）。

## Open Questions

（无——方案待用户按 Trellis 流程批准后实施）
