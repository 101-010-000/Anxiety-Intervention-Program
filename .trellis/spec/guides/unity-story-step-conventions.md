# Unity 剧情步骤扩展与交互点道具联动约定

> 来源任务：09-29-office-enter-exit-flow 收尾批（leave `hide:true` + 手机×第2/4章交互），
> trellis-check 2026-09-29 全过。适用：任何 StoryStep 新字段、StoryInteractable 行为改动。
> 完整背景见仓库 `AGENTS.md`「多章剧情」「手机道具」两节；本篇只列数据契约与硬性约定。

## 一、StoryStep 新增字段：必须同步三条消费路径

剧情步骤字段（JsonUtility 直解析）的每个新语义都要在**三条路径**各自落地，漏一条就是
"正常玩没事、读档/自检出鬼"型 bug：

| 路径 | 入口 | 说明 |
|---|---|---|
| ① 实时演出 | `StoryRunner.Next` 分发的 Do* / 协程 | 玩家实际玩到的路径 |
| ② 存档续播快进 | `SilentApply(step)` | 读档 0..step-1 静默重放副作用（传送/站位/显隐/消费交互点） |
| ③ 自检快进 | `DebugAdvance` / `_entrance.Skip()` | 冒烟自检快进到等待态用的路径 |

- ③ 通常复用 ① 的协程（`fast=true` 同帧直达终态），加字段时确认快进分支也会走到新语义
  （hide 在 `LeaveRoutine` 尾部，Skip 走完 RunPath 后自然到达 ✓）。
- ② 永远要单独写：SilentApply 不跑协程，只做终态副作用（`if (step.hide) go.SetActive(false)`）。
- **验证口径**：新增字段后跑一次「存档续播运行自检」（读该章中段存档）+ 该章剧情自检，
  两份报告都要过。

### Wrong vs Correct

- ✘ 只在 DoLeave 协程里加 `hide` 分支 → 试玩正常，读档续播后林溪又站在门口。
- ✔ 协程终态 + `SilentApply` 终态各写一份；快进路径确认自然覆盖。

## 二、StoryInteractable.propObjectName 道具联动契约

交互点可联动一个场景道具（当前用于 `手机_淡蓝`：第2章两次拿手机 + 第4章班群通知）：

```
propObjectName : string   // 场景 GameObject 名，空 = 旧行为（零变化）
```

- **判定中心 = 道具位置**（`PromptCenter`，水平距离清 y）；道具没找到 → 回退自身位置 + 警告一次。
- **Fire() → 道具 `SetActive(false)`**（「被拿起」的可见反馈）；
  **Revive()/重新武装 → `SetActive(true)`**（"快要下次交互的时候再出现"，用户定稿语义）。
- **按名解析、缓存引用**：隐藏后是 inactive，`GameObject.Find` 找不到 → 解析一次就缓存
  （`_prop`）；找 miss 置 `_propMissing` 防每帧 Find，每次武装（ShowProp）再给一次机会。
  ★ 用户手挪道具自动跟随——别改成序列化引用，"手摆物件按名生效"是本仓库约定。
- **换章天然复位**：进章 = LoadScene("Game")，道具激活态随场景重载还原；接线工具重跑会把
  遗留隐藏的道具重新激活兜底。
- 接线工具：`Tools/干预项目/手机道具/③ 摆进宿舍+接第2/4章交互`（幂等，写 radius=1.2m +
  propObjectName + 存场景）；prompt 文案**手调优先**（工具只在为空时填默认值）。

### 错误矩阵

| 条件 | 行为 |
|---|---|
| propObjectName 为空 | 全部新逻辑 no-op（第1章等交互点零变化） |
| 按名找不到道具 | 警告一次，判定回退自身位置，隐藏/重现不生效 |
| 道具被用户删了 | 同上，不抛错不卡剧情 |
