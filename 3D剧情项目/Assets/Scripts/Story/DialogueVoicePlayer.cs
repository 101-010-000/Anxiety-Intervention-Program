// 台词语音播放器（2026-09-29）：按「ch{章}:{步号}」或「md5:{正文哈希}」查 Resources/语音/manifest.txt，
// 播放对应的 CosyVoice 剪辑（阿里云百炼 cosyvoice-v3-flash，声线选角见 AGENTS.md「台词语音」节）。
//
// 设计约定：
//   · 懒创建：Ensure() 运行时自建 GameObject+AudioSource，不需要改场景/接线；
//   · 缺片静默：清单没有 / mp3 缺失（如剧本改动后未重生成、额度未补的章）→ 直接不发声，绝不报错；
//   · 自检/续播快进不发声（StorySmokeDriver.Requested / StoryResumeDriver.Active）；
//   · 音量 = GameSettings.Voice（设置页「台词语音」滑条）；Master 由 AudioListener.volume 全局承担；
//   · 挂接点：StoryRunner.PlayText 开始一句时 PlayStep；Next() 进新步先 Stop（推进即切上一句，
//     补全不切）；开场旁白/自动播放的自动推进会等语音播完（IsPlaying 门）。
//   · 班群（通知）不配音（系统通知）；纯标点台词（「…………」）生成期已跳过 = 静默停顿。
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

public class DialogueVoicePlayer : MonoBehaviour
{
    [Serializable] class VoiceEntry { public string key; public string path; public string speaker; public string voice; public string md5; }
    [Serializable] class VoiceManifest { public string model; public string generated; public VoiceEntry[] entries; }

    static readonly Regex StickerRx = new Regex(@"\[[^\]\[]{1,8}\]");   // [比心] 等贴纸标记（与生成管线一致）

    public static DialogueVoicePlayer Instance { get; private set; }

    AudioSource _src;
    Dictionary<string, string> _byKey;      // "ch1:12" / "md5:..." → Resources 相对路径（无扩展名）
    bool _triedLoad;

    public static bool IsPlaying
    {
        get { return Instance != null && Instance._src != null && Instance._src.isPlaying; }
    }

    /// 场景里没有也无所谓：第一次播台词时自建（Game 场景卸载即随之销毁，不污染主菜单）
    public static DialogueVoicePlayer Ensure()
    {
        if (Instance != null) return Instance;
        var go = new GameObject("台词语音");
        return go.AddComponent<DialogueVoicePlayer>();
    }

    void Awake()
    {
        Instance = this;
        _src = gameObject.AddComponent<AudioSource>();
        _src.playOnAwake = false;
        _src.spatialBlend = 0f;                       // UI 语音，不空间化
        _src.volume = Mathf.Clamp01(GameSettings.Voice);
        GameSettings.Changed += OnSettingChanged;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        GameSettings.Changed -= OnSettingChanged;
    }

    void OnSettingChanged()
    {
        if (_src != null) _src.volume = Mathf.Clamp01(GameSettings.Voice);
    }

    void LoadManifest()
    {
        if (_triedLoad) return;
        _triedLoad = true;
        _byKey = new Dictionary<string, string>();
        var ta = Resources.Load<TextAsset>("语音/manifest");
        if (ta == null) { Debug.LogWarning("[台词语音] Resources/语音/manifest.txt 缺失——台词全部静默（跑 额外文件/工具脚本/voice_batch_generate.py 部署）"); return; }
        VoiceManifest man = null;
        try { man = JsonUtility.FromJson<VoiceManifest>(ta.text); } catch { }
        if (man == null || man.entries == null) { Debug.LogWarning("[台词语音] manifest 解析失败——台词全部静默"); return; }
        foreach (var e in man.entries)
            if (e != null && !string.IsNullOrEmpty(e.key) && !string.IsNullOrEmpty(e.path))
                _byKey[e.key] = e.path;
        Debug.Log("[台词语音] 清单加载 " + _byKey.Count + " 条（" + man.model + "，" + man.generated + "）");
    }

    /// 播第 N 章第 idx 步的台词（StoryRunner.PlayText 调；idx=Next 自增后的 StepIndex-1）
    public void PlayStep(int chapter, int stepIdx)
    {
        if (StorySmokeDriver.Requested || StoryResumeDriver.Active) return;
        LoadManifest();
        string path;
        if (!_byKey.TryGetValue("ch" + chapter + ":" + stepIdx, out path)) return;
        Play(path);
    }

    /// 按正文哈希播（ChoicePanel 选择题解释：t="mon" 的即兴台词没有步号）
    public void PlayTextHash(string rawText)
    {
        if (StorySmokeDriver.Requested || StoryResumeDriver.Active) return;
        LoadManifest();
        string path;
        if (!_byKey.TryGetValue("md5:" + Md5(NormalizeForTts(rawText)), out path)) return;
        Play(path);
    }

    public void Stop()
    {
        if (_src != null && _src.isPlaying) _src.Stop();
    }

    void Play(string path)
    {
        var clip = Resources.Load<AudioClip>("语音/" + path);
        if (clip == null) return;                     // mp3 缺（未生成/未部署）→ 静默
        _src.Stop();
        _src.clip = clip;
        _src.Play();
    }

    // ---------------------------------------------------------------- 与生成管线（python）保持一致的归一化
    /// 贴纸标记去除 + 换行并空格 + 去首尾空白（Python: STICKER.sub→replace('\n',' ')→strip）
    public static string NormalizeForTts(string x)
    {
        if (string.IsNullOrEmpty(x)) return "";
        return StickerRx.Replace(x, "").Replace("\n", " ").Trim();
    }

    public static string Md5(string s)
    {
        using (var m = MD5.Create())
        {
            var b = m.ComputeHash(Encoding.UTF8.GetBytes(s ?? ""));
            var sb = new StringBuilder(b.Length * 2);
            foreach (var x in b) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }
    }
}
