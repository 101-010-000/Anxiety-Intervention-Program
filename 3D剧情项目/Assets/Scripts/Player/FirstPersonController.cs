// 第一人称控制器：WASD 移动 + 鼠标转视角 + 重力 + 驱动待机/行走/跑步动画。
//
// 挂法（或直接用菜单 Tools/干预项目/给玩家挂第一人称控制）：
//   1) 脚本挂在玩家根节点上（会自动补 CharacterController）
//   2) 把 FP 相机拖到 Camera Pivot（留空则自动找子物体里的相机）
//   3) 根节点负责左右转（Yaw），相机负责上下看（Pitch）
//
// ⚠️ 想改身高/视角高低，改 playerHeight / eyeHeight 这两个字段，
//    **不要去改物体的 Y** —— 物体 Y 就是脚底，一动模型就会陷进/浮出地面。
//    CharacterController 的 height 和 center 由脚本自动保持一致。
//
// 剧情对话时锁住玩家：FirstPersonController.Instance.SetLocked(true);

using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    /// <summary>当前实例。剧情/对话系统可以直接拿它来锁住移动。</summary>
    public static FirstPersonController Instance;

    [Header("引用")]
    [Tooltip("负责俯仰的相机。留空则自动找子物体里的 Camera")]
    public Transform cameraPivot;
    [Tooltip("待机/行走动画的 Animator。留空则自动找")]
    public Animator animator;

    [Header("身高（改这两个，不要改物体 Y）")]
    [Tooltip("角色总身高。CharacterController 的 height 和 center 会自动跟随")]
    public float playerHeight = 1.75f;
    [Tooltip("眼睛（相机）离地高度。想压低视角改这个")]
    public float eyeHeight = 1.4f;
    [Tooltip("第一人称下把这些部位设为【只投影】（隐藏自己但保留影子）。常用于相机所在的躯干。留空 = 什么都不隐藏")]
    public string[] firstPersonShadowsOnlyParts = { "torso" };
    [Tooltip("整个身体都只留影子（会把角色完全藏起来，一般不推荐）")]
    public bool hideOwnBody = false;
    [Tooltip("修正角度变化时身体部件被误剔除（SkinnedMeshRenderer 按 bounds 剔除的经典坑）")]
    public bool fixSkinnedCulling = true;
    [Tooltip("相机近裁剪面。相机在身体内部时，太大会把贴身的腿裁掉")]
    public float nearClip = 0.01f;

    [Header("移动（本作只用 Walk，不做跑动）")]
    [Tooltip("行走速度")]
    public float walkSpeed = 1.6f;
    [Tooltip("跑动速度。跟 walkSpeed 相同 = 不跑（按 Shift 也没变化）")]
    public float runSpeed = 1.6f;
    [Tooltip("速度变化的快慢，越大越跟手")]
    public float accel = 14f;
    public float gravity = -14f;
    [Tooltip("按住这个键提速（runSpeed == walkSpeed 时无效）")]
    public KeyCode runKey = KeyCode.LeftShift;

    [Header("视角")]
    public float mouseSensitivity = 2.0f;
    public float pitchMin = -80f;
    public float pitchMax = 80f;
    public bool invertY = false;

    [Header("动画")]
    [Tooltip("把移动速度写进 Animator 的 Speed 参数（本作混合树：0=待机 1=Walk）")]
    public bool driveAnimation = true;
    [Tooltip("静止时写入的 Speed")]
    public float idleAnimSpeed = 0f;
    [Tooltip("行走时写入的 Speed")]
    public float walkAnimSpeed = 1.0f;
    [Tooltip("动画播放速度倍率。脚打滑/跨步不匹配时调这个")]
    public float animSpeedScale = 1.0f;

    [Header("状态")]
    [Tooltip("勾上 = 剧情对话中，禁止移动和转视角")]
    public bool locked;
    public bool lockCursorOnStart = true;
    [Tooltip("按 Esc 临时解锁鼠标，点回画面重新锁定")]
    public bool allowEscToUnlock = true;

    static readonly int SpeedHash = Animator.StringToHash("Speed");

    CharacterController cc;
    float pitch;
    float velY;
    Vector3 horizVel;

    // ------------------------------------------------------------------
    void Awake()
    {
        Instance = this;
        cc = GetComponent<CharacterController>();
        ResolveRefs();
        ApplyBody();

        if (animator != null)
        {
            animator.applyRootMotion = false;   // 位移交给 CharacterController
            if (driveAnimation && animator.runtimeAnimatorController == null)
                Debug.LogWarning("[FirstPersonController] Animator 没有 Controller，播不了动画。" +
                                 "跑一下菜单 Tools/干预项目/主角改用 Walk（不跑）", this);
        }
        ApplyBody();
        ApplyFirstPersonParts();
        ApplyCameraNear();
        if (fixSkinnedCulling) FixSkinnedCulling();
    }

    void Start()
    {
        if (lockCursorOnStart) SetCursorLocked(true);
    }

    void OnDisable()
    {
        SetCursorLocked(false);
        horizVel = Vector3.zero;
        velY = 0f;
    }

    void Update()
    {
        if (allowEscToUnlock)
        {
            if (Input.GetKeyDown(KeyCode.Escape)) SetCursorLocked(false);
            else if (Cursor.lockState != CursorLockMode.Locked && Input.GetMouseButtonDown(0)) SetCursorLocked(true);
        }

        if (locked)
        {
            Step(true);            // 锁住：只施重力
            DriveAnim(idleAnimSpeed);
            return;
        }

        Look();
        Move();
    }

    // ------------------------------------------------------------------ 身高等尺寸
    void ResolveRefs()
    {
        if (cameraPivot == null)
        {
            var cam = GetComponentInChildren<Camera>(true);
            if (cam != null) cameraPivot = cam.transform;
        }
        if (cameraPivot != null) pitch = NormalizePitch(cameraPivot.localEulerAngles.x);
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
    }

    /// <summary>把 playerHeight / eyeHeight 应用到 CharacterController 和相机</summary>
    public void ApplyBody()
    {
        if (cc == null) cc = GetComponent<CharacterController>();
        if (cc != null)
        {
            cc.height = Mathf.Max(0.6f, playerHeight);
            cc.center = new Vector3(0f, cc.height * 0.5f, 0f);            // ★ center 必须跟 height 一致
            cc.radius = Mathf.Min(cc.radius, cc.height * 0.5f - 0.01f);
        }
        if (cameraPivot != null)
        {
            var p = cameraPivot.localPosition;
            cameraPivot.localPosition = new Vector3(p.x, Mathf.Clamp(eyeHeight, 0.2f, Mathf.Max(0.25f, playerHeight - 0.05f)), p.z);
        }
    }

    /// <summary>相机近裁剪面：相机在身体里时，0.05 会把贴身的腿切掉</summary>
    public void ApplyCameraNear()
    {
        if (cameraPivot == null) return;
        var cam = cameraPivot.GetComponent<Camera>();
        if (cam == null) cam = GetComponentInChildren<Camera>(true);
        if (cam != null) cam.nearClipPlane = Mathf.Max(0.001f, nearClip);
    }

    /// <summary>
    /// SkinnedMeshRenderer 是按 bounds 做视锥剔除的。相机贴近身体时，
    /// 若 bounds 没包住变形后的网格，整个部件会被误剔除 → 角度一变部件就“消失”。
    /// updateWhenOffscreen = true 让它每帧重算 bounds（只给玩家用，代价可接受）。
    /// </summary>
    public void FixSkinnedCulling()
    {
        foreach (var s in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            s.updateWhenOffscreen = true;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        ResolveRefs();
        ApplyBody();
        ApplyFirstPersonParts();
        ApplyCameraNear();
        if (fixSkinnedCulling && !Application.isPlaying) FixSkinnedCulling();
    }
#endif

    /// <summary>身体可见性：默认只把相机所在的躯干设成“只投影”，四肢/腿照常显示</summary>
    public void ApplyFirstPersonParts()
    {
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (cameraPivot != null && r.transform.IsChildOf(cameraPivot)) continue;

            if (hideOwnBody)
            {
                r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                continue;
            }

            bool hit = false;
            if (firstPersonShadowsOnlyParts != null)
            {
                string n = r.gameObject.name;
                foreach (var key in firstPersonShadowsOnlyParts)
                {
                    if (string.IsNullOrEmpty(key)) continue;
                    if (n.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) >= 0) { hit = true; break; }
                }
            }
            r.shadowCastingMode = hit ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
        }
    }

    // ------------------------------------------------------------------ 视角
    void Look()
    {
        if (Cursor.lockState != CursorLockMode.Locked) return;
        if (cameraPivot == null) return;

        float mx = Input.GetAxisRaw("Mouse X") * mouseSensitivity;
        float my = Input.GetAxisRaw("Mouse Y") * mouseSensitivity * (invertY ? 1f : -1f);

        transform.Rotate(0f, mx, 0f, Space.Self);       // 左右转身体

        pitch = Mathf.Clamp(pitch + my, pitchMin, pitchMax);
        var e = cameraPivot.localEulerAngles;
        cameraPivot.localEulerAngles = new Vector3(pitch, e.y, e.z);
    }

    // ------------------------------------------------------------------ 移动
    void Move()
    {
        float ix = Input.GetAxisRaw("Horizontal");
        float iz = Input.GetAxisRaw("Vertical");
        Vector3 wish = transform.right * ix + transform.forward * iz;
        if (wish.sqrMagnitude > 1f) wish.Normalize();

        float speed = Input.GetKey(runKey) ? runSpeed : walkSpeed;
        horizVel = Vector3.Lerp(horizVel, wish * speed, 1f - Mathf.Exp(-accel * Time.deltaTime));

        float v = new Vector2(horizVel.x, horizVel.z).magnitude;
        // 连续映射：0 → 待机，walkSpeed → 行走（本作混合树只有 0/1 两档）
        float t = Mathf.Clamp01(v / Mathf.Max(0.05f, walkSpeed));
        DriveAnim(Mathf.Lerp(idleAnimSpeed, walkAnimSpeed, t), v);

        Step(false);
    }

    void Step(bool noHoriz)
    {
        if (cc.isGrounded && velY < 0f) velY = -2f;
        velY += gravity * Time.deltaTime;

        Vector3 h = noHoriz ? Vector3.zero : horizVel;
        if (noHoriz) horizVel = Vector3.zero;
        cc.Move(h * Time.deltaTime + Vector3.up * velY * Time.deltaTime);
    }

    void DriveAnim(float s, float actualSpeed = -1f)
    {
        if (!driveAnimation || animator == null) return;
        if (animator.runtimeAnimatorController == null) return;

        animator.SetFloat(SpeedHash, s, 0.1f, Time.deltaTime);

        // 按实际速度缩放播放速度，减轻脚打滑 / 跨步对不上
        if (animSpeedScale != 1f && actualSpeed >= 0f)
            animator.speed = Mathf.Clamp(animSpeedScale * actualSpeed / Mathf.Max(0.05f, walkSpeed), 0.2f, 2.5f);
    }

    // ------------------------------------------------------------------ 外部接口
    /// <summary>剧情对话开始/结束时调用</summary>
    public void SetLocked(bool v)
    {
        locked = v;
        if (v) horizVel = Vector3.zero;
    }

    public void SetCursorLocked(bool v)
    {
        Cursor.lockState = v ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !v;
    }

    /// <summary>把相机挂到指定机位上（剧情强制视角用）</summary>
    public void SnapCameraTo(Transform mount)
    {
        if (mount == null || cameraPivot == null) return;
        cameraPivot.SetParent(mount, false);
        cameraPivot.localPosition = Vector3.zero;
        cameraPivot.localRotation = Quaternion.identity;
        locked = true;
    }

    static float NormalizePitch(float e)
    {
        return e > 180f ? e - 360f : e;
    }
}
