# 技术设计

四个修复互相独立，全部落在运行时脚本 + 剧情 json，零场景文件改动。

## ① 章末卡置顶（StoryRunner.EndRoutine）

`chapterCardGroup.gameObject.SetActive(true)` 之前加
`chapterCardGroup.transform.SetAsLastSibling()`——运行时把章节卡挪到 BlackFade 之上。
场景文件不动（BlackFade「UI 最上层」是全局约定，不能为章末卡改层级）；置顶发生在 EndCard
开始时，之后场景即转主菜单，无需还原。

## ② 走路清 Phone + 按 F 补拿

- `FirstPersonController.MoveWithInput(ix, iz)` 开头：`|ix|>0.01 || |iz|>0.01` 且
  `animator.GetBool("Phone")` → `SetBool("Phone", false)`。剧情锁住时输入为 0 不会误清；
  微信段（Typing 全锁）手机姿势保持。
- `StoryRunner.OnInteractableFired`（InteractF 分支）：`si.promptText` 命中 PHONE_TAKE_KEYS →
  SetTrigger("TakePhone") + SetBool("Phone", true)。走到点是站姿，按 F 这一刻才拿。

## ③ leave 步骤（StoryRunner + StoryData + 第2章.json）

- `StoryStep` 复用现有 `who`/`to` 字段，无需新字段；StoryData 注释补 leave 说明。
- Begin 时 `CollectAndHideEntranceNpcs()` 扩为同时扫 `enter`/`leave` 的 who：都记引用到
  `_entranceNpcs`（FindObjectsOfType 找不到禁用对象），并快照**开场原位**到
  `_npcHome[who] = (position, eulerAngles.y)`；只有 `enter` 的 who 才预禁用
  （leave-only 的角色开场在场）。
- `DoLeave(step)` 协程（`_leaveRt`，防重入）：解析 who（_entranceNpcs → FindCharacterTransform
  兜底，同 enter）；目标 = `to` 锚点（FindFadeAnchor 同名解析）‖ `_npcHome` 原位；
  `NpcEntrance.Run(currentPos, target, null)`（faceTarget=null=保持走向）；结束后把 yaw 回写成
  原位朝向；权限复用 `State.Enter`（能转不能走，演出语义一致）。
- 第2章.json：第 76 行 nar「就回到了自己的座位上」之后插 `{ "t": "leave", "who": "陆宣雨_可动" }`。

## ④ interact at 绑定 + 点复用（StoryRunner.FindFree + StoryInteractable + jsons）

- `StoryStep` 新增 `public string at = "";`。
- `FindFree(Mode m, string at = null)`：带 at 时精确匹配 `si.name == at` 的本章同模式点；
  未消费者优先；全部已消费 → 取第一个同名的 `Revive()` 后返回（第2章两次拿手机复用）；
  名字一个都匹配不上 → 警告并退回旧逻辑（第一个未消费点），不硬失败。不带 at = 旧行为。
- `StoryInteractable.Revive()`：`Consumed=false`、`PlayerInRange=false`、重新启用 Collider。
  ★ 不能叫 `Reset()`——那会撞 Unity 编辑器给 MonoBehaviour 的 Reset 消息（组件重置时被编辑器调）。
- json：ch2 两个 interact 加 `"at": "第2章_手机"`；ch3 三个 → `第3章_点饭` / `第3章_落座` /
  `第3章_办公室门`；ch4 两个 → `第4章_班群通知` / `第4章_坐下看资料`；ch5 一个 → `第5章_回座位`。
  点名以场景 dump 实测为准（2026-09-28 dump_interacts.py）。

## 风险与回归

- 第1章零改动路径：at 为空走旧 FindFree；leave 类型第1章 json 没有；回归靠第一章自检。
- C# 无编译验证手段（Unity 编译）：改动小而局部，写完人工过一遍语法/引用。
- json 是 TextAsset 数据文件（非 Unity 序列化资产），按既有惯例直接文本编辑，.meta 不动。
