// 章节选择页的单张章节卡（编号 / 标题 / 一行描述 / 状态角标）
using UnityEngine;
using UnityEngine.UI;

public class ChapterCardUI : MonoBehaviour
{
    public Button button;
    public Text   numText;      // "01"
    public Text   titleText;    // 章节标题
    public Text   descText;     // 一行描述
    public Text   stateText;    // 已完成 / 可进入 / 未解锁
    public Image  stateIcon;    // 对勾 / 锁
    public GameObject lockOverlay;   // 未解锁时压一层灰
    public Image  cardImage;

    public int chapter = 1;

    public void SetState(bool unlocked, bool completed,
                         Sprite checkIcon, Sprite lockIcon, Color lockedTint, Color normalTint)
    {
        if (titleText != null) titleText.text = GameProgress.ChapterTitle(chapter);
        if (descText != null)  descText.text  = GameProgress.ChapterDesc(chapter);
        if (numText != null)   numText.text   = chapter.ToString("00");

        if (stateText != null)
        {
            stateText.text = completed ? "已完成" : (unlocked ? "可进入" : "未解锁");
            stateText.color = completed ? new Color(0.24f, 0.60f, 0.53f)
                            : (unlocked ? new Color(0.24f, 0.60f, 0.53f) : new Color(0.62f, 0.68f, 0.70f));
        }
        if (stateIcon != null)
        {
            Sprite s = completed ? checkIcon : (unlocked ? null : lockIcon);
            stateIcon.enabled = s != null;
            if (s != null)
            {
                stateIcon.sprite = s;
                stateIcon.color = completed ? new Color(0.24f, 0.60f, 0.53f) : new Color(0.62f, 0.68f, 0.70f);
            }
        }
        if (lockOverlay != null) lockOverlay.SetActive(!unlocked);
        if (button != null) button.interactable = unlocked;
        if (cardImage != null) cardImage.color = unlocked ? normalTint : lockedTint;
    }
}
