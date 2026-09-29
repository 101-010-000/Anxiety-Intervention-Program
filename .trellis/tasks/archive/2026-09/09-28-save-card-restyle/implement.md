# 执行清单

> 顺序执行；1-4 是纯代码（agent 可完成），5-8 需要用户在 Unity 里触发/目检。

## 阶段一：代码（agent）
- [ ] 1. 写 `Assets/Editor/SaveCardArt.cs`：菜单 `Tools/干预项目/存档卡定妆照/`（①渲染5章定妆照 ②接线存读档槽位 ③渲染槽位预览）+ `Assets/_savecard_trigger.txt` 消费入口（依次①②③，用完自删触发器）。幂等。常量置顶（RADIUS/BAND_H/BAND_ALPHA/BAND_COLOR/章-地点表）。
- [ ] 2. 改 `Assets/Scripts/UI/SaveSlotUI.cs`：`public static Sprite[] ChapterArt`；Refresh() 优先定妆照（chapter-1 越界/空 → 回退 ThumbnailCache 旧截图）；`frameFilled` 恒 false。
- [ ] 3. 改 `Assets/Scripts/UI/MainMenuUI.cs`：序列化字段 `chapterArt`（Sprite[5]）；Awake 注入 `SaveSlotUI.ChapterArt`（null 容错）。
- [ ] 4. 自查：编译级语法、命名/注释风格与相邻代码一致；不引入 AddComponent（规避 MonoScript 坑）；不删 StoryRunner 抓屏。

## 阶段二：Unity 内执行（用户配合）
- [ ] 5. 用户在 Unity 里 Ctrl+R / 点一下窗口（消费触发器；或手动跑 ①②③ 菜单）。产出：
  - `Assets/assets/05_UI/存档插图/ch1..ch5.png`
  - `assets/_报告/_存档卡定妆照.txt`
  - `assets/_报告/预览/存档卡/*.png`
- [ ] 6. 目检预览：圆角、暗带、文字清晰、无 UI 入镜、ch2/ch5 画面有区分。

## 阶段三：验证与收尾
- [ ] 7. 跑 `Tools/干预项目/主界面运行自检` → `assets/_报告/_主界面运行自检.txt` 全通过。
- [ ] 8. `git diff` 检查场景文件：仅存读档页 6 个槽位子树 + MainMenuUI 字段变化；其他页零变化。
- [ ] 9. trellis-check → update-spec → commit（用户确认效果后提交）。

## 验证命令
```bash
# 报告与产物
ls "3D剧情项目/Assets/assets/_报告/预览/存档卡/"
cat "3D剧情项目/Assets/assets/_报告/_存档卡定妆照.txt"
# 场景 diff 范围
git diff --stat -- "3D剧情项目/Assets/Scenes/MainMenu.unity"
```

## 回滚点
- 阶段一全部是代码 → `git checkout -- <file>`；
- 场景改动由补丁工具落盘， revert 场景文件即可；工具幂等可重跑。
