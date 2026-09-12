// 轻提示条（"已自动存档""该章节未解锁"……）：淡入 → 停留 → 淡出。
using UnityEngine;
using UnityEngine.UI;

public class ToastUI : MonoBehaviour
{
    public CanvasGroup group;
    public RectTransform body;     // 内容条（做一点上浮）
    public Text text;
    public Image icon;

    public float fadeInSeconds  = 0.16f;
    public float holdSeconds    = 1.9f;
    public float fadeOutSeconds = 0.26f;
    public float riseFrom       = -14f;

    float    _t;
    bool     _showing;
    Vector2  _basePos;

    void Awake()
    {
        if (group == null) group = GetComponent<CanvasGroup>();
        if (body != null) _basePos = body.anchoredPosition;
        SetAlpha(0f);
    }

    public void Show(string msg, Sprite iconSprite = null)
    {
        if (group == null) group = GetComponent<CanvasGroup>();
        if (text != null) text.text = msg;
        if (icon != null)
        {
            icon.enabled = iconSprite != null;
            if (iconSprite != null) icon.sprite = iconSprite;
        }
        _t = 0f;
        _showing = true;
    }

    void Update()
    {
        if (!_showing) return;
        _t += Time.unscaledDeltaTime;

        float a;
        if (_t < fadeInSeconds) a = _t / Mathf.Max(0.001f, fadeInSeconds);
        else if (_t < fadeInSeconds + holdSeconds) a = 1f;
        else if (_t < fadeInSeconds + holdSeconds + fadeOutSeconds)
            a = 1f - (_t - fadeInSeconds - holdSeconds) / Mathf.Max(0.001f, fadeOutSeconds);
        else { a = 0f; _showing = false; }

        SetAlpha(a);
    }

    void SetAlpha(float a)
    {
        if (group != null)
        {
            group.alpha = a;
            group.blocksRaycasts = false;
            group.interactable   = false;
        }
        if (body != null) body.anchoredPosition = _basePos + new Vector2(0f, Mathf.Lerp(riseFrom, 0f, a));
    }
}
