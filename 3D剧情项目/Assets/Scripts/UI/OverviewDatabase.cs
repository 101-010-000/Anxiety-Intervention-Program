// 内容概览的数据表：每章 4 处选择（配图 + 视角分类 + 标题 + 概述）
// 由 Assets/Editor/MainMenuBuilder.cs 生成并填充，之后可以在 Inspector 里手改。
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class OverviewEntry
{
    public int    chapter = 1;              // 1..5
    public string view    = "自我";          // 自我 / 旁观 / 未来 / 其他
    public string title   = "";              // 记录标题
    public string body    = "";              // 概述（与收录图/需求文本一致）
    public Sprite frame;                     // 项目文档/选项收录内容 里的原图
}

public class OverviewDatabase : ScriptableObject
{
    public List<OverviewEntry> entries = new List<OverviewEntry>();

    public List<OverviewEntry> ForChapter(int chapter)
    {
        var list = new List<OverviewEntry>();
        for (int i = 0; i < entries.Count; i++)
            if (entries[i] != null && entries[i].chapter == chapter) list.Add(entries[i]);
        return list;
    }

    public static readonly string[] Views = { "全部", "自我", "旁观", "未来", "其他" };
}
