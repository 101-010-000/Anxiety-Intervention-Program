// 手机聊天 UI（第1章微信段的展示层）：素材 = assets/05_UI/手机_Phone/（用户给的设计稿）。
//
// ★ 铁律（用户 2026-09-27 定稿）：素材样式与比例一律不改 ——
//   · 贴图文件零改动，颜色/圆角/尾巴全部来自原图；
//   · 机身（图层 2）只做【等比缩放】（根节点 localScale），内部全部按素材原生像素摆；
//   · 气泡（图层 5/8）用 9-slice，且边框完全包住尾巴+圆角（拉伸只发生在纯色中段，永不变形）；
//   · 贴纸 1/2/3.png 1:1 原尺寸显示。
//
// 节点结构（PhoneChatBuilder 生成）：
//   手机聊天（根，等比缩放，默认 SetActive(false)）
//     ├ 机身        图层 2，1:1
//     ├ 标题        「林溪」（中文_Deng）
//     └ 消息区      RectMask2D 裁剪
//         └ 内容    运行时从上往下堆消息，超高自动滚到最新一条
//
// 驱动方 = StoryRunner：
//   · 旁白「拿起手机」→ Show()（同时徐夏动画 TakePhone）；「肩膀放松」→ Hide()（放下手机）
//   · 一句（微信）台词打字机播完 → Append(说话人, 台词)，气泡即时落入聊天流
//   · 干预题②收档 → 把第一个选中的鼓励语以「林溪（微信）」补进聊天
// 消息不挡点击（CanvasGroup.blocksRaycasts=false），推进剧情的点击穿透手机。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PhoneChatUI : MonoBehaviour
{
    [Header("素材引用（工具自动接）")]
    public Sprite frameSprite;      // 图层 2 机身 576×1145
    public Sprite bubbleLeft;       // 图层 5 白气泡（收到的，尾巴朝左）274×83
    public Sprite bubbleRight;      // 图层 7 蓝气泡（发出的，尾巴朝右）319×87
    public Sprite avatarLeft;       // 图层 6 头像（林溪侧）90×88
    public Sprite avatarRight;      // 图层 8 头像（徐夏侧）89×88
    public Font chatFont;           // 中文_Deng

    [Header("贴纸（台词整句等于标签时转贴图，1:1 显示）")]
    public string[] stickerTags = { "[比心]", "[鲜花]", "[加油]" };
    public Sprite[] stickerSprites; // 1.png / 2.png / 3.png（顺序对应 stickerTags）

    [Header("标题（聊天对象）")]
    public string headerName = "林溪";

    // ---- 布局常量：全部是素材原生像素（根节点统一等比缩放，这里不做第二次缩放） ----
    const float FRAME_W = 576f, FRAME_H = 1145f;
    const float BODY_X = 36f;                  // 消息区在机身内的边距
    const float BODY_Y = 145f;                 // 底部避开输入栏（输入栏自顶 ~1000px 起）
    const float BODY_H = 810f;                 // = 1145 - 145(底) - 190(顶，避开标题栏)
    const float AVATAR_W = 90f, AVATAR_H = 88f;
    const float AVATAR_GAP = 14f;              // 头像与气泡间距
    const float MSG_GAP = 18f;                 // 消息纵向间距
    const float FONT_SIZE = 26f;
    const float PAD_V = 18f;                   // 气泡内文字上下留白
    const float INSET_TAIL = 42f;              // 尾巴一侧的文字留白
    const float INSET_FAR = 26f;               // 开口一侧的文字留白
    static readonly Color COL_RECV = new Color32(0x24, 0x30, 0x3E, 0xFF);   // 收到（白底）→ 深色字
    static readonly Color COL_SENT = new Color32(0x17, 0x4A, 0x74, 0xFF);   // 发出（浅蓝底）→ 深藏青字（白字压不住浅蓝底）

    RectTransform _content;
    Text _title;                           // 「标题」下的联系人名（SetContact 换聊天对象用）
    CanvasGroup _group;
    float _homeX;                            // 滑入/滑出的基准位（首次接线时记录，防多次开关漂移）
    readonly List<Coroutine> _pops = new List<Coroutine>();

    public bool IsShown { get { return gameObject.activeSelf; } }
    public string CurrentContact { get { return headerName; } }

    void EnsureRefs()
    {
        if (_content != null) return;
        _group = GetComponent<CanvasGroup>();
        if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
        _group.blocksRaycasts = false;
        _group.interactable = false;
        _homeX = ((RectTransform)transform).anchoredPosition.x;
        var area = transform.Find("消息区");
        if (area != null) _content = area.Find("内容") as RectTransform;
        var titleT = transform.Find("标题");
        if (titleT != null) _title = titleT.GetComponent<Text>();
    }

    /// 换聊天对象（第2章 李同学↔林溪、第4章 班群/学姐）：只换标题文字并清空聊天流。
    /// ★ 头像暂用现有两张占位（用户约定：缺的素材先占位）——将来给了头像，直接在
    ///   Inspector 换 avatarLeft 即可，布局/样式铁律不动。
    public void SetContact(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        EnsureRefs();
        if (headerName == name && _content != null && _content.childCount > 0) return;   // 同一段聊天：不重置
        if (headerName != name) headerName = name;
        if (_title != null) _title.text = name;
        Clear();
    }

    // -------------------------------------------------------------- 开合
    /// 微信段开始：清空旧聊天、整台手机淡入 + 从右侧滑入
    public void Show()
    {
        EnsureRefs();
        Clear();
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        if (Application.isPlaying)
        {
            if (_group != null)
            {
                if (_pops.Count > 0) { foreach (var c in _pops) if (c != null) StopCoroutine(c); _pops.Clear(); }
                _pops.Add(StartCoroutine(FadeRoutine(1f, 0.28f, 60f)));
            }
        }
        else if (_group != null) _group.alpha = 1f;
    }

    /// 微信段结束：放下手机（淡出后收起）
    public void Hide()
    {
        if (!gameObject.activeSelf) return;
        EnsureRefs();
        if (Application.isPlaying && _group != null)
        {
            if (_pops.Count > 0) { foreach (var c in _pops) if (c != null) StopCoroutine(c); _pops.Clear(); }
            _pops.Add(StartCoroutine(FadeRoutine(0f, 0.25f, 40f)));
        }
        else
        {
            if (_group != null) _group.alpha = 0f;
            gameObject.SetActive(false);
        }
    }

    /// 章节开始等场合：不做动画直接收起
    public void HideImmediate()
    {
        if (_pops.Count > 0) { foreach (var c in _pops) if (c != null) StopCoroutine(c); _pops.Clear(); }
        if (_group != null) _group.alpha = 0f;
        gameObject.SetActive(false);
    }

    IEnumerator FadeRoutine(float target, float dur, float slide)
    {
        var rt = (RectTransform)transform;
        // 一律以 _homeX 为基准：滑入 = 从右侧 slide 像素进来，滑出 = 向右侧退 slide 像素，收尾无跳位
        float x0 = target > 0.5f ? _homeX + slide : rt.anchoredPosition.x;
        float x1 = target > 0.5f ? _homeX : _homeX + slide;
        float a0 = _group.alpha;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / dur;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            _group.alpha = Mathf.Lerp(a0, target, k);
            var p = rt.anchoredPosition;
            p.x = Mathf.Lerp(x0, x1, k);
            rt.anchoredPosition = p;
            yield return null;
        }
        _group.alpha = target;
        var pe = rt.anchoredPosition; pe.x = x1; rt.anchoredPosition = pe;
        if (target <= 0.01f) gameObject.SetActive(false);
    }

    // -------------------------------------------------------------- 消息
    /// 清空聊天流（保留 标题/机身）。编辑模式（预览渲染）用 DestroyImmediate，不然对象收不掉
    public void Clear()
    {
        EnsureRefs();
        if (_content == null) return;
        for (int i = _content.childCount - 1; i >= 0; i--)
        {
            var go = _content.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
        _content.sizeDelta = new Vector2(_content.sizeDelta.x, 0f);
    }

    /// 追加一条消息。speaker 带「徐夏」走右侧蓝泡，「林溪」走左侧白泡；
    /// 台词整句等于贴纸标签（如 [比心]）时按贴纸显示。
    public void Append(string speaker, string text)
    {
        EnsureRefs();
        if (_content == null || string.IsNullOrEmpty(text)) return;
        bool mine = !string.IsNullOrEmpty(speaker) && speaker.Contains("徐夏");

        int sticker = -1;
        string trimmed = text.Trim();
        if (stickerTags != null)
            for (int i = 0; i < stickerTags.Length; i++)
                if (stickerSprites != null && i < stickerSprites.Length && stickerSprites[i] != null
                    && trimmed == stickerTags[i]) { sticker = i; break; }

        RectTransform msg = NewRect("消息", _content);
        float y = -_content.sizeDelta.y;                 // 追加到当前流末尾
        float h;
        if (sticker >= 0) h = LayoutSticker(msg, mine, y, stickerSprites[sticker]);
        else h = LayoutBubble(msg, mine, y, text);

        _content.sizeDelta = new Vector2(_content.sizeDelta.x, _content.sizeDelta.y + h + MSG_GAP);
        ScrollToLatest();

        if (Application.isPlaying)                        // 气泡弹出感（编辑器预览不做动画）
        {
            msg.localScale = new Vector3(0.7f, 0.7f, 1f);
            _pops.Add(StartCoroutine(PopRoutine(msg)));
        }
    }

    float LayoutBubble(RectTransform msg, bool mine, float y, string text)
    {
        float insetL = mine ? INSET_FAR : INSET_TAIL;
        float insetR = mine ? INSET_TAIL : INSET_FAR;
        float maxBubbleW = BODY_W - AVATAR_W - AVATAR_GAP;
        float maxTextW = maxBubbleW - insetL - insetR;

        // 先量文本：单行按内容收窄，超宽折行；气泡高度 = 文本高 + 上下留白（不低于素材原生高度）
        Text t = NewText(msg, mine ? COL_SENT : COL_RECV);
        t.font = chatFont;
        if (chatFont == null) Debug.LogWarning("[PhoneChatUI] chatFont 没接，中文会显示空白 —— 重跑 Tools/干预项目/搭建手机聊天UI");
        t.rectTransform.sizeDelta = new Vector2(maxTextW, 0f);
        t.text = text;
        float prefW = t.preferredWidth;
        float bubbleW = Mathf.Clamp(prefW + insetL + insetR,
            mine ? 319f : 274f, maxBubbleW);              // 不小于素材原生宽度（保住比例）
        float innerW = bubbleW - insetL - insetR;
        t.rectTransform.sizeDelta = new Vector2(innerW, 0f);
        float textH = t.preferredHeight;
        float bubbleH = Mathf.Max(mine ? 87f : 83f, textH + PAD_V * 2f);   // 不小于素材原生高度

        ((RectTransform)msg.transform).anchoredPosition = new Vector2(mine ? BODY_W - AVATAR_W - AVATAR_GAP - bubbleW : 0f, y);
        msg.sizeDelta = new Vector2(bubbleW + AVATAR_W + AVATAR_GAP, bubbleH);

        Image bubble = NewImage("气泡", msg, mine ? bubbleRight : bubbleLeft);
        bubble.type = Image.Type.Sliced;
        var brt = (RectTransform)bubble.transform;
        brt.anchorMin = brt.anchorMax = new Vector2(mine ? 0f : 1f, 0f);
        brt.pivot = new Vector2(mine ? 0f : 1f, 0f);
        brt.anchoredPosition = Vector2.zero;
        brt.sizeDelta = new Vector2(bubbleW, bubbleH);

        // 文字以锚点内缩挂在气泡里（尾巴一侧多留白）
        var trt = (RectTransform)t.transform;
        trt.SetParent(brt, false);
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.pivot = new Vector2(0.5f, 0.5f);
        trt.offsetMin = new Vector2(insetL, PAD_V);
        trt.offsetMax = new Vector2(-insetR, -PAD_V);
        // ★ 气泡内文字一律【左对齐】（用户 2026-09-29 反馈「文字没有靠左」）：
        //   微信里自己发的消息也是贴着气泡左边起排；此前发出方（右侧蓝泡）用 MiddleRight，
        //   多行时后几行被推到右边（换行处还会留下标点前的大段空白），看着像整段没对齐。
        //   气泡自身的左右位置不变（mine 靠右、对方靠左），只改气泡【内部】的行对齐。
        t.alignment = TextAnchor.MiddleLeft;

        Image av = NewImage("头像", msg, mine ? avatarRight : avatarLeft);
        var art = (RectTransform)av.transform;
        art.anchorMin = art.anchorMax = new Vector2(mine ? 1f : 0f, 1f);
        art.pivot = new Vector2(mine ? 1f : 0f, 1f);
        art.anchoredPosition = Vector2.zero;
        art.sizeDelta = new Vector2(AVATAR_W, AVATAR_H);
        return bubbleH;
    }

    float LayoutSticker(RectTransform msg, bool mine, float y, Sprite sticker)
    {
        float sw = sticker.rect.width, sh = sticker.rect.height;    // 1:1 原尺寸
        float w = AVATAR_W + AVATAR_GAP + sw;
        ((RectTransform)msg.transform).anchoredPosition = new Vector2(mine ? BODY_W - w : 0f, y);
        msg.sizeDelta = new Vector2(w, sh);

        Image im = NewImage("贴纸", msg, sticker);
        var irt = (RectTransform)im.transform;
        irt.anchorMin = irt.anchorMax = new Vector2(mine ? 1f : 0f, 1f);
        irt.pivot = new Vector2(mine ? 1f : 0f, 1f);
        irt.anchoredPosition = new Vector2(mine ? -AVATAR_W - AVATAR_GAP : AVATAR_W + AVATAR_GAP, 0f);
        irt.sizeDelta = new Vector2(sw, sh);

        Image av = NewImage("头像", msg, mine ? avatarRight : avatarLeft);
        var art = (RectTransform)av.transform;
        art.anchorMin = art.anchorMax = new Vector2(mine ? 1f : 0f, 1f);
        art.pivot = new Vector2(mine ? 1f : 0f, 1f);
        art.anchoredPosition = Vector2.zero;
        art.sizeDelta = new Vector2(AVATAR_W, AVATAR_H);
        return sh;
    }

    /// 内容流超高后固定看最新一条（聊天贴底）：内容锚在消息区顶部，往"上"推 scroll 才能把最新一条露出来
    void ScrollToLatest()
    {
        float total = _content.sizeDelta.y;
        float scroll = Mathf.Max(0f, total - BODY_H);
        _content.anchoredPosition = new Vector2(0f, scroll);
    }

    IEnumerator PopRoutine(RectTransform msg)
    {
        float t = 0f;
        while (t < 1f && msg != null)
        {
            t += Time.unscaledDeltaTime / 0.18f;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            msg.localScale = Vector3.Lerp(new Vector3(0.7f, 0.7f, 1f), Vector3.one, k);
            yield return null;
        }
        if (msg != null) msg.localScale = Vector3.one;
    }

    // -------------------------------------------------------------- 小工具
    const float BODY_W = 504f;                 // = 576 - 36*2

    static RectTransform NewRect(string name, RectTransform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);   // 挂在内容流顶部，往下排
        rt.pivot = new Vector2(0f, 1f);
        return rt;
    }

    static Image NewImage(string name, RectTransform parent, Sprite sp)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = sp;
        img.raycastTarget = false;
        return img;
    }

    static Text NewText(RectTransform parent, Color col)
    {
        var go = new GameObject("文字", typeof(RectTransform), typeof(Text));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.color = col;
        t.fontSize = (int)FONT_SIZE;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }
}
