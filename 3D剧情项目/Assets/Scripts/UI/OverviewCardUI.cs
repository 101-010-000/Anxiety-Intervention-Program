// 内容概览页的一条选择（配图 + 视角标签 + 标题 + 概述）
using UnityEngine;
using UnityEngine.UI;

public class OverviewCardUI : MonoBehaviour
{
    public Button button;
    public Image  image;        // 收录图
    public Text   titleText;
    public Text   viewText;     // 视角标签：自我 / 旁观 / 未来 / 其他
    public Text   bodyText;     // 概述
    public Image  selectFrame;  // 悬停/查看时的描边

    public int index;           // 本章第几处选择 0..3

    public void SetDimmed(bool dimmed)
    {
        var cg = GetComponent<CanvasGroup>();
        if (cg == null) cg = gameObject.AddComponent<CanvasGroup>();
        cg.alpha = dimmed ? 0.28f : 1f;
        if (button != null) button.interactable = !dimmed;
    }
}
