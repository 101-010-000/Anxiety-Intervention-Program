# 执行计划：第2章陆宣雨入场演出

> **进度（2026-09-28 00:30）**：
> - 阶段 0/1 完成并提交（0636ee0）：Ghost shader / NpcEntrance / enter 步骤 / 接线菜单 / 第2章 json。
> - 用户实机反馈两处穿帮并已修复（待提交）：
>   ① 陆宣雨开场未隐藏——`who` 按名查找撞上多章同名实例（宿舍有第二/五章两个 陆宣雨_可动），
>   查找改为限定本章容器子树；② 第五章角色出现在第二章——新增 `ApplyChapterNpcVisibility()`：
>   Begin 时只保留 `第N章角色` 容器（兼容汉字/数字命名），其余整棵隐藏。
> - 自检实证（00:16）：第2章 79/79 ✓、Enter 1（锚点已由用户接线）；抓到 CharacterGhost 的
>   `TransformObjectToHclip` 大小写笔误（应为 HClip）已改——shader 报错消失待用户下次聚焦
>   Unity 编译后复跑自检确认。
> - 用户已跑过「NPC入场接线」菜单（Enter 1 佐证）；视觉验收（虚影渐显观感）待用户 Play。

前置：技术方案见 design.md；本任务规模中等，按 3 个阶段执行，每阶段可独立验证。
当前 Trellis 任务 ch2-5-story 仍在收尾（等用户跑各章自检），本任务独立推进、不阻塞它。

## 阶段 0：代码

- [ ] `CharacterGhost.shader`（半透明虚影，~40 行；`_Alpha` 驱动）
- [ ] `NpcEntrance.cs`：材质替换/还原（sharedMaterials 备份）、描边摘除/恢复（Layer）、
      碰撞禁用/恢复、行走动画开关（`Walk` Bool）、位移协程（from→to 插值+朝向）、
      Skip()（自检快进）
- [ ] `StoryRunner.cs`：`State.Enter` + `case "enter"`（EnterRoutine，模式照抄 FadeRoutine）；
      `Begin()` 扫描本章 enter 步骤 → 预禁用 `who` 角色；`DebugAdvance()` 覆盖 Enter
- [ ] `StoryData.cs` 注释补 enter；`StorySmokeDriver.cs` Enter 计数
- [ ] 括号/编译检查（lint_braces）

## 阶段 1：工具 + 数据

- [ ] `ChapterStoriesSetup.cs` 新菜单 `多章剧情/NPC入场接线（第2章陆宣雨）`：
      - 建 `Loc_宿舍/多章锚点/第2章_陆宣雨门口` (2.0, 0, -5.4) 朝北；接进第2章 runner 锚点池
        （落点不设锚点：enter 缺省动态走到玩家面前 1.3m）
      - 生成 `NPC_待机女_含走.controller`（待机女 + 行走，`Walk` Bool，照抄 NpcSetup 构建 API）
      - 给 `陆宣雨_可动.prefab` 换挂该 controller（幂等）；关 applyRootMotion
      - 报告 `assets/_报告/_NPC入场接线.txt`
- [ ] `第2章.json`：在「她抬头看了看宿舍门……」后插
      `{ "t": "enter", "who": "陆宣雨_可动", "from": "第2章_陆宣雨门口" }`
      （78→79 步）
- [ ] json 校验（步骤数/choice=4/fade 目标不变）

## 阶段 2：验证与收尾

- [ ] 用户在 Unity：跑 `多章剧情/NPC入场接线` 菜单（或并入一键搭建重跑）
- [ ] 自检：第2章（enter 快进不卡）、第1章（回归）
- [ ] 用户 Play 人工验收：开场宿舍无陆宣雨 → 微信段后虚影从南门渐显走近 → 落定 → 对话
- [ ] 观感微调（虚影颜色/alpha 上限/时长/走路速度）如需
- [ ] AGENTS.md 多章剧情小节补 enter 约定；提交（中文标题+要点）

## 验证命令

| 目的 | 操作 |
|---|---|
| 括号/编译 | `python 额外文件/工具脚本/lint_braces.py`（逐文件）+ Unity Console |
| 第2章自检 | Unity 菜单 `Tools/干预项目/第二章剧情运行自检` → `_第2章剧情运行自检.txt` |
| 第1章回归 | 同上第1章 |
| 人工观感 | 用户 Play 第2章到陆宣雨段 |

## 回滚点

- 阶段 0 单独 commit（纯代码，不触发任何行为——json 没插 enter 前一切照旧）。
- 出问题回退：还原 json（删 enter 步骤）即回到现状演出；代码无害残留。
