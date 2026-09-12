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
Editor/                        agent 工具（菜单 Tools/干预项目/…）
  CharRebuild.cs               角色重建：服装搭配 / 一人一色 / 身体删减
  PlayerAnimSetup.cs           主角动画：生成 AnimatorController 并挂到徐夏
  CharPreview.cs               渲染角色预览 / 材质诊断 / 全量强制重导
  AssetLocator.cs              在 Assets 里按名字找文件/目录
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
  04_音效_Audio/  05_UI/
  11_着色器_Shaders/           角色套件 ShaderGraph（CharacterLit / Toon / 子图 / HLSL）
  _报告/                       ★ 所有报告、清单、预览图都写到这里
Scenes/Test_徐夏_动画.unity     测试场景（9 个角色实例 + 相机 + 太阳）
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
   > 触发器依赖"域重载"生效：改一下任意脚本文件、或让 Unity 窗口获得焦点/按 Ctrl+R 即可。
4. **出现"洋红 / 空材质"**：先跑 `Tools/干预项目/全量强制重导`（等价 Assets → Reimport All），
   再用 `诊断角色材质` 核对（`_报告/_材质诊断.txt` 里应无 `MATERIAL_NULL`、无 `supported=False`）。
5. **产物流向**：报告 / 清单 / 预览图 → `3D剧情项目/Assets/assets/_报告/`；
   临时脚本、dump、备份、日志 → `额外文件/`（已 gitignore，不进版本库）。
6. `Library/`、`Temp/`、`Logs/`、`UserSettings/` 都是缓存：别动、别提交。
7. 提交信息用中文（一行标题 + 要点列表）；推送 `git push origin main`（大提交较慢，中断了直接重跑）。

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

---

## 六、角色一览（9 人，用于对照剧本/需求）

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
