# design — 主按钮悬停改纯变色提亮

## 方案

全部按钮统一为 ColorTint 过渡；两类按钮只有 ColorBlock 数值不同：

| 状态 | 白底按钮 `HoverTint()`（现状不动） | 主按钮 `PrimaryTint()`（新增） |
|---|---|---|
| normal | (0.96, 0.97, 0.98) —— 相对提亮的"压暗基准" | (1, 1, 1) |
| highlighted | (1, 1, 1) —— 回白 | (1.06, 1.08, 1.12) —— 真提亮（乘法 >1，深蓝底有效；白底乘不出 >1 才用相对法） |
| pressed | (0.88, 0.94, 1) | (0.88, 0.93, 1) |
| selected | = normal，不常亮 | = normal，不常亮 |
| disabled | (0.8, 0.85, 0.89, 0.6) | 同左 |

- 缩放/手感：沿用已有 `UIHoverScale`（1.035 / 0.975，Lerp 14）与 `UIButtonPolish`（手型光标 + 文字变化），主按钮与其它按钮完全一致——"大差不差"。
- 贴图：主按钮 `spriteState` 全清空（悬停不再切 `按钮_主_悬停`）；`按钮_主_*` png 留在项目里不删（`MainMenuAssets` 指纹机制会重生，删了也会回来）。
- 乘法 tint 的物理约束：`Image.color` 乘在贴图像素上，>1 才提亮、会被 clamp；深蓝底（新素材 `按钮_1`）各通道 ≈0.2~0.85，(1.06,1.08,1.12) 提亮幅度 ≈6~12%，肉眼可辨且不刺眼。

## 改动点

1. `Assets/Editor/MainMenuBuilder.cs`
   - 新增 `PrimaryTint()`（紧挨现有 `HoverTint()`）。
   - `Btn()` primary 分支：删 SpriteSwap/spriteState，改 `transition = ColorTint; colors = PrimaryTint()`。
   - `PatchBtnHover()`：主按钮识别从"贴图名 `按钮_主_*` 前缀"改为**对象名** `Btn_开始游戏 | Btn_应用 | Btn_读取 | Btn_确定`（用户换图后贴图名已不可靠）；主按钮套 `PrimaryTint()` 并清 `spriteState`；识别到 `Btn_读取/Btn_确定` 的 `Image.sprite == null` 时补 `按钮_1`；白底逻辑不变。
2. `AGENTS.md`「按钮悬停」条目同步。

## 兼容与回滚

- 不整场景重建；补丁只改 Button 组件字段 + 两个丢失的 sprite 引用，用户手改零接触。
- 回滚 = git 还原场景与 Builder（改动都在工作区，未提交前可整体撤销）。
- 触发器执行依赖 Unity 前台刷新（Ctrl+R / 激活窗口）；失败会写 `额外文件/错误_按钮悬停补丁.txt`。
