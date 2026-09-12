// 鼠标悬停/按下时轻微缩放，给按钮一点"活"的手感（配 SpriteSwap 用）。
using UnityEngine;
using UnityEngine.EventSystems;

public class UIHoverScale : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
                                       IPointerDownHandler, IPointerUpHandler
{
    public float hoverScale = 1.035f;
    public float pressScale = 0.975f;
    public float speed      = 14f;

    Vector3 _base = Vector3.one;
    float   _target = 1f;
    bool    _inited;

    void Awake() { Init(); }

    void Init()
    {
        if (_inited) return;
        _inited = true;
        _base = transform.localScale;
        _target = 1f;
    }

    public void OnPointerEnter(PointerEventData e) { _target = hoverScale; }
    public void OnPointerExit(PointerEventData e)  { _target = 1f; }
    public void OnPointerDown(PointerEventData e)  { _target = pressScale; }
    public void OnPointerUp(PointerEventData e)    { _target = hoverScale; }

    void Update()
    {
        Init();
        float cur = _base.x == 0f ? 1f : transform.localScale.x / _base.x;
        float next = Mathf.Lerp(cur, _target, Time.unscaledDeltaTime * speed);
        transform.localScale = new Vector3(_base.x * next, _base.y * next, _base.z);
    }
}
