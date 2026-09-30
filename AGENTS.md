# Agent 必读（本仓库约定）

> 接手本仓库前先读完这份文件。核心一句话：
> **`3D剧情项目/` 才是项目本体；仓库根目录下其它文件夹都是"原始素材"，只能读、只能复制，不能就地改。**

---

## 一、仓库结构

| 位置 | 是什么 | 能否修改 |
|---|---|---|
| `3D剧情项目/` | **真正的项目**：Unity 2022.3 + URP 14 工程（剧情 demo 本体） | ✅ 只在这里干活 |
| `角色模型素材/` | 角色套件**原始素材**（Materials / Meshes / Textures / Shaders / 组合角色 / 动画） | ❌ 只读 |
| `环境模型素材/` | 场景套件原始素材（教室、走廊、宿舍、食堂、楼梯间…） | ❌ 只读 |
| `ui素材/` `音效素材/` | UI / 音频原始素材 | ❌ 只读 |
| `项目文档/` | 需求文档、剧本、素材清单（.docx / .xlsx） | ❌ 只读 |
| `导出工具_Editor脚本/` | 给素材做加工的工具脚本（导出角色 FBX、测量与修复素材等） | ✅ 可改 |
| `额外文件/` | **agent 副产物**：临时脚本、dump、备份、日志等，与项目运行无关 | ✅ 随便放（已 gitignore） |

`额外文件/` 内部约定：

```
额外文件/
  日志_构建/            Unity 构建/批处理日志（*.log，项目根不放日志）
  历史Editor脚本/       旧的一次性 Editor 脚本（不参与编译，只做档案馆，别放回 Assets）
  历史脚本_运行时/      从 Assets/Scripts 里拿出来的调试/一次性 MonoBehaviour（同样不参与编译）
  旧资源/               从项目里清出来的旧资产（连 .meta 备份，恢复时整份拷回去 GUID 不变）
  场景备份/             换模型/大改前后的 .unity 快照
  旧预览/               已作废的预览图（当前有效预览在项目 assets/_报告/预览/）
  历史dump/             早期的一堆场景/素材 dump（*.txt）
  素材整理脚本/         整理原始素材时用的脚本与 dump
  工具脚本/             通用小工具（如 检查CSharp.py）
  导出给Mixamo/         给 Mixamo 上传的 FBX/OBJ（ExportForMixamo 的产物，~86MB，跑导出时会重建）
  错误_*.txt            Editor 工具运行出错时的异常堆栈（项目外的副产物）
  _backup_editor_scripts/ _backup_scene_metas/  更早的两次批量备份（保留）
```

> 2026-09-28 做过一次清理（`历史Editor脚本/ProjectTidy.cs` 是当次工具，已归档）：
> 删掉 `02_角色_Character/Mixamo已绑`（2 个，上一轮试绑产物）、`Materials_URPLit`（68 个，全项目无人引用的另一套套件材质）、
> `动画/测试跑动画.fbx`、`预览Clip` 里三个孤儿 anim、`Animators/预览/李老师_已绑.controller`（均已备份到 `额外文件/旧资源/`）；
> 旧报告 45 份 → `assets/_报告/历史/`，旧预览图 `Mixamo测试`、`腿脚` → `assets/_报告/预览/历史/`。
> ⚠ `FootWeightFix.cs` **不能动**：`CharRebuild.cs` 重建时会调 `FootWeightFix.FixIfShoe()`（其他脚部工具确实只被注释提到，已归档）。
> ⚠ `Scripts/Debug/AnimDiag.cs` 已归档；`NpcSetup` 的 PlayDiag 菜单要是还想用，把它拷回来即可。

### 素材的使用方式：复制进项目，**必须连 `.meta` 一起复制**

原始素材不直接参与项目运行。要用哪个文件，就把它复制到项目里对应目录：

```
角色模型素材/Meshes/…      → 3D剧情项目/Assets/assets/02_角色_Character/Meshes/…
角色模型素材/Textures/…    → 3D剧情项目/Assets/assets/02_角色_Character/Textures/…
环境模型素材/…             → 3D剧情项目/Assets/assets/01_场景_Scene/…
音效素材/…                 → 3D剧情项目/Assets/assets/04_音效_Audio/…
```

> ⚠️ **务必连 `.meta` 一起复制**。只复制文件、让 Unity 重新生成 `.meta`，会得到新的 GUID，
> 所有按 GUID 建立的引用（材质 ↔ 贴图 / 材质 ↔ Shader / prefab ↔ 材质 / FBX 的 externalObjects）
> 会**全部断链**。本项目已经踩过这个坑：角色套件的 ShaderGraph 没带 meta 导入 →
> 材质引用不到 shader → 角色变洋红；后来靠 `Update to URP.unitypackage` 里的原始 GUID 才修回来。

---

## 二、项目内部结构（`3D剧情项目/Assets`）

```
Editor/                        agent 工具（菜单 Tools/干预项目/…），**只保留长期工具**（一次性脚本用完移到 额外文件/历史Editor脚本/）：
  CharRebuild.cs               角色重建：服装搭配 / 一人一色 / 身体删减
  PlayerAnimSetup.cs           主角动画：生成 AnimatorController 并挂到徐夏
  CharPreview.cs               渲染角色预览 / 材质诊断 / 全量强制重导
  AnimModelMats.cs             带动画模型：把角色材质贴回 FBX 导入器（externalObjects）+ 预览渲染（见第五节）
  GameCharSwap.cs              Game 场景角色替换：旧模型 → Mixamo 已绑定模型 + 挂 Idle + 运行自检（见第三节末）
  PlayerThirdPerson.cs         主角第三人称：建 徐夏_第三人称.controller（Idle/Walk/SlowRun + Phone）+ 配镜头 + Play 自检
  ExportForMixamo.cs           导出给 Mixamo 的模型（去骨骼只留网格，T-pose rest 蒙皮结果 → 额外文件/导出给Mixamo/）
  CheckMixamoExport.cs         自检：导出的 FBX Unity 能不能读（配合上一个用）
  FootWeightFix.cs             ★ 鞋/袜踝口权重修复——被 CharRebuild 调用，不要归档！
  （其余一次性脚本已归档：MixamoRigCheck / FootDiag / FootDiagTrigger / FootSkinProbe /
    TestRunAnim / InstallFbxPkg / ProjectTidy / _TriggerTouch —— 都在 额外文件/历史Editor脚本/）
  AssetLocator.cs              在 Assets 里按名字找文件/目录
  SceneBuilder.cs              剧情主场景 Game.unity：6 个地点拼装 + 场景总览渲染（末尾会自动调 ScenePostFx）
  ScenePostFx.cs               剧情场景后期：辉光/模糊/抬黑/雾 + 6 个地点各自的氛围 Volume（见第五节）
  GameDoorBuilder.cs           门口传送：给场景里已有的门触发盒挂交互 + 「按 F 开门」+ 地点选择面板（见第五节）
  Chapter4DoorTrip.cs          第4/5章门口引导：门口 UI 在且提示统一（缺了就重建）+ 第4/5章任务触发点摆位 + 清场景残留（见第五节）
  SitSetup.cs / SitSwapSetup.cs  坐姿：生成 Sit 剪辑 + 控制器 Sit 状态 / 坐下换模型接线（宿舍×2 + 食堂×1 + 图书馆×2，见第三节）
  ChoiceTitleColor.cs          选择题题干标题配色（改蓝白色 #CCE3FF，用户 2026-10-01）
  AnimPreviewSetup.cs          角色预览场景：每个角色挂一个不一样的动画 + 运行自检（见第五节）
  MainMenuAssets.cs            主界面 UI 贴图：程序化生成 57 张 + 中文字体 + 20 张收录图导入
  MainMenuSlices.cs            切《ui素材》设计稿：圆角抠图 + 内部压平 + 九宫格 border（稿_*.png）
  MainMenuBuilder.cs           主界面场景 MainMenu.unity：主菜单/设置/存读档/章节/概览/弹窗
  SceneViewReset.cs            场景视图（编辑器摄像机）复位：摆正/回到默认 3/4 视角/聚焦选中
  （历史脚本在 额外文件/历史Editor脚本/，不要放回这里）
assets/
  01_场景_Scene/               环境模型（教室/走廊/宿舍/食堂/咨询室/图书馆…）
  02_角色_Character/
      Materials/               套件原始材质（各角色共享）
      Meshes/                  套件配件：12 上衣 / 6 下装 / 7 鞋 / 22 发型 / 4 连体装 / 帽子眼镜围巾
      Textures/                遮罩 RGBMap + 法线贴图
      组合角色/<角色>/<角色>.fbx      每个角色的模型（含 humanoid Avatar，动画用）
      角色_URP/<角色>_可动.prefab     ★ 场景/剧情里真正使用的角色
      角色_URP/材质/<角色>/           角色专属材质实例（一人一色）
  03_动作_Animation/           动画 FBX + Animators/（PC_徐夏_测试.controller = 旧的；带动画模型/<角色>_Idle.controller、
                                徐夏_第三人称.controller = 现在在用的，同源零重定向）
  04_音效_Audio/
  05_UI/                       ★ 主界面 UI 素材：背景/界面/按钮/图标/字体 + 内容概览（20 张原图）+ 设计稿_原图（《ui素材》12 张）
  06_道具_Props/               独立小道具（手机_淡蓝：源 OBJ×4 + 材质/ + 手机_淡蓝.prefab + _来源.json）
  11_着色器_Shaders/           角色套件 ShaderGraph（CharacterLit / Toon / 子图 / HLSL）
  _报告/                       ★ 所有报告、清单、预览图都写到这里
Scripts/UI/                    主界面运行时脚本（MainMenuUI / UIPanel / GameSettings / SaveSystem …）
Scripts/Player/                FirstPersonController.cs（第一/第三人称移动·视角·动画，同一个脚本）
Scripts/Visual/                DreamyFocus.cs（景深同步）、SceneOutline.shader + OutlineFeature.cs（描边，见第五节）
Scripts/Game/                  DoorInteractable.cs / DoorTravelSystem.cs / DoorSmokeDriver.cs（门口传送，见第五节）
Scenes/角色资源预览场景.unity   角色预览（9 个角色实例 + 相机 + 太阳；原名 Test_徐夏_动画）
Scenes/MainMenu.unity          主界面（Build Settings 第 0 号：主菜单 + 设置/存读档/章节选择/内容概览）
Scenes/Game.unity              剧情主场景（Build Settings 第 1 号，SceneBuilder 生成）
```

---

## 三、角色与材质要点（改之前必读）

- 角色 Shader = **`ShaderGraph_CharacterLit`**；材质的 **`_BaseMap` 是"RGB 遮罩"，不是颜色贴图**：
  R 通道 → Color A，G → Color B，B → Color C（每个颜色又分 `1/2` 两档），配 `Mask Factor` / `Mask Remap`。
  **把遮罩直接当 albedo 用（例如配 URP/Lit）就会得到"整片红"** —— 这正是本项目历史上的主要事故。
- `Color X 1` = 主色，`Color X 2` = 亮部，主色通常写进 **A 色对**；
  ⚠️ 例外（**主布料在 G 通道**，主色要写 B 色对）：`mat_top.002_jacket`、`mat_top.006_openshirt`、
  `mat_top.011_blazer`、`mat_shoes.002_sneakers`、`mat_outfit.002`（见 `CharRebuild.cs` 的 `MainOnG`）。
- 服装按套件"**同号成套**"（001 上衣 + 001 下装…）；**每个角色一套配色**，互相不撞。
- 身体删减（穿模处理）以**场景里手改出来的模板为准**（`CharRebuild.cs` 每行的 `Remove=`），不要随意增删。
- 主角徐夏的 Animator：`Locomotion` 1D 混合树（`Speed`：0 待机 / 0.5 行走 / 1 慢跑 / 2 快速跑）
  \+ `Phone`(Bool) 拿手机待机 + `TakePhone`(Trigger) 拿手机。

### 带动画模型（Mixamo 重绑的 FBX）  `assets/03_动作_Animation/带动画模型/<角色>/`

- **背景**：原角色是 CC_Base 骨架 + Unity 重定向，重定向会把腿掰歪 → 把组装好的网格导出给 Mixamo
  （`ExportForMixamo.cs`，去骨骼只留网格），Mixamo 自动绑一套自己的骨架后导回来。
- 每个角色一个文件夹，里面 `已绑定.fbx` = 模型本体（无动画），`Idle/Walk/Texting/Slow Run…` = 带模型的动作。
- ★ **材质要在这里贴**：Mixamo 导回的 FBX 里材质只是空壳（名字还在、没贴图）→ 白模。
  用 `Tools/干预项目/带动画模型：贴回角色材质`（`AnimModelMats.cs`）：把 FBX 导入器的
  **材质重映射（externalObjects）** 写成「FBX 内材质名 → `角色_URP/材质/<角色>/*.mat`」，
  以后任何地方实例化这个 FBX 材质都是对的（**共用**角色的那份材质，一人一色仍只有一处真源）。
- 名字怎么对上：Unity 导出时把 `. 空格 -` 都换成 `_`，Mixamo 往返后保留 → 两边「清洗后同名」。
  角色实例材质优先，其次是套件共享材质（眼睛/嘴/高光/眼镜）。
- ⚠ **Unity 2022.3 没有 `SetExternalObjectMap` / `AddRemappedAsset`**，改 `GetExternalObjectMap()` 返回的字典再
  `SaveAndReimport()` **不会落盘**（`_带动画模型材质.txt` 里 `.meta 复核 0 条` 就是这个坑）。
  工具现在走：① 反射调内部 `AssetImporter.AddRemap` → 复核 .meta；② 不行就直接改写 .meta 的 `externalObjects` 块 + 强制重导。
  **判断成不成看 `.meta 复核` 条数，不要看返回值。**
- 自检：报告里应有「找不到对应材质：0 ✓ / 空材质槽：0 ✓」；预览走
  `Tools/干预项目/带动画模型：渲染预览（看材质）` → `assets/_报告/预览/带动画模型/<角色>_已绑定.png`
  （和 `预览/<角色>_可动.png` 同机位，可逐像素对比：实测平均差 3~9/255，基本一致）。
- ⚠ 导进来的 FBX 默认是 **Generic / 无 Avatar**（Unity 新建 FBX 的默认值）。要让动画真的驱动人形骨架，
  得在 Rig 里设 **Humanoid + Create From This Model**（`MixamoRigCheck.cs` 里有同类代码可参考），
  否则 Animator 挂着也不动。另外每条剪辑默认都叫 `mixamo.com`，接控制器前最好改个名。

### 坐姿（坐下动作）  `Assets/Editor/SitSetup.cs` + `Assets/Scripts/Game/SitSpot.cs`

- **剧情里坐下是刚需**：第1章（教室第三排）、第2章（宿舍书桌 / 陆宣雨搬椅子）、
  **第3章 `interact` @ `Loc_食堂/多章锚点/第3章_落座`**、**第4章 `interact` @ `Loc_宿舍/多章锚点/第4章_坐下看资料`**、
  第5章（`第5章_回座位`）。
- **素材**：Mixamo 的 `Sitting Idle.fbx`（勾 In Place），放 `带动画模型/<角色>/`（和 Idle/Walk 同格式，带模型那种）。
  已有的：徐夏 / 林溪 / 王含 / 陆宣雨 / 李老师 / 组长 / 舍友A / 舍友B（2026-09-30 补齐后八个都有；
  ⚠ 文件名必须叫 `Sitting Idle.fbx`，`SitSetup` 只认这个名字。材质走「带动画模型：贴回角色材质」）。
- **工具**（`Tools/干预项目/坐姿：生成剪辑 + 加 Sit 状态`）：① 生成循环剪辑 `<角色>_Sit.anim`；
  ② 给 `<角色>_Idle.controller` 与 `徐夏_第三人称.controller` 加 Bool 参数 `Sitting` + 状态 `Sit`
  （AnyState→Sit 当 Sitting=true；Sit→默认状态 当 false）；③ 给名字含「落座/坐下/回座位」的锚点挂 `SitSpot`。
