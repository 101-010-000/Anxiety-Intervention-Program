// 通用弹层：淡入淡出 + 轻微位移/缩放，可带一层压暗遮罩。
// 所有面板（设置/存档/章节/概览/弹窗）都用它，保证动效一致。
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasGroup))]
public class UIPanel : MonoBehaviour
{
    public bool  openOnStart;          // 场景打开时就是展开状态（主菜单层）
    public bool  introFade;            // 启动时播一次淡入（主菜单层）
    public float duration     = 0.18f; // 淡入淡出时长
    public float fromScale    = 0.97f; // 起始缩放
    public float fromOffsetY  = -16f;  // 起始纵向偏移
    public GameObject dimmer;          // 可选：随面板淡入的压暗遮罩

    CanvasGroup _cg;
    RectTransform _rt;
    Vector2 _basePos;
    bool  _open;
    float _t = 1f;      // 0 = 完全隐藏，1 = 完全展开

    public bool IsOpen      { get { return _open; } }
    public bool IsAnimating { get { return !Mathf.Approximately(_t, _open ? 1f : 0f); } }

    void Awake()
    {
        _cg = GetComponent<CanvasGroup>();
        _rt = transform as RectTransform;
        if (_rt != null) _basePos = _rt.anchoredPosition;
        _open = openOnStart;
        _t = _open ? 1f : 0f;
        Apply(_t);
    }

    void Start()
    {
        if (introFade && _open)
        {
            _t = 0f;
            Apply(0f);
        }
    }

    public void Show(bool instant = false)
    {
        _open = true;
        if (instant) { _t = 1f; Apply(1f); }
    }

    public void Hide(bool instant = false)
    {
        _open = false;
        if (instant) { _t = 0f; Apply(0f); }
    }

    public void SetOpen(bool open, bool instant = false)
    {
        if (open) Show(instant); else Hide(instant);
    }

    void Update()
    {
        float target = _open ? 1f : 0f;
        if (Mathf.Approximately(_t, target)) return;
        float speed = duration <= 0.001f ? 1000f : 1f / duration;
        _t = Mathf.MoveTowards(_t, target, Time.unscaledDeltaTime * speed);
        Apply(_t);
    }

    void Apply(float t)
    {
        if (_cg == null) _cg = GetComponent<CanvasGroup>();
        float e = t * t * (3f - 2f * t);       // smoothstep，收尾更柔
        _cg.alpha          = e;
        _cg.interactable   = _open;
        _cg.blocksRaycasts = _open && t > 0.35f;

        if (_rt != null)
        {
            _rt.anchoredPosition = _basePos + new Vector2(0f, Mathf.Lerp(fromOffsetY, 0f, e));
            float s = Mathf.Lerp(fromScale, 1f, e);
            _rt.localScale = new Vector3(s, s, 1f);
        }

        if (dimmer != null)
        {
            var dg = dimmer.GetComponent<CanvasGroup>();
            if (dg == null) dg = dimmer.AddComponent<CanvasGroup>();
            dg.alpha = e;
            dg.blocksRaycasts = _open;
        }
    }
}
