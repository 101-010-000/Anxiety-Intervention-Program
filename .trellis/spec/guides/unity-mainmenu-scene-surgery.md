# Unity 主菜单（MainMenu）场景手术约定

> 来源任务：09-15-overview-2-cards-per-page。适用：任何对 `Assets/Scenes/MainMenu.unity`
> 的程序化修改。核心原则：**场景是用户手改过的，程序化修改必须增量手术，禁止整场景重建。**

## 硬性约定

1. **禁止跑 `搭建主界面UI场景`（MainMenuBuilder.Run）来"顺便"改布局**——它会全量重建，
   冲掉用户手改（页面缩放、贴图替换、删除的 Logo 等）。改概览页用
   `Tools/干预项目/概览页改每页2卡`（`OverviewTwoPerPage.cs`），改其他页仿照此模式新建手术工具。
2. **工具常量 = 场景手调基线**：用户在编辑器里手调后，必须把新值抄回工具常量
   （`OverviewTwoPerPage.CardPos/ChipX` 与 `MainMenuBuilder.BuildOverviewPage/BuildOverviewCard`），
   两处保持一致。否则下次触发工具会把手调冲掉。
3. **补丁路径创建 UI 前必须预热**：
   - 先 `MainMenuBuilder.PreloadScripts(log)`（防 MonoScript.GetClass()==null → 缺脚本）；
   - 建 `Label/Btn` 前先 `MainMenuBuilder.WarmFont()`（builder 静态 `_font` 只在整场景 Run() 赋值，
     补丁直接调会造出没字体的 Text，场景空白且不报错）。
4. **弹层根节点（Page_*/Popup_*）永远不要 SetActive(false)**：禁用 → Play 模式 Awake 不跑 →
   按钮监听全灭，表现为"点了没反应且不报错"。手术工具需防御性重新启用六个弹层根节点。
5. **概览文案源头是 `MainMenuAssets.OV_ROWS`**：改文案 → `BuildDatabase(log)` 重建资产 →
   场景内卡片文字要手动 `ui.RefreshOverview()` 刷新并存场景（编辑器/预览看的是场景存量文字，
   不会自动跟着资产变）。
6. **触发器执行链**：投放 `Assets/_overview2_trigger.txt` → 手术 + `RenderPreviews` + `SmokeTest`
   一次完成。注意：Unity 目录监视对"同内容覆写/mtime touch"不敏感，触发文件必须写入**不同内容**；
   让 Unity 刷新需真实获得焦点（AppActivate + Ctrl+R），日志见 `assets/_报告/_概览页改造.txt`。
7. **改完必做三查**：`_概览页改造.txt`（引用检查）、`_主界面运行自检.txt`（37 项无 ★）、
   `git diff` 对象名级核查（确认只动了目标页的对象）。

## 已知几何事实（改概览页布局前必读）

- 页面"卡片"容器被用户手改放大 **1.2808 倍**；"内容概览卡片.png"（1702×726 横版框，
  Simple 平铺非九宫格）被拉到 600×410 后，**框线在卡内 +159/-178**（左右 ±274）。
  任何内容（配图/标题/概述）不得越过这些线；概述靠压缩文案（OV_ROWS ≤24 字 = 一行）防出框。
- 用户手调基线（2026-09-15）：卡位 (-319,-63)/(305,-63)、视角标签两卡错位 x=-200/-173
  （文字比底低 3px、字号 25）、翻页 (-289,-291)/(-29,-293)/(231,-291)。改动前先 dump 场景现值。
