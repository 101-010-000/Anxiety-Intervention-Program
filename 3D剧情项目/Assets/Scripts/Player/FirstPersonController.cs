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

    [Header("第三人称（把镜头放到身后看自己）")]
    [Tooltip("勾上 = 第三人称：相机跑到角色身后，能看见主角")]
    public bool thirdPerson = false;
    [Tooltip("相机在身后的距离（米）")]
    public float tpDistance = 3.4f;
    [Tooltip("相机围绕的支点高度（米，一般到胸口/肩膀）")]
    public float tpHeight = 1.45f;
    [Tooltip("相机看向的高度（米，一般到胸口，别盯着后脑勺）")]
    public float tpLookHeight = 1.20f;
    [Tooltip("俯仰范围（第三人称别让人把镜头插地/翻天）。正值 = 镜头抬到角色头上往下看，负值 = 镜头压低往上看")]
    public float tpPitchMin = -45f;
    public float tpPitchMax = 40f;
    [Tooltip("贴地保护：镜头压低到快进地面时，自动把「吊臂」缩短，而不是钻到地下")]
    public bool tpKeepAboveGround = true;
    [Tooltip("贴地保护：镜头最低离脚底平面多少米")]
    public float tpMinCameraHeight = 0.35f;
    [Tooltip("挡住相机的东西（留空用默认的 Everything；一般把角色所在的 Outline 层排除）")]
    public LayerMask tpBlockMask = ~0;
    [Tooltip("贴墙时最近能拉到多近")]
    public float tpMinDistance = 0.6f;
    [Tooltip("相机碰撞用的球半径（越大越不会穿墙，越小越贴）")]
    public float tpCollisionRadius = 0.22f;
    [Tooltip("相机跟随平滑（0 = 硬跟，默认就是 0；调大才有“拖尾/弹簧”感）")]
    public float tpFollowSmooth = 0f;
    [Tooltip("被挡住后推回原距离的速度（米/秒）")]
    public float tpReturnSpeed = 4f;
    [Tooltip("★ 相机水平角（度）。第三人称下鼠标左右转的是【它】，角色不跟着转；移动方向也以它为准")]
    public float camYaw = 0f;
    [Tooltip("角色转向移动方向的速度（度/秒）。越大越跟手")]
    public float turnSpeed = 720f;

    [Header("动画")]
    [Tooltip("把移动速度写进 Animator 的 Speed 参数（本作混合树：0=待机 / 1=行走 / 2=慢跑）")]
    public bool driveAnimation = true;
    [Tooltip("静止时写入的 Speed")]
    public float idleAnimSpeed = 0f;
    [Tooltip("行走时写入的 Speed")]
    public float walkAnimSpeed = 1.0f;
    [Tooltip("★ 移动动画播放速度倍率（不影响移动速度）：1=原速（默认）。一般不用改")]
    public float animSpeedScale = 1.0f;
    [Tooltip("移动时 Speed 参数的平滑时间（秒）。越小越干脆；起步那一下不插值，直接到位")]
    public float animStartSmooth = 0.04f;

    [Header("虚拟输入（自检 / 剧情演出用）")]
    [Tooltip("勾上 = 忽略键盘，改用下面的 inputOverride（自检要靠它：编辑器 tick ≠ 游戏帧，直接注入会跑不动）")]
    public bool useInputOverride = false;
    [Tooltip("虚拟输入：x=左右，y=前后，范围 -1~1")]
    public Vector2 inputOverride = Vector2.zero;

    [Header("状态")]
    [Tooltip("勾上 = 剧情对话中，禁止移动和转视角")]
    public bool locked;
    [Tooltip("true = 只锁移动、仍可转视角（剧情台词间隙用；locked=true 时本开关无效）")]
    public bool moveLocked;
    public bool lockCursorOnStart = true;
    [Tooltip("按 Esc 临时解锁鼠标，点回画面重新锁定")]
    public bool allowEscToUnlock = true;

    static readonly int SpeedHash = Animator.StringToHash("Speed");
    static readonly int LocomotionHash = Animator.StringToHash("Locomotion");   // 控制器里的混合树状态名
    float _animSpeedLast = 0f;
    bool _wasMoving = false;

    CharacterController cc;
    float pitch;
    float velY;
    Vector3 horizVel;
    Vector3 _lastGroundedPos = new Vector3(0f, 0.1f, 0f);   // 掉出世界保护的回溯点（默认=教室地板中心）

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
        if (thirdPerson) ResetCameraNow();                    // 开局相机就摆到位（否则第一帧还在老位置）
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

        if (moveLocked)            // 剧情台词间隙：能转视角，不能走
        {
            Step(true);
            DriveAnim(idleAnimSpeed);
            return;
        }

        Move();
    }

    // 第三人称：相机的位置/朝向在 LateUpdate 里算（要等其他东西都动完）
    //
    // ★ 关键原则：位置和朝向【都只由 camYaw / pitch / 距离 决定】，不从“角色位置-LookRotation”反推。
    //   否则相机位置一滞后（tpFollowSmooth），“看向角色”的朝向就会跟着转 → 左右横移/转身时镜头会跟着叟。
    void LateUpdate() { PlaceThirdPersonCamera(); }

    /// <summary>
    /// 立刻把第三人称相机摆到位：瞬移/开场直接调，避免"第一帧镜头还在老位置"甩一下。
    /// （开局的剧情瞬移 StoryRunner.Begin() 就会调它）
    /// </summary>
    public void ResetCameraNow()
    {
        _camDist = -1f;                               // 距离重新解算，别带上一处的
        camYaw = transform.eulerAngles.y;             // 镜头先摆到角色背后
        PlaceThirdPersonCamera();
    }

    void PlaceThirdPersonCamera()
    {
        if (!thirdPerson || cameraPivot == null) return;
        if (cameraPivot.parent != transform) return;      // 被剧情挂到别的机位上了，不抢

        Vector3 pivot = transform.position + Vector3.up * tpHeight;   // 只跟角色根（脚底原点），不含任何动画偏移
        Quaternion orbit = Quaternion.Euler(pitch, camYaw, 0f);
        Vector3 back = orbit * Vector3.back;              // 从角色指向相机

        // —— 距离：碰墙拉近（带迟滞，避免一帧撞到一帧没撞到 → 一伸一缩）
        float free = CastForCamera(pivot, back);
        if (_camDist < 0f) _camDist = free;
        else if (free < _camDist - 0.02f) _camDist = free;                     // 更近了：立刻（不然穿墙）
        else if (free > _camDist + 0.12f)                                      // 明显更远：缓慢推回去
            _camDist = Mathf.MoveTowards(_camDist, free, tpReturnSpeed * Time.deltaTime);
        _camDist = Mathf.Clamp(_camDist, tpMinDistance, tpDistance);

        Vector3 target = pivot + back * _camDist;
        // —— 贴地保护：镜头要钻到地面以下时，就把吊臂缩短（仍从上方/正对着看角色）
        if (tpKeepAboveGround)
        {
            float minY = transform.position.y + tpMinCameraHeight;
            if (back.y < -0.001f)
            {
                float maxDByGround = (minY - pivot.y) / back.y;      // back.y<0 → 正数
                if (maxDByGround < _camDist) _camDist = Mathf.Max(tpMinDistance, maxDByGround);
            }
            if (target.y < minY) target.y = minY;                    // 兜底：怎么算都不能入地
        }
        if (tpFollowSmooth <= 0f) cameraPivot.position = target;               // 默认硬跟（无滞后）
        else cameraPivot.position = Vector3.Lerp(cameraPivot.position, target,
                                                  1f - Mathf.Exp(-Time.deltaTime / tpFollowSmooth));

        // 朝向：依旧只由 camYaw/pitch 决定；tpLookHeight 只是把镜头往下多看一点（朝胸口）
        float aimDown = Mathf.Atan2(Mathf.Max(0f, tpHeight - tpLookHeight), Mathf.Max(0.01f, _camDist)) * Mathf.Rad2Deg;
        cameraPivot.rotation = orbit * Quaternion.Euler(aimDown, 0f, 0f);
    }

    static readonly RaycastHit[] _camHits = new RaycastHit[16];
    float _camDist = -1f;

    /// <summary>从支点往后探，返回“能拉到多远”（撞到东西就比 tpDistance 小）</summary>
    float CastForCamera(Vector3 origin, Vector3 dir)
    {
        int n = Physics.SphereCastNonAlloc(origin, Mathf.Max(0.01f, tpCollisionRadius), dir, _camHits, tpDistance,
                                           tpBlockMask, QueryTriggerInteraction.Ignore);
        float best = tpDistance;
        for (int i = 0; i < n; i++)
        {
            var h = _camHits[i];
            if (h.collider == null) continue;
            if (h.collider.transform.IsChildOf(transform)) continue;   // 自己的碰撞体（CharacterController 就在根上）
            if (h.distance < 0.01f) continue;
            // ★ 这里【不能】按法线忽略地板/天花板：那样天花板就挡不住镜头了（踩过）。
            //   地板会挡没关系——那正是“弹簧臂贴地缩短”，而且还有 tpKeepAboveGround 兜底。
            if (h.distance < best) best = h.distance;
        }
        return best;
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

    /// <summary>相机近裁剪面：贴脸时 0.05 会把贴身的腿切掉；第三人称离得远，反而要给大一点免得 z-fighting</summary>
    public void ApplyCameraNear()
    {
        if (cameraPivot == null) return;
        var cam = cameraPivot.GetComponent<Camera>();
        if (cam == null) cam = GetComponentInChildren<Camera>(true);
        if (cam != null) cam.nearClipPlane = thirdPerson ? Mathf.Max(0.05f, nearClip) : Mathf.Max(0.001f, nearClip);
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

    /// <summary>身体可见性：第一人称下只把相机所在的躯干设成“只投影”，四肢/腿照常显示；第三人称全部正常显示</summary>
    public void ApplyFirstPersonParts()
    {
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (cameraPivot != null && r.transform.IsChildOf(cameraPivot)) continue;

            if (thirdPerson)
            {
                r.shadowCastingMode = ShadowCastingMode.On;      // 第三人称要能看见主角
                continue;
            }

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

        if (thirdPerson) camYaw += mx;                  // ★ 第三人称：只转相机，角色不跟
        else transform.Rotate(0f, mx, 0f, Space.Self);   // 第一人称：左右转身体

        float lo = thirdPerson ? tpPitchMin : pitchMin;
        float hi = thirdPerson ? tpPitchMax : pitchMax;
        pitch = Mathf.Clamp(pitch + my, lo, hi);

        if (thirdPerson) return;                        // 第三人称的相机摆位交给 LateUpdate
        var e = cameraPivot.localEulerAngles;
        cameraPivot.localEulerAngles = new Vector3(pitch, e.y, e.z);
    }

    /// <summary>运行时切换第一/第三人称（会顺手把相机位置/水平角复位）</summary>
    public void SetThirdPerson(bool v)
    {
        thirdPerson = v;
        _camDist = -1f;
        if (v) camYaw = transform.eulerAngles.y;        // 切过去时先摆到角色背后
        if (!v && cameraPivot != null && cameraPivot.parent == transform)
        {
            cameraPivot.localRotation = Quaternion.identity;
            ApplyBody();
        }
        ApplyCameraNear();
        ApplyFirstPersonParts();
    }

    // ------------------------------------------------------------------ 移动
    void Move()
    {
        float ix = useInputOverride ? inputOverride.x : Input.GetAxisRaw("Horizontal");
        float iz = useInputOverride ? inputOverride.y : Input.GetAxisRaw("Vertical");
        MoveWithInput(ix, iz);
    }

    /// <summary>
    /// 把一次输入（ix 左右 / iz 前后，范围 -1~1）喂给移动逻辑。
    /// 第三人称：方向以【相机水平角 camYaw】为准，角色自己转向移动方向（按速度方向转）；
    /// 第一人称：方向以角色自身为准。自检/剧情演出可以直接调它来“虚拟走路”。
    /// </summary>
    public void MoveWithInput(float ix, float iz)
    {
        Vector3 wish = WishDir(ix, iz);

        float speed = Input.GetKey(runKey) ? runSpeed : walkSpeed;
        horizVel = Vector3.Lerp(horizVel, wish * speed, 1f - Mathf.Exp(-accel * Time.deltaTime));

        // 第三人称：角色朝移动方向平滑转身（相机不动）
        if (thirdPerson && wish.sqrMagnitude > 1e-4f)
        {
            var want = Quaternion.LookRotation(wish, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, Mathf.Max(0f, turnSpeed) * Time.deltaTime);
        }

        float v = new Vector2(horizVel.x, horizVel.z).magnitude;
        // ★ 动画混合看【输入】，不看被 accel 平滑过的实际速度：
        //   否则 Speed 会跟着加速曲线慢慢爬上来（~0.2s 才混到 Walk），感觉就是“起步慢”。
        //   位移仍然是原速加速（v 照旧用于脚打滑缩放）。
        float animV = Mathf.Max(v, wish.magnitude * speed);
        float t = Mathf.Clamp(animV / Mathf.Max(0.05f, walkSpeed), 0f, 2f);
        DriveAnim(Mathf.Lerp(idleAnimSpeed, walkAnimSpeed, t), v);

        Step(false);
    }

    // ------------------------------------------------------------------ 剧情演出视角（NpcEntrance/StoryRunner 用）
    /// ~0.3s 平滑把视角转向目标点：身体 yaw 与镜头 pitch 一起动（v4——此前只转 yaw、
    /// 俯仰角保留原值，玩家低头看桌面时进场会盯着自己的脚）。
    /// 直接驱动 transform/pitch，与 Look() 的增量式不冲突：动画结束 pitch=目标值，不回弹。
    public System.Collections.IEnumerator LookTowardRoutine(Vector3 worldPoint, float dur = 0.3f, float targetPitch = 8f)
    {
        Vector3 d = worldPoint - transform.position; d.y = 0f;
        if (d.sqrMagnitude < 0.001f) yield break;
        float yaw0 = transform.eulerAngles.y;
        float yaw1 = Quaternion.LookRotation(d.normalized).eulerAngles.y;
        float p0 = pitch;
        float camYaw0 = camYaw;                 // ★ 第三人称的镜头水平角要一起转，否则镜头不跟（只扭角色）
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.01f, dur);
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            transform.rotation = Quaternion.Euler(0f, Mathf.LerpAngle(yaw0, yaw1, k), 0f);
            pitch = Mathf.LerpAngle(p0, targetPitch, k);
            if (thirdPerson) camYaw = Mathf.LerpAngle(camYaw0, yaw1, k);   // 镜头跟着转到同一水平角
            if (cameraPivot != null)
            {
                var e = cameraPivot.localEulerAngles;
                cameraPivot.localEulerAngles = new Vector3(pitch, e.y, e.z);
            }
            yield return null;
        }
    }

    /// <summary>输入 → 世界方向（水平）。第三人称相对相机，第一人称相对角色</summary>
    public Vector3 WishDir(float ix, float iz)
    {
        Vector3 wish = thirdPerson
            ? Quaternion.Euler(0f, camYaw, 0f) * new Vector3(ix, 0f, iz)
            : transform.right * ix + transform.forward * iz;
        wish.y = 0f;
        if (wish.sqrMagnitude > 1f) wish.Normalize();
        return wish;
    }

    void Step(bool noHoriz)
    {
        // 掉出世界保护（2026-09-28）：y 低于地面 20m = 已经在虚空里下坠（正常游玩地面在 y≈0，
        // 台阶/床铺高度远不到 -20）。拉回【最后一次踩到地面的位置】并清速度——根因无论是什么
        // （传送未执行/锚点悬空/碰撞未加载），玩家都不会无限坠落。从未落地过则回世界原点（教室地板）。
        if (cc.isGrounded && transform.position.y > -5f) _lastGroundedPos = transform.position;
        else if (transform.position.y < -20f)
        {
            cc.enabled = false;
            transform.position = _lastGroundedPos;
            cc.enabled = true;
            velY = 0f;
            horizVel = Vector3.zero;
            Debug.LogWarning("[FirstPersonController] 掉出世界，已拉回最后落地点：" + _lastGroundedPos.ToString("F1"));
            return;
        }

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

        // ★ 从【移动 → 待机】的那一下：把 Locomotion 状态从第 0 帧重播，
        //   保证每次都从待机的开始帧起（混合树的子动画一直在跑，不重置就会在任意相位接上去）。
        bool movingNow = s > 0.05f;
        if (_wasMoving && !movingNow)
        {
            var cur = animator.GetCurrentAnimatorStateInfo(0);
            // 只在“当前确实停在 Locomotion”时重置（拿手机 Phone 状态就别去打断它）
            if (cur.shortNameHash == LocomotionHash && !animator.IsInTransition(0))
                animator.Play(LocomotionHash, 0, 0f);
        }
        _wasMoving = movingNow;

        // 起步/停下都用一点点缓动（animStartSmooth，默认 0.04s ≈ 0.1s 内到位）。
        // ★ 关键不是这里的缓动，而是上面 animV 用【输入】算目标值：目标一按就到位，所以缓动再小也不会拖成“起步慢”。
        animator.SetFloat(SpeedHash, s, Mathf.Max(0f, animStartSmooth), Time.deltaTime);
        _animSpeedLast = s;

        // 只有【真的在移动】时才按速度缩放播放速度；站着不动保持 1×（不然 Idle 会被压到 0.2×）
        if (animSpeedScale != 1f && actualSpeed > 0.05f)
            animator.speed = Mathf.Clamp(animSpeedScale * actualSpeed / Mathf.Max(0.05f, walkSpeed), 0.2f, 3f);
        else
            animator.speed = 1f;
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

    /// <summary>把相机收回角色身上（第三人称会自己重新摆位）</summary>
    public void ReleaseCamera()
    {
        if (cameraPivot == null) return;
        if (cameraPivot.parent != transform) cameraPivot.SetParent(transform, false);
        cameraPivot.localRotation = Quaternion.identity;
        ApplyBody();
    }

    static float NormalizePitch(float e)
    {
        return e > 180f ? e - 360f : e;
    }
}
