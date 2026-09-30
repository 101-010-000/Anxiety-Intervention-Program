// 台词语音诊断（2026-09-29）：核对 Resources/语音 清单 ↔ 磁盘 mp3 ↔ 五章 json 的覆盖情况。
// 菜单：Tools/干预项目/台词语音/诊断 → 报告 assets/_报告/_台词语音.txt
// 用途：部署后验证；剧本改动后看哪些句子缺片（键 = 章:步号 / md5:正文哈希，与生成管线约定一致）。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class VoiceLineDiag
{
    // 与 python 管线（voice_batch_generate.py）一致的选角表——报告里展示用；换角后记得同步两边
    static readonly Dictionary<string, string> CAST = new Dictionary<string, string>
    {
        { "徐夏",   "longwan_v3 / 龙婉·细腻柔声女" },
        { "林溪",   "longyan_v3 / 龙颜·温暖春风女" },
        { "陆宣雨", "longanli_v3 / 龙安莉·利落从容女" },
        { "李老师", "longyingling_v3 / 龙应聆·温和共情女" },
        { "王含",   "longanrou_v3 / 龙安柔·温柔娴静女" },
        { "舍友A",  "longanhuan_v3 / 龙安欢·欢脱元气女" },
        { "舍友B",  "longxian_v3 / 龙仙·豪放可爱女" },
        { "李同学", "longxiaochun_v3 / 龙小淳·知性积极女" },
        { "学姐",   "longyingtao_v3 / 龙应桃·温柔淡定女" },
        { "组长",   "longze_v3 / 龙泽·温暖元气男" },
        { "张同学", "longyingxun_v3 / 龙应询·年轻青涩男" },
        { "旁白",   "longtian_v3 / 龙天·磁性理智男" },
    };

    [MenuItem("Tools/干预项目/台词语音/诊断（清单↔mp3↔剧本覆盖）")]
    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("台词语音诊断（" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "）");
        sb.AppendLine("==================================================");

        // ① 清单
        var resDir = Path.Combine(Application.dataPath, "Resources/语音");
        var manPath = Path.Combine(resDir, "manifest.txt");
        var entries = new Dictionary<string, string>();
        if (File.Exists(manPath))
        {
            var man = JsonUtility.FromJson<Man>(File.ReadAllText(manPath));
            if (man != null && man.entries != null)
                foreach (var e in man.entries)
                    if (!string.IsNullOrEmpty(e.key) && !string.IsNullOrEmpty(e.path))
                        entries[e.key] = e.path;
            sb.AppendLine("清单：manifest.txt，" + entries.Count + " 条（模型 " + (man != null ? man.model : "?") +
                          "，生成于 " + (man != null ? man.generated : "?") + "）");
        }
        else
        {
            sb.AppendLine("清单：manifest.txt ✗ 缺失（先跑 额外文件/工具脚本/voice_batch_generate.py 部署）");
        }

        // ② 磁盘 mp3 与清单对账
        int have = 0; var missingFiles = new List<string>();
        foreach (var kv in entries)
        {
            if (File.Exists(Path.Combine(resDir, kv.Value + ".mp3"))) have++;
            else missingFiles.Add(kv.Key);
        }
        sb.AppendLine("mp3 落盘：" + have + "/" + entries.Count + (missingFiles.Count == 0 ? " ✓" :
            "（缺 " + missingFiles.Count + "：如 " + string.Join("、", missingFiles.Take(5)) + "…）"));

        // ③ 剧本侧覆盖（重算键，与运行时同一套归一化）
        var jsonDir = Path.Combine(Application.dataPath, "数据/剧情");
        sb.AppendLine("--------------------------------------------------");
        var castUsed = new Dictionary<string, int>();
        foreach (var ch in Enumerable.Range(1, 5))
        {
            var p = Path.Combine(jsonDir, "第" + ch + "章.json");
            if (!File.Exists(p)) { sb.AppendLine("第" + ch + "章：json ✗"); continue; }
            var chapter = JsonUtility.FromJson<StoryChapter>(File.ReadAllText(p));
            int need = 0, covered = 0; var noEntry = new List<string>();
            for (int i = 0; i < chapter.steps.Count; i++)
            {
                var st = chapter.steps[i];
                string spk;
                if (st.t == "dlg") spk = StripSpeaker(st.s);
                else if (st.t == "mon") spk = string.IsNullOrEmpty(StripSpeaker(st.s)) ? "徐夏" : StripSpeaker(st.s);
                else if (st.t == "nar") spk = "旁白";
                else
                {
                    if (st.t == "choice")   // 选择题解释 = md5 键（徐夏 mon）
                        foreach (var opt in st.options)
                        {
                            var body = DialogueVoicePlayer.NormalizeForTts(opt.body);
                            if (body.Length == 0 || !System.Text.RegularExpressions.Regex.IsMatch(body, @"[\u4e00-\u9fffA-Za-z0-9]")) continue;
                            need++;
                            if (entries.ContainsKey("md5:" + DialogueVoicePlayer.Md5(body))) covered++;
                            else noEntry.Add("ch" + ch + ":c" + i + "解释");
                        }
                    continue;
                }
                if (spk == "班群") continue;                    // 系统通知，不配音
                var text = DialogueVoicePlayer.NormalizeForTts(st.x);
                if (text.Length == 0 || !System.Text.RegularExpressions.Regex.IsMatch(text, @"[\u4e00-\u9fffA-Za-z0-9]")) continue;
                need++;
                if (castUsed.ContainsKey(spk)) castUsed[spk]++; else castUsed[spk] = 1;
                if (entries.ContainsKey("ch" + ch + ":" + i) &&
                    File.Exists(Path.Combine(resDir, entries["ch" + ch + ":" + i] + ".mp3"))) covered++;
                else noEntry.Add("ch" + ch + ":" + i + "_" + spk);
            }
            sb.AppendLine("第" + ch + "章：需配音 " + need + "，已覆盖 " + covered +
                          (covered == need ? " ✓" : "（缺 " + (need - covered) + "：如 " + string.Join("、", noEntry.Take(5)) + "）"));
        }

        // ④ 选角一览
        sb.AppendLine("--------------------------------------------------");
        sb.AppendLine("选角（生成管线 voice_batch_generate.py 的 CAST 同款）：");
        foreach (var kv in CAST)
            sb.AppendLine("  " + kv.Key.PadRight(4) + " → " + kv.Value + "（台词 " + (castUsed.ContainsKey(kv.Key) ? castUsed[kv.Key].ToString() : "0") + " 句）");

        // ⑤ 抽查 3 个 AudioClip 能否加载
        sb.AppendLine("--------------------------------------------------");
        var spot = entries.Values.Where(v => File.Exists(Path.Combine(resDir, v + ".mp3"))).Take(3).ToList();
        foreach (var v in spot)
        {
            var clip = Resources.Load<AudioClip>("语音/" + v);
            sb.AppendLine("抽查 " + Path.GetFileName(v) + " → " + (clip != null ? clip.length.ToString("F1") + "s ✓" : "加载失败 ✗"));
        }

        var outDir = Path.Combine(Application.dataPath, "assets/_报告");
        Directory.CreateDirectory(outDir);
        var outPath = Path.Combine(outDir, "_台词语音.txt");
        File.WriteAllText(outPath, sb.ToString(), Encoding.UTF8);
        Debug.Log("[台词语音] 诊断完成 → " + outPath + "\n" + sb);
        EditorUtility.DisplayDialog("台词语音诊断", "完成，报告：assets/_报告/_台词语音.txt", "好");
    }

    static string StripSpeaker(string s)
    {
        var t = (s ?? "").Trim();
        foreach (var suf in new[] { "（微信）", "（通知）" })
            if (t.EndsWith(suf)) t = t.Substring(0, t.Length - suf.Length);
        return t.Trim();
    }

    [Serializable] class Ent { public string key; public string path; public string speaker; public string voice; public string md5; }
    [Serializable] class Man { public string model; public string generated; public Ent[] entries; }
}
