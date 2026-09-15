# 执行清单：内容概览页每页 2 张选择卡

> 前置：本清单在用户批准 design.md 后、`task.py start` 之后执行。
> 建议先让用户把当前 `MainMenu.unity` 的手改提交一次（回滚锚点）。

## 步骤

1. [ ] 运行时：`MainMenuUI.cs` 增加翻页字段/状态/接线，`RefreshOverview` 分页填充，
   `card.index = _ovPage*2 + i`，页签/筛选点击回页首，未玩到提示去掉写死的 "4"。
2. [ ] 布局常量：在 `MainMenuBuilder.cs` 落一份新布局坐标（2 卡 1180×300 竖排 +
   翻页控件），`BuildOverviewPage`/`BuildOverviewCard` 按新布局改写（仅代码，不执行重建）。
3. [ ] 新建 `Assets/Editor/OverviewTwoPerPage.cs`：菜单 `Tools/干预项目/概览页改每页2卡`，
   按 design.md §3 对现有场景做手术（删 选择_3/4、改 选择_1/2 及子物体、建翻页控件、
   SerializedObject 改 MainMenuUI 引用；动手前 ForceUpdate 预热脚本）。
4. [ ] 跑手术工具 → 看 `assets/_报告/_概览页改造.txt` 引用检查。
5. [ ] `Tools/干预项目/渲染主界面预览` 看概览页新布局；按预览微调坐标（可回第 3 步）。
6. [ ] `Tools/干预项目/主界面运行自检`：25 步自检通过、无越界、引用不丢。
7. [ ] 手动验证点（自检脚本如有覆盖更好）：翻页/页码/按钮置灰/切章切筛选回页首/
   点卡开大图索引正确/其他页面对象未动（git diff 场景文件确认只动了概览页相关对象）。
8. [ ] 收尾：`OverviewTwoPerPage.cs` 移入 `额外文件/历史Editor脚本/`（若判断为一次性）；
   跑 `trellis-check`；提交（中文提交信息）。

## 验证命令

- Unity 菜单：`Tools/干预项目/概览页改每页2卡`、`渲染主界面预览`、`主界面运行自检`
- 报告：`assets/_报告/_概览页改造.txt`、`assets/_报告/_主界面搭建.txt`（越界=无）、
  `assets/_报告/_主界面运行自检.txt`
- `git diff -- "3D剧情项目/Assets/Scenes/MainMenu.unity"` 复核改动范围

## 回滚点

- 步骤 1-2 纯代码：git checkout 还原即可。
- 步骤 3 之后：还原 `MainMenu.unity` 到手术前提交（前置快照）。
