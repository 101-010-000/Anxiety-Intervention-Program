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
  SceneBuilder.cs              剧情主场景 Game.unity：6 个地点拼装 + 场景总览渲染
  MainMenuAssets.cs            主界面 UI 贴图：程序化生成 57 张 + 中文字体 + 20 张收录图导入
  MainMenuSlices.cs            切《ui素材》设计稿：圆角抠图 + 内部压平 + 九宫格 border（稿_*.png）
  MainMenuBuilder.cs           主界面场景 MainMenu.unity：主菜单/设置/存读档/章节/概览/弹窗
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
   - `_scene_trigger.txt` → 搭建剧情主场景 Game.unity
   - `_menu_trigger.txt` → 重建主界面 MainMenu.unity
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

## 五、常用操作速查

| 目的 | 怎么做 |
|---|---|
| 改角色服装 / 配色 / 删减 | 改 `Assets/Editor/CharRebuild.cs` 顶部配置表 → `Tools/干预项目/重建角色模型` |
| 重新给主角挂动画 | `Tools/干预项目/给主角挂动画`（会重写 `PC_徐夏_测试.controller`） |
| 看角色长相 | `Tools/干预项目/渲染角色预览` → `assets/_报告/预览/*.png` |
| 查材质为什么洋红 | `Tools/干预项目/诊断角色材质` → `assets/_报告/_材质诊断.txt` |
| 材质引用变空/洋红 | `Tools/干预项目/全量强制重导` |
| 从素材里拿新配件 | 复制 FBX + **它的 .meta** 到 `assets/02_角色_Character/Meshes/…`，再在 `CharRebuild.cs` 里引用 |
| 改主界面 UI（配色/布局） | 改 `Assets/Editor/MainMenuBuilder.cs`（布局）或 `MainMenuAssets.cs`（贴图/配色）→ `Tools/干预项目/搭建主界面UI场景` |
| 看主界面长相 | `Tools/干预项目/渲染主界面预览` → `assets/_报告/预览/主界面/01~07*.png` |
| 主界面工具报了什么 | `assets/_报告/_主界面搭建.txt`（层级树 + 越界/贴图/字体自检 + 按钮对照表） |
| 主界面能不能点 | `Tools/干预项目/主界面运行自检`（真进 Play 模式点一遗 25 步）→ `assets/_报告/_主界面运行自检.txt` |
| 重新切《ui素材》设计稿 | `Tools/干预项目/切分 UI 设计稿`（改切图框改 `MainMenuSlices.cs` 的 `TABLE`）→ `assets/_报告/预览/主界面/00_设计稿切图_对照.png` 看切得对不对 |

---

## 六、主界面（MainMenu）要点

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
  ESC 逐层关闭；子页默认收起（场景里 `CanvasGroup.alpha=0`）。
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

---

## 七、角色一览（9 人，用于对照剧本/需求）

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
