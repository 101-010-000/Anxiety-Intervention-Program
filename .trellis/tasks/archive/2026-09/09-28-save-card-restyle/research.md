# 已查证事实（2026-09-28，实现前不用再查）

## A) ScenePostFx 后期预览渲染机制（`Assets/Editor/ScenePostFx.cs`）
- 入口 `RenderPreview()`（L205-206）→ `RenderPreviewInternal(bool)`（L208-327）。
- 相机（L222-233）：临时 `new GameObject("postfx_cam")` + Camera；`clearFlags=Skybox`、`fieldOfView=62`、near 0.05/far 600、`allowHDR=true`；URP 附加数据 `renderPostProcessing=true`、SMAA High、dithering；渲完 DestroyImmediate（L303-304）。
- 机位（L239-269）：遍历 `Locations` 根下每个 `Loc_*`，取 `Shell/Shell_地板` 的 Renderer bounds；眼睛在房间中心、眼高 1.62m；地板 x 宽 > z 深×1.8（窄长房）→ 沿长边看（eye.x=loc.x−len×0.20，focus.x=loc.x+len×0.45），否则 eye.z=loc.z−len×0.10、focus.z=loc.z+len×0.45；focus.y=1.30，`LookAt(focus, Vector3.up)`。
- 分辨率：RT 固定 1100×620（L235）；输出 `后期_*.png`/`原始_*.png` 到 `assets/_报告/预览/场景/`。
- `static float Shoot(Camera cam, RenderTexture rt, string path, out Vector2 sharpness)`（L1160-1180）：ReadPixels 回 Texture2D → 算平均亮度/远近清晰度 → path 非空才 EncodeToPNG。**与分辨率无关，path 可传 null，RT 任意大小可直接复用。**

## B) 可复用「单地点任意分辨率」渲染
- 没有现成方法；复用件 = `Shoot()` + 上面 L245-269 的机位计算段（需自己包一层「对单个 Loc_* 摆相机」）。
- ⚠ 渲染须在 **Game.unity 打开状态**（Volume/后期在场景里）；工具流程：保存当前场景 → OpenScene(Game) → 渲 → OpenScene(MainMenu) 接线（⚠ 自检同款注意：切场景前当前场景必须已保存，避免「保存场景?」模态卡死自动化）。

## C) 六地点键名（Moods()，L94-146）
`Loc_教室`、`Loc_走廊`、`Loc_宿舍`、`Loc_食堂`、`Loc_办公室`（即咨询室）、`Loc_图书馆`。

## D) 各章主场景（剧情 json 词频）
| 章 | 主场景 | 依据 |
|---|---|---|
| 1 | 教室 | 教室×3（唯一出现的地点词） |
| 2 | 宿舍 | 宿舍×6 |
| 3 | 办公室 | 办公室×5 > 食堂×2 |
| 4 | 图书馆 | 图书馆×10 > 宿舍×5 |
| 5 | 宿舍 | 宿舍×24（图书馆×3；用反向机位与 ch2 区分） |

## E) 存读档页槽位现状（`Assets/Editor/MainMenuBuilder.cs`）
- 网格：`BuildSlot(card, i, new Vector2(-384f + col*384f, 170f - row*230f))`（L489）→ 3 列 × 2 行，卡片 360×200。
- `BuildSlot`（L501-530）子节点顺序（=渲染序，后者在上）：空框(槽位_空) → 实底(稿_卡片_1/卡片_普通) → 选中框(稿_卡片_2, 默认 disabled) → **缩略图(316×140 @ (0,22))** → 章节(y=-52, 320×30, INK, 22) → 时间(y=-80, 320×26, MUTED, 18) → 空存档 → 点击区(透明, raycast)。
- ⚠ 现状层级缺陷：缩略图在选中框**之后**渲染（盖住线框）；章节 label 上缘 -37 与缩略图下缘 -48 区间重叠。
- `SaveSlotUI`（`Assets/Scripts/UI/SaveSlotUI.cs`）：Refresh 里 thumb 启用条件 = 有档且有 thumbPath；`ThumbnailCache.Get` 把整张截图 LoadImage 成 Sprite（无任何处理）。
- StoryRunner 抓屏（`Assets/Scripts/Story/StoryRunner.cs` L649-698）：`CaptureChoiceThumb` 挂在 `ChoicePanel.onPanelComplete`，`ScreenCapture.CaptureScreenshot` 全分辨率落盘。
