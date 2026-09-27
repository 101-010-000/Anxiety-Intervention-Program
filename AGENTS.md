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
  旧预览/               已作废的预览图（当前有效预览在项目 assets/_报告/预览/）
  素材整理脚本/         整理原始素材时用的脚本与 dump
  工具脚本/             通用小工具（如 检查CSharp.py）
  错误_*.txt            Editor 工具运行出错时的异常堆栈（项目外的副产物）
```

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
  AssetLocator.cs              在 Assets 里按名字找文件/目录
  SceneBuilder.cs              剧情主场景 Game.unity：6 个地点拼装 + 场景总览渲染（末尾会自动调 ScenePostFx）
  ScenePostFx.cs               剧情场景后期：辉光/模糊/抬黑/雾 + 6 个地点各自的氛围 Volume（见第五节）
  GameDoorBuilder.cs           门口传送：给场景里已有的门触发盒挂交互 + 「按 F 开门」+ 地点选择面板（见第五节）
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
  03_动作_Animation/           动画 FBX + Animators/PC_徐夏_测试.controller
  04_音效_Audio/
  05_UI/                       ★ 主界面 UI 素材：背景/界面/按钮/图标/字体 + 内容概览（20 张原图）+ 设计稿_原图（《ui素材》12 张）
  11_着色器_Shaders/           角色套件 ShaderGraph（CharacterLit / Toon / 子图 / HLSL）
  _报告/                       ★ 所有报告、清单、预览图都写到这里
Scripts/UI/                    主界面运行时脚本（MainMenuUI / UIPanel / GameSettings / SaveSystem …）
Scripts/Player/                FirstPersonController.cs（第一人称移动/视角/动画）
Scripts/Visual/                DreamyFocus.cs（景深同步）、SceneOutline.shader + OutlineFeature.cs（描边，见第五节）
Scripts/Game/                  DoorInteractable.cs / DoorTravelSystem.cs / DoorSmokeDriver.cs（门口传送，见第五节）
Scenes/Test_徐夏_动画.unity     测试场景（9 个角色实例 + 相机 + 太阳）
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
   - `_menu_trigger.txt` → 重建主界面 MainMenu.unity
   - `_camera_trigger.txt` → 场景视图相机复位（视角斜了/跑飞了）
   - `_phonechat_trigger.txt` → 搭建手机聊天UI（第1章微信段）+ 渲染预览
   > 触发器依赖"域重载"生效：改一下任意脚本文件、或让 Unity 窗口获得焦点/按 Ctrl+R 即可。
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
- **每个地点一套氛围**（走进去平滑换）：本地 Volume 是「覆盖」不是「叠加」，所以 `Moods()` 里写的是绝对值；
  **别把 DepthOfField 放进本地面板**，会盖掉全局的、远近分层就失效。
- 改完记得看报告：`assets/_报告/_后期效果.txt`（布置明细）、`_后期预览.txt`（带亮度 + **近/远两段清晰度**；
  同一机位还会渲一张 `原始_*.png` 作为“关后期”对照。若“清晰度 远”和“关景深时”几乎一样，说明景深没生效）。

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
- `遮罩`（全屏黑 68%）已从「对话」下提到 `UI交互` 最前（= UI 最底层，才铺得满全屏、也压不到对话条）。
  **运行时按语态启用：`dlg` 对话 / `mon` 内心独白 → 开；`nar` 旁白 → 关**（`DialogueUI.SetDim`）。场景里默认禁用。
- `BlackFade` 与 `遮罩` 职责不同、**层级不能互换**：
  · `BlackFade` = 章节转场黑幕（不透明，**最上层**，开场淡出 / 章末淡入，`StoryRunner` 驱动）；
  · `遮罩` = 对话压暗层（半透明 68%，**最底层**，按语态开关）。
- **干预选择题（ChoicePanel，2026-09-27 定稿「按钮⇄解释」互斥）**：
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
    → 对话框让位、手机回屏。干预题②收档 → 第一个选中的鼓励语以「林溪（微信）」补一条并多停一拍。
    `[比心]` 整句 → 1.png 贴纸。
  · 搭建：`Tools/干预项目/搭建手机聊天UI`（或 `_phonechat_trigger.txt`）→ 在 `UI交互` 下建唯一节点
    `手机聊天`（siblingIndex=1：遮罩之上、对话/选择题之下，默认禁用），接线 + 存场景 +
    报告 `assets/_报告/_手机聊天UI.txt` + 预览 `预览/场景/手机聊天UI_预览.png`。
  · CanvasGroup.blocksRaycasts=false：点击穿透手机，不打断"点击推进剧情"。

---

## 六、常用操作速查

| 目的 | 怎么做 |
|---|---|
| 改角色服装 / 配色 / 删减 | 改 `Assets/Editor/CharRebuild.cs` 顶部配置表 → `Tools/干预项目/重建角色模型` |
| 重新给主角挂动画 | `Tools/干预项目/给主角挂动画`（会重写 `PC_徐夏_测试.controller`） |
| 看角色长相 | `Tools/干预项目/渲染角色预览` → `assets/_报告/预览/*.png` |
| 查材质为什么洋红 | `Tools/干预项目/诊断角色材质` → `assets/_报告/_材质诊断.txt` |
| 材质引用变空/洋红 | `Tools/干预项目/全量强制重导` |
| 编辑器视角斜了/乱转/跑飞 | `Tools/干预项目/场景视图相机/…`（1 完全复位 / 2 只摆正 / 3 回原点 / 4 聚焦选中）→ `assets/_报告/_场景视图相机.txt`；或丢 `_camera_trigger.txt` 自动跑 |
| 从素材里拿新配件 | 复制 FBX + **它的 .meta** 到 `assets/02_角色_Character/Meshes/…`，再在 `CharRebuild.cs` 里引用 |
| 改主界面 UI（配色/布局） | 改 `Assets/Editor/MainMenuBuilder.cs`（布局）或 `MainMenuAssets.cs`（贴图/配色）→ `Tools/干预项目/搭建主界面UI场景` |
| 看主界面长相 | `Tools/干预项目/渲染主界面预览` → `assets/_报告/预览/主界面/01~07*.png` |
| 调剧情场景的后期（辉光/模糊/雾/氛围） | 改 `Assets/Editor/ScenePostFx.cs` 顶部常量或 `Moods()` 表 → `Tools/干预项目/场景后期效果/① 一键布置`（或丢 `_postfxfull_trigger.txt`） |
| 看后期效果 / 对比开关后期 | `Tools/干预项目/场景后期效果/③ 渲染后期预览` → `assets/_报告/预览/场景/后期_*.png`（开）与 `原始_*.png`（关）成对出现；报告 `_后期预览.txt` 里带平均亮度差值 |
| 不要后期了 | `Tools/干预项目/场景后期效果/④ 关闭后期效果`（停用不是删除，跑一次 ① 就回来） |
| 搭/刷新门口传送（按 F 选地点传走） | `Tools/干预项目/搭建门口传送`（或丢 `_doors_trigger.txt`） |
| 验证门口传送能不能用 | `Tools/干预项目/门口传送运行自检`（真进 Play 模式走一遍）→ `assets/_报告/_门口传送运行自检.txt` |
| 重建/改第一章剧情 UI（谨慎） | 把 `额外文件/历史Editor脚本/Chapter1StoryBuilder.cs` 拷回 `Assets/Editor/` → **手动**跑菜单（会重建 `ChoicePanel`/`WalkHint`/`ChapterCard`/`BlackFade`；`对话/对话框/名字/对话内容` 已冻结只读）→ 用完移走 |
| 第一章剧情运行自检 | `Tools/干预项目/第一章剧情运行自检`（PhoneChatSmoke.cs，独立入口）→ `assets/_报告/_第一章剧情运行自检.txt` |
| 生成/修复选择题选项行 | `Tools/干预项目/生成选择题按钮行`（预置3行 = 补缺接线 / 强制重建 = 弃手调重建 / 选择题行去掉解释块）→ `assets/_报告/_选择题按钮行.txt`；选项行样式直接在 Scene 里改 |
| 搭/看手机聊天 UI（微信段） | `Tools/干预项目/搭建手机聊天UI`（或丢 `_phonechat_trigger.txt`，自动搭+出预览）→ 报告 `assets/_报告/_手机聊天UI.txt`、预览 `预览/场景/手机聊天UI_预览.png` |
| 主界面工具报了什么 | `assets/_报告/_主界面搭建.txt`（层级树 + 越界/贴图/字体自检 + 按钮对照表） |
| 主界面能不能点 | `Tools/干预项目/主界面运行自检`（真进 Play 模式点一遗 25 步）→ `assets/_报告/_主界面运行自检.txt` |
| 重新切《ui素材》设计稿 | `Tools/干预项目/切分 UI 设计稿`（改切图框改 `MainMenuSlices.cs` 的 `TABLE`）→ `assets/_报告/预览/主界面/00_设计稿切图_对照.png` 看切得对不对 |
| 在场景里直接手改弹层 | 打开 `Scenes/MainMenu.unity` 直接改（弹层默认展开可见）；若被收起/想一键恢复，丢 `Assets/_panels_visible_trigger.txt`，刷新后自动全展开并存盘 |

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
