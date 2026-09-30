// 剧情切视角（StoryRunner 的 cut 步骤，2026-09-29）：临时第三人称相机跟拍指定角色
// （如第三章李老师"到时间了……"的 cutaway），主角原地不动、输入暂停。
// 配对使用：CutawayCamera.Show(target) → …… → CutawayCamera.Restore()。
//   · Show 会先关掉主角的 FP_相机（避免双渲染 + 双 AudioListener），由 StoryRunner 停 FirstPersonController；
//   · 机位 = 角色身后 distance 米、高 height，LookAt 角色 headY 高度，LateUpdate 持续跟随；
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

    static CutawayCamera _active;
    static Camera _playerCam;       // 被暂时关掉的主角相机（FP_相机）

    public static void Show(Transform target)
    {
        Restore();                                   // 幂等：先收旧的
        if (target == null) return;
        var main = Camera.main;                      // ★ 先取再关（关掉后 Camera.main 就不是它了）
        if (main != null) { _playerCam = main; main.transform.gameObject.SetActive(false); }

        var go = new GameObject("CutawayCamera");
        _active = go.AddComponent<CutawayCamera>();
        _active.follow = target;
        _active._cam = go.AddComponent<Camera>();
        _active._cam.fieldOfView = 48f;
        _active._cam.nearClipPlane = 0.05f;
        _active._cam.farClipPlane = 600f;
        _active._cam.clearFlags = CameraClearFlags.Skybox;
        var extra = _active._cam.GetUniversalAdditionalCameraData();
        extra.renderPostProcessing = true;
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
        transform.position = follow.position - fwd * distance + Vector3.up * height;
        transform.LookAt(follow.position + Vector3.up * headY, Vector3.up);
    }
}
