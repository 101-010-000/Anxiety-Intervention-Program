# Journal - 黑蛋仔 (Part 1)

> AI development session journal
> Started: 2026-09-15

---



## Session 1: 陆宣雨退场穿模：via 经由点走位 + 任务收尾归档

**Date**: 2026-09-29
**Task**: 陆宣雨退场穿模：via 经由点走位 + 任务收尾归档
**Branch**: `ugui`

### Summary

排查第2章陆宣雨退场穿桌椅：根因=NpcEntrance 直线 Lerp 直移 transform 不经物理（项目无 NavMesh）。实现 enter/leave 的 via 经由锚点列表（RunPath 分段走+段间平滑转向，缺锚点警告兜底，trellis-check 零问题）；代码已随并行窗口 161d262a 入库。用户跑菜单建出场景路线点并定稿单拐弯方案（双点方案已撤销）。本次收尾提交：路线点落场景 + AGENTS.md/spec 沉淀 + 任务归档。用户遗留：拖路线点到过道后再试玩验证。

### Git Commits

| Hash | Message |
|------|---------|
| `4ce25595` | (see git log) |

### Status

[OK] **Completed**


## Session 2: 剧情调试：回退到上一句（仅编辑器）

**Date**: 2026-09-30
**Task**: 剧情调试：回退到上一句（仅编辑器）
**Branch**: `ugui`

### Summary

给 StoryRunner 加仅编辑器的逐句回退：Play 中按 Backspace 退回当前连续文本段的上一句并重播（打字机/语态/语音）；微信段用 PhoneChatUI.RemoveLast 精确摘气泡。trellis-check 抓到并修掉 RemoveLast 的 Destroy 帧末生效取错节点问题；spec/AGENTS.md 已沉淀。Unity 未运行，编译与 Play 实测待用户。

### Main Changes

- StoryRunner：新增 `#if UNITY_EDITOR` 调试块（_chatLog/_chatClearStep、Backspace→DebugBack、TrimChatTo + onCleared 同步）
- PhoneChatUI：新增 RemoveLast（_msgs/_msgHeights 精确回收）+ onCleared 事件
- spec/guides/unity-story-step-conventions.md：新增 §三 调试回退的两个硬边界
- AGENTS.md：§六速查加调试回退行

### Git Commits

(No commits - planning session)

### Testing

- [OK] 静态检查通过：花括号配平、#if/#endif 6:6 配对
- [OK] trellis-check 发现 PhoneChatUI.RemoveLast 初版 childCount 取错节点 → 已修为 _msgs 引用回收
- [OK] 未执行：Unity 未运行，编译/Play 实测待用户自测（Backspace 逐句回退、微信气泡一致）

### Status

[OK] **Completed**

### Next Steps

- 用户在 Unity 刷新编译后试 Play：连按 Backspace 回退、微信段气泡、非文本态无反应
