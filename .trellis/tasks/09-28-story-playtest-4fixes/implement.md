# 执行计划

按依赖顺序实施（①②④ 互相独立，③ 依赖 StoryStep/StoryRunner 的 ④ 前置改动可并行写）：

1. [ ] `StoryData.cs`：StoryStep 加 `at` 字段；头注释补 `leave` 步骤类型说明。
2. [ ] `StoryInteractable.cs`：加 `Revive()`（⚠ 命名避开 MonoBehaviour.Reset 消息）。
3. [ ] `FirstPersonController.cs`：`MoveWithInput` 开头清 Phone。
4. [ ] `StoryRunner.cs` 四处：
   - a. 字段区：`_leaveRt`、`_npcHome`（who → 开场 pos/yaw 快照）。
   - b. `CollectAndHideEntranceNpcs`：扫 enter+leave、快照原位、只禁用 enter 的 who。
   - c. step 分派：`case "leave": DoLeave(step); break;` + `DoLeave/LeaveRoutine` 实现
     （复用 NpcEntrance.Run、State.Enter 权限）。
   - d. `EndRoutine`：chapterCardGroup 置顶后 SetActive。
   - e. `FindFree(m, at)`：at 精确匹配 + 同名 Revive 复用 + 兜底回退；interact 分派传 `step.at`。
   - f. `OnInteractableFired`：InteractF 命中 PHONE_TAKE_KEYS 时补拿手机动画。
5. [ ] json：第2章（2 个 interact 加 at + 插 leave）、第3章（3 个 at）、第4章（2 个 at）、
   第5章（1 个 at）。
6. [ ] AGENTS.md「多章剧情」一节补 5 条约定（leave / at 绑定 / Revive / 章末卡置顶 / 走路清 Phone）。
7. [ ] 记忆文件 playtest-bugs-2026-09-28.md 标记已修。

## 验证（交给用户在 Unity 里跑，不远程触发）

- Unity 重编译零报错（脚本改动触发自动编译）。
- `Tools/干预项目/第1章剧情运行自检`（回归）→ `_报告/_第一章剧情运行自检.txt` 全步通过。
- `Tools/干预项目/第2章剧情运行自检` → 108/108 步、Interact 2、Leave 生效、无报错。
- `Tools/干预项目/第3章剧情运行自检` → 全步通过（at 绑定后 Fire 的点即步骤对应的点）。
- 真机试玩第2章：走路低头漂移消失；陆宣雨对话完走回座位；章末显示「第二章 完」卡。
