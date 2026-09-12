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
    public Image  selectFrame;   // 选中态外框

    public int index;

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
        if (frameFilled != null) frameFilled.enabled = has;

        if (thumb != null)
        {
            var sprite = !has || string.IsNullOrEmpty(info.thumbPath) ? null : ThumbnailCache.Get(info.thumbPath);
            thumb.enabled = sprite != null;
            if (sprite != null) thumb.sprite = sprite;
        }
        if (selectFrame != null) selectFrame.enabled = selected;
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
