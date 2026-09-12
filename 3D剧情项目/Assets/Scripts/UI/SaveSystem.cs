// 存档 / 章节进度：JSON 存到 persistentDataPath/saves/slotN.json
// 主菜单的「读取存档」「章节选择」和游戏内的自动存档共用这一套。
// 运行时的截图缩略图（UI-088）先留字段，AI 的文本全量存档等剧情系统接入后再补。
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public class SaveData
{
    public int    chapter      = 1;      // 章节号 1..5
    public string chapterTitle = "";     // 章节标题（存档列表上显示）
    public string nodeId       = "";     // 剧情节点 id（剧情系统接入后写）
    public int    step         = 0;      // 节点内第几句
    public string timeTag      = "";     // 存档时间文本 "2026-09-13 03:10"
    public int    unlockedChapter  = 1;  // 存档时已解锁到第几章
    public int    completedChapter = 0;  // 存档时已通关到第几章
    public string thumbnail    = "";     // 缩略图文件名（可选，运行时截图）
    public List<string> facts  = new List<string>();   // 剧情状态（已读、已获得道具等）
}

/// 给 UI 用的存档槽信息
public class SaveSlotInfo
{
    public int      index;
    public bool     exists;
    public SaveData data;
    public string   chapterText = "空存档";
    public string   timeText    = "";
    public string   thumbPath   = "";
}

public static class SaveSystem
{
    public const int SlotCount = 6;      // 6 个手动存档位（自动存档单独占 0 号位，见 AutoSlot）

    public const int AutoSlot = -1;      // 约定的自动存档槽（自动存档不占玩家槽位）

    static string _dir;

    public static string Dir
    {
        get
        {
            if (string.IsNullOrEmpty(_dir))
            {
                _dir = Path.Combine(Application.persistentDataPath, "saves");
                try { if (!Directory.Exists(_dir)) Directory.CreateDirectory(_dir); }
                catch (Exception e) { Debug.LogWarning("[SaveSystem] 存档目录创建失败：" + e.Message); }
            }
            return _dir;
        }
    }

    public static string FileOf(int index)
    {
        return Path.Combine(Dir, "slot" + (index + 1) + ".json");
    }

    public static bool Exists(int index)
    {
        if (index < 0 || index >= SlotCount) return false;
        try { return File.Exists(FileOf(index)); }
        catch { return false; }
    }

    public static SaveData Load(int index)
    {
        if (!Exists(index)) return null;
        try
        {
            string json = File.ReadAllText(FileOf(index));
            var d = JsonUtility.FromJson<SaveData>(json);
            if (d.facts == null) d.facts = new List<string>();
            return d;
        }
        catch (Exception e) { Debug.LogWarning("[SaveSystem] 读档失败 " + index + "：" + e.Message); return null; }
    }

    /// 写档（自动补时间戳 / 进度快照）
    public static SaveData Write(int index, SaveData data)
    {
        if (data == null) data = new SaveData();
        if (string.IsNullOrEmpty(data.timeTag)) data.timeTag = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        data.unlockedChapter  = GameProgress.Unlocked;
        data.completedChapter = GameProgress.Completed;
        if (string.IsNullOrEmpty(data.chapterTitle)) data.chapterTitle = GameProgress.ChapterTitle(data.chapter);
        try
        {
            File.WriteAllText(FileOf(index), JsonUtility.ToJson(data, true));
            Debug.Log("[SaveSystem] 已存档 slot" + (index + 1) + "：" + data.chapterTitle);
        }
        catch (Exception e) { Debug.LogWarning("[SaveSystem] 存档失败 " + index + "：" + e.Message); }
        return data;
    }

    public static void Delete(int index)
    {
        try { if (File.Exists(FileOf(index))) File.Delete(FileOf(index)); }
        catch (Exception e) { Debug.LogWarning("[SaveSystem] 删档失败 " + index + "：" + e.Message); }
    }

    public static SaveSlotInfo Info(int index)
    {
        var info = new SaveSlotInfo { index = index, exists = Exists(index) };
        if (info.exists)
        {
            info.data = Load(index);
            if (info.data != null)
            {
                info.chapterText = string.IsNullOrEmpty(info.data.chapterTitle)
                    ? GameProgress.ChapterTitle(info.data.chapter)
                    : info.data.chapterTitle;
                info.timeText = info.data.timeTag;
                if (!string.IsNullOrEmpty(info.data.thumbnail))
                {
                    string p = Path.Combine(Dir, info.data.thumbnail);
                    if (File.Exists(p)) info.thumbPath = p;
                }
            }
        }
        return info;
    }