- ★ **坐下的表现 = 换模型（用户 2026-09-30 定稿；2026-10-01 升级为「跟用户摆的坐姿模型」）**：
  · 场景里预先摆一份**默认隐藏的坐姿模型**（`带动画模型/徐夏/Sitting Idle.fbx` 的实例，
    Animator = `徐夏_第三人称.controller` + `SitHere` 强制坐姿）；
  · 坐下（剧情锁住自动坐）→ **站立模型（`徐夏_已绑定`）整棵藏掉 + 坐姿模型亮出**；
    对话结束（解锁）后按 WASD / 走开 / 再按 F → 换回站立模型（`SitSpot.seatedModel` / `hideStandingModel`）。
  · ★ **坐姿位置/朝向 = 坐姿模型在场景里摆好的位置/朝向**（`SitSpot.useSeatedModelPose`，默认开）：
    用户把模型摆到椅子上，坐姿就坐那儿；**座位标记点同时对齐到模型**（X/Z + 朝向），所以 F 提示点也在椅子边。
    ⚠ 以前坐姿是「座位标记点 + 最近凳子朝向」，标记点在书桌边 → 人会坐在桌边悬空（用户 10-01 反馈后改的）。
  · ⚠ **座位标记半径 = 2.2m**（≥ 剧情 F 交互点半径），否则玩家站在「按 F」提示范围内按下 F 却不会坐下。
  · ⚠ **剧情锁住（对话中）时 WASD / F 都不起身**（`SitSpot.Update` 判 `!_fpc.locked`）——坐的模型不许动；
    但 `d > standDistance`（走开 / 剧情传送）不受锁影响，转场照样会起身。
  · ★ **取消坐下 = 回到【坐下前站的位置】**（用户 2026-10-01）：`Seat()` 记下当时玩家的位置/朝向，
    起身（WASD/F/`SitSpot.StandUp()`）时先把 CC 关了挪回去再打开 —— 不然人会站在椅子/桌子中间。
    被剧情传走 / 自己走开（`d > standDistance`）那条路径**不往回传**（会跟剧情传送打架）。
    · 记录是**全场共享 + 一次坐下只记一次**：宿舍第4/5章两个座位点重叠、坐下时两个组件会接连 `Seat()`，
      不共享的话后坐的那个会把「已经被挪到椅子上」的位置当成原站位（探针踩过）。
    · 明确起身后若对话还锁着，`_suppressSit`（静态）压住【所有座位】自动坐回，解锁才恢复
      （否则隔壁那个重叠座位会立刻把人按回去——探针踩过）。
  · 藏站立模型 = 把它的渲染器 `enabled=false`（描边外壳一起没，比换透明材质干净）——
    `FirstPersonController.SetStandingModelVisible(bool)`。
  · ⚠ **两个模型名都要列进 `firstPersonShadowsOnlyParts`**，否则第一人称坐姿模型的头会怼进相机。
  · ★ **不要黑底的「按 F 坐下/起身」小提示**（用户 2026-10-01）：`SitSpot.showPrompt` 默认 **false**
    （工具接线时也一律写 false）；交互提示统一用游戏原有的蓝色那套 `UI交互/交互提示`（剧情点自己给）。
    那个黑底小提示是 `SitSpot` 运行时自建的 `SitPrompt` 画布，只有以后真要开「自由按 F 坐下」的点才开。
  · ★ **只有剧情要坐的才坐**（用户 2026-09-30 定稿）：自由「按 F 坐下」的点**一律关掉**（`SitSpot.enabled=false`），
    只留三个剧情座位：`Loc_食堂/多章锚点/第3章_落座`、`Loc_宿舍/多章锚点/第4章_坐下看资料`、
    `Loc_宿舍/多章锚点/第5章_回座位`（都是 `剧情锁住自动坐下` 模式）。
  · 坐姿模型 = **用户按地点各摆的一份**（2026-10-01：`Loc_宿舍/…/徐夏任务视角切换`、`Loc_食堂/…/徐夏任务视角切换`；
    上一版叫「玩家切换」，现留作隐藏备用）。工具会：① 从「第N章角色」容器挪到该 Loc 的 `多章锚点` 下
    （容器按章隐藏时它跟着没 → 第4章的座位就换不了模型）；② 默认 `SetActive(false)`；
    ③ 补 Animator（`徐夏_第三人称.controller`，Sit 状态）+ `SitHere`（Mixamo 的 FBX 本身不带 Animator，不补就是 T-pose）；
    ④ **按地点分配**：宿舍的两个座位用宿舍那份、食堂的座位用食堂那份（优先名字带「任务视角」= 用户最新摆的，
    其次「玩家切换」，再其次离地点最近的）；⑤ 把座位标记点对齐到模型。
  · 接线工具：`Tools/干预项目/坐姿换模型：接线（宿舍 + 食堂）`（`Assets/Editor/SitSwapSetup.cs`，幂等，
    报告 `assets/_报告/_坐姿换模型.txt`）；自检 `assets/_报告/_坐姿换模型自检.txt`（逐座位查：坐姿模型亮、
    站立模型藏、Animator 在 `Sit`、Hips 0.55m / 站姿 0.90m、坐姿与用户摆位偏差 0.00m、
    取消坐下回到原站位偏差 ~0.00m、走开/被传走能换回来且不回传）；
    截图存档 `assets/_报告/预览/坐姿换模型/`（临时截图驱动用完归档在 `额外文件/历史脚本_运行时/`）。
  · ★ **图书馆两个坐下点（用户 2026-10-01 加的模型）**：
    · `第4章_图书馆座位`（"走到林溪旁边的位置"）→ **暂不接换模型**（待用户确认位置）：那个点在书架边的过道里，
      就地坐像"贴着书架坐"、第三人称镜头还会插进书架；而用户摆的坐姿模型在一张阅览桌的椅子上、离林溪 (201.1,-6.2) 有 ~6m，
      跟着模型坐会把玩家瞬移过去。要接的话：把 F 点也挪到那张椅子，或把林溪也挪过去（先问用户）。
    · `第5章_图书馆躲避`（"在图书馆找个凳子坐下"）→ **固定座位、坐姿跟用户摆的模型**（用户 2026-10-01 定稿：
      "坐的位置就按我摆的模型"）：SitSpot = `useSeatedModelPose=true` + radius 2.2；工具还会把**剧情 F 点本身**
      也挪到模型那把椅子上（`第5章_图书馆躲避` 物体），并把同一点上的 `StoryInteractable` 从 `anySeat`
      改成普通半径（否则玩家站不到"任意凳子 0.4m"判定里、触发不了）。
      · ⚠ 之前那版是 `anySeat+sitAtPlayer`（坐玩家挑的凳子）——和"按模型坐"冲突（模型摆位白摆），已改。
    · 图书馆那份坐姿模型 = 用户拖进来的 `Sitting Idle`（没改名）→ `FindSitVariants` 也认名字带 `Sitting` 的；
      它当时挂在 `Loc_图书馆/第四章角色` 下，工具按「最近 Loc」归到 `Loc_图书馆/多章锚点` 并隐藏。
  · ⚠ **SitSpot 的判定半径一律从本节点（剧情 F 交互点）算**：`anySeat` / `sitAtPlayer` 时 `SeatPos()` 会跟着玩家跑，
    用 `SeatPos()` 判距离会永远 0 → 满场景乱坐（踩过：在宿舍出生就触发图书馆的座位）。
  · ⚠ **凳子/家具的「节点原点 vs 可见网格」能差 ~3m**（这批 `凳子2` 全是固定 2.98m 偏移，方向随实例 yaw）：
    任何「按坐标摆的点」都别信节点 —— 判距离用 `Collider/bounds` 的表面点，`SitSpot.DebugSeats()` 可直接打印对照。
  · ⚠ **非凸 MeshCollider 的 `ClosestPoint` 会把入参原地返回**（场景设施碰撞体全是非凸的！）——
    用它当"凳子表面距离"会让任意位置都算贴身（`SitSpot` / `StoryInteractable` 的 `SurfacePoint` 都已跳过非凸 MeshCollider，
    改用渲染包围盒）。排查这类问题看 `SitSpot.DebugSeats()`。
  · ⚠ **别用 static 字典缓存凳子名单**：编辑器「关闭域重载」时静态缓存会跨 Play 会话残留（Unity 复用 instanceID），
    会拿着上一次场景的凳子判定 —— 现在 `SitSpot`/`StoryInteractable` 都改成实例缓存 + 1s TTL。
  · ⚠ **重建场景锚点时别把图书馆这两个点也对齐到模型**（工具 ④ 已跳过 `Loc_图书馆`，由 ④′ 处理）：
    第4章那两个 F 点被挪走的话，剧情里玩家就跑错地方；`SitSwapSetup.RestoreAnchor()` 会把挪走的挪回设计位。
  · ⚠ 还没接的「坐下」点：第5章 `第5章_食堂座位`（任意凳子模式，凳子名单里只认 `凳子2 (24)`）——
    要换模型的话在 `SitSwapSetup` 的 ④′ 里照图书馆那样补一个 SitSpot 即可。
  · ⚠ 找坐姿模型时**只认 prefab/FBX 实例的根**（`GetOutermostPrefabInstanceRoot(go)==go`）——
    `FindObjectsOfType<GameObject>` 会把 `mixamorig:*` 骨头也列出来，光看“源资产路径”会选到骨头上（踩过）。
- ⚠ 坐姿剪辑的基准是**脚在地面、屁股在椅面** → 实测 Hips Y ≈ **0.55m**（站姿 Idle ≈ 0.93m），
  所以**座位点的 Y 必须是地面（0）**、朝向 = 面向桌子（现有三个锚点正好都在 Y=0）。
  · `SitSpot.SeatPos()` 取地面：**玩家脚底 Y 与“向下打到的所有面里最低的那个”取小**。
    别用“第一次向下命中的面”—— 座位标记常是凳子的子物体（Y≈0.5 坐垫高度），会把人坐到半空（实测 Hips 2m+）。
  · 坐下期间 `CharacterController` **保持关闭**（`Stand()` 再打开）：胶囊插在凳子正中间，
    开着会被 collide-and-slide 顶到凳面上去；`FirstPersonController.Step()` 已加 `!cc.enabled` 早退。
- **运行时**：`SitSpot`（座位点）—— 玩家站到座位上且**剧情把玩家锁住**（locked=true，即正在对话）→ 自动坐下；
  对话结束（解锁）后按 WASD / 走开 / 被传送 → 起身（锁住期间 WASD 不起身）。
  **不需要改 StoryRunner**（靠"站在座位上 + 被锁住"这个组合判定，和现有 interact 流程天然对上）。
  现在只有 3 个剧情座位（宿舍×2、食堂×1）挂着启用的 SitSpot，`onlyChapters` 清空 = 不限章节。
  NPC 用 `SitHere`（Start 把 Sitting 置 true），把 NPC 实例摆到座位上即可。
- 第三人称镜头有**坐姿档**：`FirstPersonController.SetSitting(true)` 会把 `tpHeight` 1.45→0.95、
  `tpLookHeight` 1.2→0.72（不然坐着镜头盯头顶），起身自动还原。

### 去掉某部件（眼镜等）  `Assets/Editor/HidePart.cs`

- **需求**：陆宣雨不带眼镜（用户 2026-09-29）。
- **做法：把那个子网格的三角形清空**，不是换透明材质 ——
  带动画模型整身是「一块合并网格 + 多个子网格」，眼镜就是其中一个子网格；
  **描边（OutlineFeature）给对象的每个子网格都画外壳，跟材质无关** → 换透明材质也会剩一副黑眼镜框。
  工具会：① 拷一份网格资产（`角色_URP/去部件/<角色>_<源>_无glasses.asset`）把该子网格置空
  （**自检：剩余三角形必须为 0**，只读网格上 SetTriangles 会静默失败）；
  ② 把当前场景里对应实例的 `SkinnedMeshRenderer.sharedMesh` 换成它；③ 清掉 `CharRebuild` 里该角色的 `Glasses=`。
- 工具**同时**做两件事：
  · ①**清空子网格**（网格资产 `角色_URP/去部件/<角色>_<源>_无glasses.asset`）+ 把它挂到**所有场景**（Game / 角色资源预览场景 /
    MainMenu）里对应实例上 —— 这步才是真正把眼镜去掉（连描边外壳一起）。
  · ②把 FBX 导入器里那个材质映射成 `隐藏部件_不渲染.mat`（全透明、不写深度）—— 管的是**新拖进来的实例、
    工程窗口缩略图、带动画模型预览渲染**（这些不在场景里，清空网格管不到）。
- ⚠ **网格覆盖是加在实例上的**：以后若重跑「Game角色替换」或新拖 FBX 进场景，**要再跑一次本工具**
  （导入器映射是永久的，所以至少不会显示眼镜本体，只会剩描边外壳）。
- ⚠ 定槽位要用【FBX 文件里的材质名顺序】（`FbxMaterialNames`）或【网格引用】，**不能靠"材质名里有 glasses"** ——
  ① 被映射成隐形材质后就认不出来了；踩过两次（第一次漏掉预览场景，第二次整个模型都认不出）。
- ⚠ 重建网格资产要**原地改**，不要 DeleteAsset + CreateAsset（会换 GUID，把场景里已挂的引用弄断）。
- 菜单：`Tools/干预项目/角色：去掉部件（眼镜）`（触发器 `Assets/_hidepart_trigger.txt`）；报告 `assets/_报告/_去掉部件.txt`。

### Game 场景角色替换（GameCharSwap）  `Assets/Editor/GameCharSwap.cs`

- **做了什么**（2026-09-28 定稿）：把 `Game.unity` 里 52 个角色实例（11 个剧情角色 + 41 个路人）的
  人物模型从旧的 CC_Base 版换成 `带动画模型/<角色>/已绑定.fbx`，并在原地挂该角色自己的 **Idle**（同源、零重定向）。
- **位置 / 层级 / 组件一律不动**：工具只删「实例根下**来自 prefab 的**子物体」（`GetCorrespondingObjectFromSource != null`），
  手动加的东西（如玩家身上那个 `FP_相机`）一律保留；实例根（名字/变换/局部层/胶囊碰撞体/玩家组件）原封不动。
  （所以实例仍然是旧 `<角色>_可动.prefab` 的实例，只是模型被换成新的 —— 想回滚直接 Revert 这个实例即可）
- **路人不是另一套模型**：路人和厨师就是这 9 个角色，只是实例上做了**材质覆盖**（`Materials/路人材质.mat`）。
  覆盖表按【原材质名 → 覆盖材质】记录下来（名字里的 `.` `-` 空格 都当 `_` 比，因为 Unity 导出 FBX 时就这么改的名），
  再映到新模型的同名子网格上；厨师是**部分覆盖**（只改了 9 个材质，眼睛/眉毛留着），工具按名逐槽处理，不会一刀切。
- **Idle 控制器**：`assets/03_动作_Animation/Animators/带动画模型/<角色>_Idle.controller`
  \+ 循环副本 `<角色>_Idle.anim`（源 = 该角色 `带动画模型/<角色>/Idle.fbx` 的 clip，`loopTime=true`）。
  控制器里额外加了旧参数名 `Speed` / `Phone` / `TakePhone`，免得 `FirstPersonController` 写参数时报错。
- ⚠ **Mixamo 导回的 FBX 里没有 Animator 组件**（模型本身不带）→ 工具会自己 `AddComponent<Animator>()` 到新模型根上
  （不加就永远 rest 姿势）。Animator 必须坐在新模型根上：Generic 动画是**按节点路径**回放的，路径相对 Animator 所在节点。
- ⚠ **NPC 根上那个旧 Animator 要停用**（`enabled=false` + controller 置空）：旧骨架已经被删，留着只会跟新 Animator 抢同一副骨架。
- ⚠ **玩家（`Player_徐夏`）**：`FirstPersonController` 的 `firstPersonShadowsOnlyParts` 原来是 `[torso]`，
  新模型是**一整块合并网格**，做不到“只藏躯干” → 工具把它自己模型的渲染器名字整个加进名单（= 第一人称下整身只投影）。
  另外玩家的**移动动画还没重建**：根上的 Animator 现在挂的是 `徐夏_Idle`，走路/跑要等把
  `PC_徐夏_测试.controller` 用新模型自己的 clip（Idle / Walk / Slow Run）重做（新导入里还没有太快跑/慢跑/拿手机）。
- **自检（③）写法有坑**：`EditorApplication.update` 的订阅在**进 Play 模式的那次域重载时会被冲掉**，
  所以自检靠一个状态文件 `额外文件/_gamechkar_smoke.state` + `[InitializeOnLoad]` 在重载后重新接管
  （仓库里 DoorSmokeDriver 那套是等价的思路）。自检不看“normalizedTime 有没有推进”（那只能证明状态机在跑），
  而是直接量 **骨头世界坐标的位移**：52/52 位移 > 0 才算真的在播。
