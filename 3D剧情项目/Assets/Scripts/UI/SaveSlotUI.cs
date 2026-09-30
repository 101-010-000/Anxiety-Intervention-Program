// 存读档页的单个槽位（缩略图 / 章节 / 时间 / 空态）
using UnityEngine;
using UnityEngine.UI;

public class SaveSlotUI : MonoBehaviour
{
    public Button button;
    public Image  thumb;         // 缩略图（有档且有截图时显示）
    public Image  frameEmpty;    // 虚线空框（空档时显示）
    public Image  frameFilled;   // 实心底（有档时显示）
    public Text   chapterText;
    public Text   timeText;
    public Text   emptyText;     // "空存档"
    public Image  selectFrame;   // 选中态描边（显隐由 SlotSelectFx 的淡入驱动）
    public SlotSelectFx selectFx;// 选中/悬停动效

    public int index;

    /// 每章定妆照（MainMenuUI.Awake 注入，索引 = 章-1）；优先于运行时截图整卡铺图
    public static Sprite[] ChapterArt;

    public void Refresh(bool selected)
    {
        var info = SaveSystem.Info(index);
        bool has = info != null && info.exists;

        if (chapterText != null)
        {
            chapterText.text = has ? info.chapterText : "";
            chapterText.gameObject.SetActive(has);
        }
        if (timeText != null)
        {
            timeText.text = has ? info.timeText : "";
            timeText.gameObject.SetActive(has);
        }
        if (emptyText != null) emptyText.gameObject.SetActive(!has);
        if (frameEmpty != null)  frameEmpty.enabled = !has;
        if (frameFilled != null) frameFilled.enabled = false;   // 有档卡整卡铺定妆照，白实底不再用

        if (thumb != null)
        {
            var sprite = ChapterSpriteOf(has, info);            // 优先本章定妆照（圆角+暗带已烘进图）
            if (sprite == null && has && !string.IsNullOrEmpty(info.thumbPath))
                sprite = ThumbnailCache.Get(info.thumbPath);    // 缺图/老档 → 回退运行时截图
            thumb.enabled = sprite != null;
            if (sprite != null) thumb.sprite = sprite;
        }
        if (selectFx != null) selectFx.SetSelected(selected);
        else if (selectFrame != null) selectFrame.enabled = selected;   // 未接动效的旧场景兜底
    }

    /// 本章定妆照（章号越界 / 数组空 / 那张为空 → null，由调用方回退运行时截图）
    static Sprite ChapterSpriteOf(bool has, SaveSlotInfo info)
    {
        if (!has || ChapterArt == null || info == null || info.data == null) return null;
        int ch = info.data.chapter;
        if (ch < 1 || ch > ChapterArt.Length) return null;
        return ChapterArt[ch - 1];
    }
}

/// 运行时把截图文件读成 Sprite（存档缩略图用）
public static class ThumbnailCache
{
    static readonly System.Collections.Generic.Dictionary<string, Sprite> _map =
        new System.Collections.Generic.Dictionary<string, Sprite>();

    public static Sprite Get(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        Sprite s;
        if (_map.TryGetValue(path, out s)) return s;
        try
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (!tex.LoadImage(System.IO.File.ReadAllBytes(path))) { Object.Destroy(tex); return null; }
            tex.wrapMode = TextureWrapMode.Clamp;
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[ThumbnailCache] 读缩略图失败：" + e.Message);
            s = null;
        }
        _map[path] = s;
        return s;
    }
}
