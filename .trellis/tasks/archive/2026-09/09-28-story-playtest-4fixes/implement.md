# 执行计划

按依赖顺序实施（①②④ 互相独立，③ 依赖 StoryStep/StoryRunner 的 ④ 前置改动可并行写）：

1. [x] `StoryData.cs`：StoryStep 加 `at` 字段；头注释补 `leave` 步骤类型说明。
2. [x] `StoryInteractable.cs`：加 `Revive()`（⚠ 命名避开 MonoBehaviour.Reset 消息）。
3. [x] `FirstPersonController.cs`：`MoveWithInput` 开头清 Phone。
4. [x] `StoryRunner.cs` 四处：
   - a. 字段区：`_leaveRt`、`_npcHome`（who → 开场 pos/yaw 快照）。
   - b. `CollectAndHideEntranceNpcs`：扫 enter+leave、快照原位、只禁用 enter 的 who。
   - c. step 分派：`case "leave": DoLeave(step); break;` + `DoLeave/LeaveRoutine` 实现
     （复用 NpcEntrance.Run、State.Enter 权限）。
   - d. `EndRoutine`：chapterCardGroup 置顶后 SetActive。
   - e. `FindFree(m, at)`：at 精确匹配 + 同名 Revive 复用 + 兜底回退；interact 分派传 `step.at`。
   - f. `OnInteractableFired`：InteractF 命中 PHONE_TAKE_KEYS 时补拿手机动画。
5. [x] json：第2章（2 个 interact 加 at + 插 leave）、第3章（3 个 at）、第4章（2 个 at）、
   第5章（1 个 at）。
6. [x] AGENTS.md「多章剧情」一节补 5 条约定（leave / at 绑定 / Revive / 章末卡置顶 / 走路清 Phone）。
7. [x] 记忆文件 playtest-bugs-2026-09-28.md 标记已修。
8. [x] leave/enter 走位支持 via 经由点（2026-09-29 试玩追加：陆宣雨退场直线穿桌椅——
   NpcEntrance 本就是直线 Lerp 直移 transform，不经物理，门口(74.54,-2.45)→座位(75.42,4.07)
   恰好纵贯西墙桌椅区）：
   - a. [x] `StoryData.cs`：StoryStep 加 `List<string> via`（同名解析同 to，缺锚点警告跳过）。
   - b. [x] `NpcEntrance.cs`：新增 `RunPath(Vector3[] pts, Transform faceTarget)` 分段走
     （全程 SetWalk/关碰撞体，段间 RotateTowards 平滑转向，fast 快进语义不变）；
     `Run(from,to,faceTarget)` 改薄包装，行为不变。
   - c. [x] `StoryRunner.cs`：LeaveRoutine / EnterRoutine 解析 step.via → pts。
   - d. [x] `第2章.json`：leave 加 `"via": ["第2章_陆宣雨路线_1"]`。
   - e. [x] `ChapterStoriesSetup.cs`：幂等菜单「补第2章陆宣雨路线锚点」（缺省过道估计位
     world (77.6, 0, 0.8)，已存在不动；用户 Scene 里手拖生效）。

## 验证（交给用户在 Unity 里跑，不远程触发）

- Unity 重编译零报错（脚本改动触发自动编译）。
- `Tools/干预项目/第1章剧情运行自检`（回归）→ `_报告/_第一章剧情运行自检.txt` 全步通过。
- `Tools/干预项目/第2章剧情运行自检` → 108/108 步、Interact 2、Leave 生效、无报错。
- `Tools/干预项目/第3章剧情运行自检` → 全步通过（at 绑定后 Fire 的点即步骤对应的点）。
- 真机试玩第2章：走路低头漂移消失；陆宣雨对话完走回座位；章末显示「第二章 完」卡。

## 收尾记录（2026-09-29 归档时）

- via 经由点方案 trellis-check 核验通过（P0/P1/P2 零问题）；代码改动已随 `161d262a` 入库。
- 用户真机试玩第2章发现退场穿桌椅 → 追加第 8 项（via 走位）并已交付；用户 09-29 跑菜单建出
  场景路线点 `第2章_陆宣雨路线_1` 并定稿**单拐弯方案**（曾短暂配双点已按要求撤销）。
- 用户侧遗留（不影响归档）：把路线点拖到过道合适位置 → 再试玩一次第2章看退场效果。
- 期间并行窗口持续提交（坐姿系统/存档续播/走位 v6 等），归档前工作区仅剩本任务 3 文件 + 1 个
  无关报告（主角第三人称自检，留给并行窗口）。
