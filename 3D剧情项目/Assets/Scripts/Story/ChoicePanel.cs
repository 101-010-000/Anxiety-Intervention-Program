// 干预选择题面板：「按钮 ⇄ 解释」互斥切换（用户定稿 2026-09-27）——
//   点选任一选项 → 该行【变灰】（Button 的 ColorTint disabled 态，interactable=false 自然变灰；
//     ★不换贴图：用户已在场景里把行手调成蓝色云朵气泡，运行时绝不碰每行的 Image.sprite）
//   → 进入【解释态】：选项行、确认按钮、面板自己的 Dim 全部临时隐藏，
//     解释借【底部对话框】播放（DialogueUI.PlayLine，mon 独白样式：无名牌、浅蓝灰斜体）。
//     ★解释渲染在黑色遮罩之上：Dim 藏掉后，「对话」框直接坐在 UI交互 画布最底层的「遮罩」之上，
//       中间没有 ChoicePanel 的东西挡着 → 清晰可读（以前解释发暗就是被这层 Dim 盖的）。
//   → 玩家点击任意处（全屏透明捕获层接住）→ 退出解释态：
//     选项行重新出现（已选行因 interactable=false 自然是灰的、点不动）、confirm 恢复显示
//     （未全选仍灰、全选则点亮）→ 继续选剩下的……如此往复，直到全选 → "记入焦虑记录本" → 收档回剧情。
// 未选完不能关闭（ESC/点空白都不行），选择顺序记进 PlayerPrefs 供二期联动概览页。
//
// ★ 选项行为【场景预置】（最多 3 行，选项_0/1/2，用 Tools/干预项目/生成选择题按钮行 生成/接线）：
//   Open() 只做：写题干与每行文字、复位状态（按钮恢复可点/解释态复位）、按剧本选项数开关行，
//   并先把对话框收起来。运行时【绝不 Destroy】行、也【绝不覆盖】用户在场景里手调的贴图/字号/颜色。
//   rowNormal/font/headColor/bodyColor 仅是 Editor 工具生成行时的默认值。
// 解释播出框 = StoryRunner.Instance.dialogue（public 静态入口，无需任何新场景接线）；
// 选择题期间 StoryRunner 在 State.Choice，Update 的 switch 没有 Choice 分支、OnLineTyped 只在 Typing 态
// 动作 → 播解释/收解释都不会推进剧情、不冒 ▼。
//
// ★ 解释点击捕获层（运行时懒建，不落盘）：ChoicePanel 根下新建「解释点击层」——
//   stretch 全屏、透明 Image(raycastTarget=true) + Button，面板子树最上层，天然接住全屏点击；
//   只在解释展示期间 SetActive(true)。开场只建一次，重开面板复用；面板一收，下次 Open 复位。
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ChoicePanel : MonoBehaviour
{
    [Serializable]
    public class ChoiceRow
    {
        public GameObject root;          // 选项_0/1/2（行根）
        public Image headImage;          // 头 的 Image（运行时不改它：变灰交给 Button disabled tint）
        public Button headButton;        // 头 的 Button（选中后禁用 → 自然变灰、不可再点）
        public Text headText;            // 头/标题
        // 旧版行里还有 body/bodyText/bodyLayout（行内解释区），解释已改走底部对话框，字段已删——
        // 场景里旧数据多出的这几个序列化键 Unity 会静默忽略，无需清理场景文件。
        // 旧版还有 rowSelected（选中态贴图）：已删，场景里残留的序列化键同样被静默忽略。
    }

    [Header("引用（工具自动接）")]
    public UIPanel panel;              // 淡入淡出 + 遮罩（复用主菜单 UIPanel）
    public Text titleLabel;            // 题干
    public RectTransform rowsParent;   // 选项行容器（Content，带 VerticalLayoutGroup）
    public ScrollRect scroll;
    public Button confirmButton;
    public Text confirmLabel;

    [Header("样式（工具生成行时的默认值；运行时不覆盖场景里手调的样式）")]
    public Sprite rowNormal;
    public Font font;
    public Color headColor = new Color(0.13f, 0.17f, 0.23f);
    public Color bodyColor = new Color(0.35f, 0.41f, 0.49f);

    [Header("选项行（场景预置，最多 3 行；用 Editor 工具生成/接线）")]
    public List<ChoiceRow> rows = new List<ChoiceRow>();

    [Tooltip("第几章（StoryRunner.Open 前设置）。选择顺序记录键按章隔离：story.choice.ch<N>.<题号>")]
    public int chapter = 1;

    [Tooltip("最后一次退出解释态且已全选时通知（StoryRunner 用来抓存档缩略图：此刻面板完整显示全部已选）")]
    public System.Action onPanelComplete;

    StoryStep _step;
    System.Action<List<int>> _onDone;
    readonly List<int> _order = new List<int>();
    int _choiceIndex;          // 第几个干预点（记 PlayerPrefs 用）
    bool _explaining;          // 解释态：行/confirm/Dim 全藏、捕获层开着、对话框在播解释
    GameObject _clickCatcher;  // 解释点击层（运行时懒建一次；null=还没建，销毁后也会重新懒建）
    Transform _dim;            // 面板自己的压暗层（懒查找缓存；找不到只是没有明暗，不报错）

    int OptionCount { get { return _step != null && _step.options != null ? _step.options.Count : 0; } }

    public bool IsOpen { get { return panel != null && panel.IsOpen; } }
    public int SelectedCount { get { return _order.Count; } }
    public bool AllSelected { get { return OptionCount > 0 && _order.Count >= OptionCount; } }

    /// 打开一道题。onDone 在收档确认后回调，参数=选择顺序（首项=第一个选的下标）
    public void Open(StoryStep step, int choiceIndex, System.Action<List<int>> onDone)
    {
        _step = step; _choiceIndex = choiceIndex; _onDone = onDone;
        _order.Clear();

        // 复位解释态：上一题若没收干净（异常路径），这里兜底拉回初始样子
        _explaining = false;
        if (_clickCatcher != null) _clickCatcher.SetActive(false);
        if (_dim == null) _dim = transform.Find("Dim");   // 懒查找并缓存；找不到就 null 跳过（明暗只是退化）
        if (_dim != null) _dim.gameObject.SetActive(true);

        if (titleLabel != null) titleLabel.text = step.title;
        ResetRows();       // 行是场景预置的：只复位状态，绝不 Destroy

        // 开题先收起对话框：题干在面板 Title 上，上一句台词的旧框留着是噪音；首次选中再出框。
        // （「对话」节点在场景里默认禁用、还没 ShowNode 过时，框本来就没显示，不必也不能收——HideNode 里要起协程）
        var d = Box();
        if (d != null && d.gameObject.activeSelf) d.HideNode();

        int count = OptionCount;
        if (count > rows.Count)
        {
            Debug.LogError("[ChoicePanel] 剧本选项数 " + count + " 超过场景预置行数 " + rows.Count +
                           "（最多 3 行）。请先在 Unity 里跑 Tools/干预项目/生成选择题按钮行（预置3行）。");
            count = rows.Count;
        }

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row == null || row.root == null) { Debug.LogWarning("[ChoicePanel] rows 第 " + i + " 项没接线，跳过"); continue; }

            row.root.SetActive(i < count);
            if (i >= count) continue;

            var opt = step.options[i];
            if (row.headText != null) row.headText.text = opt.head;
            if (row.headButton != null)
            {
                row.headButton.interactable = true;   // 变灰=disabled tint：恢复可点就恢复原色
                row.headButton.onClick.RemoveAllListeners();
                int idx = i;
                row.headButton.onClick.AddListener(() => Select(idx));
            }
        }

        if (confirmButton != null)
        {
            confirmButton.interactable = false;
            confirmButton.gameObject.SetActive(true);   // 解释态里它被藏过，开题恢复
            confirmButton.onClick.RemoveAllListeners();
            confirmButton.onClick.AddListener(Confirm);
        }
        RefreshConfirmLabel();
        if (panel != null) panel.Show();
        Rebuild();
    }

    public void Select(int idx)
    {
        if (_step == null || IsSelected(idx)) return;
        if (idx < 0 || idx >= rows.Count) return;
        var row = rows[idx];
        if (row == null) return;

        // 只标记已选：按钮禁用 → ColorTint disabled 态自然把行压灰（不换贴图，用户手调的气泡图原样保留）
        _order.Add(idx);
        if (row.headButton != null) row.headButton.interactable = false;
        RefreshConfirmLabel();

        EnterExplain(idx);
    }

    /// 进入解释态：行/confirm/Dim 全部临时隐藏，解释借底部对话框播（mon 样式）。
    /// （此时行已不可点，用户唯一的出口是点捕获层退出解释。）
    void EnterExplain(int idx)
    {
        _explaining = true;

        for (int i = 0; i < rows.Count; i++)
            if (rows[i] != null && rows[i].root != null) rows[i].root.SetActive(false);
        if (confirmButton != null) confirmButton.gameObject.SetActive(false);

        // 藏掉面板自己的 Dim：不藏它会盖住底下的对话框，解释文字发暗（历史问题就在这）
        if (_dim == null) _dim = transform.Find("Dim");
        if (_dim != null) _dim.gameObject.SetActive(false);

        EnsureCatcher();
        if (_clickCatcher != null) _clickCatcher.SetActive(true);

        var opt = _step.options != null && idx < _step.options.Count ? _step.options[idx] : null;
        var d = Box();
        if (d != null && opt != null && !string.IsNullOrEmpty(opt.body))
        {
            d.ShowNode();
            d.PlayLine(new StoryStep { t = "mon", x = opt.body });
        }
    }

    /// 退出解释态（点任意处）：收框 → Dim/行/confirm 全部回来 → 继续选。
    void ExitExplain()
    {
        if (!_explaining) return;
        _explaining = false;
        if (_clickCatcher != null) _clickCatcher.SetActive(false);

        var d = Box();
        if (d != null) d.SkipTyping();   // 防后台还在打字（Choice 态 onLineTyped 不会推进剧情，安全）
        // 框只在真显示过时才收（「对话」节点默认禁用，对禁用节点 HideNode 会起协程 → 异常）
        if (d != null && d.gameObject.activeSelf) d.HideNode();

        if (_dim != null) _dim.gameObject.SetActive(true);
        for (int i = 0; i < rows.Count; i++)
            if (rows[i] != null && rows[i].root != null) rows[i].root.SetActive(i < OptionCount);
        // 行重新出现时，已选行 interactable=false → disabled tint 自然是灰的、点不动
        if (confirmButton != null)
        {
            confirmButton.gameObject.SetActive(true);
            confirmButton.interactable = AllSelected;
        }
        RefreshConfirmLabel();
        Rebuild();

        // 全选后的解释退出 = 面板完整显示全部已选、确认键点亮的一帧 → 存档缩略图在这里抓
        if (AllSelected && onPanelComplete != null) onPanelComplete();
    }

    /// 自检用：选中下一个未选项；返回是否还有剩余
    public bool DebugSelectNext()
    {
        for (int i = 0; i < OptionCount; i++) if (!IsSelected(i)) { Select(i); return !AllSelected; }
        return false;
    }

    void Confirm()
    {
        if (!AllSelected) return;
        // 从解释态也能直接收档（自检 DebugConfirm 路径）：顺带关捕获层、清解释标志；
        // Dim/行不用管——面板马上整体收掉，下次 Open() 会复位。
        _explaining = false;
        if (_clickCatcher != null) _clickCatcher.SetActive(false);

        PlayerPrefs.SetString("story.choice.ch" + chapter + "." + _choiceIndex, string.Join(",", _order));
        PlayerPrefs.Save();
        if (panel != null) panel.Hide();
        var d = Box();
        // 收档同时收框：解释是借对话框播的，面板收了框也该收（之后 StoryRunner 的下一句会自己 ShowNode）
        if (d != null && d.gameObject.activeSelf) d.HideNode();
        var cb = _onDone; _onDone = null;
        if (cb != null) cb(_order);
    }

    /// 自检用：直接点收档
    public void DebugConfirm() { Confirm(); }

    bool IsSelected(int idx) { return _order.Contains(idx); }

    /// 解释的播出框：底部对话框（StoryRunner 单例的 public 字段，运行时直接取，无需场景额外接线）
    DialogueUI Box()
    {
        var r = StoryRunner.Instance;
        return r != null ? r.dialogue : null;
    }

    /// 解释点击捕获层：运行时懒建一次（不落盘、不进场景文件），面板子树最上层接住全屏点击。
    void EnsureCatcher()
    {
        if (_clickCatcher != null) return;
        var go = new GameObject("解释点击层", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        go.transform.SetAsLastSibling();          // 面板最后一个子节点 → 渲染/射线都在最上
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        var img = go.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0f);    // 全透明，只为接射线
        img.raycastTarget = true;
        var btn = go.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(ExitExplain);
        go.SetActive(false);                      // 只在解释展示期间启用
        _clickCatcher = go;
    }

    void RefreshConfirmLabel()
    {
        if (confirmLabel != null && _step != null)
            confirmLabel.text = AllSelected ? "记入焦虑记录本" : ("已记录 " + _order.Count + "/" + OptionCount);
    }

    void Rebuild()
    {
        if (rowsParent != null) LayoutRebuilder.ForceRebuildLayoutImmediate(rowsParent);
        if (scroll != null) scroll.verticalNormalizedPosition = 1f;
    }

    /// 复位每行状态：按钮恢复可点（解释态可能藏过行）。
    /// 行节点是场景预置的，这里【绝不 Destroy】也【不改贴图】——用户在场景里手调的东西要一直留着。
    void ResetRows()
    {
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row == null || row.root == null) continue;
            if (row.headButton != null) row.headButton.interactable = true;
        }
    }
}
