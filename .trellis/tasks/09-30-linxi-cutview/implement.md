# 执行清单

前置：`task.py start` 已把本任务设为 current。

1. [ ] 读 `第1章.json` 确认结构（裸数组 vs `{"steps":[]}`）→ 按 design §1 插 2 个 cut 步骤（先 81 后 55）。
2. [ ] `StoryData.cs` StoryStep 加 `camH`/`lookH`（缺省 -1）；`CutawayCamera.Show` 加重载（≤0 取 1.55/1.35）；`DoCut` 透传。第3章 json 不动。
3. [ ] 林溪 cut 步骤写坐姿机位 `"camH": 1.15, "lookH": 0.95`。
4. [ ] 跑 `voice_shift_steps.py` ×2（先 P=81 后 P=55，按脚本顶部常量说明改）→ 核对 manifest/mp3 两处同步 → 需要时重部署 `Resources/语音/ch1/manifest.txt`。
5. [ ] 写 `Assets/Editor/LinxiCutviewSetup.cs`（幂等接线：容器限定找「林溪」→ Animator+controller+SitHere；报告 `_第1章林溪切视角.txt`）+ 丢触发器 `_linxicutview_trigger.txt`。
6. [ ] 交用户：刷新 Unity（执行接线工具）→ 丢 `_story1smoke_trigger.txt` 跑第1章自检 → 语音诊断对账。
7. [ ] 用户 Play 目检（坐姿/切入切回/气泡）→ trellis-check → 中文提交信息 commit。

⚠ 全程不用 MCP 驱动 Unity 编辑器（用户在场）；工具异常落盘 `额外文件/错误_*.txt`。
⚠ 不动：`林溪_宿舍`、第4/5章 json、ChoicePanel、对话框三节点、第3章 cut 用法。
