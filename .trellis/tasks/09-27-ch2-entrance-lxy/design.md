# 技术方案：第2章陆宣雨入场演出（虚影渐显 + 走近 + 落定）

对应 `prd.md`。核心原则：**不动 ShaderGraph、不动角色材质资产、不新建 UI**；
新增面收敛为一个 Ghost shader + 一个入场驱动组件 + `enter` 步骤类型 + 工具扩展。

## D1 步骤类型 `enter`（StoryRunner 扩展，模式照抄 fade）

json 形态（插在「她抬头看了看宿舍门……」nar 之后、陆宣雨第一句 dlg 之前）：

```json
{ "t": "enter", "who": "陆宣雨_可动", "from": "第2章_陆宣雨门口" }
```

- 字段：`who`=场景里角色实例名（`FindObjectsOfType` + 名字匹配，避免 GameObject.Find
  找不到禁用对象）；`from`=门口起点锚点名（接在 runner 锚点池里，与 fade 共用）；
  `to`=**可选**——缺省时落点为**玩家面前**：enter 开始快照玩家位置 P，
  终点 = P + (from→P 方向单位向量) × 1.3m（从她的来向走到玩家跟前），
  到位转身面向玩家。★ 不写死落点坐标：玩家此刻在手机交互点 2.2m 半径内但位置不精确
  （用户 2026-09-27 指出"万一玩家不在书桌旁"）。显式给 `to` 锚点名时才走固定落点（特殊演出预留）。
  `x`=可选参数串（`dur=秒`，默认按距离/1.6m/s 估）。
- 运行：`State.Enter`（全锁，同 Fade）→ 协程 `EnterRoutine(step)`：
  1. 找到角色 + from 锚点；算终点（玩家面前或 to 锚点）；角色 SetActive(true)
     （若已激活，先瞬移到 from）。
  2. 摘描边（Layer 暂改为默认层，记原值）、禁 CapsuleCollider。
  3. 换虚影材质（D2），alpha 从 0 起。
  4. 位移插值 from→终点（面朝移动方向），alpha 随进度升到 0.75；播行走动画（D3）。
  5. 到位：切回待机动画 → 换回原材质 → 恢复 Layer/碰撞 → 面向玩家。
  6. `Next()`。
- `DebugAdvance()`：Enter 态 = 立即跳到终态（自检不等待演出）——协程加 `_fast` 标志或
  DebugAdvance 直接调组件的 `Skip()`。
- 开场禁用：Begin() 时扫描本章 json 里的 enter 步骤，收集 `who` 列表 → 这些角色
  SetActive(false)（第2章=陆宣雨；记录以便 enter 时再启用）。★ 这样 json 是唯一事实源，
  不需要在场景/工具里额外配"开场隐藏"标记。

## D2 虚影材质（新 shader：`Assets/Scripts/Story/CharacterGhost.shader`）

- 简单半透明 lit（或 unlit + 微环境色），~40 行 HLSL：
  - 属性：`_Color`（默认淡青白 (0.75, 0.85, 0.92)）、`_Alpha`（驱动渐显）。
  - `Blend SrcAlpha OneMinusSrcAlpha`、`ZWrite Off`、`RenderQueue Transparent`、`Cull Back`。
  - 顶点按法线给一点点明暗（有体积感，不是一张平纸）。
- 应用方式：运行时 `renderer.materials` 逐个替换为**同一个** ghost 材质实例（共享，仅
  `_Alpha` 全局渐变）；落定时把 `renderer.materials` 还原为保存的原数组。
  - ★ 用 `sharedMaterials` 保存原数组再赋 `materials`（实例化的 ghost），还原时把
    sharedMaterials 数组写回并销毁实例，防材质泄漏。
- 身体多部件（ModularBody/服装/头发多个 SkinnedMeshRenderer）统一处理：遍历她根下所有
  `Renderer`。
- 为什么不做原材质透明：CharacterLit 是 ShaderGraph + 9 角色共享 + RGBMap 遮罩体系
  （AGENTS 第三节的历史事故区），加透明分支风险远大于收益；"虚影"本来就是模糊的，
  单色半透明符合观感。

## D3 行走动画接线

