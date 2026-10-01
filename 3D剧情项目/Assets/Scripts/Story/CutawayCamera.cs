// 剧情切视角（StoryRunner 的 cut 步骤，2026-09-29）：临时第三人称相机跟拍指定角色
// （如第三章李老师"到时间了……"的 cutaway），主角原地不动、输入暂停。
// 配对使用：CutawayCamera.Show(target) → …… → CutawayCamera.Restore()。
//   · Show 会先关掉主角的 FP_相机（避免双渲染 + 双 AudioListener），由 StoryRunner 停 FirstPersonController；
//     关掉后场景里就没收音器了 → cutaway 相机自己挂一个 AudioListener（台词语音/一切声音靠它，2026-10-01 修静默）；
//   · 机位 = 角色身后 distance 米、高 height，LookAt 角色 headY 高度，LateUpdate 持续跟随；
//     json 可带 camH / lookH 覆盖机位与视线高度（≤0 或缺省 = 1.55 / 1.35；坐姿角色给低些——第1章林溪 1.15/0.95）
//   · 角色站定时鼠标可环绕/俯仰看（谈话段"视角可转动"，用户 2026-10-01）；一走动自动回正成跟拍。
//   · 开 URP 后期（renderPostProcessing），梦核雾/景深/调色与主相机一致，切过去不跳质感。
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class CutawayCamera : MonoBehaviour
{
    public Transform follow;
    public float distance = 2.6f;   // 跟拍距离（角色身后）
    public float height = 1.55f;    // 机位高度
    public float headY = 1.35f;     // 看向角色的“头”的高度
    Camera _cam;                    // Show() 挂上的相机（和本组件同 GameObject，Restore 销毁整棵即回收）
    float _yaw, _pitch;             // 鼠标环绕角/俯仰附加角（目标站定时可转，走动自动回正——2026-10-01）
    Vector3 _lastPos;
    bool _hasLastPos;

    static CutawayCamera _active;
    static Camera _playerCam;       // 被暂时关掉的主角相机（FP_相机）

    public static void Show(Transform target, float camH = -1f, float lookH = -1f)
    {
        Restore();                                   // 幂等：先收旧的
        if (target == null) return;
        var main = Camera.main;                      // ★ 先取再关（关掉后 Camera.main 就不是它了）
        if (main != null) { _playerCam = main; main.transform.gameObject.SetActive(false); }

        var go = new GameObject("CutawayCamera");
        _active = go.AddComponent<CutawayCamera>();
        _active.follow = target;
        if (camH > 0f) _active.height = camH;        // json 可选机位（坐姿等特殊高度）；≤0 = 保持默认 1.55/1.35
        if (lookH > 0f) _active.headY = lookH;       // LateUpdate 读的就是这两个实例字段，Show 时定死即可
        _active._cam = go.AddComponent<Camera>();
        _active._cam.fieldOfView = 48f;
        _active._cam.nearClipPlane = 0.05f;
        _active._cam.farClipPlane = 600f;
        _active._cam.clearFlags = CameraClearFlags.Skybox;
        var extra = _active._cam.GetUniversalAdditionalCameraData();
        extra.renderPostProcessing = true;
        // ★ 台词语音的收音兜底：FP_相机关掉后场景里就没有 AudioListener 了（台词语音是 2D AudioSource，
        //   没收音器 = 整场静默）。Restore 销毁整棵 GO 时它一起回收，不会和主角的组成双收音。
        go.AddComponent<AudioListener>();
    }

    public static void Restore()
    {
        if (_active != null) { Destroy(_active.gameObject); _active = null; }
        if (_playerCam != null) { _playerCam.transform.gameObject.SetActive(true); _playerCam = null; }
    }

    void LateUpdate()
    {
        if (follow == null) { Restore(); return; }   // 跟丢（被删/章卸载）→ 自己收摊
        Vector3 fwd = follow.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        fwd.Normalize();

        // 老师站定后视角可转（用户 2026-10-01）：鼠标环绕/俯仰；目标一走动就自动回正——
        // 走位跟拍保持稳定，谈话时把镜头交给玩家。FPC 已被 StoryRunner 停用，鼠标输入归这里。
        float dt = Time.deltaTime;
        bool moving = _hasLastPos && (follow.position - _lastPos).sqrMagnitude > (0.15f * dt) * (0.15f * dt);
        _lastPos = follow.position; _hasLastPos = true;
        if (moving)
        {
            _yaw = Mathf.MoveTowards(_yaw, 0f, 240f * dt);
            _pitch = Mathf.MoveTowards(_pitch, 0f, 90f * dt);
        }
        else
        {
            float mx = Mathf.Clamp(Input.GetAxis("Mouse X"), -3f, 3f);
            float my = Mathf.Clamp(Input.GetAxis("Mouse Y"), -3f, 3f);
            _yaw += mx * 2.0f;
            _pitch = Mathf.Clamp(_pitch + my * 1.4f, -25f, 42f);
        }

        // 0° 时与旧机位完全一致：目标身后 distance、高 height，LookAt headY
        Quaternion yawRot = Quaternion.AngleAxis(_yaw, Vector3.up);
        Vector3 back = yawRot * (-fwd);
        Vector3 right = yawRot * Vector3.Cross(Vector3.up, fwd);
        back = Quaternion.AngleAxis(_pitch, right) * back;
        Vector3 pos = follow.position + back.normalized * distance + Vector3.up * height;
        pos.y = Mathf.Max(pos.y, 0.3f);              // 别钻地
        transform.position = pos;
        transform.LookAt(follow.position + Vector3.up * headY, Vector3.up);
    }
}
