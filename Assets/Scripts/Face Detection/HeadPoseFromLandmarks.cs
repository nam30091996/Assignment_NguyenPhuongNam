using UnityEngine;
using Mediapipe.Tasks.Vision.FaceLandmarker;
using System.Collections.Generic;

/// <summary>
/// Map Face Landmarker result -> Camera pose (yaw/pitch/roll + parallax/dolly),
/// giữ camera trong biên 5m x 5m. Gọi OnLandmarkResult(result) từ runner callback.
/// </summary>
public class HeadPoseFromLandmarks : MonoBehaviour
{
    [Header("Target to control (Main Camera or its rig)")]
    public Transform cameraTarget;

    [Header("Rotation clamp (degrees)")]
    [Range(0f, 90f)] public float yawMax   = 30f; // quay trái/phải
    [Range(0f, 60f)] public float pitchMax = 20f; // ngẩng/cúi
    [Range(0f, 60f)] public float rollMax  = 15f; // nghiêng đầu

    [Header("Parallax & Dolly")]
    public float parallaxX = 0.30f;  // dịch ngang nhẹ theo yaw_norm
    public float dollyZMax = 0.25f;  // tiến/lùi nhẹ theo pitch_norm (âm/ dương tùy quy ước)

    [Header("Smoothing")]
    public float rotLerp = 10f;
    public float posLerp = 10f;

    [Header("Clamp bounds (meters) - keep inside 5x5")]
    public float halfSize = 2.5f;

    // internal
    private Quaternion baseRot;
    private Vector3 basePos;

    // current targets
    private volatile float targetYawDeg, targetPitchDeg, targetRollDeg;
    private volatile float targetParallaxX, targetDollyZ;

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
        var targetRot = baseRot * Quaternion.Euler(targetPitchDeg, targetYawDeg, targetRollDeg);
        cameraTarget.rotation = Quaternion.Slerp(cameraTarget.rotation, targetRot, Time.deltaTime * rotLerp);

        // position with clamp inside 5x5
        var targetPos = basePos + new Vector3(targetParallaxX, 0f, targetDollyZ);
        targetPos.x = Mathf.Clamp(targetPos.x, -halfSize, halfSize);
        targetPos.z = Mathf.Clamp(targetPos.z, -halfSize, halfSize);
        cameraTarget.position = Vector3.Lerp(cameraTarget.position, targetPos, Time.deltaTime * posLerp);
    }

    /// <summary>
    /// Gọi hàm này từ callback của runner: e.g. runner.OnResult += OnLandmarkResult;
    /// </summary>
    public void OnLandmarkResult(FaceLandmarkerResult result)
    {
        if (result.facialTransformationMatrixes != null &&
            result.facialTransformationMatrixes.Count > 0)
        {
            // Dùng trực tiếp Matrix4x4 Unity đã quy đổi sẵn từ MediaPipe
            // (trong extension của plugin z-axis đã được xử lý).
            var M = result.facialTransformationMatrixes[0];

            // Suy orientation từ cột của ma trận: forward = cột Z, up = cột Y
            Vector3 forward = new Vector3(M.m02, M.m12, M.m22);
            Vector3 up      = new Vector3(M.m01, M.m11, M.m21);

            // Nếu thấy trục bị ngược (quay trái thành quay phải), có thể đảo dấu tại đây:
            // forward.x *= -1f; // tùy webcam/orientation thực tế

            var q = SafeLookRotation(forward, up);
            var e = q.eulerAngles;

            float yaw   = Normalize180(e.y);
            float pitch = Normalize180(e.x);
            float roll  = Normalize180(e.z);

            targetYawDeg   = Mathf.Clamp(yaw,   -yawMax,   yawMax);
            targetPitchDeg = Mathf.Clamp(pitch, -pitchMax, pitchMax);
            targetRollDeg  = Mathf.Clamp(roll,  -rollMax,  rollMax);

            // Parallax/Dolly: quy ước đơn giản theo tỉ lệ clamped
            float yawNorm   = Mathf.Clamp(targetYawDeg / Mathf.Max(1e-3f, yawMax), -1f, 1f);
            float pitchNorm = Mathf.Clamp(targetPitchDeg / Mathf.Max(1e-3f, pitchMax), -1f, 1f);

            targetParallaxX = yawNorm * parallaxX;
            targetDollyZ    = -pitchNorm * dollyZMax; // cúi (pitch +) -> tiến (âm); đổi dấu nếu muốn
            return;
        }

        // Fallback nhẹ nếu không có matrix: ước lượng từ landmarks (nếu có)
        if (result.faceLandmarks != null && result.faceLandmarks.Count > 0)
        {
            var lms = result.faceLandmarks[0]; // NormalizedLandmarks (0..1)
            // MediaPipe Face Mesh indices (tham khảo): mắt trái ~33, mắt phải ~263, mũi ~1
            // Nếu model của bạn dùng tập mốc khác, thay index phù hợp.
            int iLeftEye = 33, iRightEye = 263, iNose = 1;
            if (lms.landmarks.Count > Mathf.Max(iLeftEye, iRightEye, iNose))
            {
                var L = lms.landmarks[iLeftEye];
                var R = lms.landmarks[iRightEye];
                var N = lms.landmarks[iNose];

                // roll ~ góc nghiêng đường mắt
                float rollRad = Mathf.Atan2(R.y - L.y, R.x - L.x);
                float rollDeg = -rollRad * Mathf.Rad2Deg;

                // yaw ~ N.x lệch so với trung điểm LR
                float mx = 0.5f * (L.x + R.x);
                float yawNorm = Mathf.Clamp((N.x - mx) / 0.2f, -1f, 1f);
                float yawDeg = yawNorm * yawMax;

                // pitch ~ N.y so với đường mắt
                float my = 0.5f * (L.y + R.y);
                float pitchNorm = Mathf.Clamp((my - N.y) / 0.15f, -1f, 1f);
                float pitchDeg = pitchNorm * pitchMax;

                targetYawDeg   = Mathf.Clamp(yawDeg,   -yawMax,   yawMax);
                targetPitchDeg = Mathf.Clamp(pitchDeg, -pitchMax, pitchMax);
                targetRollDeg  = Mathf.Clamp(rollDeg,  -rollMax,  rollMax);

                targetParallaxX = Mathf.Clamp(targetYawDeg / Mathf.Max(1e-3f, yawMax), -1f, 1f) * parallaxX;
                targetDollyZ    = -Mathf.Clamp(targetPitchDeg / Mathf.Max(1e-3f, pitchMax), -1f, 1f) * dollyZMax;
            }
        }
    }

    // Helpers
    static float Normalize180(float angle)
    {
        angle = Mathf.Repeat(angle + 180f, 360f) - 180f;
        return angle;
    }

    static Quaternion SafeLookRotation(Vector3 forward, Vector3 up)
    {
        // Tránh LookRotation với forward quá nhỏ
        if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
        if (up.sqrMagnitude < 1e-6f) up = Vector3.up;
        return Quaternion.LookRotation(forward.normalized, up.normalized);
    }
}