- 验证口径（本次结果）：实例 52/52 在播、空材质槽 0、洋红 0、路人材质槽 401；场景里 `X_已绑定` 模型 52 个、旧模型 0 个。
- ⚠ 编辑模式下角色显示 **rest 姿势（T-pose）**，Play 里才是 Idle，别当成坏了。

### 主角与第三人称（FirstPersonController）

- **主角 = 场景根的 `Player_徐夏`**（`Loc_*` 以外的那个），根上挂 `CharacterController` + `FirstPersonController`
  （+ 已停用的旧 Animator），子物体：`徐夏_已绑定`（新模型）+ `FP_相机`（**手动加的，不是 prefab 件**）。
- **视角是第一/第三称共用一个脚本**：`FirstPersonController.thirdPerson`（勾上=第三人称）。
  切换用 `SetThirdPerson(bool)`（运行时会顺手把相机位置复位），剧情用的 `SetLocked/SetCursorLocked/SnapCameraTo` 接口没变，
  门口传送（`player.transform.position + 0.9m` 探针）也不需要改。
- **操作方式（第三人称，用户 2026-09-28 定稿）：鼠标左/右【只转镜头】，角色不跟着转；WASD 相对镜头移动；
  角色自己转向移动方向（`turnSpeed` 度/秒）**。
  · 实现：`camYaw`（相机水平角）只在 `Look()` 里被鼠标改；身体只在此 `MoveWithInput()` 里被
   `Quaternion.RotateTowards` 转向；`LateUpdate` 用 `camYaw` 摆相机 → 两者天生解耦。
  · `WishDir(ix,iz)`：第三人称 = `Quaternion.Euler(0,camYaw,0) * (ix,0,iz)`；第一人称 = 角色自身方向（老行为）。
  · `MoveWithInput(ix,iz)` 是公开接口（`Move()` 调它）；另有 `useInputOverride / inputOverride` 虚拟输入，
     自检和剧情演出可以直接让它走路（★ 自检千万不要用编辑器 tick 手动调，tick ≠ 游戏帧，会跑不动）。
- ⚠ **相机位置和朝向都不能从“角色位置”反推**（踩过：`rotation = LookRotation(角色位置 - 相机位置)` + 位置带平滑
  → 角色一横移/转身，相机位置滞后一拍，朝向就跟着变，看起来就是“镜头跟着玩家晃”）。
  现在两者**只由 `camYaw / pitch / 距离` 决定**：`cameraPivot.rotation = Quaternion.Euler(pitch+aimDown, camYaw, 0)`，
  与角色无关（`aimDown` 只是“往下看胸口”的固定几何角）。
- **第三人称怎么摆**：`LateUpdate` 里把相机放到「角色 + tpHeight」支点的背后 tpDistance 米，
  `tpBlockMask` 满墙自动拉近（球半径 tpCollisionRadius，最短 tpMinDistance）；
  俯仰用 `tpPitchMin/Max`（**用户定稿：上抬 +40° / 下压 −45°**，正值=镜头抬到头上往下看）；
  `tpFollowSmooth=0`（默认）就是硬跟，调大才有拖尾感。
  · ⚠ **贴地保护**：支点 1.45m + 距离 3.4m 时，压到 −45° 会让镜头到地面下 0.95m →
    `tpKeepAboveGround=true` 会在要入地时**自动缩短吊臂**（最低离地 `tpMinCameraHeight=0.35`），仍对着角色，不钻地。
    想让 −45° 完整用出来：把 `tpHeight` 调高或 `tpDistance` 调小（例：距离 ≤1.55m 就能在离地 0.35m 以上走满 −45°）。
  · ⚠ `tpBlockMask` 默认排除了 **Outline 层（8）**——角色/道具都在那层，不然身后站个 NPC 镜头就会被硬拉近。
  · ⚠ **`CastForCamera` 里不要按法线忽略“朝上/朝下的面”**（踩过）：当时为了不让地板把镜头往下拽加了
    `normal.y>0.7 / <-0.7 就 continue`，结果**天花板也一起被忽略**，镜头直接穿顶。
    地板会挡是正常的——那正是“弹簧臂贴地缩短”，另外还有 `tpKeepAboveGround` 兜底。
  · ⚠ 相机碰撞要能跳过玩家自己：**CharacterController 就在根节点上**，所以射线过滤用 `collider.transform.IsChildOf(玩家)`。
- **模型显示**：`thirdPerson=true` 时 `ApplyFirstPersonParts()` 把所有部件设回 `ShadowCastingMode.On`；
  第一人称才按 `firstPersonShadowsOnlyParts` 藏躯干。
  主角模型整棵子树在 **Outline 层**（跟 NPC 一样有描边），`FP_相机` 仍在 Default。
- **主角动画**：`Animators/带动画模型/徐夏_第三人称.controller`（零重定向，用徐夏自己的剪辑）：
  混合树 `Speed`：0=Idle / 1=Walk / 2=SlowRun，另有 `Phone`=true → Texting（剧情微信段会 `SetBool("Phone")`）。
  · 根上旧 Animator 已停用（旧骨架已删）；`FirstPersonController.animator` 指向**模型上那个** Animator。
  · 本作设计是「**只走不跑**」：`runSpeed == walkSpeed == 1.6`。想开跑把 runSpeed 改 3.2（混合树第 2 档已经在）。
  · `Move()` 写进 Animator 的 Speed 是 `clamp(animV / walkSpeed, 0, 2)`（不再卡在 1），所以有第 2 档就能用上。
  · **起步要快**：`animV = max(实际速度, 输入速度)` —— 动画混合看**输入**（瞬时），不看被 `accel` 平滑过的实际速度，
    否则 Speed 会跟着加速曲线爬上来（~0.2s 才混到 Walk，感觉就是“起步慢”）；位移仍按原速加速。
    `animStartSmooth` 只管“停下/微调”的过渡（默认 0 = 都不要过渡）。
- **自检**：`Tools/干预项目/主角第三人称/② 运行自检`（或丢 `Assets/_player3psmoke_trigger.txt`）→ 报告 `assets/_报告/_主角第三人称自检.txt`。
  实测过的：按 W 走 0.68m 且位移沿相机前方 0.98 / 角色朝向 0.97（自己转向移动方向）；按 D 沿相机右方 0.95；
  动画骨头位移 1.16m；主角在相机视锥内。
  · ⚠ 检测逻辑在**运行时**脚本 `Assets/Scripts/Player/PlayerThirdPersonSmokeDriver.cs`（协程，用真游戏帧），
    编辑器只负责进/退 Play。**关键坑（都踩过）**：
    ① 把检测挂在 `EditorApplication.update` 里跑 → 进 Play 的域重载会把订阅和静态状态全冲掉 → 写到一半就没了；
    ② 进 Play 前把场景弄脏 → Unity 会弹「保存场景?」模态框，自动化直接卡死；
    ③ 自举要“看文件就跑完把文件删了”，否则一次自检会起好几个驱动，互相干扰测量结果；
    ④ 自检要在开局先清 `useInputOverride`/`camYaw`，并临时停掉 `StoryRunner`（剧情开场会锁住玩家），
       否则角色一直在走，镜头/朝向测出来全是错的。
  · 状态全用**文件**记（`额外文件/_player3p_run` / `_player3p_done` / `_player3p_smoke.state`），不用静态变量。

---

## 四、干活约定

1. **改 Unity 资产只用两种方式**：① 在 Unity 编辑器里操作；② 写 `Assets/Editor/*.cs` 工具 + 菜单项批量改。
2. **不要手写/移动 Unity 资产文件**（`.prefab` / `.mat` / `.fbx` / `.png` / `.meta`）；确需移动，请用 Unity 操作，或连 `.meta` 一起移动。
3. **一次性自动化（触发器）**：把触发器文件丢进 `Assets/`，编辑器下次刷新/重编译时会自动跑并删除触发器：
   - `_rebuild_trigger.txt` → 重建 9 个角色 prefab
   - `_preview_trigger.txt` → 渲染 9 张角色预览
   - `_diagmat_trigger.txt` → 材质诊断
   - `_reimport_trigger.txt` / `_fullreimport_trigger.txt` → 强制重导 / 全量重导
   - `_anim_trigger.txt` → 主角动画接入
   - `_scene_trigger.txt` → 搭建剧情主场景 Game.unity（会顺带跑一次后期）
   - `_postfxfull_trigger.txt` → 后期布置 + 渲染每屋一张「原/后」对比预览（一次搞定，推荐）
   - `_postfx_trigger.txt` → 只布置后期；`_postfxpreview_trigger.txt` → 只渲染后期预览
   - `_doors_trigger.txt` → 搭/刷新门口传送；`_doorssmoke_trigger.txt` → 门口传送运行自检
   - `_ch4door_trigger.txt` → 第4/5章门口引导（重建门口 UI + 修剧情点摆位 + 清场景残留）
   - `_travel45_trigger.txt` → 第4/5章跳转改造（interact+fade 锚点/F点 + 林溪_宿舍实例，幂等）→ 报告 `assets/_报告/_第4-5章跳转改造.txt`
   - `_animpreview_trigger.txt` → 角色预览场景：每个角色挂一个动画；`_animpreviewsmoke_trigger.txt` → 动画运行自检
   - `_menu_trigger.txt` → 重建主界面 MainMenu.unity
   - `_camera_trigger.txt` → 场景视图相机复位（视角斜了/跑飞了）
   - `_phonechat_trigger.txt` → 搭建手机聊天UI（第1章微信段）+ 渲染预览
   - `_colliders_trigger.txt` → 给 6 个地点的墙板/家具补碰撞体（防穿模/掉虚空）
   - `_phonech2_trigger.txt` → 手机摆进宿舍+接第2/4章交互（半径1.2m、以手机为判定中心、F 拿起/点开后隐藏、重新武装时重现）
   - `_officedoorflow_trigger.txt` → 办公室进出流程（南墙换门框墙+门外走廊4×4m+锚点重摆+出口交互点，幂等，跑完自删）
     （工具 `Assets/Editor/OfficeDoorFlowSetup.cs`，报告 `assets/_报告/_办公室进出流程.txt`、预览 `预览/场景/办公室门_外|内.png`）
   - `_npcwalk_trigger.txt` → NPC 走路动画接入（扫描带 Walk.fbx 的角色：生成 `<角色>_Walk.anim` 循环副本 + Idle↔Walk 过渡，幂等，跑完自删）
   - `_sitswap_trigger.txt` → 坐姿换模型接线（宿舍/食堂剧情座位；用用户摆好的坐姿模型，幂等）→ 报告 `assets/_报告/_坐姿换模型.txt`
   - `_choicetitle_trigger.txt` → 选择题题干标题改蓝白色 `#CCE3FF`（幂等）→ 报告 `assets/_报告/_选择题标题配色.txt`
   > 触发器依赖"域重载"生效：改一下任意脚本文件、或让 Unity 窗口获得焦点/按 Ctrl+R 即可。
   > ⚠ 编辑器在**后台未聚焦**时不会自动刷新：要么手动 `资产 → 刷新`，要么让窗口获得焦点（2026-10-01 实测：
   >   鼠标点菜单栏（`资产`）→ 点`刷新` 这条路是可靠的；直接向窗口发合成 Ctrl+R 不一定送达）。
4. **出现"洋红 / 空材质"**：先跑 `Tools/干预项目/全量强制重导`（等价 Assets → Reimport All），
   再用 `诊断角色材质` 核对（`_报告/_材质诊断.txt` 里应无 `MATERIAL_NULL`、无 `supported=False`）。
5. **产物流向**：报告 / 清单 / 预览图 → `3D剧情项目/Assets/assets/_报告/`；
   临时脚本、dump、备份、日志、异常堆栈 → `额外文件/`（已 gitignore，不进版本库；
   Editor 工具出错时会把堆栈写到 `额外文件/错误_*.txt`）。
6. **别把垃圾留在项目里**：`3D剧情项目/` 根目录不放日志；`Assets/` 里不放临时 `.txt`、
   用完的触发器；一次性/历史 Editor 脚本用完就移到 `额外文件/历史Editor脚本/`
   （留在 `Assets/Editor/` 会被 Unity 编译，既容易误用也会拖慢导入）。
7. `Library/`、`Temp/`、`Logs/`、`UserSettings/` 都是缓存：别动、别提交。
8. 提交信息用中文（一行标题 + 要点列表）；推送 `git push origin main`（大提交较慢，中断了直接重跑）。

---

## 五、剧情场景后期（ScenePostFx）要点

- **入口**：`Tools/干预项目/场景后期效果/`（① 一键布置 / ② 只刷新资产 / ③ 渲染后期预览 / ④ 关闭）。
  也可丢 `Assets/_postfxfull_trigger.txt`（自动跑 ①＋③，最省事）。
- **资产**：`Assets/URP/后期/Post_全局.asset` + `Post_<地点>.asset`×6；预览图在
  `assets/_报告/预览/场景/后期_*.png`（开后期），成对的 `原始_*.png` 是同一机位的关后期版。
- ⚠ **最大的坑**：`URP_Renderer.asset` 的 `postProcessData` 如果是空的，**URP 会静默跳过所有后期** ——
  相机勾了 Post Processing、Volume 里 Bloom 调多大都没用，不报错也不提示。
  工具每次都会自己检查并填上（只改项目自己的 renderer，不碰 URP 包里的）。
- **当前基调（梦核 / 朦胧，用户明确要的方向）**：
  近处实、越远越蒙 —— 靠 **Gaussian 景深**（6.5m 内完全不动，6.5→24m 逐步化开）+ **雾**（0.065，4%/13%/38%/70%）
  来做纵深；调色保持对比（contrast +10、饱和 +4），**不用“全场变灰”冒充朦胧**（那会把远近糊成一团）。
  · ⚠ 景深别调太猛：3→9m 那版把整张图都糊了，用户反馈“太模糊”。现在 6.5→24m 是合适的。
  + Neutral 色调映射 + Bloom（阈值 0.95 / 强度 0.42 / 散射 0.78）+ LiftGammaGain 抬黑 +0.028
  + 偏品红 + 冷影暖高光 + 胶片颗粒。
  · **四角压暗（Vignette）默认 intensity = 0**：用户明确不要，别再开。
  · **不往场景里加任何自发光物体**：Bloom 靠场景自身的亮部。
- ⚠ **景深只能用 Gaussian，不要改回 Bokeh**：Bokeh 是“以对焦点为中心、前后都糊”，
  模糊度和 |1-对焦/深度| 挂钩 —— 对焦 5m 时 1m 处就直接糊到顶，物理上做不到“近处一大片清晰”。
  Gaussian 只糊 gaussianStart 以后，前面完全不动。两者最大模糊半径都被写死在 shader 里（14px）。
- **抬黑（Lift）**：套件里的衣服本来就深色，而场景只有 2 盏无影平行光 + 环境光，室内一盏灯都没有
  （`Area_灯组` 是空节点）→ 角色暗部会成一团黑。现在靠 `BLACK_LIFT=0.028` + `AMBIENT=0.25` 顶住；
  **不要再把 BLACK_LIFT 调到 0.05 以上**（那是上一版“全场发灰”的根源），要真正解决得给房间加灯。
- **`Assets/Scripts/Visual/DreamyFocus.cs`**：挂在 `FP_相机` 上，把 `hazeStart / hazeEnd`
  同步到运行期 Volume（改的是 `volume.profile` 实例，不会污染 .asset）；
  Play 模式里可以直接拖这两个值看效果，剧情系统也可以调 `SetHaze(start, end)` 把说话的人拉清楚。
- **雾**走 `RenderSettings`（Exp2，浓度 0.065）；角色 Shader 是 URP Lit 模板，**本来就吃雾**。

### 轮廓描边（OutlineFeature）

- **需求**：只给**角色 + 道具**描黑边，墙 / 天花板 / 地板不描；而且描边要贴在物体表面上，
  **不能是“跟随摄像机”的屏幕空间效果**。
- **做法：反向外壳（inverted hull）**，不是屏幕空间边缘检测。
  `Outline.shader` 把顶点沿法线外推 → `Cull Front` 只画背面 → 物体正面已经在不透明阶段画过，
  壳上被挡住的像素 ZTest 失败，只留轮廓外那一圈 = 描边。
  · 是真实几何体 → **贴在物体表面上，视角怎么转都是那一条**；
  · 走正常深度测试 → **被墙挡住的部分自动不描**（不用额外算遮挡）；
  · 蒙皮后的顶点也外推 → 角色动起来描边跟着动。
