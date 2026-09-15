# implement — 主按钮悬停改纯变色提亮

## 前置

- [x] 用户已批准 prd/design 方案（Trellis 最终摘要获批）。

## 步骤

1. `MainMenuBuilder.cs`：新增 `PrimaryTint()`；`Btn()` primary 分支改 ColorTint（见 design.md 表格）。
2. `MainMenuBuilder.cs`：`PatchBtnHover()` 主按钮改按对象名识别（`Btn_开始游戏/Btn_应用/Btn_读取/Btn_确定`），套 `PrimaryTint()`、清 `spriteState`；`Btn_读取/Btn_确定` 的 `Image.sprite` 为空时补 `按钮_1`；报告里分别统计主按钮数。
3. 更新 `AGENTS.md`「按钮悬停」条目。
4. 丢 `Assets/_btnhover_trigger.txt`，激活 Unity（pid 见运行时）触发重编译执行；轮询 `assets/_报告/_按钮悬停改色.txt` 与错误文件。
5. 验证（python 脚本，注意 YAML 的 \uXXXX 转义需解码）：
   - 4 个主按钮 `m_Transition: 1`、spriteState 全空、颜色数值正确；
   - `Btn_读取/确定` sprite → `按钮_1.png`；
   - 28 个白底按钮 transition/配色不变；
   - Logo 仍缺席、用户换图与布局微调未被冲掉；
   - `额外文件/错误_按钮悬停补丁.txt` 不存在。

## 风险文件 / 回滚

- `3D剧情项目/Assets/Scenes/MainMenu.unity`（补丁写入）、`3D剧情项目/Assets/Editor/MainMenuBuilder.cs`、`AGENTS.md`。
- 全部未提交改动可用 git 整体还原；Unity 侧无持久状态。

## 验证命令

- 场景核验：仓库根 `python` 内联脚本（见上一步骤 5）。
- 视觉抽查：Unity Play 模式手晃 / `Tools/干预项目/渲染主界面预览`（静态图看不到 hover，仅看布局未坏）。