- 新 controller：`NPC_待机女_含走.controller`（工具生成，放 `03_动作_Animation/Animators/`）：
  - 默认状态 待机（`待机女` clip 循环）+ `走`（`行走` clip 循环），Bool 参数 `Walk`。
  - 生成方式照抄 NpcSetup.SetupNpcIdle() 的 AnimatorController 构建代码（同一套 API）。
- 陆宣雨 prefab 换挂这个 controller（工具改 prefab 资产，幂等：已挂则跳过）；
  其他 NPC 不动（将来谁要入场谁再换）。
- 入场协程：`animator.SetBool("Walk", true)` 出发，到位 `false`（过渡 0.15s）。
- 动画位移：clip 若带位移（root motion），关 `applyRootMotion`，位移全由协程做（动画纯视觉）。

## D4 描边与碰撞的坑（已预判）

- 描边黑壳：OutlineFeature 用 overrideMaterial 画 `Outline` 层所有 Renderer 的背面外壳，
  材质不透明——虚影阶段会看到"实心黑边浮在半透明人外面"。解法：入场期间把她根节点
  Layer 从 `Outline` 改为 `Default`（记原层名，落定恢复）。外壳 Feature 只按层收集，改层即摘除。
- 胶囊碰撞：EnsureCharacterColliders 给她加的 CapsuleCollider 在根节点——入场中禁用
  （`enabled=false`，记状态，落定恢复），避免幽灵挡人/玩家提前撞停。

## D5 锚点与工具（ChapterStoriesSetup 扩展，幂等）

- 新锚点（进 `Loc_宿舍/多章锚点/`，跑一次工具补上）：
  - `第2章_陆宣雨门口`：南门内 (2.0, 0, -5.4)，朝北（面向屋内）。
  - 落点**不设锚点**：缺省动态走到玩家面前 1.3m（见 D1）；`to` 只在特殊演出需要固定落点时才建锚点。
  - 锚点接进第2章 runner 的锚点池（enter 与 fade 共用，名字索引）。
- 工具新菜单/并入现有菜单：`多章剧情/NPC入场接线（第2章陆宣雨）`：
  建锚点 + 生成 controller + 改 prefab 挂 controller + 报告。重跑不覆盖手调位置
  （幂等规则同现有）。
- 陆宣雨的**场景初始摆位**不再重要（开场必被禁用、enter 时重摆到 from），但保留现状不动。

## D6 json 改动（第2章，仅 1 步）

在 `{ "t": "nar", "x": "她抬头看了看宿舍门，刚好看到宿舍长端着水杯走进来。" }` 之后插入：

```json
{ "t": "enter", "who": "陆宣雨_可动", "from": "第2章_陆宣雨门口" },
```

其余 77 步零改动。`end` 后无需清理（章节结束即场景卸载/回主菜单）。

## D7 自检

- StorySmokeDriver：Enter 态计数（`_sawEnter`）；DebugAdvance 的 Skip 使自检不等演出。
- 人工验收（用户 Play）：观感——虚影从门口走来越来越清晰、落定、开口说话。

## D8 交付物清单

| 文件 | 类型 |
|---|---|
| `Assets/Scripts/Story/CharacterGhost.shader` | 新（虚影材质） |
| `Assets/Scripts/Story/NpcEntrance.cs` | 新（入场驱动静态工具/组件：材质替换、移动、动画、描边/碰撞开关） |
| `Assets/Scripts/Story/StoryRunner.cs` | 改：enter 步骤 + State.Enter + Begin 时禁用 enter 角色 |
| `Assets/Scripts/Story/StoryData.cs` | 改：注释补 enter/who/from/to |
| `Assets/Scripts/Story/StorySmokeDriver.cs` | 改：Enter 计数 |
| `Assets/Editor/ChapterStoriesSetup.cs` | 改：NPC 入场接线菜单（锚点+controller+prefab） |
| `Assets/assets/03_动作_Animation/Animators/NPC_待机女_含走.controller` | 新（工具生成） |
| `Assets/数据/剧情/第2章.json` | 改：插 1 步 enter（78→79 步） |
| `Game.unity` | 工具写入：锚点 ×1（门口）+ runner 锚点接线 |
| `assets/_报告/_NPC入场接线.txt` | 报告 |
