using UnityEngine;
using Mediapipe.Tasks.Vision.FaceLandmarker;
using System.Collections.Generic;

/// Map Face Landmarker result -> Camera pose (yaw/pitch/roll + parallax/dolly).
/// GỌI: poseCtrl.OnLandmarkResult(result) từ callback của runner.
public class HeadPoseFromLandmarks : MonoBehaviour
{
    [Header("Target to control (Main Camera or its rig)")]
    public Transform cameraTarget;

    [Header("Rotation clamp (degrees)")]
    [Range(0f, 90f)] public float yawMax   = 30f;
    [Range(0f, 60f)] public float pitchMax = 20f;
    [Range(0f, 60f)] public float rollMax  = 15f;

    [Header("Parallax & Dolly")]
    public float parallaxX = 0.30f;     // dịch ngang nhẹ theo yaw_norm
    public float dollyZMax = 0.25f;     // tối đa tiến/lùi (mét)

    [Header("Smoothing")]
    public float rotLerp = 10f;
    public float posLerp = 10f;

    [Header("Clamp bounds (meters) - keep inside 5x5")]
    public float halfSize = 2.5f;

    [Header("Depth from pose translation Z")]
    public bool useDepthFromMatrix = true;   // bật dùng translation Z thay vì pitch
    public float depthRangeZ = 0.08f;        // khoảng biến thiên Z (đơn vị ma trận) quy về [-1..1]
    public bool invertDepth = false;         // nếu thấy tiến/lùi bị ngược, tick vào đây
    [Header("Depth smoothing")]
    [Range(0.01f, 1f)] public float depthEmaAlpha = 0.15f;  // EMA cho depth
    [Range(0f, 0.2f)]  public float depthDeadzone = 0.02f;  // bỏ rung nhỏ
    private float depthEma = 0f;                            // buffer EMA

    // internal
    private Quaternion baseRot;
    private Vector3 basePos;

    private volatile float targetYawDeg, targetPitchDeg, targetRollDeg;
    private volatile float targetParallaxX, targetDollyZ;

    // lưu baseline Z (chuẩn trung tính) sau calibrate
    private bool hasDepthBaseline = false;
    private float baselineZ = 0f;

    void Reset()
    {
        cameraTarget = Camera.main ? Camera.main.transform : null;
    }

    void Start()
    {
        if (cameraTarget == null) cameraTarget = (Camera.main ? Camera.main.transform : transform);
        baseRot = cameraTarget.rotation;
        basePos = cameraTarget.position;
    }

    void Update()
    {
        if (cameraTarget == null) return;

        // rotation
        var targetRot = baseRot * Quaternion.Euler(-targetPitchDeg, -targetYawDeg, -targetRollDeg);
        cameraTarget.rotation = Quaternion.Slerp(cameraTarget.rotation, targetRot, Time.deltaTime * rotLerp);

        // position with clamp inside 5x5
        var targetPos = basePos + new Vector3(targetParallaxX, 0f, targetDollyZ);
        targetPos.x = Mathf.Clamp(targetPos.x, -halfSize, halfSize);
        targetPos.z = Mathf.Clamp(targetPos.z, -halfSize, halfSize);
        cameraTarget.position = Vector3.Lerp(cameraTarget.position, targetPos, Time.deltaTime * posLerp);
    }

    /// Gọi từ runner: runner.OnResult += poseCtrl.OnLandmarkResult;
    public void OnLandmarkResult(FaceLandmarkerResult result)
    {
        if (result.facialTransformationMatrixes == null || result.facialTransformationMatrixes.Count == 0)
            return;

        // Lấy ma trận pose của mặt đầu tiên (Matrix4x4 Unity, đã chuyển trục đúng trong file Result)
        var M = result.facialTransformationMatrixes[0];

        // Orientation từ cột ma trận: forward = cột Z, up = cột Y
        Vector3 forward = new Vector3(M.m02, M.m12, M.m22);
        Vector3 up      = new Vector3(M.m01, M.m11, M.m21);

        var q = SafeLookRotation(forward, up);
        var e = q.eulerAngles;

        float yaw   = Normalize180(e.y);
        float pitch = Normalize180(e.x);
        float roll  = Normalize180(e.z);

        targetYawDeg   = Mathf.Clamp(yaw,   -yawMax,   yawMax);
        targetPitchDeg = Mathf.Clamp(pitch, -pitchMax, pitchMax);
        targetRollDeg  = Mathf.Clamp(roll,  -rollMax,  rollMax);

        // Parallax ngang theo yaw
        float yawNorm = Mathf.Clamp(targetYawDeg / Mathf.Max(1e-3f, yawMax), -1f, 1f);
        targetParallaxX = yawNorm * parallaxX;

        // ---- DEPTH / DOLLY Z ----
        if (useDepthFromMatrix)
        {
            // Translation lấy từ cột 3: (m03, m13, m23)
            float tz = M.m23;

            if (!hasDepthBaseline)
            {
                baselineZ = tz;         // set baseline một lần (hoặc gọi CalibrateDepth() để set lại)
                hasDepthBaseline = true;
            }

            // Chuẩn hóa [-1..1]
            float depthNorm = (baselineZ - tz) / Mathf.Max(1e-4f, depthRangeZ);

            // Deadzone nhỏ để tránh rung
            if (Mathf.Abs(depthNorm) < depthDeadzone) depthNorm = 0f;

            // Clamp đúng biên chuẩn hóa
            depthNorm = Mathf.Clamp(depthNorm, -1f, 1f);

            if (invertDepth) depthNorm = -depthNorm;

            // EMA smoothing cho độ sâu
            depthEma = Mathf.Lerp(depthEma, depthNorm, depthEmaAlpha);

            // Map sang dolly (biên độ nhỏ để không chạm clamp 5m x 5m)
            targetDollyZ = depthEma * dollyZMax;   // ví dụ dollyZMax = 0.25
        }
        else
        {
            float pitchNorm = Mathf.Clamp(targetPitchDeg / Mathf.Max(1e-3f, pitchMax), -1f, 1f);
            // cũng có thể áp EMA nếu muốn:
            depthEma = Mathf.Lerp(depthEma, -pitchNorm, depthEmaAlpha);
            targetDollyZ = depthEma * dollyZMax;
        }
    }

    /// Gọi hàm này khi bạn đứng tư thế trung tính để chốt baseline Z
    public void CalibrateDepth()
    {
        hasDepthBaseline = false; // sẽ set baselineZ ở khung hình kế tiếp
    }

    // Helpers
    static float Normalize180(float angle)
    {
        angle = Mathf.Repeat(angle + 180f, 360f) - 180f;
        return angle;
    }

    static Quaternion SafeLookRotation(Vector3 forward, Vector3 up)
    {
        if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
        if (up.sqrMagnitude < 1e-6f) up = Vector3.up;
        return Quaternion.LookRotation(forward.normalized, up.normalized);
    }
}
