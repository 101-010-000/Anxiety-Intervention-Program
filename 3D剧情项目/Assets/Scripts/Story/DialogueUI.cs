// 底部对话框：三语态渲染（对话/旁白/独白）+ 打字机 + 淡入淡出。
//
// 层级适配（两种都支持，优先用户在场景里手搭的那套）：
//   ① UI交互 / 对话 → 对话框 → 名字、对话内容        ← 用户 2026-09-23 手搭，布局一律不动
//   ② Dialog  → DialogBack → name、neirong            ← 旧结构回退
//
// ★「对话」节点允许在场景里默认 SetActive(false)（用户要求：进游戏按需启用）——
//   节点禁用时 Awake 不会执行，所以 ShowNode() 会自己激活节点再补一次接线（EnsureRefs 幂等）。
// 三语态只切样式，不换框（设计定稿：一种 UI 三种语态）：
//   dlg 对话 = 名牌显示（微信台词名牌直接显示"林溪（微信）"）+ 深色常规；
//   nar 旁白 = 名牌隐藏 + 中灰；mon 独白 = 名牌隐藏 + 浅蓝灰斜体。
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class DialogueUI : MonoBehaviour
{
    static readonly Color COL_DLG = new Color32(0x21, 0x28, 0x33, 0xFF);
    static readonly Color COL_NAR = new Color32(0x4A, 0x55, 0x63, 0xFF);
    static readonly Color COL_MON = new Color32(0x5B, 0x6E, 0x84, 0xFF);

    /// 一句台词打字机播完时触发（StoryRunner 用它进入"间隙"状态）
    public System.Action onLineTyped;

    CanvasGroup _box;
    Text _name;
    Text _content;
    CanvasGroup _nameGroup;
    Coroutine _boxRt;      // 框体淡入淡出
    Coroutine _lineRt;     // 当前这句的打字机
    bool _refsReady;
    GameObject _dim;       // 「遮罩」：只在 对话 dlg / 内心独白 mon 时启用（用户规则）

    string _full = "";
    bool _typing;
    float _nameTarget;

    public bool IsTyping { get { return _typing; } }
    public bool NodeVisible { get { return _box != null && _box.alpha > 0.01f; } }

    void Awake() { EnsureRefs(); }

    // ------------------------------------------------------------------ 接线（幂等：节点默认禁用时 Awake 会被跳过，由 ShowNode 补跑）
    void EnsureRefs()
    {
        if (_refsReady) return;
        _refsReady = true;

        _box = GetComponent<CanvasGroup>();
        if (_box == null) _box = gameObject.AddComponent<CanvasGroup>();
        _box.alpha = 0f;
        _box.interactable = false;
        _box.blocksRaycasts = false;        // 台词推进走 Input，不挡 UI 点击

        // 文本容器：用户新结构「对话框」→ 名字 / 对话内容；旧结构回退 DialogBack → name / neirong
        Transform box = transform.Find("对话框");
        if (box == null) box = transform.Find("DialogBack");
        _name = FindText(box, "名字");
        if (_name == null) _name = FindText(box, "name");
        _content = FindText(box, "对话内容");
        if (_content == null) _content = FindText(box, "neirong");

        if (_name != null)
        {
            _nameGroup = _name.GetComponent<CanvasGroup>();
            if (_nameGroup == null) _nameGroup = _name.gameObject.AddComponent<CanvasGroup>();
            _nameGroup.alpha = 0f;
            _nameGroup.blocksRaycasts = false;
        }

        // 遮罩（用户规则）：只有【对话 dlg / 内心独白 mon】才启用，旁白 nar 不压暗。
        // 工具已把它提到画布最前（UI 最底层）；这里兼容它挂在「对话」下或画布下两种位置。
        Transform dimT = transform.Find("遮罩");
        if (dimT == null && transform.parent != null) dimT = transform.parent.Find("遮罩");
        _dim = dimT != null ? dimT.gameObject : null;
        if (_dim != null)
        {
            var dg = _dim.GetComponent<Graphic>();
            if (dg != null) dg.canvasRenderer.SetAlpha(0f);
            _dim.SetActive(false);
        }

        if (_name == null || _content == null)
            Debug.LogError("[DialogueUI] 对话下没找到「名字 / 对话内容」的 uGUI Text —— 先跑 Tools/干预项目/搭建第一章剧情");
        else if (_content.font == null || !_content.font.HasCharacter('中'))
            Debug.LogWarning("[DialogueUI] 对话内容 用的不是中文字体（中文会成方块/空白）—— 重跑一次 Tools/干预项目/搭建第一章剧情 即可");
    }

    static Text FindText(Transform parent, string childName)
    {
        if (parent == null) return null;
        var t = parent.Find(childName);
        return t != null ? t.GetComponent<Text>() : null;
    }

    // ------------------------------------------------------------------ 节点显隐
    /// 对话节点开始：按需激活节点 + 整框淡入
    public void ShowNode()
    {
        if (!gameObject.activeSelf) gameObject.SetActive(true);   // 默认禁用时在这里启用（Awake 同步补跑）
        EnsureRefs();
        FadeBox(1f, 0.25f);
    }

    /// 对话节点结束：整框淡出
    public void HideNode()
    {
        EnsureRefs();
        FadeBox(0f, 0.25f);
        SetDim(false);
    }

    void FadeBox(float target, float dur)
    {
        if (_box == null) return;
        if (_boxRt != null) StopCoroutine(_boxRt);
        _boxRt = StartCoroutine(BoxRoutine(target, dur));
    }

    IEnumerator BoxRoutine(float target, float dur)
    {
        float from = _box.alpha, t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.01f, dur);
            _box.alpha = Mathf.Lerp(from, target, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)));
            yield return null;
        }
        _box.alpha = target;
    }

    // ------------------------------------------------------------------ 一句台词
    /// 播一句：旧句快速淡出 → 换样式与文本 → 打字机逐字 → 播完回调 onLineTyped
    public void PlayLine(StoryStep step)
    {
        EnsureRefs();
        SetDim(step.t == "dlg" || step.t == "mon");
        if (_lineRt != null) { StopCoroutine(_lineRt); _lineRt = null; }
        if (_content != null && _content.canvasRenderer.GetAlpha() > 0.01f)
            _content.CrossFadeAlpha(0f, 0.10f, true);      // 旧句淡出
        _lineRt = StartCoroutine(LineRoutine(step));
    }

    IEnumerator LineRoutine(StoryStep step)
    {
        yield return new WaitForSecondsRealtime(0.10f);

        bool dlg = step.t == "dlg";
        _nameTarget = dlg ? 1f : 0f;
        if (dlg && _name != null) _name.text = step.s ?? "";    // 名牌原样显示（含"（微信）"后缀）

        Color col = COL_DLG; FontStyle st = FontStyle.Normal;
        if (step.t == "nar") col = COL_NAR;
        else if (step.t == "mon") { col = COL_MON; st = FontStyle.Italic; }
        if (_content != null)
        {
            _content.color = col;
            _content.fontStyle = st;
            _content.canvasRenderer.SetAlpha(1f);
            _content.CrossFadeAlpha(1f, 0f, true);
        }

        _full = step.x ?? "";
        _typing = true;

        float cps = Mathf.Max(2f, GameSettings.TextCharsPerSecond);
        int n = 0; float acc = 0f;
        if (_content != null) _content.text = "";
        while (n < _full.Length)
        {
            acc += Time.deltaTime * cps;
            while (acc >= 1f && n < _full.Length) { acc -= 1f; n++; }
            if (_content != null) _content.text = _full.Substring(0, n);
            yield return null;
        }
        if (_content != null) _content.text = _full;
        _typing = false;
        if (onLineTyped != null) onLineTyped.Invoke();
    }

    /// 打字中点击：立刻补全全句（下一次点击才推进）
    public void SkipTyping()
    {
        if (!_typing) return;
        if (_lineRt != null) { StopCoroutine(_lineRt); _lineRt = null; }
        _typing = false;
        if (_content != null) _content.text = _full;
        if (onLineTyped != null) onLineTyped.Invoke();
    }

    // ------------------------------------------------------------------ 名牌
    void Update()
    {
        if (_nameGroup != null)
            _nameGroup.alpha = Mathf.MoveTowards(_nameGroup.alpha, _nameTarget, Time.unscaledDeltaTime / 0.15f);
    }

    /// 遮罩开关（用户规则：对话 dlg / 内心独白 mon 开，旁白 nar 关）
    void SetDim(bool on)
    {
        if (_dim == null) return;
        if (!_dim.activeSelf) _dim.SetActive(true);
        var g = _dim.GetComponent<Graphic>();
        if (g != null) g.CrossFadeAlpha(on ? 1f : 0f, 0.22f, true);
    }
}
