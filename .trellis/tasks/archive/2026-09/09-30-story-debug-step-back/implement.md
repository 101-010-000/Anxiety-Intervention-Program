# Implement — 剧情调试：回退到上一句

## 改动清单

| 文件 | 位置 | 内容 |
|---|---|---|
| `Assets/Scripts/Story/StoryRunner.cs` | 字段区（`#if UNITY_EDITOR`） | `_chatLog`（屏幕气泡↔步号）、`_chatClearStep`（回退下限） |
| 同上 | `OnDestroy` / `Begin` | 订阅/退订 `PhoneChatUI.onCleared`，`Begin` 重置日志 |
| 同上 | `PlayText` 微信分支尾 | Append 后登记气泡步号 |
| 同上 | `Update` 首 | `Backspace` → `DebugBack()` |
| 同上 | 类尾部（`#if UNITY_EDITOR`） | `DebugBack()` / `IsTextStep()` / `TrimChatTo()` / `debugBackKey` |
| `Assets/Scripts/Story/PhoneChatUI.cs` | 字段 + `Clear` + 新增 `RemoveLast` + `Append` | `_msgs`/`_msgHeights` 记录、`onCleared` 事件、精确摘最后一条气泡 |

## 设计取舍

- **只在连续文本段内退**：往回扫到第一个非文本步骤（`interact/walk/fade/enter/leave/choice/card`）就停。
  避免重放世界状态、重新武装交互点、重跑 NPC 走位。
- **不跨手机聊天流的清空点**：`PhoneChatUI.Clear()`（`Show` 重新亮屏 / `SetContact` 换联系人）会触发
  `onCleared`，把 `_chatClearStep` 记为当时步号；`target < _chatClearStep` 时拒绝回退，保证
  手机内容 == 正常玩到该句时的状态。
- **回退即重播**：`StepIndex = target; Next();` 复用正常播放路径（打字机/语态/语音全一致），
  不做"只改文字"的旁路。

## 质量检查（trellis-check）

- 静态：两文件花括号配平（189/189、35/35）、`#if`/`#endif` 配对（6/6）、无同名符号冲突。
- **检查发现并已修**：`PhoneChatUI.RemoveLast()` 初版用 `_content.childCount - 1` 取最后一条，
  而 `Destroy` 帧末才生效 —— 连续回退两条气泡时会重复指向同一条、高度记错。改为用
  `List<RectTransform> _msgs` 记录节点引用后按引用回收。
- 范围纪律：仅两个运行时脚本；`PhoneChatUI` 的新增项都服务于本调试功能，无行为影响。
- 未执行项：Unity 编辑器未运行，**无法编译/Play 实测**（依赖域重载 + 手动按键）。
  需用户刷新资源编译后自测；正式构建由 `#if UNITY_EDITOR` 排除。

## 验证口径（用户侧）

1. 刷新 Unity 资源确保编译通过（无 CS 错误）。
2. 进 Play，连续对话里连按 `Backspace`：应逐句回退到本段首句后停住（Console 有提示）。
3. 微信段（第2/4章）回退：气泡数量、联系人与"正常玩到上一句"一致。
4. 选择题/交互段按 `Backspace`：无任何反应。
