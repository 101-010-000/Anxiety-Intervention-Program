# 第1章微信段切视角到林溪（宿舍坐姿 cutaway）

## Goal

第1章微信段（徐夏↔林溪）按《剧本更新.docx》要求切到林溪视角：从「手机震动了一下」起，
镜头切成宿舍里林溪的第三人称跟拍（cutaway），玩家以林溪视角看完微信往来并作答
「帮林溪补充一句鼓励回复」选择题，答完切回徐夏继续原剧情。用户已手动在场景里摆好林溪实例。

## 剧本依据（额外文件/剧本更新_extract.txt 62–103 行）

- 65 行：（视角切换至林溪）→ 66：手机震动了一下。→ 67：是徐夏发来的。→ 68：（玩家拿起手机）
- 72/78 行：林溪抱着手机琢磨回复 / 林溪稍微坐正了一点身体（→ 坐姿演出）
- 83–86 行：林溪内心独白（json 已有 mon s=林溪）
- 92–100 行：请代入林溪的视角，帮林溪补充一句鼓励徐夏的话（3 选项 = json 第 80 步现有 choice）
- 101–103 行：（视角切换回徐夏）徐夏看着手机……不过，无论如何，都该回宿舍了。（json 已有）

## 现状（2026-09-30 已核实，勿重查）

1. **用户已加场景实例**：`GameRoot/Locations/Loc_宿舍/第一章角色`（active=True，用户手建），
   子节点 = PrefabInstance，源资产 = `assets/03_动作_Animation/带动画模型/林溪/Sitting Idle.fbx`
   （guid c535bfe6c1ef58344b7250326545fb6f），名字覆盖=「林溪」，
   local pos(-1.685, 0.073, 1.009)，yaw 68.6°，**m_AddedComponents 为空**
   → 没有 Animator/控制器，现在是静止绑定姿势，切过去不会是坐姿。场景已保存（22:51）。
2. **第4章的 `Loc_宿舍/第四章角色/林溪_宿舍`（新模型换装版）是另一实例，不得动**。
3. `StoryRunner.DoCut`（~678 行）现成：who 空 → `CutawayCamera.Restore()`；
   否则 `FindCharacterTransform(step.who)` → `CutawayCamera.Show(t)`；找不到只警告跳过（不卡流程）。
4. `FindCharacterTransform`：精确名匹配 + 优先【本章容器】子树（正则 `^第([0-9一二三四五])章角色$`）
   → 第1章 runner 找「林溪」必中用户实例（`林溪_宿舍` 不同名，不会误抓）。
5. `CutawayCamera.Show`：distance 2.6 / height 1.55 / headY 1.35，LateUpdate 跟拍，
   自动关 FP_相机、开 URP 后期；`Restore()` 恢复。
6. 第1章 json（121 步，0-based 下标）：54=徐夏坐直身子点开聊天框(nar，**留在徐夏视角**) /
   55=手机震动了一下 / 56=是徐夏发来的 / 57-59=徐夏微信 / 60=林溪抱着手机 / 61-62=林溪微信 /
   66=坐正身体 / 71-74=林溪 mon / 76-79=林溪微信 / **80=choice（帮林溪补充鼓励话）** /
   81-82=徐夏看着手机…(nar) / 83=都该回宿舍了。
7. 语音：`Assets/Resources/语音/ch1/manifest.txt` 键 = `ch1:{步号}`；cut 是非语音步骤
   **不需重新合成**，但插步会让后续台词步号整体移位。
8. 容器显隐：`第一章角色` active=True → 第1章全程可见（切视角前玩家根本看不到宿舍，无穿帮）；
   第2-5章 runner Begin 只保留本章容器 → 到第2章自动隐藏 ✓ 无需改显隐逻辑。
9. 坐姿接线先例：`陆宣雨_可动_坐着` = Sitting Idle FBX 实例 + `SitHere`（Start 把 Sitting 置 true）；
   `Animators/带动画模型/林溪_Idle.controller` 已被 SitSetup 加过 Sitting 参数 + Sit 状态，
   剪辑与用户实例同源（同一副 Mixamo 林溪骨架）→ Generic 控制器按节点路径回放，直接可驱动。

## Requirements

- R1 `第1章.json`：54/55 之间插 `{"t":"cut","who":"林溪"}`；80(choice) 之后、81 之前插 `{"t":"cut"}`（切回）。
- R2 语音步号位移：两处非语音插入 → 用 `额外文件/工具脚本/voice_shift_steps.py` 同步 manifest 键与
  mp3 文件名（连 .meta，`Assets/Resources/语音/` 与 `额外文件/语音生成/output/` 两处）
  → Unity 菜单 `Tools/干预项目/台词语音/诊断` 对账（`assets/_报告/_台词语音.txt` 无缺）。
- R3 坐姿接线（幂等 Editor 工具）：给「林溪」实例确保 Animator（缺则加）+
  controller=`林溪_Idle.controller` + `SitHere`（缺则加，已有的不动）；存场景 + 报告。
- R4 cut 可选机位参数：`StoryStep` 加可选 `camH`/`lookH`（缺省 = 现值 1.55/1.35，
  第3章 json 零改动零影响）；林溪这次用坐姿机位（建议 camH≈1.15 / lookH≈0.95，实现时按摆位微调）。
- R5 第1章剧情运行自检跑通（`Tools/干预项目/第1章剧情运行自检`）。
- R6 遵循仓库约定：场景改动只走 `Assets/Editor/*.cs` 幂等工具；报告进 `assets/_报告/`；
  临时脚本用完归档 `额外文件/历史Editor脚本/`。

## Constraints

- 不动 `林溪_宿舍`（第4章）与第4/5章 json。
- 不动 ChoicePanel / 选择题规则（**用户定稿 2026-09-30：保持全选交卷**，这题只是发生在林溪视角）。
- 不动「对话框/名字/对话内容」三个节点（2026-09-23 铁律）。
- 第3章 cut（李老师）行为零变化。
- **不用 MCP 驱动 Unity 编辑器**（用户在场）：Editor 工具以菜单 + 触发器文件交付，
  由用户刷新 Unity 执行；自检同样丢 `Assets/_story1smoke_trigger.txt` 由用户触发。

## Acceptance Criteria

- [ ] Play 第1章到微信段：第 55 步起镜头在宿舍林溪（坐姿、播 Sitting 动画），微信气泡 UI 照常显示；
      第 80 题答完切回徐夏，81 步起演出照旧。
- [ ] 语音诊断：`_台词语音.txt` 剧本覆盖无缺（位移后键全部命中）。
- [ ] 第1章自检报告通过（快进路径 cut 直接 Restore，不会卡步骤）。
- [ ] 第3章 cut 不受影响（缺省机位参数不变）。
- [ ] 场景工具重跑幂等；报告落 `assets/_报告/_第1章林溪切视角.txt`。