- **怎么做到“只描角色和道具”**：工具会自动建一个叫 `Outline` 的层，
  把每个 `Loc_*/Content`（道具）与 `Loc_*/第X章角色`（角色）整棵子树放进去；
  Feature 只画这一层。`Shell*`（地板/墙）不动。想多描就把物体 Layer 改成 `Outline`。
- **参数改哪儿：只改 `Assets/URP/后期/描边.mat` 一个地方。**
  Feature 运行期**不会**写这个材质，而是直接把它当 `overrideMaterial` 用，
  所以 Inspector 里拖 `_OutlineWidth / _OutlineColor / _FadeStart / _FadeEnd` 是**立刻生效**的（Play 模式也行）。
  · ⚠ 踩过的坑：之前版本在 `Create()` 里 `new Material(...)` 克隆一份、每帧再用 settings 覆盖参数，
    所以改材质完全没反应 —— 已经改成“材质是唯一参数源”。
  · 跑一次 ① 会把 `ScenePostFx.cs` 顶部的 `OUTLINE_*` 常量写回材质，重置成设计值。
- **线宽**是屏幕像素（`_OutlineWidth`，默认 2.0）：顶点 shader 里用
  `2*dist*tan(fov/2)/屏幕高` 换算成世界距离，所以离得远外壳自动变粗、看起来粗细稳定。
- ⚠ **两个把线弄脏的坑（已修，别再踩）**：
  · **线宽千万不要乘淡出系数** —— 中远距离线宽掉到 1 像素以下就会碎成一串虚点，看着全是噪点。
    远处淡出要交给 **alpha**（`Blend SrcAlpha OneMinusSrcAlpha`），线宽恒定不变。
  · **要加 `Offset -1, -1`** —— 外壳和物体自己的表面会 z-fighting，不定程度地冒噪点/麻点。
- **遮挡/时序**：`RenderPassEvent.AfterRenderingOpaques`，和普通不透明物体同阶段，
  后面还有雾/调色/景深，所以黑边会一起被糊，不会“背景糊但边很锐”。
- **为什么不用屏幕空间边缘检测**（试过，已弃）：那是“画面上的线”，随相机移动而变；
  而且要把墙/天花板一起纳入判断才能算清楚，和“只描角色道具”的需求相冲。
- **参考**：`Assets/Scripts/Visual/SceneOutline.shader` + `OutlineFeature.cs`。

### 角色碰撞体（第 8 节）

- 玩家是 `CharacterController`，要撞不过 NPC，就需要 NPC 身上有碰撞体。
- 工具（`ScenePostFx.cs` 的 `EnsureCharacterColliders`）会给每个 `Loc_*/第X章角色/*` 实例
  加一个 **CapsuleCollider**（共 52 个）：
  · 尺寸由角色的渲染包围盒算：`高 = 包围盒高×0.96`、`半径 = 高×0.16`（**不拿 X 跨度** ——
    角色是 T-pose，手臂张开会算出一个巨大的胶囊）；
  · 放在角色**根节点**上（根节点只有 yaw、scale 1），所以跟着角色动；
  · **不碰 `Player_徐夏`** —— 它挂在 `GameRoot` 下、不在 `第X章角色` 里，而且它自己有 CharacterController。
- 层就是 `Outline` 层，跟其它层默认碰撞，不需要改 Physics 矩阵。

### 场景设施碰撞体（SceneColliders，2026-09-28）

- **背景**：地板是 SceneBuilder 用 `CreatePrimitive(Cube)` 拼的（自带 BoxCollider），但四面墙是
  走廊套件的 FBX 墙板、家具是寝室套件等 FBX prefab——**导入时不带碰撞体** → 玩家能穿过墙板/家具，
  穿出墙就是地板外的虚空（用户反馈"贴墙掉下去"的根因）。
- **工具**：`Assets/Editor/SceneColliders.cs`，菜单 `Tools/干预项目/场景碰撞体/`
  （① 给设施加碰撞体（幂等）/ ② 只诊断），或丢 `_colliders_trigger.txt`。
  报告 `assets/_报告/_场景碰撞体.txt`（每地点统计 + 新增明细 + 编辑器射线验证）。
- **规则**：MeshFilter+Renderer、自己没有任何 Collider、最大边 ≥5cm → 加 **MeshCollider（convex=false，静态）**；
  名字带「角色」的容器整棵跳过（胶囊体归 `EnsureCharacterColliders` 管）；已有 Collider 的一律不动
  （门口触发盒 isTrigger、套件自带碰撞体的件都照旧）。
- **裸边护栏**：某条地板边缘 1.2m 检查带内没有 ≥2m 宽的遮挡（=这条边没墙）→ 自动放无渲染的
  `防坠护栏_方向` BoxCollider。门洞所在的边有墙板覆盖，不会误加；若某扇门扇是敞开模型，
  走出去由掉出世界保护（FirstPersonController y<-20 拉回）兜底。
- ⚠ **跑完会保存 Game.unity**（含你未保存的手改）；NPC 走位是 transform 直移、不受影响；
  家具多在 Outline 层，第三人称镜头 `tpBlockMask` 排除该层 → 新碰撞体不会把镜头拉近。
- **每个地点一套氛围**（走进去平滑换）：本地 Volume 是「覆盖」不是「叠加」，所以 `Moods()` 里写的是绝对值；
  **别把 DepthOfField 放进本地面板**，会盖掉全局的、远近分层就失效。
- 改完记得看报告：`assets/_报告/_后期效果.txt`（布置明细）、`_后期预览.txt`（带亮度 + **近/远两段清晰度**；
  同一机位还会渲一张 `原始_*.png` 作为“关后期”对照。若“清晰度 远”和“关景深时”几乎一样，说明景深没生效）。

### 手机道具（PhonePropSetup，2026-09-28）

- **来源/许可**：Sketchfab `4ff99cf1`「Low Poly Mobile Phone」by kimmy.k，**CC-BY 4.0（★ 发布/商用需在致谢里署名 kimmy.k）**。
  走 asset-search 流程（43 候选 → 用户从 8 个入围里点名 → 详情 API 许可证据 → 下载+SHA256），
  manifest/许可证据在 `Downloads/asset-search/20260928-phone-lowpoly/`（验收后可删，GLB sha256 已抄进 `06_道具_Props/手机/_来源.json`）。
- **格式转换**：项目没有 glTFast/Blender、Unity 2022.3 读不了 GLB → `额外文件/工具脚本/glb2obj_phone.py`
  （纯 stdlib，GLB→OBJ）按材质拆成 **4 个单材质 OBJ**（外壳/屏幕/按键/镜头；拆件是为了绕开 OBJ 导入器材质槽顺序不稳的坑）。
  转换时烘掉两个源坑：整机斜置 45°（FBX 遗留变换）、尺寸是真机 4 倍 → 归一化成 **0.081×0.016×0.155 m、屏幕朝 +Y 落地**。
- **工具**：`Assets/Editor/PhonePropSetup.cs`，菜单 `Tools/干预项目/手机道具/`（① 幂等全做 / ② 只诊断），或丢
  `_phoneprop_trigger.txt`。做：OBJ 关材质导入 → 生成 4 个 URP Lit 材质 → 拼预置体
  `06_道具_Props/手机/手机_淡蓝.prefab`（根+4 子网格，**整棵 Outline 层**吃全局描边；无碰撞体）→
  预览 `assets/_报告/预览/道具/手机_淡蓝.png` + 报告 `assets/_报告/_手机道具.txt`。
  **日常只用 prefab**，4 个 OBJ 是源网格存档；工具重跑是原地覆盖预制体（GUID 不变），场景里已摆的实例不受影响。
- ⚠ **幂等重建别用 DeleteAsset**：材质/预制体都用「同路径原地覆盖」（CreateAsset / SaveAsPrefabAsset，GUID 不变）；
  DeleteAsset 再建会换 GUID，把场景里已摆放的实例断成 missing prefab。
- ⚠ **编辑器的 `cam.Render()` 剔除是全局按层的，不管相机在哪个场景**：首版预览用 Outline+Default 层当剔除，
  把打开着的 Game 场景（角色脚/地板）全拍进了预览图。渲染隔离预览要学 `CharPreview`：用专用层
  （31）+ `cullingMask = 1<<31`，把实例临时挪到该层再拍。

### 门口传送（DoorTravelSystem）

- **需求**：走到**真正的那扇门**前面 → 弹「按 F 开门」→ 按 F 选地点 → 传过去。
- **注意：是传送不是 LoadScene** —— 6 个地点本来就在同一个 `Game.unity` 里（按 `GAP=40m` 并排摆成片场），
  所以"切换场景"= 传送到另一个地点的门口。以后要真换 Unity 场景，改 `DoorTravelSystem.TravelTo()` 一行。
- **门触发盒由人自己放在场景里**（勾了 `Is Trigger` 的 BoxCollider，挂在 `Loc_*` 下面）。
  ⚠ **工具不自己凭空摆位** —— 早期版本按"南墙正中"瞎猜，位置全是错的（真的门在 `Content/门` 之类的地方）。
  工具每次会重新扫一遍，新加的门只要勾了 Is Trigger 就会被识别。
- **资产 / 脚本**：
  · `Assets/Scripts/Game/DoorInteractable.cs`（挂在门触发盒上，只负责报名字 + 给落点）
  · `Assets/Scripts/Game/DoorTravelSystem.cs`（单例：提示 / F 键 / 选择面板 / 传送）
  · `Assets/Scripts/Game/DoorSmokeDriver.cs`（Play 模式自检驱动，平时不动）
  · `Assets/Editor/GameDoorBuilder.cs`（`Tools/干预项目/搭建门口传送`，或丢 `_doors_trigger.txt`）
  · 自检：`Tools/干预项目/门口传送运行自检`（或丢 `_doorssmoke_trigger.txt`）→ `assets/_报告/_门口传送运行自检.txt`
- **自动搭什么**：给每个门触发盒挂 `DoorInteractable` + 按 `Loc_*` 推导地点/标题；
  每间房建一个 `Arrive_<地点>` 落点（摆在触发盒**前方朝屋内 1.4m**）；再加 `UI_门口交互` Canvas（提示 + 选择面板）
  和一个 `EventSystem`（**Game.unity 原本没有 EventSystem，没有它 UI 按钮点不动**）。
- **落点必须放在触发盒外面**：不然传送落地正好落进触发盒里，提示会卡住不消失。
- **目标列表按地点去重**：一间房可能有好几个门（教室 2 个、走廊 2 个），但目标只该出一个。
- **四个容易踩的坑（都已处理，别改回去）**：
  · **判定要用"胸口高度"的探针点，不能用脚底** —— 门触发盒底面往往离地几厘米，
    拿脚底去比会差之毫厘判成"在外"（实际踩过：盒底 `y=0.05`、脚底 `y=0.03`）。
    `DoorTravelSystem.probeHeight`（默认 0.9m）+ `Contains()` 里 5cm 容差。
  · 面板打开时必须同时 `SetLocked(true)` + `SetCursorLocked(false)` + **把 `allowEscToUnlock` 设 false**；
    不然鼠标一解锁、点一下按钮，`FirstPersonController` 会把鼠标重新锁回去，UI 点不动。
  · 传送前要把 `CharacterController.enabled = false`，挪完再开 —— 不然位置会被它拽回去。
  · **写自检别用 `EditorApplication.update` 的 tick 当等待单位** —— tick ≠ 游戏帧，
    "等 6 tick"时游戏可能才跑几帧，扫描还没生效就断言了。
    现在自检跑在 Play 模式的协程里（`DoorSmokeDriver`，用 `yield return null` 等真游戏帧），
    编辑器只负责置标志 → 进 Play → 轮询 `Finished` → 写报告 → 退出。
- 想改门位/触发范围：直接改那个门自己的 BoxCollider；想改显示名/章节：改它上面的 `DoorInteractable`。

#### ★ 剧情导流（`door` 步骤，2026-09-29 第4/5章定稿；⚠ 同日晚些已退役）

> **⚠ 2026-09-29 晚用户定稿**：第4/5章**不再用 door 选择面板导流**，改回前三章的
> **interact→fade**（门口 F 交互 → 黑屏落到指定锚点 `第N章_目的地到达`）。第4/5章 json 里的
> door 步骤已全部替换（第4章还加了林溪 enter/leave 进宿舍演出，实例 `林溪_宿舍` 在
> `Loc_宿舍/第四章角色` 下——★ 必须与图书馆那位 `林溪_可动` 不同名，按名找会抓错）。
> door 机制本身保留在引擎（`StoryRunner.DoDoor`）备用。场景侧由
> `Tools/干预项目/第4-5章跳转改造（幂等）`（`Chapter45TravelSetup.cs`，或丢 `_travel45_trigger.txt`）
> 搭建，报告 `assets/_报告/_第4-5章跳转改造.txt`。下面这段 door 机制文档保留作参考。

- **需求（用户 2026-09-29）**：剧情要换地点时**不能自己瞬移过去**——
  ① 「场景不应该是自己直接跳转，而是引导玩家去门口选择跳转」→ 亮目标卡把玩家引到门口，自己按 F 选地点；
  ② 「跳转也应该在另一个场景的门口，让玩家自己去寻找任务触发点」→ **落点就是那个地点的门口**
  （门自己的 `arrivePoint`，不在屋内深处），进去以后玩家自己走到触发点按 F 才继续剧情。
- **json**：`{ "t": "door", "to": "Loc_图书馆", "x": "走到门口，按 F 前往图书馆" }`
  （`to` = 目的地点的 locationId；`x` = 左上角目标卡文案；
  可选 `anchor` = 落点改写为某个锚点——**平时别用**，除非真要让剧情落点不是门口）
- **运行时**：`StoryRunner.DoDoor()` → `DoorTravelSystem.EnterStoryMode(to, anchor, 回调)` + 打开门系统
  （**平时剧情是把它整体关掉的**）→ `State.WaitDoor`（能走能转，左上角目标卡常驻）→ 玩家走到任意一个门按 F →
  面板里**只有目标地点可点**（其余置灰并标「本章不去」，目标标「本章前往」）→ 点它即传送
  → `TravelTo()` 回调 `StoryRunner.OnDoorArrived()` → 关掉门系统 → 继续剧情（= 紧跟着的 `interact` 步骤
  就是玩家要自己找的触发点）。
  · 为什么只放行一个地点：全放行的话玩家点食堂/教室，剧情就断了（或得写一堆补丁）。
  · `ToDone()`（章节跑完）会把门系统**重新打开**，不然出了剧情也开不了门（以前就是永远关着）。
  · 自检/调试：`StoryRunner.DebugAdvance()` 在 `WaitDoor` 调 `DoorTravelSystem.DebugTravelToStoryGoal()`，
    走的是和玩家完全同一条 `TravelTo()`。
  · 步骤分布（2026-09-29）：第4章 55→59（门口→图书馆 + 林溪旁边按 F）；
    第5章 27→28（宿舍门口→图书馆 + 找个凳子坐下）、30→35（图书馆门口→宿舍 + 自己的座位）、
    82→83（宿舍门口→食堂 + 找个凳子坐下）。第5章原来的 27 `walk` 已并入 door（Touch 盒可能被玩家绕开→卡住）。
- ★ **落点就是门口**：到达后玩家站在 `Arrive_<地点>`（触发盒前方朝屋内 1.4m，自动算的），
  不会直接落到屋里深处的座位上——所以每个“跳过去”的目的地都必须在附近放一个 `interact` 触发点，
  否则玩家进去后不知道要做什么（第4章：`第4章_图书馆座位`（林溪旁边那把椅子，radius 3）；
  第5章：`第5章_图书馆躲避`、`第5章_回座位`（复用/Revive）、`第5章_食堂座位`（「任意凳子」模式，见下）。）
- ★ **交互 UI 统一：门口提示就是剧情那套 `UI交互/交互提示`**（用户 2026-09-29：「玩家的交互 UI 要统一用那个已有的 UI……
  不要 UI 白色的这个 UI，而且跟我们的 UI 重叠了」）。
  `GameDoorBuilder.BuildUI()` 现在直接找 `交互提示`（F_交互.png 蓝气泡）当 `promptRoot/promptLabel`，
  **不再新建白底提示**（旧版的 `提示_按F` 用的是已不存在的贴图名 `面板_暗`/`按钮_次_普通` → Sprite=null
  → 画面上就是一个纯白方块，还跟“按 F 交谈”叠在同一个位置）。
  面板本身也统一到现有 UI 词汇：`卡片=面板_亮`、`目标行=按钮_次_悬停`、`关闭=按钮_图标_悬停`（名字都要用磁盘上真有的贴图）。
  预览图：`assets/_报告/预览/场景/门口面板_剧情导流.png`（临时工具已归档 `额外文件/历史Editor脚本/_DoorUIPreview.cs`）。
