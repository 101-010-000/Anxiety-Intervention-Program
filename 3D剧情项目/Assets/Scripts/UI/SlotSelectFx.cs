// 存档槽选中/悬停动效：缩放（悬停 + 按下 + 选中复合）与角标淡入、空框变色。
// 取代槽位上原来的 UIHoverScale —— 缩放只归这一个组件管，避免两处写 localScale 打架。
// 选中入场有一记轻微的"弹一下"（正弦半波），落回常驻放大值；取消选中时平滑缩回。
// 选中识别 = 右上角对勾角标 + 虚线空框由灰变蓝（同一条线变色，不额外画框）。
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class SlotSelectFx : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
                                         IPointerDownHandler, IPointerUpHandler
{
    [Header("引用（场景接线）")]
    public CanvasGroup badgeCg;      // 右上角对勾角标
    public CanvasGroup frameCg;      // （已弃用的描边层，留作兼容，可空）
    public Image       emptyFrame;   // 虚线空框（选中时变蓝）

    public Color frameColorSelected = new Color(0.298f, 0.624f, 0.91f); // 主蓝 #4C9FE8
    public Color frameColorNormal   = Color.white;                      // 未选中色（贴图原色，写死不缓存，防场景存了选中态导致回不去）

    public float hoverScale  = 1.02f;
    public float pressScale  = 0.99f;
    public float selectScale = 1.03f;
    public float speed       = 12f;

    Vector3 _base = Vector3.one;
    bool    _hover, _press, _selected;
    float   _selT;   // 选中态持续秒数（驱动入场弹跳）
    float   _cur = 1f;

    void Awake() { _base = transform.localScale; }

    public void SetSelected(bool on)
    {
        if (_selected == on) return;
        _selected = on;
        _selT = 0f;
    }

    public void OnPointerEnter(PointerEventData e) { _hover = true; }
    public void OnPointerExit (PointerEventData e) { _hover = false; _press = false; }
    public void OnPointerDown (PointerEventData e) { _press = true; }
    public void OnPointerUp   (PointerEventData e) { _press = false; }

    void Update()
    {
        _selT += Time.unscaledDeltaTime;

        float pop = 0f;
        if (_selected && _selT < 0.28f)
            pop = Mathf.Sin(_selT / 0.28f * Mathf.PI) * 0.018f;

        float target = (_hover ? hoverScale : 1f) * (_press ? pressScale : 1f) * (_selected ? selectScale : 1f) + pop;
        _cur = Mathf.Lerp(_cur, target, Time.unscaledDeltaTime * speed);
        transform.localScale = _base * _cur;

        float k = Time.unscaledDeltaTime * 10f;
        if (badgeCg != null) badgeCg.alpha = Mathf.Lerp(badgeCg.alpha, _selected ? 1f : 0f, k);
        if (frameCg  != null) frameCg.alpha = Mathf.Lerp(frameCg.alpha, _selected ? 1f : 0f, k);
        if (emptyFrame != null)
            emptyFrame.color = Color.Lerp(emptyFrame.color, _selected ? frameColorSelected : frameColorNormal, k);
    }
}
