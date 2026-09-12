// 玩家设置：音量 / 文字速度 / 自动播放 / 自动存档 / 全屏
// 主菜单设置页与游戏内设置页共用一份数据，存 PlayerPrefs，改完立刻生效。
using UnityEngine;

public static class GameSettings
{
    public const string K_MASTER     = "opt.vol.master";   // 0..1 总音量
    public const string K_BGM        = "opt.vol.bgm";      // 0..1 音乐
    public const string K_SFX        = "opt.vol.sfx";      // 0..1 音效
    public const string K_VOICE      = "opt.vol.voice";    // 0..1 台词语音
    public const string K_TEXT_SPEED = "opt.text.speed";   // 0..1 慢 → 快
    public const string K_AUTO_DELAY = "opt.auto.delay";   // 0..1 快 → 慢
    public const string K_AUTO_PLAY  = "opt.auto.play";    // 0/1 自动播放
    public const string K_AUTO_SAVE  = "opt.auto.save";    // 0/1 做出选择后自动存档
    public const string K_FULLSCREEN = "opt.screen.full";  // 0/1 全屏

    public static float Master     = 0.80f;
    public static float Bgm        = 0.70f;
    public static float Sfx        = 0.85f;
    public static float Voice      = 1.00f;
    public static float TextSpeed  = 0.55f;
    public static float AutoDelay  = 0.50f;
    public static bool  AutoPlay   = false;
    public static bool  AutoSave   = true;
    public static bool  Fullscreen = true;

    /// 任何一项设置变化后触发（UI 用来刷新显示）
    public static event System.Action Changed;

    static bool _loaded;

    // ------------------------------------------------------------------ 读写
    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        Master     = PlayerPrefs.GetFloat(K_MASTER, Master);
        Bgm        = PlayerPrefs.GetFloat(K_BGM, Bgm);
        Sfx        = PlayerPrefs.GetFloat(K_SFX, Sfx);
        Voice      = PlayerPrefs.GetFloat(K_VOICE, Voice);
        TextSpeed  = PlayerPrefs.GetFloat(K_TEXT_SPEED, TextSpeed);
        AutoDelay  = PlayerPrefs.GetFloat(K_AUTO_DELAY, AutoDelay);
        AutoPlay   = PlayerPrefs.GetInt(K_AUTO_PLAY, AutoPlay ? 1 : 0) == 1;
        AutoSave   = PlayerPrefs.GetInt(K_AUTO_SAVE, AutoSave ? 1 : 0) == 1;
        Fullscreen = PlayerPrefs.GetInt(K_FULLSCREEN, Fullscreen ? 1 : 0) == 1;
        Apply();
    }

    public static void Save()
    {
        PlayerPrefs.SetFloat(K_MASTER, Master);
        PlayerPrefs.SetFloat(K_BGM, Bgm);
        PlayerPrefs.SetFloat(K_SFX, Sfx);
        PlayerPrefs.SetFloat(K_VOICE, Voice);
        PlayerPrefs.SetFloat(K_TEXT_SPEED, TextSpeed);
        PlayerPrefs.SetFloat(K_AUTO_DELAY, AutoDelay);
        PlayerPrefs.SetInt(K_AUTO_PLAY, AutoPlay ? 1 : 0);
        PlayerPrefs.SetInt(K_AUTO_SAVE, AutoSave ? 1 : 0);
        PlayerPrefs.SetInt(K_FULLSCREEN, Fullscreen ? 1 : 0);
        PlayerPrefs.Save();
    }

    public static void ResetToDefault()
    {
        Master = 0.80f; Bgm = 0.70f; Sfx = 0.85f; Voice = 1.00f;
        TextSpeed = 0.55f; AutoDelay = 0.50f;
        AutoPlay = false; AutoSave = true; Fullscreen = true;
        Save(); Apply(); Notify();
    }

    public static void Notify()
    {
        if (Changed != null) Changed();
    }

    /// 把设置作用到引擎上。音量用 AudioListener.volume 兜底，
    /// 具体音频分轨（BGM/音效/语音）由后续 AudioHub 读取 Bgm/Sfx/Voice 三个值。
    public static void Apply()
    {
        AudioListener.volume = Mathf.Clamp01(Master);
        if (Application.isPlaying) Screen.fullScreen = Fullscreen;
    }

    // ------------------------------------------------------------------ 派生值
    /// 打字机速度（字/秒）
    public static float TextCharsPerSecond { get { return Mathf.Lerp(12f, 60f, Mathf.Clamp01(TextSpeed)); } }
    /// 自动播放时每句停留秒数
    public static float AutoDelaySeconds { get { return Mathf.Lerp(0.8f, 3.4f, Mathf.Clamp01(AutoDelay)); } }
}