- ⚠ **门口 UI 曾经整个丢了**（`DoorTravelSystem.panel/listRoot/closeButton` 都是空 —— `UI_门口交互` 画布在某轮
  UI 整理时被删掉），症状是「按 F 只弹提示、面板不出现」，而且**平时剧情把门系统整体关掉、根本没人发现**。
  现在由 `Tools/干预项目/第4/5章门口引导`（`Assets/Editor/Chapter4DoorTrip.cs`）检测缺件/提示没统一就自动跑 `GameDoorBuilder` 重建。
- ⚠ **目标列表要在 `Awake()` 里建，不能等 `Start()`**：剧情 runner 的 `Begin()`（也在 Start）会把门系统 `enabled=false`，
  谁先跑不确定；一旦被先关掉，`Start()` 永远不跑 → `Destinations` 空着 → 开门面板一个按钮都没有
  （2026-09-29 自检抳到：“门触发盒 8 个 / 去重后目标地点 0 个”，时序时好时坏）。
  现在 `Awake()` 里 `RebuildDestinations()+BuildButtonLists()`，`OpenPanel()` 也再兜一道。
- ⚠ **剧情触发盒不是门**：`GameDoorBuilder.FindSceneDoorTriggers()` 现在会跳过带 `StoryInteractable` 的触发盒
  （踩过：第5章_宿舍门口 的走动 Touch 盒、教室 `BumpPoint`（张知远撞人）都被误挂上 `DoorInteractable`，
  站在那儿会弹「按 F 开门」）。已经误挂的由 `Chapter4DoorTrip` 清掉。
- ⚠ **别把 `~PlayerThirdPersonSmoke` 存进场景**：它是主角第三人称自检的**运行时自举**驱动（看到 `_player3p_run` 才建），
  落盘后**每次 Play 都会自己跑一遍自检、用虚拟输入挪玩家/镜头**（踩过：场景里残留了 2 个）。
  `Chapter4DoorTrip` 会顺手清掉。
- ⚠ **门口传送自检先停剧情**：选中的那一章 runner 会在 `Start` 里自动 `Begin()` 并把门系统关掉，
  自检就会假失败（`DoorSmokeDriver` 现在先 `r.enabled=false` + `sys.enabled=true`，只改运行期状态、不落盘）。

### 角色动画预览（AnimPreviewSetup）

- **场景**：`Assets/Scenes/角色资源预览场景.unity`（原名 `Test_徐夏_动画.unity`）——
  9 个角色 prefab 实例排成一排 + 相机 + 太阳。
- **做法**：`Assets/Editor/AnimPreviewSetup.cs`，菜单
  `Tools/干预项目/角色预览场景：每个角色挂一个动画`（或丢 `_animpreview_trigger.txt`）。
  9 个动画 ↔ 9 个角色按名字排序一一对应（多于角色就轮流），报告 `assets/_报告/_角色动画预览.txt`。
- ⚠ **一个角色身上有两个 Animator**：prefab 根一个、模型内部 `Base_F_body`/`Base_M_body` 一个。
  只有**离 `SkinnedMeshRenderer` 最近的那个**才真的在驱动身体。
  所以工具是从每个蒙皮网格往上找最近的 Animator 当"驱动者"，只给它换 Controller
  —— 一开始按"场景里所有 Animator"配，结果 9 个角色配成了 18 份、标签也翻倍，已改。
- **不动原动画 FBX 的导入设置**：`拿手机` / `Walk` 这类一次性动作单独做 `loopTime=true` 的 `.anim`
  副本（`assets/03_动作_Animation/预览Clip/`）给预览用。
  直接改 FBX 的 loopTime 会让游戏里的"拿手机"变成循环 —— 别那么干。
- 角色/动画 FBX 都是 `animationType: 3`（Humanoid），所以动作会自动重定向到各自身形上。
  Animator 的 Avatar 是 prefab 自带的，工具不碰。
- 头顶标签是场景里的 `标签_<动画名>` 节点（`TextMesh` + 中文字体）；设 `ADD_LABEL=false` 再跑一次就没有。
- **自检**：`Tools/干预项目/角色预览场景：动画运行自检`（或 `_animpreviewsmoke_trigger.txt`）——
  真进 Play 采样 `normalizedTime` 看有没有推进，能抓出"挂了 controller 但没在播"
  （没 Avatar / Avatar 不是 Humanoid / state 的 Motion 为空）。报告 `assets/_报告/_角色动画预览自检.txt`。

### 第一章剧情（Chapter1Story）

- **搭建工具已归档**（2026-09-23 用户定稿）：`Chapter1StoryBuilder`（连同它的自动触发器）已移到
  `额外文件/历史Editor脚本/`。场景里的接线/UI 已序列化落盘，**不依赖工具**，游戏照跑。
  需要重建时：把文件拷回 `Assets/Editor/` → 手动跑菜单 → **用完再移走**。
  ⚠ **不要再用 `Assets/_xxx_trigger.txt` 自动触发器**：它会在刷新/重载时自己跑，把用户在场景里的手改覆盖掉
  （踩过：用户手调的对话 UI 被重跑写回）。
- **UI 挂在用户手搭的画布 `UI交互` 下**（sortingOrder 100）：
  · `对话`（子：`遮罩` + `对话框` → `名字` / `对话内容`）——底部对话框；
  · `交互提示`（子：`键位`(F) / `文字`）——“按 F 交谈”提示（**复用它，不另建 TalkPrompt**）。
  · ⚠ **这两个节点在场景里默认 `SetActive(false)` 是约定，不是遗漏**：运行时由 `DialogueUI.ShowNode()` / `StoryRunner` 按需启用。
  · 工具**不动任何布局数值**，只做四件事：挂脚本、**把 Text 字体换成 `中文_Deng`**（用户节点默认是 Unity 内置 Arial，无中文字形 → 字全是空白）、补 CanvasGroup、补 `Indicator(▼)`。
- **运行时节点**：`StorySystem`（场景根，挂 `StoryRunner` + `StorySmokeDriver`）、`Loc_教室/StoryStart|TalkPoint|BumpPoint`。
- **交互点 `StoryInteractable`**：`InteractF`（组长：进半径出提示、按 F 触发）与 `Touch`（张知远撞人）。
  · ⚠ `armed` 由 runner 控制：**只有走到对应等待步骤才允许触发**。不加这个开关，开场旁白段（自由走动）路过组长按一下 F 就会把交互点消费掉，之后按 F 没反应。
- **三语态**：`dlg` 名牌 + 深色；`nar` 无名牌 + 中灰；`mon` 无名牌 + 浅蓝灰斜体（都走同一个 `对话` 框）。
- **数据**：`Assets/数据/剧情/第1章.json`（117 步：card/nar/dlg/mon/interact/walk/choice/end）。
- **自检**：`Tools/干预项目/第一章剧情运行自检`（`PhoneChatSmoke.cs`，真进 Play 跑完 117 步；或丢
  `_story1smoke_trigger.txt`）→ `assets/_报告/_第一章剧情运行自检.txt`。
  （旧入口在归档的 Chapter1StoryBuilder 里，已与现在的 ChoicePanel API 脱节，别再拷回来用。）
- ★ **用户定稿（2026-09-23）：「对话框 / 名字 / 对话内容」的大小·相对位置·颜色一律不准再改。**
  工具对这三个节点只做**只读快照**（`LayoutDialogue` → 报告里列当前值），**一个字都不写回**；
  字体也只在“真的没有中文字形”时才补（`font.HasCharacter('中')` 判断）。
  运行时的正文颜色由三语态（dlg/nar/mon）切换 —— 那是演出需求，不是工具覆盖。
- ⚠ **`BlackFade` 必须启用**（`StoryRunner.Begin()` 现在会自己 `SetActive(true)` 兜底）：
  它在场景里被禁用时**所有黑幕都不生效**，于是"开场先黑住再瞬移"这招白做 ——
  玩家会看到「瞬移 4.4m + 镜头第一帧摆位」露在画面里（低帧率下被拆成几段，看着就像"角色自己向右向前挪了几下"）。
  实测口径：开局探针 150 帧里**玩家根位移 0.000m**、落点就是 `Loc_教室/StoryStart`（4.23, 0.03, −4.05）；
  黑幕能在运行时被找到（= 已启用）才算对。
- `遮罩`（全屏黑 68%）已从「对话」下提到 `UI交互` 最前（= UI 最底层，才铺得满全屏、也压不到对话条）。
  **运行时按语态启用：`dlg` 对话 / `mon` 内心独白 → 开；`nar` 旁白 → 关**（`DialogueUI.SetDim`）。场景里默认禁用。
- `BlackFade` 与 `遮罩` 职责不同、**层级不能互换**：
  · `BlackFade` = 章节转场黑幕（不透明，**最上层**，开场淡出 / 章末淡入，`StoryRunner` 驱动）；
  · `遮罩` = 对话压暗层（半透明 68%，**最底层**，按语态开关）。
- **干预选择题（ChoicePanel，2026-09-27 定稿「按钮⇄解释」互斥）**：
  · ★ **题干标题 = 蓝白色 #CCE3FF**（用户 2026-10-01）：面板背景透明、题干直接压在深色 Dim 上，
    原来的深蓝灰 `#293342` 几乎看不见 → 改成蓝白。改色工具 `Tools/干预项目/选择题：题干标题改蓝白色`
    （`Assets/Editor/ChoiceTitleColor.cs`，幂等；触发器 `_choicetitle_trigger.txt`）→ 报告 `assets/_报告/_选择题标题配色.txt`。
    只改 `ChoicePanel.titleLabel`（选项行/确认按钮配色不动）。
  · 全选流程：一题内所有选项各选一遍才能交卷；选择顺序存 `story.choice.<题号>`（第1章 4 题 = 0~3）。
  · 交互闭环：点选项 → 该行**只变灰**（Button ColorTint 的 disabled 态，★不换贴图，`rowSelected` 字段已删）
    → 进解释态：选项行 + Confirm + **面板自己的 Dim** 全部临时隐藏，解释借**底部对话框**播 mon 独白
    （`DialogueUI.PlayLine(new StoryStep{t="mon", x=解释})`，对话框 UI 零改动）→ **点任意处**（运行时懒建的
    全屏透明捕获层「解释点击层」接住，不落盘）→ 行/Confirm/Dim 恢复（已选行保持灰）→ 循环；
    全选后「记入焦虑记录本」点亮 → 交卷回剧情。
  · ★ **解释期必须藏面板 Dim**：ChoicePanel 子树在 `对话` 之后渲染，Dim 不藏会把解释文字压暗
    （踩过：解释发灰就是它）。藏掉后解释直接坐在最底层 `遮罩` 之上，清晰可读。
  · ★ **选项行是场景预置**（`ChoicePanel.rows` 序列化引用，最多 3 行 `选项_0/1/2`，挂 `List/View/Content` 下）：
    用户可在 Scene 视图直接手调贴图/字号/颜色；运行时**绝不 Destroy、绝不覆盖手调样式**（只写文字、
    开关行、禁按钮）。行的生成/接线/清理用 `Tools/干预项目/生成选择题按钮行（预置3行 / 强制重建 /
    选择题行去掉解释块）`，报告 `assets/_报告/_选择题按钮行.txt`。
  · 选择题期间剧情零推进：`State.Choice` 下 StoryRunner 点击 switch 无分支、`OnLineTyped` 只在 Typing 态动作
    → 解释播/收都不会误触发推进。
- **手机聊天 UI（PhoneChatUI，2026-09-27）**：微信段（徐夏 ↔ 林溪）的展示层，素材 = 用户设计稿
  `assets/05_UI/手机_Phone/`（8 张图：图层 2 机身 / 图层 5 白泡·尾巴左 / 图层 7 蓝泡·尾巴右 /
  图层 6、8 头像 / 1、2、3.png 贴纸）。
  · ★ **铁律：素材样式与比例一律不改** —— 机身只等比缩放（根 localScale 0.9，屏幕正中），
    内部按素材原生像素摆；气泡 9-slice 的 border 按像素测量写死（尾巴整体包进"上"角块 →
    拉伸只发生在纯色中段，永不变形）；贴纸 1:1 显示；贴图只改 Sprite 导入方式，像素零改动。
  · ★ **显示规则 v5（用户定稿）**：微信段"二选一"——（微信）台词只落在手机 UI（对话框不出现，
    打字机隐形跑维持节奏，气泡即时报）；旁白/独白走对话框，此时手机暂时收起；点完再遇微信台词
    → 对话框让位、手机回屏。干预面板出现时手机也收起（选项与手机不同屏）。
    `[比心]` 整句 → 1.png 贴纸。
  · ★ **气泡内文字一律左对齐**（2026-09-29 用户反馈「文字没有靠左」）：发出方（右侧蓝泡）此前用
    `MiddleRight`，多行时后几行被推到右边、换行处还会在行首留一大段空白，看着像整段没对齐。
    现在两侧统一 `TextAnchor.MiddleLeft`（跟微信一样：文字贴着气泡左边起排）；气泡自身的左右位置不变。
    自查：第4章「学姐」那段（= 用户截图的同一段）的对照图在 `预览/场景/手机聊天_第4章对齐检查.png`；
    重渲的办法：临时把 `额外文件/历史Editor脚本/_PhoneAlignPreview.cs` 拷回 `Assets/Editor/`，
    丢个 `Assets/_phonealign_trigger.txt` 触发即可（用完再移回去）。
  · 搭建：`Tools/干预项目/搭建手机聊天UI`（或 `_phonechat_trigger.txt`）→ 在 `UI交互` 下建唯一节点
    `手机聊天`（siblingIndex=1：遮罩之上、对话/选择题之下，默认禁用），接线 + 存场景 +
    报告 `assets/_报告/_手机聊天UI.txt` + 预览 `预览/场景/手机聊天UI_预览.png`。
  · CanvasGroup.blocksRaycasts=false：点击穿透手机，不打断"点击推进剧情"。
- **任务栏 WalkHint（2026-09-27 定稿「左上角目标卡」）**：走动段的目标提示条（如「走到教室门口」），
  `UI交互/WalkHint`，默认禁用、由 StoryRunner 按需亮起。
  · 样式：距屏上/左各 48px 的 430×72 玻璃卡（`面板_玻璃.png` 九宫格，白×0.82）+ 左缘 5×36 淡蓝竖条
  （#4C9FE8，MainMenuAssets.ACCENT）+ 左对齐 26 号深蓝灰文字。**别再用 `F_交互.png` 当底图**——
  那是键帽整图且 border=0，Sliced 拉伸必压扁（旧版任务栏难看的根源）。
  · 运行时（`StoryRunner.ShowWalkHint/HideWalkHint`）：无「→ 」前缀；0.35s 淡入 → 常驻 4s →
  收到 55% 透明度；隐藏 0.3s 淡出。重复触发重置计时。
  · 改样式跑 `Tools/干预项目/任务栏样式/改为左上角目标卡`（`Assets/Editor/WalkHintRestyle.cs`，
  幂等、只写 WalkHint 子树、会覆盖该节点手调值）→ 报告 `assets/_报告/_任务栏改造.txt` +
  预览 `预览/任务栏_目标卡.png`。

### 多章剧情（第 2–5 章，2026-09-27）

- **结构**：`StorySystem` 下每章一个 runner 子节点（`第2章`~`第5章`；第1章 runner 仍在根上，原样不动）：
  各绑 `Assets/数据/剧情/第N章.json` + `chapterIndex=N` + 各自 startAnchor/fadeAnchors，
  **11 个 UI 引用全部指向 `UI交互` 画布下同一套节点**（工具从第1章 runner 复制）——多章零新建 UI。
  主菜单选章写 `GameProgress.SelectedChapter`，只有匹配章的 runner `Begin()`，其余静默。
- **数据**：`第2章.json`~`第5章.json`（79/104/79/92 步）。★ 正文体以
  `项目文档/剧本更新.docx`（提取稿 `额外文件/剧本更新_extract.txt`）为唯一权威源，旧版剧本只作对照。
  步骤类型在第1章八种之外新增 `fade`（`to`=锚点名）；微信台词 `s` 带「（微信）」，
  班群通知 `s="班群（通知）"` 同样落手机 UI。
