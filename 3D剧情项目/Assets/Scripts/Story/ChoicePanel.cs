// 干预选择题面板：全选流程（需求定稿）——
//   点选任一选项 → 该选项下方展开对应解释 → 标记"已记录"（不可再点）
//   → 剩余选项继续可选 → 全部选过一遍 → "记入焦虑记录本"按钮点亮 → 点击收档回剧情。
// 未选完不能关闭（ESC/点空白都不行），选择顺序记进 PlayerPrefs 供二期联动概览页。
//
// 面板由 Chapter1StoryBuilder 生成（复用主菜单九宫格贴图），本脚本运行时动态建选项行。
// 解释区高度不用 ContentSizeFitter（量 Image 不可靠），改成激活后量 Text 首选高度写 LayoutElement。
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ChoicePanel : MonoBehaviour
{
    [Header("引用（工具自动接）")]
    public UIPanel panel;              // 淡入淡出 + 遮罩（复用主菜单 UIPanel）
    public Text titleLabel;            // 题干
    public RectTransform rowsParent;   // 选项行容器（带 VerticalLayoutGroup）
    public ScrollRect scroll;
    public Button confirmButton;
    public Text confirmLabel;

    [Header("样式（工具自动接）")]
    public Sprite rowNormal;
    public Sprite rowSelected;
    public Font font;
    public Color headColor = new Color(0.13f, 0.17f, 0.23f);
    public Color bodyColor = new Color(0.35f, 0.41f, 0.49f);

    StoryStep _step;
    System.Action<List<int>> _onDone;
    readonly List<int> _order = new List<int>();
    readonly List<Image> _rowImgs = new List<Image>();
    readonly List<Button> _rowBtns = new List<Button>();
    readonly List<GameObject> _rowBodies = new List<GameObject>();
    readonly List<Text> _rowBodyTexts = new List<Text>();
    int _choiceIndex;          // 第几个干预点（记 PlayerPrefs 用）

    public bool IsOpen { get { return panel != null && panel.IsOpen; } }
    public int SelectedCount { get { return _order.Count; } }
    public bool AllSelected { get { return _step != null && _step.options.Count > 0 && _order.Count >= _step.options.Count; } }

    /// 打开一道题。onDone 在收档确认后回调，参数=选择顺序（首项=第一个选的下标）
    public void Open(StoryStep step, int choiceIndex, System.Action<List<int>> onDone)
    {
        _step = step; _choiceIndex = choiceIndex; _onDone = onDone;
        _order.Clear();
        if (titleLabel != null) titleLabel.text = step.title;
        ClearRows();

        for (int i = 0; i < step.options.Count; i++)
        {
            var opt = step.options[i];
            var rowGO = new GameObject("选项_" + i, typeof(RectTransform));
            rowGO.transform.SetParent(rowsParent, false);
            var vg = rowGO.AddComponent<VerticalLayoutGroup>();
            vg.spacing = 8f;
            vg.childForceExpandWidth = true; vg.childForceExpandHeight = false;
            vg.childAlignment = TextAnchor.UpperCenter;
            var fitter = rowGO.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // ---- 头部按钮（选项标题）
            var headGO = new GameObject("头", typeof(RectTransform));
            headGO.transform.SetParent(rowGO.transform, false);
            var hle = headGO.AddComponent<LayoutElement>();
            hle.minHeight = 62f; hle.preferredHeight = 62f;
            var himg = headGO.AddComponent<Image>();
            himg.sprite = rowNormal; himg.type = Image.Type.Sliced;
            himg.raycastTarget = true;
            var hbtn = headGO.AddComponent<Button>();
            hbtn.targetGraphic = himg;
            hbtn.transition = Selectable.Transition.ColorTint;
            hbtn.colors = Tint();
            var head = MakeText(headGO.transform, "标题", opt.head, 28, headColor, TextAnchor.MiddleLeft);
            StretchWith(head, 26f, 8f, 26f, 8f);
            head.fontStyle = FontStyle.Bold;

            // ---- 解释区（选中后展开；高度激活后量文字再写 LayoutElement）
            var bodyGO = new GameObject("解释", typeof(RectTransform));
            bodyGO.transform.SetParent(rowGO.transform, false);
            var bodyImg = bodyGO.AddComponent<Image>();
            bodyImg.color = new Color(0.93f, 0.95f, 0.97f, 0.9f);
            bodyImg.raycastTarget = false;
            var ble = bodyGO.AddComponent<LayoutElement>();
            ble.minHeight = 0f; ble.preferredHeight = 0f;
            var body = MakeText(bodyGO.transform, "正文", opt.body, 24, bodyColor, TextAnchor.UpperLeft);
            StretchWith(body, 22f, 14f, 22f, 14f);
            bodyGO.SetActive(false);
            _rowBodies.Add(bodyGO);
            _rowBodyTexts.Add(body);

            int idx = i;
            hbtn.onClick.AddListener(() => Select(idx));
            _rowImgs.Add(himg);
            _rowBtns.Add(hbtn);
        }

        if (confirmButton != null)
        {
            confirmButton.interactable = false;
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
        _order.Add(idx);
        if (idx >= 0 && idx < _rowImgs.Count && _rowImgs[idx] != null)
        {
            if (rowSelected != null) { _rowImgs[idx].sprite = rowSelected; _rowImgs[idx].type = Image.Type.Sliced; }
            _rowBtns[idx].interactable = false;
        }

        if (idx >= 0 && idx < _rowBodies.Count)
        {
            var go = _rowBodies[idx];
            go.SetActive(true);
            var le = go.GetComponent<LayoutElement>();
            var txt = _rowBodyTexts[idx];
            if (le != null && txt != null)
            {
                // 先让布局铺开一次拿到宽度，再量文字首选高度写回去；量不到就给兜底值
                Rebuild();
                float w = ((RectTransform)txt.transform).rect.width;
                le.preferredHeight = w > 1f ? Mathf.Max(60f, txt.preferredHeight + 28f) : 120f;
            }
        }

        if (AllSelected && confirmButton != null) confirmButton.interactable = true;
        RefreshConfirmLabel();
        Rebuild();
    }

    /// 自检用：选中下一个未选项；返回是否还有剩余
    public bool DebugSelectNext()
    {
        for (int i = 0; i < _step.options.Count; i++) if (!IsSelected(i)) { Select(i); return !AllSelected; }
        return false;
    }

    void Confirm()
    {
        if (!AllSelected) return;
        PlayerPrefs.SetString("story.choice." + _choiceIndex, string.Join(",", _order));
        PlayerPrefs.Save();
        if (panel != null) panel.Hide();
        var cb = _onDone; _onDone = null;
        if (cb != null) cb(_order);
    }

    /// 自检用：直接点收档
    public void DebugConfirm() { Confirm(); }

    bool IsSelected(int idx) { return _order.Contains(idx); }

    void RefreshConfirmLabel()
    {
        if (confirmLabel != null && _step != null)
            confirmLabel.text = AllSelected ? "记入焦虑记录本" : ("已记录 " + _order.Count + "/" + _step.options.Count);
    }

    void Rebuild()
    {
        if (rowsParent != null) LayoutRebuilder.ForceRebuildLayoutImmediate(rowsParent);
        if (scroll != null) scroll.verticalNormalizedPosition = 1f;
    }

    void ClearRows()
    {
        _rowImgs.Clear(); _rowBtns.Clear(); _rowBodies.Clear(); _rowBodyTexts.Clear();
        if (rowsParent == null) return;
        for (int i = rowsParent.childCount - 1; i >= 0; i--) Destroy(rowsParent.GetChild(i).gameObject);
    }

    Text MakeText(Transform parent, string name, string content, int size, Color color, TextAnchor align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.font = font; t.text = content; t.fontSize = size; t.color = color; t.alignment = align;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    static void StretchWith(Text t, float l, float top, float r, float b)
    {
        var rt = (RectTransform)t.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -top);
    }

    static ColorBlock Tint()
    {
        var c = ColorBlock.defaultColorBlock;
        c.normalColor = new Color(0.96f, 0.97f, 0.98f);
        c.highlightedColor = Color.white;
        c.pressedColor = new Color(0.88f, 0.92f, 0.96f);
        c.selectedColor = c.normalColor;
        c.fadeDuration = 0.08f;
        return c;
    }
}
