# Unity uGUI 场景 UI 改动（底板/标签/加图）约定

> 来源任务：09-30-choice-title-clarity（用户反馈"所有选择题标题都不清晰"）。
> 适用：任何给场景里 uGUI 文字/节点加底板、加图、调层级的程序化改动。
> 完整背景见仓库 `AGENTS.md`「干预选择题」一节；本篇只列硬性约定与排查口径。

## 渲染顺序契约（最易踩）

1. **uGUI 同一 Canvas 内 = 层级顺序渲染：父节点的 Graphic 永远先画（垫底），子节点后画（在上）**。
   → 想给文字加底板：**底板与文字必须是兄弟节点**（底板 sibling 在前），或"容器挂图+文字做子节点"
   （选项行的「行根挂云朵图 + 标题做子节点」即此结构）。
   → **底板千万别做成文字节点的子节点**——子底板会画在父文字上面，把字整个盖住
   （症状：报告里 Text 状态全正常 cull=False、rect 正确，画面上却"文字消失"）。
2. 同节点 Image+Text 按组件顺序画，且只共享一个 RectTransform（文字没内边距）——能不用就不用。

## 工具链怪癖（遇到别慌）

3. **该场景 ChoicePanel 的 Title 节点上 `AddComponent<Image>()` 恒返回 null**（组件清单健康、
   activeSelf=True 也一样，未深究根因）。要给已有节点加 Graphic 组件时，改用
   **`new GameObject(name, typeof(...))` 构造器建新节点再挂**（全项目工具实测可靠），并重接序列化引用。
4. CopySerialized 搬 Text 等组件：先 `AddComponent` 目标 → `EditorUtility.CopySerialized(src, dst)`
   → **先改序列化引用（如 ChoicePanel.titleLabel）再 DestroyImmediate(src)**，避免悬空引用。
5. `uGUI Text.material` 属性**运行时永远回落字体默认材质**——排查报告里看到"Text 上有材质覆盖
   Default UI Material"多为假象；判真覆盖要看序列化 `m_Material`（SerializedObject）是否 fileID 0。

## 验证口径

6. **场景 YAML 里中文名 = 带引号的大写 `\uXXXX` 转义**（如 `m_Name: "\u9898\u5E72\u5E26"`=题干带）。
   用中文原文 grep 场景文件**恒为 0，别误判"存盘失败"**；验节点用
   `'\"'+''.join('\\u%04X' % ord(ch))+'\"'`（大写 hex）字节搜，或直接信工具报告的运行时路径 + 预览图。
7. 编辑器渲染 UI 预览的既有套路（照 `MainMenuBuilder.RenderPreviews` / `ChoiceTitlePlate.Preview`）：
   临时切 `ScreenSpaceCamera` + 正交相机（orthoSize=540 → 1 UI 单位=1px）→ RT 1920×1080 渲两遍 →
   ReadPixels 存 PNG → **finally 里逐项还原 + 存盘 sheds 脏标记**。亮背景可读性测试 = clearFlags 换纯色再渲一张。
8. 排查"谁渲染歪了"：遍历 Canvas 下 activeInHierarchy 且未 cull 的 Graphic，打印
   路径/anchoredPosition/rect/颜色（模板见 `ChoiceTitlePlate.DumpDarkGraphics`）——
   颜色是快速指纹（本次靠墨蓝色锁定"消失的题干"和无关的 `交互提示` 残留）。

## Wrong vs Correct

- ✘「题干看不见 → 改颜色/加描边/查材质」——先查结构：底板是不是做成了文字的子节点。
- ✔「题干看不见 → DumpDarkGraphics 找墨蓝 Graphic 在哪 → 发现 Text 正常但被兄弟/子节点图盖住 → 改成兄弟节点结构」。
- ✘「给节点加组件用 AddComponent 失败 → 换 Undo.AddComponent 反复试」——直接构造器建新节点，别在怪癖上耗。