- **角色容器按章显隐**：Begin 时只保留 `第N章角色` 容器（名字兼容汉字「第三章角色」与数字
  「第3章角色」两种写法），其它章的容器整棵隐藏——同一 Loc 摆了多章容器（宿舍有第二/五章
  两套），不过滤会互相穿帮。`enter` 的 `who` 查找同样限定本章容器（同名实例多章都有，
  如宿舍有两个 陆宣雨_可动）。
- **fade（黑屏转场）**：黑幕淡入 → 传送玩家到 `fadeAnchors` 里与 `to` 同名的锚点 → 淡出。
  用于更新版剧本的「黑屏/刷新」跳转（第3章进办公室、第5章宿舍↔图书馆等）。
  ⚠ BlackFade 在 UI 最上层，黑屏期间对话框不可见 → 时间流逝旁白用独立 nar 步骤（放 fade 前后）。
  ★ **`"showChars": true`（2026-09-30）**：落地时点亮【落点锚点所在 `Loc_*`】下的本章角色容器
  （`Begin` 时 `PreHideDeferredChars` 先预藏，黑屏期间 `SetCharContainersAt` 点亮 → 淡出时人已在座不穿帮；
  `SilentApply` 续播同路径）。用于「剧中才出现」的角色：`Loc_食堂/第五章角色`（陆宣雨+舍友A/B 坐着）
  只在第5章到食堂那幕出现——宿舍/图书馆的第五章容器不带此标记，`Begin` 全地点显示不受影响。
- **cut（切视角，2026-09-30 第3章）**：`{ "t": "cut", "who": "李老师_可动" }` = 镜头切成该角色的
  **第三人称跟拍视角**（`CutawayCamera`：角色身后 2.6m/高 1.55 LookAt 头部，持续跟随，开 URP 后期与主相机同质感）；
  `{"t":"cut"}`（who 空）= 切回主角相机。**主角原地不动、不传送**——cutaway 期间 FPC 整个停用（对话本就锁行走），
  Show 会关 `FP_相机` 防双渲染/双 AudioListener。cut 是瞬时状态翻转，紧跟的 dlg/nar 承担时长；
  `SilentApply` 快进直接 Restore；`ToDone` 兜底恢复。第3章用法：食堂听完干预题后切到李老师办公室
  看她说"到时间了……"那段，说完切回，主角还在座位上。
- **door（门口传送导流，2026-09-29 第4/5章）**：`{ "t":"door", "to":"Loc_图书馆",
  "x":"走到门口，按 F 前往图书馆" }` —— 不再由剧情自己瞬移，而是亮目标卡把玩家引到门口、
  让他自己按 F 选地点（面板里只有 `to` 可点），**落点就是那个地点的门口**，
  跟着的 `interact` 步骤才是“玩家自己找”的触发点。机制详见§五「门口传送 / 剧情导流」。
  第4章第 55 步、第5章第 27/30/82 步都已从 `fade` 换成它（第5章 27 的 `walk` 并入 door）。
  ⚠ **2026-09-29 晚已全部换回 interact→fade**（用户改主意，见§五导流小节顶部的退役说明）；
  现在的写法：`{ "t":"interact","x":"走到门口，前往X","at":"第N章_去X" }` + `{ "t":"fade","to":"第N章_X到达" }`。
- **交互点章节归属**：`StoryInteractable.chapterTag`（默认1）；`FindFree()` 只取本章未消费的点。
  `promptText` 非空时 F 提示整句显示它（如「拿起手机」），空则默认「与<displayName>交谈」。
  ★ **任意座位模式 `anySeat`（第5章「找个凳子坐下」）**：判定不看节点自身位置，而是「玩家【碰到】了
  【本节点所在 `Loc_*`】里的任意一把凳子/椅子」（凳子名单按 Loc 缓存；跨地点不算，宿舍坐着不能过关）。
  · **距离要用凳子自身的碰撞体/渲染包围盒算表面距离**（`Collider.ClosestPoint` → `Renderer.bounds.ClosestPoint`），
    **不能用节点原点**：这批 `凳子2` 的**节点位置和它真正可见的网格差了近 3m**（例：
    `凳子2 (50)` 节点在 (202.8,1.0,−0.2)、渲染包围盒中心在 (199.8,0.6,−0.5)）——按原点判会
    「大多数凳子不触发、却在凳子旁边 3m 的空地上触发」（2026-09-29 逐凳探针实测：按原点只有 8/34 触发）。
  · `radius` = 允许离**凳子表面**的间隙（玩家胶囊半径 0.28 → 0.3 = 必须实贴凳子，0.4 = 贴住再留 0.12m）；
  · ★ **`seatObjectName` 限定单凳（2026-09-30）**：非空时 anySeat 只认名字等于它的那一把
    （第5章食堂 `第5章_食堂座位` = `凳子2 (24)`，舍友们坐的那桌——之前食堂 45 把凳子全都能触发）。
    找不到该凳子只警告一次，交互永远到不了位；节点原点无关（过滤后仍按表面距离算）。
- ★ **interact 必须点名（`at` 字段，2026-09-28）**：一章多个 F 交互点时 json 里 `"at": "交互点GameObject名"`，
  FindFree 按名精确匹配。不点名 = 取"第一个找到的未消费点"，**会武装错点**（试玩实测：第3章点餐
  步骤武装了座位点，走到窗口没提示；自检 Fire() 不看位置验不出来）。同名点已全部消费 →
  `StoryInteractable.Revive()` 复用（第2章两次拿手机；⚠ 不能叫 `Reset()`，撞编辑器消息）；
  点名没匹配 → 警告后回退旧行为（第1章 json 不带 at，走旧逻辑）。第2-5章 interact 已全部补 at。
- **NPC 入场（enter 步骤，第2章陆宣雨）**：`{ "t": "enter", "who": "角色实例名", "from": "门口锚点名" }`。
  Begin 时按 json 预禁用 who（开场不在场）；enter 时启用并从 from 锚点走到玩家面前
  （缺省落点=玩家面前 1.3m，`to` 显式锚点可覆盖），播行走动画、到位面向玩家接对话。
  ★ **enter 落点尽量给显式 `to`（站位锚点）**：缺省「玩家面前1.3m」是按门→玩家方向硬算的、
  不看碰撞，会落进家具（2026-09-29 第4章林溪落进书桌边椅子里穿模，试玩截图实锤）。
  现有：`第4章_林溪站位` / `第2章_陆宣雨站位`——由「第4-5章跳转改造」工具在「路线拐点→书桌」
  走廊上用 `Physics.CheckCapsule` 自动探测净空生成默认位（★ 两人**各探一个点、分开站**；
  ⚠ 探测胶囊底心必须抬过地板碰撞体顶面，否则每个候选点都"撞地板"→全走廊无净空——
  第一版踩过，两人因此一起退到路线拐点变成同点，被用户打回）。
  镜头 0.3s 平滑转向门口（`FirstPersonController.LookTowardRoutine`，yaw+pitch 一起动）。
  ★ 全程真实形象——虚实渐变（幽灵层/透明材质）已于 2026-09-28 按用户要求整体移除。
  锚点解析约定：接线池没有就全场景按名找——把「第2章_陆宣雨门口」拖到任意门前即生效。
  ★ 走路动画怎么来的（2026-09-29）：NPC 走位靠 `NpcEntrance.SetWalk` 写 Animator 的
  `Speed`（0.65/0），消费它的是 `<角色>_Idle.controller` 里的 Idle↔Walk 双向过渡
  （Speed>0.5 切换，工具 `Tools/干预项目/NPC走路动画/接入` 生成循环副本+接线，报告
  `_NPC走路接入.txt`；有 `Walk.fbx` 的角色自动处理，GameCharSwap 重跑不冲掉）。
  ⚠ **_anim 必须挑"活着的"Animator（NpcEntrance v6 修复）**：GameCharSwap 在 NPC 实例根上
  留了个停用的旧 Animator（controller=null），`GetComponentInChildren<Animator>()` 从根
  深度优先先拿到它 → Speed 写进死组件 → 走位全程播待机（试玩"陆宣雨飘进来"的根因）。
  要过滤 `enabled && runtimeAnimatorController != null`。参数缺失现在会报警一次，不再静默。
  ⚠ 入场自检用 `_entrance.Skip()` 快进，验证不了走路动画——要验走路得真看（或量骨头位移）。
- **NPC 退场（leave 步骤，2026-09-28）**：`{ "t": "leave", "who": "角色实例名", "to": "座位锚点名" }`——从当前位置走回
  落点（`to` 显式锚点优先，同名解析同 fade；缺省=Begin 快照的场景手摆 pos/yaw），到位回原朝向、
  保持在场待机。状态复用 State.Enter（能转不能走），自检快进同 enter（`_entrance.Skip()`）。
  ★ **`hide:true` = 到位直接整棵隐藏**（2026-09-29 第4章林溪「一起去图书馆」）：走到门口即隐藏，
  **不转身、不在门口待机**（用户明确：不要先转身再隐藏）；续播 SilentApply 同步处理。
  第2章陆宣雨对话完"回到自己的座位上"用它——演完站桩在玩家旁边不是正常游戏表现。
  ⚠ **第2章陆宣雨的场景摆位就在门口锚点旁（实测差 0.3m）**——"开场原位"对她是门口不是座位，
  所以 json 的 leave 带了 `"to": "第2章_陆宣雨座位"`：场景里放一个该名空物体（摆到她的桌椅旁）即生效；
  不放该锚点则回落到摆位快照（想用摆位方案：直接把她的实例拖到座位旁即可）。
  ★ **第5章舍友三人组出门（2026-09-30）**：json 84-86 步三个 leave（`hide:true`）= 陆宣雨/舍友A/舍友B
  从座位走【各自】的门口点消失（"他们先去食堂"），演完才轮到玩家的 第5章_去食堂 交互。
  ⚠ **路线/门口锚点每人独立、不共用**（用户定稿）：`第5章_陆宣雨|舍友A|舍友B_路线_1` +
  `…_门口` 共 6 个，都在 `Loc_宿舍/多章锚点/` 下，手拖各自生效。
  ⚠ **坐姿 NPC 起身**：`NpcEntrance.SetWalk(true)` 会清 `Sitting`（陆宣雨_可动_坐着 是 SitHere 坐姿，
  不清会坐着滑走）；舍友A/B 无自身 Walk.fbx，`NpcWalkSetup` 会借陆宣雨的同骨架 Walk 剪辑生成
  各自循环副本并接线（同源骨架 Humanoid 重定向）。
- ★ **走位绕路（via 经由点，2026-09-29）**：`enter`/`leave` 可带 `"via": ["锚点名", …]` 分段走
  （`NpcEntrance.RunPath`：逐段直线匀速；★ v8 起每段【先原地转身到位再起步】、段内朝向锁死，
  到位面向玩家是原地平滑转身——用户录屏反馈"过拐点斜着走"，旧的边走边转+瞬切已废；
  起点=当前位置，即退场是从**主角交谈点**出发不是门口）。锚点同名解析同 fade（接线池没有就全场景按名找），
  **缺锚点=警告一次 + 退化直线**（旧行为，不会报错）。
  ★ **林溪有【自己的】拐点 `第4章_林溪路线_1` + 站位 `第4章_林溪站位`**（用户 2026-09-29 定稿：
  两人拐点、站位都要分开，各自手拖互不影响）——由「第4-5章跳转改造」工具生成默认位
  （拐点=陆宣雨拐点旁横向偏移；站位=各自拐点→书桌走廊上物理探测净空）。不够绕再放
  `第4章_林溪路线_2` 加进 via。
  （⚠ 为什么不上 NavMesh：项目故意不上——剧情演出路线要可控可手调（本节上方有专题说明），
  NPC 走位是 transform 直移不经物理，桌椅碰撞体只拦玩家，绕行靠 via。）
  场景路线点用 `Tools/干预项目/多章剧情/补第2章陆宣雨路线锚点（幂等）` 建
  （`Loc_宿舍/多章锚点/第2章_陆宣雨路线_1`，缺省过道估计位 world (77.6,0,0.8)，**已存在绝不改位置**，
  用户在 Scene 里手拖生效）；第2章退场已配该点（用户 2026-09-29 定稿：一个拐弯就够）——路线不合适就拖它；
  要再加拐点，json via 列表里加 `第2章_陆宣雨路线_2`…（场景放同名空物体即可；缺锚点只警告一次、
  退场跳过该点退化少一段，不报错）。
  ⚠ **穿模根因别再往碰撞体上找**：NPC 走位是 transform 直移、不经物理（走位中还会主动关自身碰撞体），
  桌椅的碰撞体只拦玩家；项目无 NavMesh（剧情演出路线要可控可手调，故意不上）。
- ★ **章末卡显示（2026-09-28）**：`EndRoutine` 显示「第N章 完」卡前运行时 `SetAsLastSibling()` 置顶——
  BlackFade 是全 UI 最上层的设计约定不能动，而章节卡排它下面，不置顶就被纯黑盖住（试玩实测
  "章末只有黑屏"）。置顶后随 EndCard → 主菜单离场，无需还原。
- ★ **走动自动收手机（2026-09-28）**：`FirstPersonController.MoveWithInput` 有输入即清 `Phone`
  （低头漂移修复：Texting 出口只看 Phone 不看 Speed，此前没有任何路径清它）。微信段全锁不动、
  不受影响；按 F 触发「拿起手机」类提示的瞬间 StoryRunner 会补拿（`OnInteractableFired`）。
- **出生点按名约定**：场景里放一个名为「第N章起点」（汉字章号）的物体即为该章出生点
  （用户手放优先于工具接线的 startAnchor）。第2章用户已自放「第二章起点」。
- ★ **剧情点要摆到「对应角色/家具」旁边，不能停在工具默认位**（2026-09-29 用户反馈：
  「触发对话点也应该在对应的角色附近才行」）。已修：
  · `第4章_图书馆座位` = 图书馆里 **林溪旁边那把椅子**（200.23, 0, −6.56，朝向林溪 65°）——
    它现在既是第4章的 F 触发点（`interact` 59 步），也是第5章“图书馆坐下来”的参考位；
    原先在 207.5（离林溪 6.4m 还背对她）。
  · 宿舍书桌旁的四个点归位到 **长桌（书桌）前 1.5m 处**（80.63, 0, 4.50，面朝桌子）：
    `第2章_手机` / `第4章_班群通知` / `第4章_坐下看资料` / `第5章_回座位`
    ——剧本原文就是「徐夏走到书桌前」「把手机从桌角拿过来」「回到自己的位置上」，
    它们以前都在房间正中的工具默认位（离桌子 6.2m）。
  · 第5章两个“到了地点之后要自己找”的触发点 = **任意凳子模式**（`StoryInteractable.anySeat`，2026-09-29 用户：
    「在图书馆和食堂都有，走到任意一个板凳旁就可以触发，触发范围要缩小，碰到凳子才触发」）：
    `第5章_图书馆躲避`（207.5,−6）保持任意凳子 radius 0.4；`第5章_食堂座位` 2026-09-30 起
    加 `seatObjectName="凳子2 (24)"` 只认舍友们那桌的凳子、radius 收紧到 **0.3**（实测反馈“范围有点大”）。
    · 凳子名单按 Loc 缓存（`SitSpot.IsSeatName`：凳/椅/chair/stool/bench/沙发），图书馆 34 把、食堂 45 把；
      **跨地点不算**（在宿舍坐着不能过关），离凳 1.2m 也不算。
    · 自检报告见 `assets/_报告/_第5章凳子触发自检.txt`（临时探针已归档：`额外文件/历史Editor脚本/_SeatProbe.cs`、
      `额外文件/历史脚本_运行时/_SeatProbeDriver.cs`）。
  · 摆位工具：`Tools/干预项目/第4/5章门口引导`（`Assets/Editor/Chapter4DoorTrip.cs`，幂等，
    报告 `assets/_报告/_第4章门口引导.txt`）。改完仍可进 Scene 手调；重跑只按规则重算上面这几个点。
  · ⚠ 还没动的地方（等用户确认后再改）：第3章 `第3章_点饭`（在 118.7,−2.5，离食堂窗口/厨师约 10m）、
    `第3章_落座`（在 118.7,0.5，离坐着的林溪 118.85,5.99 约 5.5m）；第3章中途的两个 `fade`
    （进/出办公室）也还是剧情自己跳（用户只让先处理 4、5 章）。
- **手机换聊天对象**：微信台词里非「徐夏」的说话人 → 自动推导联系人名 → `PhoneChatUI.SetContact()`
  （换标题+清空聊天流）。第2章 李同学↔林溪、第4章 班群/学姐 自动切换。★ 头像占位：沿用第一章
  两张（用户约定缺素材先占位），有新头像在 `手机聊天` 节点 Inspector 换 `avatarLeft` 即可。
