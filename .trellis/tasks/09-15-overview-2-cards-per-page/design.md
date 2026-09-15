# 技术设计：内容概览页每页 2 张选择卡

## 总体思路

**代码 + 场景增量手术**，不重建整场景（保护用户手改）。三层改动：

1. 运行时脚本 `MainMenuUI.cs`：卡片数组 4→2、分页状态与翻页控件接线。
2. 搭建器 `MainMenuBuilder.cs`：同步更新 `BuildOverviewPage`/`BuildOverviewCard`，
   保持"重建也能得到一致结果"（builder 仍是布局权威，但本次**不执行重建**）。
3. 新增一次性 Editor 工具（`Assets/Editor/`，做完按仓库约定移入
   `额外文件/历史Editor脚本/`）：对现有 `MainMenu.unity` 的 `Page_内容概览` 做
   就地改造并保存。

## 1. 运行时 `MainMenuUI.cs`

- `ovCards` 改为 `OverviewCardUI[2]`（场景里旧序列化 size=4 由手术工具改写，见 §3）。
- 新增序列化字段：
  - `Button ovPrevPage, ovNextPage;`
  - `Text ovPageLabel;`
- 新增状态 `int _ovPage = 0;`（0 起始）。
- `WireOverview`：接 `ovPrevPage/ovNextPage` → `StepOverviewPage(±1)`。
- `RefreshOverview` 改为分页填充：
  - 每页条数 `pageSize = ovCards.Length`（=2）；
  - 填 `_ovList[_ovPage*pageSize + i]`，`card.index = _ovPage*pageSize + i`
    （保证 `OpenBig(idx)` 直接索引 `_ovList` 正确）；
  - 越界卡 `SetActive(false)`；
  - 页码 `ovPageLabel.text = $"{_ovPage+1} / {总页数}"`，总页数按当章条数算；
  - `ovPrevPage.interactable = _ovPage > 0`，`ovNextPage.interactable = _ovPage < 总页数-1`；
  - 章页签/视角页签点击回调里 `_ovPage = 0`（含 `SetView` 内兜底）；
  - `_ovPage` 超界时夹回（数据固定每章 4 条，正常不会发生，防御性处理）。
- `未玩到提示` 文案去掉写死的 "4"，改为 `"下面是它的 N 处选择记录"`（N=当章条数）。

## 2. 布局（builder 与手术工具共用一份坐标常量）

页签区不动（章页签 y=244、视角页签 y=186）。卡片区改为竖排 2 张：

- 尺寸：`1180 × 300`（原 736×270；宽度吃满面板中部，高度加高让概述更松）。
- 位置：`选择_1 (0, +160)`，`选择_2 (0, -160)`。
- 卡内元素随新尺寸重排（手术工具按比例改 RectTransform）：
  - 配图：380×214 → 约 `470×260`，x 从 -158 移到约 -300；
  - 视角底/视角：跟着配图右侧，x 约 -50；
  - 标题：x=200 → 约 20，宽 320 → 约 560；字号 23 → 26；
  - 概述：宽 320 → 约 560，高 130 → 约 160，字号 18 → 20；
  - 选中框、点击区：改为 `1180×300` 与卡片同尺寸（点击区仍是 alpha=0、
    raycastTarget=true、Button.targetGraphic 指向它，结构不变）。
- 翻页控件放在卡片右下/中下空档：`Btn_上一页 (‑430, ‑382)`、`页码 (0, ‑382)`、
  `Btn_下一页 (430, ‑382)`，尺寸约 `150×50`（次按钮样式，白底 HoverTint，与 Btn_返回 一致）。
- 具体数值在实现时以 `主界面搭建报告` 的越界检查 + 预览图微调为准。

> 九宫格注意：`稿_卡片_1` 的 border 是按原卡尺寸设的，放大到 1180×300 后圆角/描边
> 由 9-slice 拉伸，不会变形；实现后用预览图确认一次。

## 3. 场景手术工具（关键，防手改丢失）

`Assets/Editor/OverviewTwoPerPage.cs`，菜单 `Tools/干预项目/概览页改每页2卡`：

1. 打开 `Assets/Scenes/MainMenu.unity`（已打开则直接用当前场景）。
2. 找到 `Page_内容概览`（沿 Canvas 层级按名查找，参考 builder 的命名）。
3. 卡片改造：
   - `选择_1/选择_2`：按 §2 调整自身与子物体 RectTransform（底/配图/视角底/视角/标题/概述/选中框/点击区）。
   - `选择_3/选择_4`：`DestroyImmediate`。
4. 新建 `Btn_上一页/页码/Btn_下一页`（复用 builder 的 `Btn`/`Label` 风格逻辑或手工等价创建，
   挂 `UIButtonPolish`，次按钮 HoverTint）。
5. 更新场景中 `MainMenuUI` 的序列化引用：
   - ⚠️ 必须先对程序集做 `AssetDatabase.ImportAsset(ForceUpdate)` 式预热
     （AGENTS.md 的"新脚本 MonoScript.GetClass()==null"坑），确认 `MonoScript.GetClass()` 非 null；
   - 用 `SerializedObject` 把 `ovCards` 数组 size 改为 2 并填 `选择_1/选择_2` 的
     `OverviewCardUI`，填 `ovPrevPage/ovNextPage/ovPageLabel` 引用。
6. 保存场景；输出报告到 `assets/_报告/_概览页改造.txt`（改了哪些对象、引用检查结果）。

## 兼容与回滚

- `MainMenuBuilder.BuildOverviewPage` 同步改为 2 卡 + 翻页布局，未来全量重建结果一致
  （重建仍会冲手改，这是既有已知行为，不在本任务处理）。
- 回滚：git 还原 `MainMenu.unity` + 两个 .cs 即可（手术只动这三个文件；场景当前有用户
  未提交手改，**实现前先请用户确认是否先提交一次快照**，作为回滚锚点）。

## 不做的事

- 不加重力滚动/Scrollbar（页面风格是固定网格，翻页更贴现状）。
- 不改 `OverviewDatabase` 数据（每章 4 条不变）。
- 不动其他页面任何对象。
