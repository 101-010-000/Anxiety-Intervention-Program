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

    /// 是否允许触发/显示（StoryRunner 只在进入对应的等待步骤时置 true）。
    /// ★ 不加这个开关：开场旁白段（自由走动）路过组长时按一下 F 就会把交互点消费掉，
    ///   等到真正该交互时提示还在、按 F 却没反应。
    public bool armed = true;

    /// 触发时回调（StoryRunner 运行期接上）
    public System.Action<StoryInteractable> onTriggered;

    public bool PlayerInRange { get; private set; }
    public bool Consumed { get; private set; }

    FirstPersonController _player;

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
            Vector3 a = transform.position; a.y = 0f;
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
    }
}