- **拿/放手机动画**：关键词数组（`拿起手机/拿过手机/拿出手机/把手机从桌角拿过来` 等）+ 兜底
  （微信台词出现自动补拿、转入当面对话自动放下）。
- **实体手机道具联动（2026-09-28）**：`StoryInteractable.propObjectName`（可空字符串，按名解析场景里的
  道具，手挪自动跟随）= 交互判定中心改用道具位置 + Fire 后道具整棵 `SetActive(false)`（「拿起」的可见反馈）
  + Revive/重新武装时道具重现（"快要下次交互的时候再出现"，第2章两次拿手机复用同一点）。
  第2章接线：`Loc_宿舍/多章锚点/第2章_手机` 半径 1.2m + 联动 `手机_淡蓝`（用户手摆在 `Loc_宿舍` 下），
  工具 `Tools/干预项目/手机道具/③ 摆进宿舍+接第2/4章交互`（或丢 `_phonech2_trigger.txt`）→
  报告 `assets/_报告/_手机交互.txt`（会存场景）。未配道具的交互点（第1章等）行为零变化。
  第4章接线（2026-09-29 用户：「点开通知看看吧」改成和第2章一样）：`第4章_班群通知` 同样
  半径 1.2m + 联动同一部 `手机_淡蓝`（prompt 工具默认「点开通知」，场景里手调成「点开班群通知」，
  手调优先——重跑工具不覆盖文案）；按 F 后手机隐藏，班群（通知）台词
  走手机 UI（拿手机动画有微信台词自动补拿兜底）。同一部手机跨章共用没问题——换章会重载 Game 场景，手机回到桌上。
- **干预题记录键**：`story.choice.ch<N>.<题号>`（按章隔离；第1章旧键 `story.choice.<题号>` 已废弃）。
- **搭建工具**：`Assets/Editor/ChapterStoriesSetup.cs`，菜单 `Tools/干预项目/多章剧情/`：
  「一键搭建第2-5章（幂等）」建 runner×4 + `Loc_*/多章锚点/`（起点/交互点/fade落点）+ 接线 + 存场景 +
  报告 `assets/_报告/_多章剧情搭建.txt`；「只看接线状态」诊断。**幂等规则：节点已存在只补缺引用，
  绝不动位置/朝向**（手调优先）；摆位默认坐标按 Loc 中心估，跑完进 Scene 手调。
- **自检**：`Tools/干预项目/第N章剧情运行自检`（N=1..5，`PhoneChatSmoke.cs`；或丢
  `Assets/_storyNsmoke_trigger.txt`）→ `assets/_报告/_第N章剧情运行自检.txt`。
  ⚠ 自检会重开 Game 场景——跑之前场景必须先保存。
  ⚠ 自检入口会强制 `GameProgress.SelectChapter(N)`；runner 的 Begin 只接本章（chapterTag）
  交互点——两条铁律防"残留章 runner 后台开跑互抢接线"（踩过：第1/3/4/5章自检集体卡死
  WaitInteract，根因即此）。
- **待补（用户黄亮标注，内容等用户提供后加 json 步骤即可）**：第3章食堂隔壁桌外人对话、
  第4章宿舍舍友抱怨对话——预留 `dlg s=旁人甲/旁人乙`（名牌对话，无实体）。

### 台词语音（DialogueVoicePlayer，2026-09-29）

- **声源**：阿里云百炼 `cosyvoice-v3-flash`（CosyVoice 大模型 TTS），经 **bailian-cli**（`bl`，npm 全局装，
  key 在 `C:\Users\LF\.bailian\config.json`，⚠ 别让它进仓库/日志）生成 mp3。
  音色池：普通话女声 20 个 / 男声 10 个（`bl speech synthesize --list-voices --model cosyvoice-v3-flash`）。
- **选角（12 个真实不同的"人"，用户需求"每人不同声线、全普通话、mon=本人、旁白独立"）**：
  徐夏=龙婉(细腻柔声女)｜林溪=龙颜(温暖春风女)｜陆宣雨=龙安莉(利落从容女)｜李老师=龙应聆(温和共情女)｜
  王含=龙安柔(温柔娴静女)｜舍友A=龙安欢(欢脱元气女)｜舍友B=龙仙(豪放可爱女)｜李同学(微信)=龙小淳(知性积极女)｜
  学姐(微信)=龙应桃(温柔淡定女)｜组长=龙泽(温暖元气男)｜张同学=龙应询(年轻青涩男)｜**旁白=龙天(磁性理智男)**。
  mon（内心独白）沿用徐夏声线；**班群（通知）不配音**（系统通知）；纯标点台词（「…………」）生成期跳过=静默。
- **情绪层（2026-09-29 尝试后当晚全面退役）**：曾给台词按剧情内容加 prosody（音高/音量/SSML 停顿）表达
  焦虑/安慰/开心，用户试听连续否决——**神经 TTS 压音高会连音色质感一起偏移**（徐夏句"变粗/像男生"）、
  SSML 停顿拖时长（"语速不对"）。**终版定稿：全部 466 句一律纯默认 prosody（语速1.0/音高1.0/音量50），
  唯一保留的是按音色的响度归一化**。管线里 `emotion_for/OVERRIDES/SSML_CUSTOM` 保留代码但不再被调用
  （build_tasks 里 `e = None`）；要复活情绪层先读这段教训。管线合成完**自动套用归一化增益**
  （读 norm_state.json 的 gains）。
- **生成管线**：`额外文件/工具脚本/voice_batch_generate.py <章号1..5>`（幂等，只补缺；贴纸 `[比心]` 剔除、
  断点续跑、限流重试）→ 中转 `额外文件/语音生成/output/ch{N}/*.mp3` + `manifest.json` →
  拷进 `Assets/Resources/语音/ch{N}/` + `manifest.txt`（**键 = `ch{N}:{步号}`（台词）/ `md5:{正文哈希}`（选择题解释）**，
  归一化规则两边必须一致：去 `[..]` 贴纸 → 换行并空格 → trim）。
  ⚠ CosyVoice 免费额度约 1 万字符（全剧本 9.6k 字会耗尽）→ 余额不足报 `HTTP 400 Arrearage`
  （充值后重跑管线补缺；2026-09-29 已充值并全量生成 466/466 ✓）。
- **运行时** `Assets/Scripts/Story/DialogueVoicePlayer.cs`：懒创建（`Ensure()` 自建 GameObject+AudioSource，
  **不改场景不接线**）；`Resources/语音/manifest.txt` 查键 → `Resources.Load<AudioClip>` 播放；
  **缺片静默**（清单没有/mp3 缺 = 不发声，绝不报错）；自检（`StorySmokeDriver.Requested`）与
  续播快进（`StoryResumeDriver.Active`）不发声；音量 = `GameSettings.Voice`（Master 由 AudioListener 全局管）。
- **挂接点**（都在 `StoryRunner`/`ChoicePanel`，一处一处列清楚防误动）：
  · `PlayText()` 开头 `PlayStep(chapterIndex, StepIndex-1)` —— 一句开始就播（微信台词也播=发送方"语音消息"感）；
  · `Next()` 开头 `Stop()` —— 推进即切上一句（打字中点击补全**不切**，再点推进才切）；
  · `NarFree`（开场旁白）**纯点击推进**（2026-09-29 用户定稿：去掉定时自动播，自由移动保留）；
    `AutoPlay` 的定时推进加 `!DialogueVoicePlayer.IsPlaying` 门 —— 等语音播完再走；
  · `ChoicePanel.EnterExplain` 播解释（`PlayTextHash(opt.body)`，md5 键）、`ExitExplain` 停。
- **响度归一化（2026-09-29，用户反馈"各人音量忽大忽小"后加）**：各 CosyVoice 音色基准响度差最大
  **13.4dB**（学姐龙应桃 -33.8 vs 舍友B龙仙 -20.4）→ `额外文件/工具脚本/voice_normalize.py` 按音色
  校准到 -22dB（基准只测无情绪参数的句子，情绪相对轻重保留；增益范围钳 ±6/+12dB、alimiter 防切顶、
  libmp3lame -q:a 2 重编码；幂等状态记 `norm_state.json`）。⚠ **每次重新生成台词后要重跑归一化+重部署**
  （删 norm_state.json 或换目标值）。修完后各音色实测收敛到 -20~-23dB。
- **剧本改动后**：改了 `第N章.json` → 重跑 `voice_batch_generate.py N` → 跑 `voice_normalize.py` → 把 `output/ch{N}` 拷进
  `Resources/语音/ch{N}` + 覆盖 `manifest.txt`（清单每次全量重写，键跟着新步号走）→
  Unity 里跑 `Tools/干预项目/台词语音/诊断（清单↔mp3↔剧本覆盖）`（`Editor/VoiceLineDiag.cs`）对账 →
  报告 `assets/_报告/_台词语音.txt`。
  ⚠ **只插入非语音步骤（walk/leave/interact/fade）时不用重新合成**：后续台词步号整体后移，
  跑 `额外文件/工具脚本/voice_shift_steps.py`（按"插入位置+条数"改顶部常量）把步号键与 mp3 文件名
  （连 .meta）在 Resources 与 output 两处同步位移，再跑上面诊断对账即可（2026-09-30 第5章 +3 实测）。
  ⚠ 该脚本改键在前、改名在后，失败重跑前先看输出——半途状态用 `voice_shift_steps_fix.py` 补完。
- 试音档案：`额外文件/声线试音_v3/`（选角依据）；edge-tts 免费池（仅 3 普通话女声）已被否，弃用。

---

## 六、常用操作速查

| 目的 | 怎么做 |
|---|---|
| 改角色服装 / 配色 / 删减 | 改 `Assets/Editor/CharRebuild.cs` 顶部配置表 → `Tools/干预项目/重建角色模型` |
| 重新给主角挂动画 | `Tools/干预项目/给主角挂动画`（会重写 `PC_徐夏_测试.controller`） |
| 看角色长相 | `Tools/干预项目/渲染角色预览` → `assets/_报告/预览/*.png` |
| 查材质为什么洋红 | `Tools/干预项目/诊断角色材质` → `assets/_报告/_材质诊断.txt` |
| Mixamo 重绑的模型贴角色材质 | `Tools/干预项目/带动画模型：贴回角色材质`（或丢 `Assets/_animmodelmats_trigger.txt`，会顺带渲染预览）→ 报告 `assets/_报告/_带动画模型材质.txt`、预览 `assets/_报告/预览/带动画模型/<角色>_已绑定.png` |
| Game 场景角色换新模型 | `Tools/干预项目/Game角色替换/① 试运行`（只看）→ `② 执行替换` → `③ 运行自检`；触发器 `_gcharprobe_trigger.txt` / `_gcharsvap_trigger.txt` / `_gcharsmoke_trigger.txt`；报告 `assets/_报告/_Game角色替换.txt`、`_Game角色替换自检.txt` |
| 主角换第三人称 / 调镜头 | `Tools/干预项目/主角第三人称/① 配置` → `② 运行自检`（触发器 `_player3p_trigger.txt` / `_player3psmoke_trigger.txt`）；报告 `assets/_报告/_主角第三人称.txt`、`_主角第三人称自检.txt` |
| NPC 走路动画接入（有 Walk.fbx 的角色） | `Tools/干预项目/NPC走路动画/接入（扫描带Walk.fbx的角色）`（`NpcWalkSetup.cs`，幂等；或丢 `Assets/_npcwalk_trigger.txt` 自动跑）→ 报告 `assets/_报告/_NPC走路接入.txt`（含根位移/In Place 体检） |
| 材质引用变空/洋红 | `Tools/干预项目/全量强制重导` |
| 编辑器视角斜了/乱转/跑飞 | `Tools/干预项目/场景视图相机/…`（1 完全复位 / 2 只摆正 / 3 回原点 / 4 聚焦选中）→ `assets/_报告/_场景视图相机.txt`；或丢 `_camera_trigger.txt` 自动跑 |
| 从素材里拿新配件 | 复制 FBX + **它的 .meta** 到 `assets/02_角色_Character/Meshes/…`，再在 `CharRebuild.cs` 里引用 |
| 改主界面 UI（配色/布局） | 改 `Assets/Editor/MainMenuBuilder.cs`（布局）或 `MainMenuAssets.cs`（贴图/配色）→ `Tools/干预项目/搭建主界面UI场景` |
| 看主界面长相 | `Tools/干预项目/渲染主界面预览` → `assets/_报告/预览/主界面/01~07*.png` |
| 调剧情场景的后期（辉光/模糊/雾/氛围） | 改 `Assets/Editor/ScenePostFx.cs` 顶部常量或 `Moods()` 表 → `Tools/干预项目/场景后期效果/① 一键布置`（或丢 `_postfxfull_trigger.txt`） |
| 看后期效果 / 对比开关后期 | `Tools/干预项目/场景后期效果/③ 渲染后期预览` → `assets/_报告/预览/场景/后期_*.png`（开）与 `原始_*.png`（关）成对出现；报告 `_后期预览.txt` 里带平均亮度差值 |
| 不要后期了 | `Tools/干预项目/场景后期效果/④ 关闭后期效果`（停用不是删除，跑一次 ① 就回来） |
| 角色预览场景里看动画 | `Tools/干预项目/角色预览场景：每个角色挂一个动画`（或丢 `_animpreview_trigger.txt`）→ 报告 `assets/_报告/_角色动画预览.txt` |
| 验证动画真的在播 | `Tools/干预项目/角色预览场景：动画运行自检`（真进 Play 采样 normalizedTime）→ `assets/_报告/_角色动画预览自检.txt` |
| 搭/刷新门口传送（按 F 选地点传走） | `Tools/干预项目/搭建门口传送`（或丢 `_doors_trigger.txt`） |
| 门口面板不见了 / 白底提示跟剧情 UI 重叠 / 剧情要引导玩家自己走过去 | `Tools/干预项目/第4/5章门口引导（走到门口按F去下个地点）`（`Chapter4DoorTrip.cs`，或丢 `_ch4door_trigger.txt`）→ `assets/_报告/_第4章门口引导.txt`（幂等：缺 UI/提示没统一就重建、修剧情点摆位、清残留节点） |
| 第4/5章跳转改回 interact→fade / 林溪进宿舍演出 | `Tools/干预项目/第4-5章跳转改造（幂等）`（`Chapter45TravelSetup.cs`，或丢 `_travel45_trigger.txt`）→ 报告 `assets/_报告/_第4-5章跳转改造.txt`（fade 落点/门口 F 点都在 `Loc_*/多章锚点/` 下，手拖按名生效） |
| 验证门口传送能不能用 | `Tools/干预项目/门口传送运行自检`（真进 Play 模式走一遍）→ `assets/_报告/_门口传送运行自检.txt` |
| 场景设施穿模/贴墙掉虚空 | `Tools/干预项目/场景碰撞体/① 给设施加碰撞体`（或丢 `_colliders_trigger.txt`）→ 报告 `assets/_报告/_场景碰撞体.txt` |
| 手机道具重跑/重渲预览 | `Tools/干预项目/手机道具/①`（幂等，或丢 `_phoneprop_trigger.txt`）→ 报告 `assets/_报告/_手机道具.txt`、预览 `预览/道具/手机_淡蓝.png`；换手机源件先跑 `额外文件/工具脚本/glb2obj_phone.py` |
| 手机接第2/4章交互（靠近拿取/拿起消失） | `Tools/干预项目/手机道具/③ 摆进宿舍+接第2/4章交互`（幂等，或丢 `_phonech2_trigger.txt`）→ 报告 `assets/_报告/_手机交互.txt`；手机实例以场景里 `手机_淡蓝` 为准（手挪自动跟随），行为在 `StoryInteractable.propObjectName` |
| 重建/改第一章剧情 UI（谨慎） | 把 `额外文件/历史Editor脚本/Chapter1StoryBuilder.cs` 拷回 `Assets/Editor/` → **手动**跑菜单（会重建 `ChoicePanel`/`WalkHint`/`ChapterCard`/`BlackFade`；`对话/对话框/名字/对话内容` 已冻结只读）→ 用完移走 |
| 第一章剧情运行自检 | `Tools/干预项目/第一章剧情运行自检`（PhoneChatSmoke.cs，独立入口）→ `assets/_报告/_第一章剧情运行自检.txt` |
| 搭建第2-5章剧情（runner/锚点/接线） | `Tools/干预项目/多章剧情/一键搭建第2-5章（幂等）`（ChapterStoriesSetup.cs）→ 报告 `assets/_报告/_多章剧情搭建.txt`；摆位默认值可随后在 Scene 里手调（重跑不覆盖） |
| 第N章剧情运行自检（N=1..5） | `Tools/干预项目/第N章剧情运行自检`（或丢 `Assets/_storyNsmoke_trigger.txt`）→ `assets/_报告/_第N章剧情运行自检.txt`；⚠ 自检前场景要先保存（会重开场景） |
| 调试：回退到上一句（仅编辑器） | Play 中按 **`Backspace`** 逐句回退并重播（含语音）；只在正停在一句话上时生效，不跨交互/走位/转场，也不跨手机聊天换段（实现见 `.trellis/spec/guides/unity-story-step-conventions.md` §三） |
| 台词语音：生成/补缺（改了第N章 json 后） | `python 额外文件/工具脚本/voice_batch_generate.py N`（幂等）→ 把 `额外文件/语音生成/output/ch{N}` 拷进 `Assets/Resources/语音/ch{N}`、`manifest.json` 拷成 `manifest.txt` → Unity 菜单 `Tools/干预项目/台词语音/诊断` 对账（报告 `assets/_报告/_台词语音.txt`）；选角/管线细节见 §五「台词语音」节 |
| 坐下换模型（宿舍×2 + 食堂×1 + 图书馆×2 剧情座位；用用户手摆的坐姿模型） | `Tools/干预项目/坐姿换模型：接线（宿舍 + 食堂）`（`Assets/Editor/SitSwapSetup.cs`，或丢 `_sitswap_trigger.txt`）→ `assets/_报告/_坐姿换模型.txt`；自检 `_坐姿换模型自检.txt` |
| 换人后坐姿位置不对（坐在桌边悬空） | 把坐姿模型（场景里 `徐夏任务视角切换`）拖到椅子上/转个方向 → 重跑上面那个工具（会把座位标记点也对齐到模型） |
| 生成/修复选择题选项行 | `Tools/干预项目/生成选择题按钮行`（预置3行 = 补缺接线 / 强制重建 = 弃手调重建 / 选择题行去掉解释块）→ `assets/_报告/_选择题按钮行.txt`；选项行样式直接在 Scene 里改 |
| 选择题题干看不清（深色 Dim 上压深字） | `Tools/干预项目/选择题：题干标题改蓝白色`（`Assets/Editor/ChoiceTitleColor.cs`，或丢 `_choicetitle_trigger.txt`）→ `#CCE3FF`、报告 `assets/_报告/_选择题标题配色.txt` |
| 搭/看手机聊天 UI（微信段） | `Tools/干预项目/搭建手机聊天UI`（或丢 `_phonechat_trigger.txt`，自动搭+出预览）→ 报告 `assets/_报告/_手机聊天UI.txt`、预览 `预览/场景/手机聊天UI_预览.png` |
| 改走动段任务栏样式（左上角目标卡） | `Tools/干预项目/任务栏样式/改为左上角目标卡`（幂等，只写 WalkHint 子树，覆盖其手调值）→ 报告 `assets/_报告/_任务栏改造.txt`、预览 `预览/任务栏_目标卡.png` |
| 主界面工具报了什么 | `assets/_报告/_主界面搭建.txt`（层级树 + 越界/贴图/字体自检 + 按钮对照表） |
| 主界面能不能点 | `Tools/干预项目/主界面运行自检`（真进 Play 模式点一遗 25 步）→ `assets/_报告/_主界面运行自检.txt` |
| 重新切《ui素材》设计稿 | `Tools/干预项目/切分 UI 设计稿`（改切图框改 `MainMenuSlices.cs` 的 `TABLE`）→ `assets/_报告/预览/主界面/00_设计稿切图_对照.png` 看切得对不对 |
| 在场景里直接手改弹层 | 打开 `Scenes/MainMenu.unity` 直接改（弹层默认展开可见）；若被收起/想一键恢复，丢 `Assets/_panels_visible_trigger.txt`，刷新后自动全展开并存盘 |
| 存读档卡片美化 / 重渲定妆照 | `Tools/干预项目/存档卡定妆照/`（① 渲染5章定妆照 ② 接线存读档槽位 ③ 渲染槽位预览；或丢 `_savecard_trigger.txt` 自动①②③）→ 报告 `assets/_报告/_存档卡定妆照.txt`、预览 `预览/存档卡/`、图 `05_UI/存档插图/` |
| 验证读档续播（每章一档+回到存档步） | `Tools/干预项目/存档续播运行自检`（模拟读第2章 step43 存档，真进 Play；或丢 `_resumesmoke_trigger.txt`）→ 报告 `assets/_报告/_存档续播运行自检.txt` |