    public static bool HasAny
    {
        get
        {
            for (int i = 0; i < SlotCount; i++) if (Exists(i)) return true;
            return false;
        }
    }

    /// 最近一次存档的槽位（没有则 -1）
    public static int LatestSlot()
    {
        int best = -1; DateTime bestTime = DateTime.MinValue;
        for (int i = 0; i < SlotCount; i++)
        {
            var d = Load(i);
            if (d == null) continue;
            DateTime t;
            if (!DateTime.TryParse(d.timeTag, out t)) t = DateTime.MinValue;
            if (best < 0 || t > bestTime) { best = i; bestTime = t; }
        }
        return best;
    }

    public static void DeleteAll()
    {
        for (int i = 0; i < SlotCount; i++) Delete(i);
    }
}

/// 章节解锁 / 通关进度（PlayerPrefs 里几条整数，够用）
public static class GameProgress
{
    const string K_UNLOCKED = "flow.unlocked";    // 已解锁到的章节（1..5）
    const string K_DONE     = "flow.completed";   // 已通关章节（0..5）
    const string K_NEXT     = "flow.chapter";     // 待进入章节（章节选择 / 读档写入，游戏场景读它）

    public static readonly string[] ChapterTitles =
    {
        "小组作业的汇报",
        "一条不想回的邀约",
        "食堂、迷茫与咨询室",
        "考试周与复习计划",
        "宿舍里的误会",
    };

    public static readonly string[] ChapterDescs =
    {
        "教室 · 走廊      被交到手上的汇报任务",
        "宿舍              一条邀约，两种念头",
        "食堂 · 咨询办公室  考研还是就业，看不见方向",
        "宿舍 · 图书馆      考试周和论文一起压过来",
        "宿舍 · 食堂        一句没说开的话",
    };

    public static string ChapterTitle(int chapter)
    {
        int i = Mathf.Clamp(chapter, 1, ChapterTitles.Length) - 1;
        return "第" + Mathf.Clamp(chapter, 1, ChapterTitles.Length) + "章 · " + ChapterTitles[i];
    }

    public static string ChapterDesc(int chapter)
    {
        int i = Mathf.Clamp(chapter, 1, ChapterDescs.Length) - 1;
        return ChapterDescs[i];
    }

    public static int Unlocked
    {
        get { return Mathf.Clamp(PlayerPrefs.GetInt(K_UNLOCKED, 1), 1, ChapterTitles.Length); }
        set { PlayerPrefs.SetInt(K_UNLOCKED, Mathf.Clamp(value, 1, ChapterTitles.Length)); PlayerPrefs.Save(); }
    }

    public static int Completed
    {
        get { return Mathf.Clamp(PlayerPrefs.GetInt(K_DONE, 0), 0, ChapterTitles.Length); }
        set { PlayerPrefs.SetInt(K_DONE, Mathf.Clamp(value, 0, ChapterTitles.Length)); PlayerPrefs.Save(); }
    }

    public static bool IsUnlocked(int chapter) { return chapter <= Unlocked; }
    public static bool IsCompleted(int chapter) { return chapter <= Completed; }

    /// 每章通关：解锁下一章。需求规定通关后不自动跳转，所以这里只记进度。
    public static void MarkCompleted(int chapter)
    {
        if (chapter > Completed) Completed = chapter;
        if (chapter + 1 > Unlocked) Unlocked = Mathf.Min(chapter + 1, ChapterTitles.Length);
    }

    /// 选中要去哪一章（章节选择 / 读取存档 / 开始游戏都走这里），游戏场景启动时读它
    public static void SelectChapter(int chapter)
    {
        PlayerPrefs.SetInt(K_NEXT, Mathf.Clamp(chapter, 1, ChapterTitles.Length));
        PlayerPrefs.Save();
    }

    public static int SelectedChapter
    {
        get { return Mathf.Clamp(PlayerPrefs.GetInt(K_NEXT, 1), 1, ChapterTitles.Length); }
    }

    public static void NewGame()
    {
        Unlocked = 1;
        Completed = 0;
        SelectChapter(1);
    }
}
