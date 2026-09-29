// 剧情交互点：两种模式（挂在场景的触发节点上）
//   InteractF —— 玩家走近（半径内）出"按 F 交谈"提示，按 F 触发（如：组长）
//   Touch     —— 玩家走进触发盒即触发（如：教室门口撞张知远）
// 触发后回调 StoryRunner（运行期由 runner 统一接线），一次性触发后自禁用。
using UnityEngine;

public class StoryInteractable : MonoBehaviour
{
    public enum Mode { InteractF, Touch }

    public Mode mode = Mode.InteractF;
    public string displayName = "";
    public float radius = 2.2f;          // F 模式判定半径（水平距离）
    public KeyCode key = KeyCode.F;
    public bool oneShot = true;

    [Tooltip("属于第几章（1-5）。多章交互点并存时，StoryRunner 只取本章的（FindFree 过滤）。默认 1 兼容第一章既有节点。")]
    public int chapterTag = 1;

    [Tooltip("F 提示整句（如「拿起手机」）。留空 = 默认「与<displayName>交谈」。")]
    public string promptText = "";

    [Tooltip("道具联动（可空）：场景里的道具 GameObject 名（如 手机_淡蓝）。" +
             "① 交互判定中心 = 道具位置（玩家要走近道具才能交互，按名解析，手挪道具自动跟随）；" +
             "② Fire 后道具整棵隐藏（「拿起」的可见反馈）；③ Revive/重新武装时道具重新出现（下次交互前）。")]
    public string propObjectName = "";

    /// 是否允许触发/显示（StoryRunner 只在进入对应的等待步骤时置 true）。
    /// ★ 不加这个开关：开场旁白段（自由走动）路过组长时按一下 F 就会把交互点消费掉，
    ///   等到真正该交互时提示还在、按 F 却没反应。
    public bool armed = true;

    /// 触发时回调（StoryRunner 运行期接上）
    public System.Action<StoryInteractable> onTriggered;

    public bool PlayerInRange { get; private set; }
    public bool Consumed { get; private set; }

    FirstPersonController _player;
    GameObject _prop;          // 按名解析后缓存（隐藏后是 inactive，GameObject.Find 找不到，必须缓存）
    bool _propMissing;         // 找过没找到：不再每帧 Find（ShowProp 时会再给一次机会）

    /// 交互判定中心：联动道具的位置，没配道具就用自身（水平距离判定会把 y 清零）
    public Vector3 PromptCenter
    {
        get { var p = ResolveProp(); return p != null ? p.transform.position : transform.position; }
    }

    GameObject ResolveProp()
    {
        if (string.IsNullOrEmpty(propObjectName)) return null;
        if (_prop != null) return _prop;
        if (_propMissing) return null;
        _prop = GameObject.Find(propObjectName);
        if (_prop == null)
        {
            _propMissing = true;
            Debug.LogWarning("[StoryInteractable] 没找到联动道具「" + propObjectName + "」（交互中心回退到自身位置，隐藏/重现不生效）", this);
        }
        return _prop;
    }

    /// 重新武装时让联动道具回到场景（StoryRunner 进入 interact 步骤时调；Revive 里也会调）
    public void ShowProp()
    {
        if (_prop == null) _propMissing = false;   // 每次武装都再试一次，别让一次 Find 失败永久失效
        var p = ResolveProp();
        if (p != null && !p.activeSelf) p.SetActive(true);
    }

    void HideProp()
    {
        var p = ResolveProp();
        if (p != null) p.SetActive(false);
    }

    FirstPersonController Player
    {
        get
        {
            if (_player == null) _player = FindObjectOfType<FirstPersonController>();
            return _player;
        }
    }

    void Update()
    {
        if (Consumed || onTriggered == null) return;
        if (!armed) { PlayerInRange = false; return; }

        if (mode == Mode.InteractF)
        {
            var p = Player;
            if (p == null) return;
            Vector3 a = PromptCenter; a.y = 0f;
            Vector3 b = p.transform.position; b.y = 0f;
            bool inRange = (a - b).sqrMagnitude <= radius * radius;
            if (inRange != PlayerInRange) PlayerInRange = inRange;
            if (inRange && Input.GetKeyDown(key)) Fire();
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (Consumed || !armed || mode != Mode.Touch || onTriggered == null) return;
        if (other.GetComponentInParent<FirstPersonController>() == null) return;
        Fire();
    }

    /// 触发（oneShot 时自禁用）；自检/调试可直接调
    public void Fire()
    {
        if (Consumed) return;
        if (oneShot)
        {
            Consumed = true;
            PlayerInRange = false;
            var col = GetComponent<Collider>();
            if (col != null) col.enabled = false;
        }
        if (onTriggered != null) onTriggered(this);
        HideProp();      // 联动道具整棵隐藏 = 「被拿起」的可见反馈（没配道具时 no-op）
    }

    /// 消费后复用（2026-09-28）：同一章多次用同一个点（第2章两次"拿起手机"）。
    /// ⚠ 不能叫 Reset()——那会撞 Unity 编辑器给 MonoBehaviour 的 Reset 消息（组件被重置时被编辑器调用）。
    public void Revive()
    {
        Consumed = false;
        PlayerInRange = false;
        var col = GetComponent<Collider>();
        if (col != null) col.enabled = true;
        ShowProp();      // 下一次交互快要开始时，道具先回到桌上（第2章第二次拿手机）
    }
}