---

## 七、主界面（MainMenu）要点

- **入口场景**：`Scenes/MainMenu.unity`（Build Settings 第 0 号）。全部是 uGUI（`Canvas` + `Image` + `Text`），
  **没装 TextMeshPro**，中文字体用 `assets/05_UI/字体_Font/中文_Deng.ttf`（等线；Unity 内置字体没有汉字）。
- **贴图是程序化生成的**（`MainMenuAssets.cs`，SDF 画圆角/描边 + 1px 抗锯齿，清透治愈风）：
  背景 4 张 / 面板与卡片 12 张 / 按钮与控件 18 张 / 图标 22 张，九宫格 border 写进了 TextureImporter。
  删掉某个 png 再跑一次工具就会重新生成；**改了调色板下次跑工具会自动全量重生**（`_生成版本.txt` 是调色板指纹）。
- **《ui素材》的 12 张设计稿已入库并切成能用的贴图**：原稿在 `assets/05_UI/设计稿_原图/`，
  由 `MainMenuSlices.cs`（`Tools/干预项目/切分 UI 设计稿`）按框切出 `稿_*.png`——
  背景 `稿_背景_主菜单`、页面底 `稿_面板_弹窗`、卡片 `稿_卡片_1~4`、列表行/次按钮/页签 `稿_列表_默认|选中`。
  切图会按圆角抠出透明外圈、并把原稿里画死的内部内容压平成纯色，所以拿到的底图干净可拉伸；
  切图对不对看 `assets/_报告/预览/主界面/00_设计稿切图_对照.png`。
  主按钮/图标/slider/开关/虚线槽位仍是程序化生成（设计稿里没有可用的一套），配色已改成稿子的淡蓝。
- **页面**：主菜单（开始游戏/读取存档/章节选择/内容概览/设置/退出）+ 4 个子页 + 大图查看 + 确认弹窗 + Toast，
  ESC 逐层关闭。**弹层在场景里默认展开**（`CanvasGroup.alpha=1`，重建也保持展开，就是为了能在 Scene 视图直接手改）；
  **运行时仍从收起开始**（`UIPanel.Awake` 按 `openOnStart` 重置透明度/交互，和场景里存成什么样无关）；Toast 例外，场景里保持透明。
- **运行时脚本**（`Assets/Scripts/UI/`）：`MainMenuUI`（接线/切页/筛选/读档）、`UIPanel`（淡入淡出）、
  `GameSettings`（PlayerPrefs：音量×4/文字速度/自动播放/自动存档/全屏）、`SaveSystem`（6 个存档槽 JSON + 章节进度）、
  `OverviewDatabase`（内容概览 20 条，`assets/05_UI/内容概览/概览数据.asset`）。
- **跳转**：所有"进游戏"都写 `GameProgress.SelectChapter(n)` 再 `LoadScene("Game")`；游戏场景启动时读 `GameProgress.SelectedChapter`。
- 重建后**务必看报告里的"自动检查"**：应出现 `越界元素：无 ✓`、`贴图/字体/引用检查：全部通过 ✓`、
  `自检（重开场景后）：MainMenuUI ✓`；若报 `MainMenuUI 引用丢失`，说明 Unity 把组件写成了缺脚本，重跑一次即可。
- ⚠️ **踩过的坑**：刚改过/新拷进来的 `.cs`，`MonoScript.GetClass()` 可能是 `null`，
  这时 `AddComponent` 出来的组件会被 Unity 写成"内联 MonoScript"——**新会话里就是缺脚本，整个菜单死掉**（不高亮、不报错）。
  `MainMenuBuilder.PreloadScripts()` 会先 `ImportAsset(ForceUpdate)` 把这件事卡在搭场景之前；
  如果你在别的脚本里 `AddComponent` 自己写的组件，也要先做这一步。
- ⚠️ **九宫格 border 要小于控件最小用量的高度**：按钮/页签这类贴图会用在 50~76px 的小控件上，
  border 给 30 就会上下重叠、图形被压坏（踩过：关闭/返回/页签“看着不对”）。
  改 border 后跑一次搭场景工具即可（`_生成版本.txt` 指纹变了会自动全量重生程序化贴图）。
- ⚠️ **手改面板时别把弹层根节点 SetActive(false)**：根节点禁用后 Play 模式下 `Awake` 不跑，
  ConfirmDialog/按钮的监听全部挂不上——表面看场景正常，运行时点退出/取消**毫无反应且不报错**。
  曾因此自检挂过"点取消 → 弹窗关闭"；`OverviewTwoPerPage.cs` 的手术工具会顺带把六个弹层根节点重新启用。
- ⚠️ **补丁工具调 `MainMenuBuilder.Label/Btn` 前必须先 `MainMenuBuilder.WarmFont()`**：
  builder 的静态 `_font` 只在整场景搭建 `Run()` 里赋值，补丁路径直接调会造出**没字体的 Text**
  （场景里空白、不报错，运行时自检只查 text 值查不出来）。
- 内容概览页是**每页 2 张选择大卡 + 底部翻页**（上一页/页码/下一页，`MainMenuUI._ovPage`）；
  该页的"卡片"容器被手改放大了 1.2808 倍，概览页布局改动以
  `Tools/干预项目/概览页改每页2卡`（`OverviewTwoPerPage.cs`，`_overview2_trigger.txt` 触发，
  跑完自动渲染预览 + 运行自检）为准，**别跑整场景重建**（会冲掉其他页手改的缩放和布局）。
- 按钮悬停：**全部按钮纯变色（ColorTint），不换贴图**。主按钮（开始游戏/应用/读取/确定，按对象名识别）
  用 `MainMenuBuilder.PrimaryTint()` 真提亮（normal 原色、悬停 `(1.06,1.08,1.12)` 微蓝提亮、按下压蓝）；
  白底按钮（次按钮/图标钮/页签）用 `HoverTint()` 相对提亮（normal 压暗 `(0.96,0.97,0.98)`、悬停回白；
  白底乘法提不动才用相对法）。两者 Selected=normal 不常亮，缩放统一由 `UIHoverScale`（1.035/0.975），
  按钮挂 `UIButtonPolish`（手型光标 + 悬停文字变化）。
  只改按钮悬停、不想动场景手改时，用 `Tools/干预项目/按钮悬停改成纯变色`（或丢 `_btnhover_trigger.txt`）——
  **别为这事跑整场景重建**，会把手改冲掉（如已删的 Logo 徽标、用户自换的按钮素材）。
- 子页（设置/存读档/章节/概览）的整屏底按 ui-002 的样子：**深色底(0.94) + 中间浅色大圆角面板**
  （面板就是 ui-002 切出来的 `稿_面板_弹窗`）。
- **存读档槽位（2026-09-28 起用「章节定妆照」，用户定稿）**：有档卡整卡铺每章预渲染场景图
  （`05_UI/存档插图/ch1..ch5.png`，圆角+底部暗带烘进 PNG，ch1教室/ch2宿舍/ch3办公室/ch4图书馆/ch5宿舍180°反打），
  白色实底恒隐藏，章名/时间压在暗带上（章节 (0,-64) 白、时间 (0,-88) 浅灰 17 号）。
  运行时 `SaveSlotUI.ChapterArt`（`MainMenuUI.Awake` 注入），缺图回退旧截图 `ThumbnailCache`
  （StoryRunner 抓屏逻辑保留，`SaveData.thumbnail` 照写）。工具与布局基线见
  `Tools/干预项目/存档卡定妆照/`（幂等补丁，只动 `槽位_1..6` 子树 + `chapterArt` 字段）。
  ⚠ **真实场景里 `SaveSlotUI.selectFrame` 全部未接线（`{fileID: 0}`），选中态由 `SlotSelectFx`
  （角标+空框变色）驱动**——别按 `MainMenuBuilder.BuildSlot` 源码以为有「选中框」节点，
  手术工具接线前先 grep 场景 YAML 核实（踩过：硬校验选中框导致 6 槽全报错）。
- **存档系统（2026-09-28 定稿：每章一档 + 章内续播）**：
  · **每章一档**：`StoryRunner.AutoSave`（写档唯一入口）把第 N 章的档写进 **N 号槽**（槽 6 留空）；
    重玩本章只覆盖本章自己的档。时机 = 每道干预题交卷后（受设置"选择后自动存档"开关控制）
    + 章末通关兜底存（`force:true` 不受开关控制）。
  · **缩略图**：选择题全选完毕、面板完整显示的那一刻抓屏（`ChoicePanel.onPanelComplete` →
    `StoryRunner.CaptureChoiceThumb`，`WaitForEndOfFrame` + `ScreenCapture.CaptureScreenshot`），
    文件存 `persistentDataPath/saves/`，名字带章号+题号；本章没抓到新图时存档沿用槽里旧图。
  · **章内续播**：存档的 `step` = "下一个待执行步骤号"（`Next()` 先自增再执行）。读档时
    `MainMenuUI` 写 `GameProgress.SetResume(章, 步)` → Game 场景 `StoryRunner.Begin()` 一次性消费：
    **静默快进 0..step-1**（`SilentApply`：fade/enter/leave/walk/interact 只做传送+站位，
    interact 顺带 `Fire()` 消费交互点+隐藏联动道具；choice 只对齐 `_choiceCounter`；微信台词进
    `_resumeChat` 队列）→ 黑幕淡入后从 step 步正常播。⚠ 续播前要先把「对话」节点激活（微信台词的
    隐形打字机在禁用节点上起不了协程，踩过）；⚠ `SelectChapter` 会顺带 `ClearResume`——读档路径
    是先 SelectChapter 再 SetResume，其它入口天然"从头播本章"。
  · **通关档（`SaveData.chapterDone`，2026-09-30）**：章末存档带标记；主菜单读到它**弹确认框
    "重玩本章"**（从头播，不续播——续播只剩一张结束卡，"一进去就跳游戏结束"的根源，踩过）；
    旧档没标记的兜底：Begin 里 `step>=总步数 → resume=0`（整章从头重播，不放空壳结局）。
  · ⚠ **自检驱动绝不能存进场景**：`ResumeDriverTemp` 曾被连着 Game.unity 存过盘 → 一次自检两个驱动
    并跑、报告全部双份。`StoryResumeSmoke` 现在 Run 前清场、Finish 后重开场景丢弃临时对象
    （★退 Play 后 DestroyImmediate 删的是 Play 实例，编辑态实例会活到下次保存——别改回去）。
  · **防污染**：`StorySmokeDriver.Requested` 或 `StoryResumeDriver.Active` 为真时 AutoSave 直接返回
    （★别写成 `FindObjectOfType<StorySmokeDriver>()`——那个组件常驻场景，会把真实游玩的存档全挡掉，踩过）。
  · **自检**：`Tools/干预项目/存档续播运行自检`（`StoryResumeSmoke.cs`，或丢 `_resumesmoke_trigger.txt`）
    → 模拟读第2章 step43 存档，验证快进副作用+状态机推进 → 报告 `assets/_报告/_存档续播运行自检.txt`。
    ⚠ 编辑器里 Game 场景加载+首帧着色器预热很慢，超时给 360 秒。

---

## 八、角色一览（9 人，用于对照剧本/需求）

| 角色 | 性别 | 身份（参考 `项目文档/需求文档.docx`） | 出场 |
|---|---|---|---|
| 徐夏 | 女 | 主角（可操作） | 1–5 章 |
| 林溪 | 女 | 朋友 | 1、3、4 章 |
| 陆宣雨 | 女 | 宿舍长 | 2、5 章 |
| 王含 | 女 | 同学 | 3 章 |
| 李老师 | 女 | 辅导员 | 3 章 |
| 舍友A / 舍友B | 女 | 室友 | 5 章 |
| 组长 | 男 | 小组组长 | 1 章 |
| 张知远 | 男 | 同班同学 | 1 章 |

> 文档里**没有任何外形描写**，只规定性别/身份/出场章节 —— 造型（发型、服装、配色）可自由安排，只要性别一眼可辨、彼此不撞即可。
