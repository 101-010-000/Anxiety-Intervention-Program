# 技术设计：第1章微信段切视角到林溪

## 改动总览（4 个文件 + 1 个工具 + 1 个脚本跑批）

| # | 文件 | 改动 |
|---|---|---|
| 1 | `Assets/数据/剧情/第1章.json` | 插 2 个 cut 步骤（纯数据，直接编辑） |
| 2 | `Assets/Scripts/Story/StoryStep` 所在文件（`StoryData.cs`） | StoryStep 加可选 `camH`/`lookH` |
| 3 | `Assets/Scripts/Story/StoryRunner.cs` + `CutawayCamera.cs` | DoCut 透传机位参数；Show 加带默认值的重载 |
| 4 | `Assets/Editor/LinxiCutviewSetup.cs`（新建） | 幂等接线工具：Animator+controller+SitHere |
| 5 | `额外文件/工具脚本/voice_shift_steps.py`（改顶部常量后运行） | 步号键位移 ×2 |

## 1. json 插步（先插后面的，再插前面的）

0-based 下标：在 54/55 之间插 `{"t":"cut","who":"林溪"}`；在 80(choice)/81 之间插 `{"t":"cut"}`。
⚠ 编辑前确认 json 是裸数组还是 `{"steps":[...]}`（StoryData.cs 的读法为准）。
⚠ 先插位置 81（偏后的）再插位置 55，避免位置互相影响。

## 2. 语音步号位移（无需重新合成）

- 两次位移按**位置从大到小**跑：先 P=81（choice 后）×1，再 P=55 ×1（第二次跑时原 55 仍叫 55，
  因为第一次只动了 ≥81 的键）。
- ⚠ 脚本「改键在前、改名在后」，失败重跑前先看输出；半途状态用 `voice_shift_steps_fix.py` 补完（AGENTS 已警示）。
- 跑完把 `额外文件/语音生成/output/ch1/manifest.json` 拷成 `Assets/Resources/语音/ch1/manifest.txt`
  （脚本若已两处同步则只核对），诊断对账留到用户刷新 Unity 后执行。

## 3. cut 机位参数（向后兼容）

- `StoryStep` 加 `public float camH = -1f; public float lookH = -1f;`（-1=用缺省）。
- `CutawayCamera.Show(Transform target, float camH = -1f, float lookH = -1f)`：
  ≤0 时取类内默认 1.55/1.35。LateUpdate 用实例字段（Show 时定死，不每帧读）。
- `DoCut`：`CutawayCamera.Show(t, step.camH, step.lookH)`。
- 林溪这题 json 写 `"camH": 1.15, "lookH": 0.95`（坐姿头高 ~0.95）。
- 第3章 json 不动 → 全部走缺省 → 行为零变化。

## 4. 场景接线工具 `Assets/Editor/LinxiCutviewSetup.cs`（幂等）

- 菜单 `Tools/干预项目/第1章林溪切视角/接线（幂等）`；触发器 `Assets/_linxicutview_trigger.txt`（跑完自删）。
- 找实例：全场景（含禁用）找 GO 名恰为「林溪」、祖先有匹配 `^第[0-9一二三四五]章角色$` 的容器
  （**必须限定容器**，防止将来别人摆同名节点被误接线）；找不到/找到多个 → 报告警告并中止。
- 接线（缺啥补啥，已有的不动）：
  1. `Animator`：无则 AddComponent；`runtimeAnimatorController = Animators/带动画模型/林溪_Idle.controller`；
     ⚠ FBX 实例的 Animator 必须在**模型根**上（Generic 按节点路径回放，路径相对 Animator 所在节点）。
  2. `SitHere`（`Assets/Scripts/Game/SitSpot.cs`，Start 置 Sitting=true）：无则 AddComponent。
  3. 自检（编辑器即可）：controller 非空、`controller.parameters` 含 `Sitting`、
     Sit 状态存在且 motion=`林溪_Sit`；实例Renderer bounds 中心离地高度记录进报告（坐姿应明显低于站姿）。
- 报告 `assets/_报告/_第1章林溪切视角.txt`（实例路径/摆位/接线明细/自检结果）；跑完保存 Game.unity。
- ⚠ 场景里若检测到该实例缺 Animator 却也找不到 controller 资产 → 报错落盘 `额外文件/错误_*.txt`，不静默。

## 5. 验证

1. 用户刷新 Unity → 触发器跑接线工具 → 报告核对。
2. 丢 `Assets/_story1smoke_trigger.txt` → 第1章自检（快进路径 cut 直接 Restore，不卡步骤）
   → `assets/_报告/_第1章剧情运行自检.txt`。
3. 用户真机 Play 到微信段目检（坐姿 + 切入切回 + 气泡正常）→ trellis-check → 提交。

## 6. 手机视角联动（第二轮迭代，2026-10-01 用户试玩反馈）

试玩发现：切到林溪视角后，手机 UI 仍按徐夏的手机渲染（徐夏的消息在右蓝泡、标题显示「林溪」）。
需求定稿：**手机 = 当前视角角色的手机**——

- `PhoneChatUI` 新增 `phoneOwner`（缺省「徐夏」）+ `SetOwner()`：`IsMine()` 按主人判（不再写死
  `Contains("徐夏")`）；头像按【发送者身份】取（`AvatarOf`：徐夏→avatarRight 槽那张脸、其余→avatarLeft），
  不再按左右槽位取——气泡换边时头像必须跟着人走。精灵序列化引用不变，场景零改动。
- `StoryStep` 新增可选 `contact`：cut 步骤带 `"contact":"徐夏"` → `DoCutNow` 里 `SetOwner(who)` +
  `SetContact(contact)`（标题换人）。切回（who 空）与 `ToDone` 兜底都把 owner 复位「徐夏」。
- `SilentApply` 的 cut 分支同样复刻 owner/contact——续播点落在切视角区间里时，`FlushResumeChat`
  的微信回放才按对面的手机渲染。
- 回归面：第2章微信段、第4章班群/学姐不经过 cut → owner 恒为徐夏，行为零变化；第3章李老师
  cut 无微信台词，联动无害。
- ★ check 补抓的 P1（已修）：`PlayText`/`FlushResumeChat` 的「对方开口=换聊天对象」判定原来写死
  `!s.Contains("徐夏")`——林溪视角下林溪开口会被错判成换段、标题错回「林溪」，续播路径还会清掉
  已回放记录。已改为 `!s.Contains(phoneChat.CurrentOwner)`（新增只读属性），徐夏视角行为逐一等价；
  `SetContact` 的「同段不重置」短路保留（第2/4章换段清流依赖它）。

## 回滚

- json：git 还原 `第1章.json`；语音键：`voice_shift_steps.py` 无自动回滚——回滚 json 前先确认是否需要逆位移
  （本次两处各 +1：逆操作 = 分别在 56/82 位跑 ×1 的负向，或直接重新全量对账）。
- 场景：git 还原 Game.unity（工具只动 林溪 实例子树）。
- 运行时代码：git 还原 StoryData/StoryRunner/CutawayCamera。
