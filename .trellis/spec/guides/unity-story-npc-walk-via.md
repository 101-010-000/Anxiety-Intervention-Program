# Unity 剧情 NPC 走位（enter/leave/via）约定

> 来源任务：09-28-story-playtest-4fixes（试玩反馈"陆宣雨退场穿桌椅"）。适用：任何剧情 NPC
> 走位/路径相关改动。完整背景见仓库 `AGENTS.md`「多章剧情」一节；本篇只列数据契约与硬性约定。

## 数据契约（StoryStep，JsonUtility 解析）

- `enter`：`who`（角色实例名）+ `from`（门口锚点）+ `to`（可选落点）+ `via`（可选途经锚点列表）
- `leave`：`who` + `to`（座位锚点，可选）+ `via`（可选途经锚点列表）
- 锚点解析统一走 `FindFadeAnchor`：runner 接线池 → 全场景按名找（**用户手放/拖动同名空物体即生效**）

### 错误矩阵（全部警告 + 降级，绝不抛错卡流程）

| 条件 | 行为 |
|---|---|
| who 找不到 / 不在场 | 警告，跳过该步骤（Next()） |
| via 某项锚点缺失 | 警告一次，跳过该点（路线少一段） |
| via 全缺 / json 没写 via | 两点直线（旧行为） |
| leave 无 to 且无原位快照 | 警告，跳过退场 |
| `RunPath(pts)` 长度 <2 | `Done=true` 直接返回（防崩） |

## 硬性约定

1. **走位 = transform 直移，不经物理**：`NpcEntrance.RunPath` 逐段直线 Lerp 匀速（SPEED=1.35），
   走位中主动关自身碰撞体、到位恢复。**穿模排查别往碰撞体/物理上找**——桌椅碰撞体只拦玩家的
   CharacterController，NPC 本来就"穿"过去，绕路只能靠 via 路线点。
2. **刻意不上 NavMesh**：剧情演出路线要可控可手调，别"顺手"引入 NavMeshAgent。
3. **起点语义**：enter 起点 = `from` 锚点；leave 起点 = 角色**当前位置**（= 主角交谈点，不是门口）。
4. **拐角平滑转向**：第 2 段起 `RotateTowards` 480°/s；`fast`（Skip 快进）= 剩余段同帧直达终态，
   自检快进**验证不了路径形状和走路动画**，验那些得真看或量骨头位移。
5. **`_anim` 选取过滤**：`enabled && runtimeAnimatorController != null`——NPC 实例根上有
   GameCharSwap 停用的死 Animator，不过滤就会把 Speed 写进死组件（"飘进来"事故）。
6. **路线点管理**：菜单工具 `多章剧情/补第2章陆宣雨路线锚点（幂等）` 只建 `路线_1`
   （**已存在绝不改位置**）；更多拐点 = json via 列表加名 + 场景放同名空物体。锚点在手，json 别写坐标。

## Wrong vs Correct

- ✘「NPC 穿桌椅 → 给家具/角色加碰撞体」——碰撞体拦不住 transform 直移，白改。
- ✔「NPC 穿桌椅 → 加/拖 via 路线点，让折线绕开家具，用户在 Scene 手拖定路线」。
