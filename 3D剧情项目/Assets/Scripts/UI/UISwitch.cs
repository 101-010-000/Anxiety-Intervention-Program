// 开关（设置页的"全屏 / 自动播放 / 自动存档"）：切槽贴图 + 滑块滑过去。
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class UISwitch : MonoBehaviour
{
    public RectTransform knob;        // 滑块
    public Image         track;       // 槽
    public Sprite        onSprite;    // 开态槽贴图
    public Sprite        offSprite;   // 关态槽贴图
    public float         onX  = 26f;
    public float         offX = -26f;
    public float         duration = 0.12f;
    public bool          isOn = true;

    public System.Action<bool> OnChanged;

    float _knobT = 1f;
    bool  _inited;

    void Awake()
    {
        var btn = GetComponent<Button>();
        if (btn != null) btn.onClick.AddListener(Toggle);
        Init();
    }

    void Init()
    {
        if (_inited) return;
        _inited = true;
        if (knob != null) knob.anchoredPosition = new Vector2(isOn ? onX : offX, knob.anchoredPosition.y);
        ApplySprites();
        _knobT = isOn ? 1f : 0f;
    }

    public void SetOn(bool value, bool notify = false)
    {
        Init();
        isOn = value;
        ApplySprites();
        if (notify && OnChanged != null) OnChanged(isOn);
    }

    public void Toggle()
    {
        SetOn(!isOn, true);
    }

    void ApplySprites()
    {
        if (track != null)
        {
            if (isOn && onSprite != null) track.sprite = onSprite;
            if (!isOn && offSprite != null) track.sprite = offSprite;
        }
    }

    void Update()
    {
        Init();
        float target = isOn ? 1f : 0f;
        if (Mathf.Approximately(_knobT, target)) return;
        float speed = duration <= 0.001f ? 1000f : 1f / duration;
        _knobT = Mathf.MoveTowards(_knobT, target, Time.unscaledDeltaTime * speed);
        if (knob != null)
            knob.anchoredPosition = new Vector2(Mathf.Lerp(offX, onX, _knobT), knob.anchoredPosition.y);
    }
}
