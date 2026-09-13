// 按钮小优化：悬停时换"手型"光标 + 文字颜色轻微变化（配 SpriteSwap 用）。
// 贴图态由 Button 的 SpriteSwap 负责，这里只管光标与文字，避免"悬停了却看不出反应/状态不对"。
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class UIButtonPolish : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public Text      label;             // 按钮文字（可空）
    public Color     normalLabel = Color.white;
    public Color     hoverLabel  = Color.white;
    public Texture2D cursorTexture;     // 光标_手（由场景搭建脚本接好）
    public Vector2   cursorHotspot = new Vector2(13f, 3f);
    public bool      handCursor = true;

    void Awake()
    {
        if (label != null) label.color = normalLabel;
    }

    public void OnPointerEnter(PointerEventData e)
    {
        if (label != null) label.color = hoverLabel;
        if (handCursor && cursorTexture != null) Cursor.SetCursor(cursorTexture, cursorHotspot, CursorMode.Auto);
    }

    public void OnPointerExit(PointerEventData e)
    {
        if (label != null) label.color = normalLabel;
        if (handCursor) Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
    }

    void OnDisable()
    {
        if (label != null) label.color = normalLabel;
    }
}
