# 技术设计

## 总览
新增一个 Editor 工具 `Assets/Editor/SaveCardArt.cs`（渲染定妆照 → 像素处理 → 场景补丁接线 → 预览），加两处小改运行时（`SaveSlotUI.cs` / `MainMenuUI.cs`）。事实依据见 `research.md`。

## 1. 资产流水线（SaveCardArt.cs）
菜单 `Tools/干预项目/存档卡定妆照/`，三个入口 + 触发器：
- ① 渲染5章定妆照：提示保存当前场景（有脏数据直接返回并写报告）→ `OpenScene("Assets/Scenes/Game.unity")` → 对 5 个 (地点, yaw偏移) 组合各渲一张：
  - 表：ch1=Loc_教室/0°，ch2=Loc_宿舍/0°，ch3=Loc_办公室/0°，ch4=Loc_图书馆/0°，ch5=Loc_宿舍/180°。
  - 相机参数与机位算法照抄 `ScenePostFx.RenderPreviewInternal`（L222-233 相机、L245-269 地板 bounds 机位；ch5 在算好的 yaw 上 +180° 绕注视点转）。
  - RT = 720×400；复用 `ScenePostFx.Shoot(cam, rt, null, out _)` 的读回逻辑（若 Shoot 不可直接复用，就地 ReadPixels，几行而已）。
  - 像素后处理（Texture2D 上直接改，RGBA32）：
    - 圆角：四角象限内 `dist(圆心) > RADIUS(24px)` → alpha=0，2px 线性过渡抗锯齿；
    - 暗带：底部 `BAND_H(120px)` 线性渐变 `BAND_COLOR(#16202E)` alpha 0→`BAND_ALPHA(0.72)`，最底 40px 恒 0.72；
    - 常量集中文件顶部。
  - 写 `Assets/assets/05_UI/存档插图/ch1.png..ch5.png` → `TextureImporter` 设 `TextureImporterType.Sprite`（无 9 宫格、mipmap 关）。
- ② 接线存读档槽位（幂等补丁，见 §2）。
- ③ 渲染槽位预览：编辑器临时相机对 `Page_存读档` 整页出图（1920×1080）+ 单槽三态特写 → `assets/_报告/预览/存档卡/`；报告 `assets/_报告/_存档卡定妆照.txt`（渲染参数、处理常量、接线明细、0 错误自检）。
- 触发器：`Assets/_savecard_trigger.txt` → 依次 ①②③，`[InitializeOnLoad]` 消费后自删（照抄仓库既有触发器模式）。
- 流程顺序：①渲完切回 MainMenu 场景再 ②接线 ③预览（②③要求 MainMenu.unity 打开，改完存盘）。

## 2. 场景补丁（幂等，只碰约定子树）
对 `Page_存读档` 下 6 个 `槽位_N`：
- `缩略图`：sizeDelta (360,200)、anchoredPosition (0,0)、raycastTarget=false；**siblingIndex 移到 `选中框` 之前**（图在下、线框在上，修 R4）；
- `实底`：`enabled=false`（节点保留，兼容旧序列化字段）；
- `章节`：anchoredPosition (0,-64)、sizeDelta (320,28)、颜色白 (1,1,1,1)、字号保持 22；
- `时间`：anchoredPosition (0,-88)、sizeDelta (320,22)、颜色 (0.86,0.90,0.94,0.92)、字号 18→17；
- `空存档` / `空框` / `点击区` / `选中框`：一律不动；
- `MainMenuUI` 组件：新增序列化字段 `chapterArt`（Sprite[5]）接入 5 张定妆照。
- 幂等规则：已接线只覆盖上述约定值，绝不触碰其他节点/其他页。

## 3. 运行时改动
- `SaveSlotUI.cs`：
  - +`public static Sprite[] ChapterArt;`（MainMenuUI.Awake 注入）；
  - Refresh() 有档时：`thumb.sprite =` 定妆照（`chapter-1` 合法且非空）→ 否则回退旧截图逻辑（ThumbnailCache）；thumb 启用条件相应放宽；
  - `frameFilled` 恒 false；布局不改（rect 由补丁工具落盘）。
- `MainMenuUI.cs`：+`[SerializeField] public Sprite[] chapterArt`（5 位）；Awake 里 `SaveSlotUI.ChapterArt = chapterArt`（放现有初始化处，注意 null 容错）。
- `ThumbnailCache` / `StoryRunner` 抓屏逻辑：原样保留（R6）。

## 4. 兼容与回滚
- 定妆照缺失/越界 → 自动回退旧截图路径，老档不炸；
- 新字段全有默认值；回滚 = revert 代码 + revert 场景（补丁只触碰约定子树，diff 可控）；
- 明确不做：网格/页面布局、StoryRunner 抓屏、手机聊天/选择题/其他页 UI。

## 5. 风险
- ⚠ 编辑器切场景会丢未保存改动 → 工具入口第一步就检查 `SceneIsDirty`，脏则中止并写报告提示先保存；
- ⚠ `MonoScript.GetClass()` 为 null 坑（AGENTS.md）：如补丁需 AddComponent 必须先 `PreloadScripts` 式 ForceUpdate 导入——本设计只改已有组件字段，不 AddComponent，规避该坑；
- ⚠ 定妆照是 720×400 非九宫格 Sprite，Image 显示无需 preserveAspect（比例恰好=卡片 1.8:1）。
