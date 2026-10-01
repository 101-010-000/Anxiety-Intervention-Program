# 执行清单

前置：任务 `task.py start` 后才动手（用户已看过方案）。

1. [x] 读 `面板_标题条.png` 平均色/不透明度 → 实测中蓝 #77B9F1 半透明 → 定墨蓝字 `#1B2C42`；border 导入已有 (32,0,32,0)。
2. [x] 写 `Assets/Editor/ChoiceTitlePlate.cs`（实施中迭代 v1→v3，结构见 design.md 实施结果；报告 `_选择题题干清晰化.txt`；异常落盘 `额外文件/错误_选择题题干.txt`）。
3. [x] 触发器 `Assets/_choicetitleplate_trigger.txt` 已执行（MCP 驱动刷新，触发器自删）。
4. [x] 预览图已渲：`assets/_报告/预览/场景/选择题题干_修后.png` + `_亮背景.png`（工具内置，照 MainMenuBuilder 套路）。
5. [x] 报告对账：材质覆盖=无（历史提示是假象）、底板已挂、选项行/confirm/对话框未触碰；预览目检通过；幂等重跑通过；场景落盘按转义形态字节确认。
6. [ ] 用户 Play 一章的选择题验收 → trellis-check → 提交（中文提交信息）。

验证命令/口径：
- 报告路径 `assets/_报告/_选择题题干清晰化.txt`；
- 验收标准见 prd.md（幂等重跑、亮暗背景可读、其余 UI 零变化）。

回滚：工具只改 Game.unity 里 Title 子树；git 还原 Game.unity 即回滚。
