// 主界面（主菜单）控制器：开始游戏 / 读取存档 / 章节选择 / 内容概览 / 设置 / 退出
// 所有引用由 Assets/Editor/MainMenuBuilder.cs 在搭场景时接好，运行时不 FindObject。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class MainMenuUI : MonoBehaviour
{
    public const string GAME_SCENE = "Game";

    // ------------------------------------------------------------------ 引用
    [Header("弹层（主菜单 + 4 个页面 + 大图 + 确认 + toast）")]
    public UIPanel       menuLayer;
    public UIPanel       settingsPanel;
    public UIPanel       savePanel;
    public UIPanel       chapterPanel;
    public UIPanel       overviewPanel;
    public UIPanel       imagePanel;
    public ConfirmDialog confirm;
    public ToastUI       toast;

    [Header("主菜单按钮")]
    public Button btnStart, btnLoad, btnChapter, btnOverview, btnSettings, btnQuit;
    public Text   txtProgressHint;
    public Text   txtVersion;

    [Header("设置页")]
    public Slider    sldMaster, sldBgm, sldSfx, sldVoice, sldTextSpeed, sldAutoDelay;
    public Text      valMaster, valBgm, valSfx, valVoice, valTextSpeed, valAutoDelay;
    public UISwitch  swFullscreen, swAutoPlay, swAutoSave;
    public Button    btnSettingsApply, btnSettingsReset, btnSettingsBack;

    [Header("存读档页")]
    public SaveSlotUI[] slots = new SaveSlotUI[SaveSystem.SlotCount];
    public Button btnSaveRead, btnSaveDelete, btnSaveBack;
    public Text   txtSaveTip;
    public Sprite[] chapterArt = new Sprite[5];   // 每章定妆照（存档卡整卡铺图，SaveCardArt 工具接线）

    [Header("章节选择页")]
    public ChapterCardUI[] chapters = new ChapterCardUI[5];
    public Button btnChapterBack;

    [Header("内容概览页")]
    public OverviewDatabase overviewDb;
    public Button[]        ovChapterTabs      = new Button[5];
    public Text[]          ovChapterTabLabels = new Text[5];
    public Button[]        ovViewTabs         = new Button[5];
    public Text[]          ovViewTabLabels    = new Text[5];
    public OverviewCardUI[] ovCards           = new OverviewCardUI[2];
    public Text   ovChapterTitle, ovChapterDesc, ovEmptyHint;
    public Button btnOverviewBack;
    public Button ovPrevPage, ovNextPage;      // 概览翻页：每页 2 张卡，同章其余选择翻页看
    public Text   ovPageLabel;

    [Header("大图查看")]
    public Image  bigImage;
    public Text   bigTitle, bigBody, bigCounter;
    public Button btnBigClose, btnBigPrev, btnBigNext;

    [Header("状态角标图标")]
    public Sprite iconCheck, iconLock;
    [Header("页签贴图（选中态换底）")]
    public Sprite tabNormalSprite, tabSelectedSprite;

    [Header("配色")]
    public Color accentColor = new Color(0.216f, 0.596f, 0.529f);
    public Color mutedColor  = new Color(0.616f, 0.678f, 0.694f);

    // ------------------------------------------------------------------ 内部状态
    int    _selectedSlot = -1;
    int    _ovChapter    = 1;
    string _ovView       = "全部";
    int    _ovPage       = 0;
    int    _bigIndex     = 0;
    List<OverviewEntry> _ovList = new List<OverviewEntry>();

    // ------------------------------------------------------------------ 生命周期
    void Awake()
    {
        GameSettings.EnsureLoaded();
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        WireMainButtons();
        WireSettings();
        WireSavePanel();
        WireChapterPanel();
        WireOverview();
        WireImageViewer();

        SaveSlotUI.ChapterArt = chapterArt;   // 定妆照注入（空/越界由 SaveSlotUI 回退到运行时截图）

        RefreshProgressHint();
        RefreshSlots();
        RefreshChapters();
        RefreshOverview();
        SyncSettingsUI();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) Back();

        if (imagePanel != null && imagePanel.IsOpen)
        {
            if (Input.GetKeyDown(KeyCode.LeftArrow))  StepBig(-1);
            if (Input.GetKeyDown(KeyCode.RightArrow)) StepBig(1);
        }
    }

    /// ESC / 返回：优先关最上面那一层
    public void Back()
    {
        if (confirm != null && confirm.panel != null && confirm.panel.IsOpen) { confirm.Close(); return; }
        if (imagePanel    != null && imagePanel.IsOpen)    { imagePanel.Hide();    return; }
        if (overviewPanel != null && overviewPanel.IsOpen) { overviewPanel.Hide(); return; }
        if (chapterPanel  != null && chapterPanel.IsOpen)  { chapterPanel.Hide();  return; }
        if (savePanel     != null && savePanel.IsOpen)     { savePanel.Hide();     return; }
        if (settingsPanel != null && settingsPanel.IsOpen) { CloseSettings();      return; }
    }

    // ------------------------------------------------------------------ 主菜单
    void WireMainButtons()
    {
        if (btnStart    != null) btnStart.onClick.AddListener(OnStartGame);
        if (btnLoad     != null) btnLoad.onClick.AddListener(OnOpenSave);
        if (btnChapter  != null) btnChapter.onClick.AddListener(OnOpenChapters);
        if (btnOverview != null) btnOverview.onClick.AddListener(OnOpenOverview);
        if (btnSettings != null) btnSettings.onClick.AddListener(OnOpenSettings);
        if (btnQuit     != null) btnQuit.onClick.AddListener(OnQuit);
    }

    void OnStartGame()
    {
        if (!SaveSystem.HasAny)
        {
            GameProgress.NewGame();
            LoadGameScene();
            return;
        }
        ShowConfirm("开始游戏",
            "检测到已有存档。\n从第 1 章重新开始吗？（不会删除已有存档）",
            delegate { GameProgress.NewGame(); LoadGameScene(); },
            "重新开始", "取消");
    }

    void OnOpenSave()
    {
        RefreshSlots();
        if (savePanel != null) savePanel.Show();
    }

    void OnOpenChapters()
    {
        RefreshChapters();
        if (chapterPanel != null) chapterPanel.Show();
    }

    void OnOpenOverview()
    {
        _ovChapter = Mathf.Clamp(GameProgress.SelectedChapter, 1, 5);
        _ovPage = 0;
        RefreshOverview();
        if (overviewPanel != null) overviewPanel.Show();
    }

    void OnOpenSettings()
    {
        SyncSettingsUI();
        if (settingsPanel != null) settingsPanel.Show();
    }

    void OnQuit()
    {
        ShowConfirm("退出游戏", "确定要退出吗？", delegate
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }, "退出", "取消");
    }

    void ShowConfirm(string title, string body, System.Action onOk, string ok, string cancel)
    {
        if (confirm != null) confirm.Open(title, body, onOk, ok, cancel);
        else if (onOk != null) onOk();
    }

    bool _loading;   // 异步加载进行中（防双击重入）

    void LoadGameScene()
    {
        if (_loading) return;
        GameSettings.Save();
        if (Application.CanStreamedLevelBeLoaded(GAME_SCENE))
        {
            _loading = true;
            StartCoroutine(LoadGameSceneRoutine());
        }
        else if (toast != null)
        {
            toast.Show("场景 " + GAME_SCENE + " 还没加进 Build Settings");
        }
    }

    // ------------------------------------------------------------------ 异步加载 + 加载遮罩
    // Game 场景很重（6 个地点整场景），同步 LoadScene 会把点击冻在原地数秒且无任何反馈。
    // 这里改成 LoadSceneAsync：点完立刻出遮罩 → 显示进度 → 激活新场景。
    // 遮罩是运行时挂在 UI_Canvas 最顶的纯黑全屏（不透明：场景激活瞬间的主线程卡顿也看不见）；
    // 场景激活时 MainMenu 整个卸载、遮罩随之销毁，Game 侧 StoryRunner 本来就从全黑淡入，视觉无缝。
    IEnumerator LoadGameSceneRoutine()
    {
        var overlay = new GameObject("加载遮罩", typeof(RectTransform), typeof(Image));
        var canvas  = GetComponent<Canvas>();
        if (canvas == null)   // 理论不会发生（本组件就挂在 UI_Canvas 上），兜底而已
        {
            canvas = overlay.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 999;
        }
        overlay.transform.SetParent(canvas.transform, false);
        overlay.transform.SetAsLastSibling();
        var rt = (RectTransform)overlay.transform;
        rt.anchorMin = Vector2.zero;  rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;  rt.offsetMax = Vector2.zero;
        overlay.GetComponent<Image>().color = Color.black;   // raycastTarget 默认开 → 顺带挡住底下按钮

        var label = new GameObject("文字", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        label.transform.SetParent(overlay.transform, false);
        label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        label.rectTransform.sizeDelta = new Vector2(800f, 120f);
        label.alignment = TextAnchor.MiddleCenter;
        label.fontSize  = 30;
        label.color     = new Color(0.78f, 0.85f, 0.91f, 1f);
        label.font      = CloneMenuFont();

        string title = GameProgress.ChapterTitle(GameProgress.SelectedChapter);

        yield return null;   // 先让遮罩渲染出一帧，再开始加载

        var op = SceneManager.LoadSceneAsync(GAME_SCENE);
        op.allowSceneActivation = false;
        // progress 封顶 0.9：剩下 0.1 是"激活权限"，留给 allowSceneActivation。
        // 显示进度与真实进度解耦（真实进度会瞬间蹿满、把等待全晾给结尾的静止字幕）：
        // 显示值以固定速率追真实值——既不会跑在加载前面，也不会瞬间拉满；
        // 走满 100% 的同一帧立刻激活场景，结尾不空等。
        const float rate = 0.42f;   // 每秒最多涨 42% → 最快约 2.4 秒走完
        float shown = 0f;
        while (shown < 1f || op.progress < 0.9f)
        {
            float target = Mathf.Min(op.progress / 0.9f, 1f);
            shown = Mathf.MoveTowards(shown, target, rate * Time.unscaledDeltaTime);
            label.text = title + "\n正在加载… " + Mathf.RoundToInt(shown * 100f) + "%";
            yield return null;
        }
        label.text = title + "\n正在进入…";
        op.allowSceneActivation = true;
        // 激活后的首帧着色器编译会阻塞主线程几秒（编辑器专属，打包版基本没有），
        // 这段无法用任何 UI 动画遮盖——黑屏停在"正在进入…"是能做到的最好状态。
    }

    /// 拿菜单里现成的字体（中文_Deng），不引新资源；实在没有就用系统雅黑兜底
    Font CloneMenuFont()
    {
        foreach (var t in GetComponentsInChildren<Text>(true))
            if (t.font != null) return t.font;
        return Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 16);
    }

    public void RefreshProgressHint()
    {
        if (txtProgressHint == null) return;
        int slot = SaveSystem.LatestSlot();
        if (slot < 0)
        {
            txtProgressHint.text = "还没有存档 · 将从第 1 章开始";
            return;
        }
        var info = SaveSystem.Info(slot);
        if (info == null || info.data == null) { txtProgressHint.text = ""; return; }
        txtProgressHint.text = "上次进度：" + info.chapterText + "   " + info.timeText;
    }

    // ------------------------------------------------------------------ 设置页
    void WireSettings()
    {
        Hook(sldMaster,    valMaster,    delegate (float v) { GameSettings.Master    = v; GameSettings.Apply(); }, true);
        Hook(sldBgm,       valBgm,       delegate (float v) { GameSettings.Bgm       = v; }, false);
        Hook(sldSfx,       valSfx,       delegate (float v) { GameSettings.Sfx       = v; }, false);
        Hook(sldVoice,     valVoice,     delegate (float v) { GameSettings.Voice     = v; }, false);
        Hook(sldTextSpeed, valTextSpeed, delegate (float v) { GameSettings.TextSpeed = v; }, true);
        Hook(sldAutoDelay, valAutoDelay, delegate (float v) { GameSettings.AutoDelay = v; }, true);

        if (swFullscreen != null) swFullscreen.OnChanged = delegate (bool v) { GameSettings.Fullscreen = v; GameSettings.Apply(); };
        if (swAutoPlay   != null) swAutoPlay.OnChanged   = delegate (bool v) { GameSettings.AutoPlay  = v; };
        if (swAutoSave   != null) swAutoSave.OnChanged   = delegate (bool v) { GameSettings.AutoSave  = v; };

        if (btnSettingsApply != null) btnSettingsApply.onClick.AddListener(delegate
        {
            GameSettings.Save();
            if (toast != null) toast.Show("设置已保存");
        });
        if (btnSettingsReset != null) btnSettingsReset.onClick.AddListener(delegate
        {
            GameSettings.ResetToDefault();
            SyncSettingsUI();
            if (toast != null) toast.Show("已恢复默认设置");
        });
        if (btnSettingsBack != null) btnSettingsBack.onClick.AddListener(CloseSettings);
    }

    void CloseSettings()
    {
        GameSettings.Save();
        if (settingsPanel != null) settingsPanel.Hide();
    }

    void Hook(Slider slider, Text label, System.Action<float> apply, bool percent)
    {
        if (slider == null) return;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.onValueChanged.AddListener(delegate (float v)
        {
            apply(v);
            if (label != null) label.text = percent ? Mathf.RoundToInt(v * 100f) + "%" : v.ToString("0.00");
        });
        GameSettings.Changed += delegate
        {
            if (label != null) label.text = percent ? Mathf.RoundToInt(slider.value * 100f) + "%" : slider.value.ToString("0.00");
        };
    }

    public void SyncSettingsUI()
    {
        SetSlider(sldMaster,    valMaster,    GameSettings.Master,    true);
        SetSlider(sldBgm,       valBgm,       GameSettings.Bgm,       true);
        SetSlider(sldSfx,       valSfx,       GameSettings.Sfx,       true);
        SetSlider(sldVoice,     valVoice,     GameSettings.Voice,     true);
        SetSlider(sldTextSpeed, valTextSpeed, GameSettings.TextSpeed, true);
        SetSlider(sldAutoDelay, valAutoDelay, GameSettings.AutoDelay, true);

        if (swFullscreen != null) swFullscreen.SetOn(GameSettings.Fullscreen);
        if (swAutoPlay   != null) swAutoPlay.SetOn(GameSettings.AutoPlay);
        if (swAutoSave   != null) swAutoSave.SetOn(GameSettings.AutoSave);
    }

    void SetSlider(Slider s, Text label, float value, bool percent)
    {
        if (s == null) return;
        s.value = Mathf.Clamp01(value);
        if (label != null) label.text = percent ? Mathf.RoundToInt(s.value * 100f) + "%" : s.value.ToString("0.00");
    }

    // ------------------------------------------------------------------ 存读档页
    void WireSavePanel()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null) continue;
            slots[i].index = i;
            int index = i;
            if (slots[i].button != null) slots[i].button.onClick.AddListener(delegate { SelectSlot(index); });
        }
        if (btnSaveRead != null) btnSaveRead.onClick.AddListener(delegate
        {
            if (_selectedSlot < 0) { Toast("先选一个存档位"); return; }
            var info = SaveSystem.Info(_selectedSlot);
            if (info == null || !info.exists) { Toast("这个存档位是空的"); return; }
            GameProgress.SelectChapter(info.data.chapter);
            LoadGameScene();
        });
        if (btnSaveDelete != null) btnSaveDelete.onClick.AddListener(delegate
        {
            if (_selectedSlot < 0) { Toast("先选一个存档位"); return; }
            if (!SaveSystem.Exists(_selectedSlot)) { Toast("这个存档位本来就是空的"); return; }
            int slot = _selectedSlot;
            ShowConfirm("删除存档", "确定删除「" + SaveSystem.Info(slot).chapterText + "」这个存档吗？", delegate
            {
                SaveSystem.Delete(slot);
                RefreshSlots();
                RefreshProgressHint();
                if (toast != null) toast.Show("存档已删除");
            }, "删除", "取消");
        });
        if (btnSaveBack != null) btnSaveBack.onClick.AddListener(delegate { if (savePanel != null) savePanel.Hide(); });
    }

    void SelectSlot(int index)
    {
        _selectedSlot = index;
        RefreshSlots();
    }

    public void RefreshSlots()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null) continue;
            slots[i].index = i;
            slots[i].Refresh(i == _selectedSlot);
        }
        if (txtSaveTip != null)
        {
            int slot = SaveSystem.LatestSlot();
            txtSaveTip.text = slot < 0
                ? "还没有任何存档 · 游戏里做出选择后会自动存档"
                : "最近存档：" + SaveSystem.Info(slot).chapterText + "   " + SaveSystem.Info(slot).timeText;
        }
    }

    // ------------------------------------------------------------------ 章节选择页
    void WireChapterPanel()
    {
        for (int i = 0; i < chapters.Length; i++)
        {
            if (chapters[i] == null) continue;
            chapters[i].chapter = i + 1;
            int chapter = i + 1;
            if (chapters[i].button != null) chapters[i].button.onClick.AddListener(delegate { OnChapterClicked(chapter); });
        }
        if (btnChapterBack != null) btnChapterBack.onClick.AddListener(delegate { if (chapterPanel != null) chapterPanel.Hide(); });
    }

    void OnChapterClicked(int chapter)
    {
        if (!GameProgress.IsUnlocked(chapter))
        {
            Toast("第 " + (chapter - 1) + " 章通关后解锁第 " + chapter + " 章");
            return;
        }
        GameProgress.SelectChapter(chapter);
        LoadGameScene();
    }

    public void RefreshChapters()
    {
        for (int i = 0; i < chapters.Length; i++)
        {
            if (chapters[i] == null) continue;
            int chapter = i + 1;
            chapters[i].SetState(GameProgress.IsUnlocked(chapter), GameProgress.IsCompleted(chapter),
                iconCheck, iconLock, new Color(0.85f, 0.88f, 0.88f, 0.75f), Color.white);
        }
    }

    // ------------------------------------------------------------------ 内容概览页
    void WireOverview()
    {
        for (int i = 0; i < ovChapterTabs.Length; i++)
        {
            int chapter = i + 1;
            if (ovChapterTabs[i] != null) ovChapterTabs[i].onClick.AddListener(delegate { _ovChapter = chapter; _ovView = "全部"; _ovPage = 0; RefreshOverview(); });
        }
        for (int i = 0; i < ovViewTabs.Length; i++)
        {
            int idx = i;
            if (ovViewTabs[i] != null) ovViewTabs[i].onClick.AddListener(delegate { SetView(OverviewDatabase.Views[idx]); });
        }
        if (ovPrevPage != null) ovPrevPage.onClick.AddListener(delegate { StepOverviewPage(-1); });
        if (ovNextPage != null) ovNextPage.onClick.AddListener(delegate { StepOverviewPage(1); });
        for (int i = 0; i < ovCards.Length; i++)
        {
            int idx = i;
            if (ovCards[i] != null && ovCards[i].button != null)
                ovCards[i].button.onClick.AddListener(delegate { OpenBig(ovCards[idx].index); });
        }
        if (btnOverviewBack != null) btnOverviewBack.onClick.AddListener(delegate { if (overviewPanel != null) overviewPanel.Hide(); });
    }

    void SetView(string view)
    {
        _ovView = view;
        var list = overviewDb == null ? null : overviewDb.ForChapter(_ovChapter);
        if (view != "全部" && list != null)
        {
            bool any = false;
            for (int i = 0; i < list.Count; i++) if (list[i].view == view) any = true;
            if (!any)
            {
                Toast("第 " + _ovChapter + " 章没有「" + view + "」视角的记录");
                _ovView = "全部";
            }
        }
        _ovPage = 0;
        RefreshOverview();
    }

    void StepOverviewPage(int delta)
    {
        int pageSize = Mathf.Max(1, ovCards.Length);
        int pages    = Mathf.Max(1, Mathf.CeilToInt((_ovList == null ? 0 : _ovList.Count) / (float)pageSize));
        _ovPage      = Mathf.Clamp(_ovPage + delta, 0, pages - 1);
        RefreshOverview();
    }

    public void RefreshOverview()
    {
        _ovList = overviewDb == null ? new List<OverviewEntry>() : overviewDb.ForChapter(_ovChapter);

        // 每页 ovCards.Length 张卡（=2），_ovPage 从 0 起
        int pageSize = Mathf.Max(1, ovCards.Length);
        int pages    = Mathf.Max(1, Mathf.CeilToInt(_ovList.Count / (float)pageSize));
        if (_ovPage >= pages) _ovPage = pages - 1;

        for (int i = 0; i < ovCards.Length; i++)
        {
            var card = ovCards[i];
            if (card == null) continue;
            int listIdx = _ovPage * pageSize + i;
            if (listIdx < _ovList.Count)
            {
                var e = _ovList[listIdx];
                card.gameObject.SetActive(true);
                card.index = listIdx;
                if (card.image != null)
                {
                    card.image.sprite = e.frame;
                    card.image.enabled = e.frame != null;
                }
                if (card.titleText != null) card.titleText.text = e.title;
                if (card.viewText  != null) card.viewText.text  = e.view;
                if (card.bodyText  != null) card.bodyText.text  = e.body;
                card.SetDimmed(_ovView != "全部" && e.view != _ovView);
            }
            else card.gameObject.SetActive(false);
        }

        if (ovPageLabel != null) ovPageLabel.text = (_ovPage + 1) + " / " + pages;
        if (ovPrevPage != null)  ovPrevPage.interactable = _ovPage > 0;
        if (ovNextPage != null)  ovNextPage.interactable = _ovPage < pages - 1;

        if (ovChapterTitle != null) ovChapterTitle.text = GameProgress.ChapterTitle(_ovChapter);
        if (ovChapterDesc  != null) ovChapterDesc.text  = GameProgress.ChapterDesc(_ovChapter);

        bool unlocked = GameProgress.IsUnlocked(_ovChapter);
        if (ovEmptyHint != null)
        {
            ovEmptyHint.gameObject.SetActive(!unlocked);
            ovEmptyHint.text = "第 " + _ovChapter + " 章还没玩到，下面是它的 " + _ovList.Count + " 处选择记录";
        }

        for (int i = 0; i < ovChapterTabs.Length; i++)
        {
            bool on = (i + 1) == _ovChapter;
            bool ok = GameProgress.IsUnlocked(i + 1);
            if (ovChapterTabs[i] != null && ovChapterTabs[i].image != null && tabNormalSprite != null && tabSelectedSprite != null)
                ovChapterTabs[i].image.sprite = on ? tabSelectedSprite : tabNormalSprite;
            if (ovChapterTabLabels[i] != null)
            {
                ovChapterTabLabels[i].text  = GameProgress.IsCompleted(i + 1) ? "第" + (i + 1) + "章 ✦" : "第" + (i + 1) + "章";
                ovChapterTabLabels[i].color = on ? accentColor : (ok ? mutedColor : new Color(mutedColor.r, mutedColor.g, mutedColor.b, 0.55f));
            }
        }
        for (int i = 0; i < ovViewTabs.Length; i++)
        {
            bool on = OverviewDatabase.Views[i] == _ovView;
            if (ovViewTabs[i] != null && ovViewTabs[i].image != null && tabNormalSprite != null && tabSelectedSprite != null)
                ovViewTabs[i].image.sprite = on ? tabSelectedSprite : tabNormalSprite;
            if (ovViewTabLabels[i] != null)
            {
                ovViewTabLabels[i].text  = OverviewDatabase.Views[i];
                ovViewTabLabels[i].color = on ? accentColor : mutedColor;
            }
        }
    }

    // ------------------------------------------------------------------ 大图查看
    void WireImageViewer()
    {
        if (btnBigClose != null) btnBigClose.onClick.AddListener(delegate { if (imagePanel != null) imagePanel.Hide(); });
        if (btnBigPrev  != null) btnBigPrev.onClick.AddListener(delegate { StepBig(-1); });
        if (btnBigNext  != null) btnBigNext.onClick.AddListener(delegate { StepBig(1); });
    }

    void OpenBig(int index)
    {
        if (_ovList == null || index < 0 || index >= _ovList.Count) return;
        _bigIndex = index;
        ApplyBig();
        if (imagePanel != null) imagePanel.Show();
    }

    void StepBig(int delta)
    {
        if (_ovList == null || _ovList.Count == 0) return;
        _bigIndex = (_bigIndex + delta + _ovList.Count) % _ovList.Count;
        ApplyBig();
    }

    void ApplyBig()
    {
        var e = _ovList[_bigIndex];
        if (bigImage != null)
        {
            bigImage.sprite = e.frame;
            bigImage.enabled = e.frame != null;
        }
        if (bigTitle   != null) bigTitle.text   = e.view + " ｜ " + e.title;
        if (bigBody    != null) bigBody.text    = e.body;
        if (bigCounter != null) bigCounter.text = (_bigIndex + 1) + " / " + _ovList.Count;
    }

    // ------------------------------------------------------------------ 小工具
    void Toast(string msg)
    {
        if (toast != null) toast.Show(msg);
        else Debug.Log("[MainMenu] " + msg);
    }
}
