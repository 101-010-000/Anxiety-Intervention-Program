# Implement：第三章办公室段改为李老师视角演出

## 前置（部分已完成）

- [x] 走廊李老师实例就位并改名 `李老师_走廊`（用户摆放，2026-10-01）
- [x] `徐夏_办公室坐姿` 复制到 `Loc_办公室/第三章角色`（prefab 链接 + 徐夏_第三人称 + SitHere）
- [ ] **等用户**：把 `徐夏_办公室坐姿` 摆到办公室沙发上（位置+朝向）
- [ ] **等用户**：确认 `第3章_李老师视角` 锚点语义（默认当作老师进屋后的站位/落点）

## 步骤

1. **引擎：stage 步骤**（`Assets/Scripts/Story/StoryRunner.cs`）
   - [x] StoryStep 增加 `show/hide`（List<string>）、`hidePlayer/showPlayer`（bool）、`standAt`（string）字段（StoryData.cs）
   - [x] DoStage：按名解析 show/hide 物体 → SetActive；hidePlayer/showPlayer → `FirstPersonController.SetStandingModelVisible`；standAt → 关 CC/挪/开 CC + ResetCameraNow；完事 Next()
   - [x] SilentApply 增加 stage 分支（同步执行同一操作）
   - [x] ToDone 兜底 showPlayer=true + 坐姿模型 hide（防中途退出剧情留下隐形/重影）
   - [x] 编译 0 错误（2026-10-01 refresh_unity 后 console 无 error CS）

1.5 **第二轮反馈修复（2026-10-01 用户试玩后）**
   - [x] `bg` 字段（leave 后台走位）+ `auto` 字段（nar 播完自动推进，复用 Gap 的 AutoPlay 通道）
   - [x] cut 遇 `_leaveRt` 未完 → `CutAfterWalk` 等走完再切（黑屏切实例零残帧保持）
   - [x] stage hidePlayer 顺带挂起 SitSpot（修"两个徐夏"：黑屏传送触发「被传走→起身」把站立模型亮回来）；showPlayer/ToDone 恢复
   - [x] CutawayCamera 站定可环绕/俯仰（移动自动回正；0° 与旧机位一致）
   - [x] json：走廊 leave 挪到三句旁白前（bg），去 via（直线走到门口），旁白加 auto
   - [x] 3 句旁白语音键 +1 定向改名（双 manifest 集合比对对齐 ✓，不重新合成）
2. **CutawayCamera 可选机距**（仅当走廊跟拍实测蹭墙才做）
   - [ ] cut 步骤可选 `dist`，CutawayCamera.Show 重载；默认行为不变
3. **json：第3章.json 按 design.md 草案改写**
   - [ ] 65 步 who 改 `李老师_走廊`；删 71–75；插 leave/fade/stage/cut/leave；119 后插 cut + stage(standAt)
   - [ ] 精确计算插删差值 → 决定语音键是否位移（目标：零位移或一次 shift）
4. **场景接线工具** `Assets/Editor/Ch3TeacherPovSetup.cs`（幂等）
   - [ ] 补建锚点：`第3章_老师到门口`（走廊侧办公室门内 0.6m 朝门）、`第3章_老师路线_1..2`（走廊拐点默认估计位）、`第3章_老师站位`（办公室内、面向坐姿模型）、`第3章_办公室起身`（沙发侧、面向门口）
   - [ ] 校验：走廊/办公室李老师实例（活 Animator、材质非空）、坐姿模型存在且在第三章角色容器、stage 引用可解析
   - [ ] 报告 `assets/_报告/_第三章老师视角.txt`；场景保存
5. **语音对账**
   - [ ] 若有位移：跑 `额外文件/工具脚本/voice_shift_steps.py`（CHAPTER=3，SHIFT/THRESHOLD 按实算）
   - [ ] Unity 跑 `Tools/干预项目/台词语音/诊断` → `assets/_报告/_台词语音.txt` 467/467 ✓
6. **自检**
   - [ ] `Tools/干预项目/第三章剧情运行自检`（先保存场景）→ `assets/_报告/_第三章剧情运行自检.txt`
   - [ ] 手工过一遍关键镜头：走廊跟拍不穿墙、门口消失被黑屏盖住、办公室跟拍、切回起身、出门
7. **收尾**
   - [ ] 截图存档 `assets/_报告/预览/场景/`（走廊跟拍/办公室跟拍/切回后）
   - [ ] trellis-check 全量检查 → 提交（中文提交信息）

## 验证命令

- 编译：看 `Editor.log` 尾部 / `Library/ScriptAssemblies/*.dll` mtime
- 语音诊断：`Tools/干预项目/台词语音/诊断（清单↔mp3↔剧本覆盖）`
- 剧情自检：`Tools/干预项目/第三章剧情运行自检`（触发器 `_story3smoke_trigger.txt`）

## 回滚点

- json 单文件还原即可回退剧情；stage 分支删除即回退引擎；场景锚点/实例留存无害。
