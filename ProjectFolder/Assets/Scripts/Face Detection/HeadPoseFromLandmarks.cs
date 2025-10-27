using UnityEngine;
using Mediapipe.Tasks.Vision.FaceLandmarker;
using System.Collections.Generic;

/// Map Face Landmarker result -> Camera pose (yaw/pitch/roll + parallax/dolly).
/// Call poseCtrl.OnLandmarkResult(result) from runner's callback.
public class HeadPoseFromLandmarks : MonoBehaviour
{
    [Header("Target to control (Main Camera or its rig)")]
    public Transform cameraTarget;

    [Header("Rotation clamp (degrees)")]
    [Range(0f, 90f)] public float yawMax   = 30f;
    [Range(0f, 60f)] public float pitchMax = 20f;
    [Range(0f, 60f)] public float rollMax  = 15f;

    [Header("Parallax & Dolly")]
    public float parallaxX = 0.30f;     // subtle lateral shift based on yaw_norm
    public float dollyZMax = 2.5f;     // maximum forward/backward

    [Header("Smoothing")]
    public float rotLerp = 10f;
    public float posLerp = 10f;

    [Header("Clamp bounds (meters) - keep inside 5x5")]
    public float halfSize = 2.5f;

    [Header("Depth from pose translation Z")]
    public bool useDepthFromMatrix = true;
    public float depthRangeZ = 0.08f;
    public bool invertDepth = false;
    [Header("Depth smoothing")]
    [Range(0.01f, 1f)] public float depthEmaAlpha = 0.15f;
    [Range(0f, 0.2f)]  public float depthDeadzone = 0.02f;
    private float depthEma = 0f;

    // internal
    private Quaternion baseRot;
    private Vector3 basePos;

    private volatile float targetYawDeg, targetPitchDeg, targetRollDeg;
    private volatile float targetParallaxX, targetDollyZ;
    
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

    public void OnLandmarkResult(FaceLandmarkerResult result)
    {
        if (result.facialTransformationMatrixes == null || result.facialTransformationMatrixes.Count == 0)
            return;

        // Get the pose matrix of the first face (Unity Matrix4x4, axes already converted in the Result file)
        var M = result.facialTransformationMatrixes[0];

        // Orientation from matrix columns: forward = Z column, up = Y column
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

        // Horizontal parallax based on yaw
        float yawNorm = Mathf.Clamp(targetYawDeg / Mathf.Max(1e-3f, yawMax), -1f, 1f);
        targetParallaxX = yawNorm * parallaxX;

        // ---- DEPTH / DOLLY Z ----
        if (useDepthFromMatrix)
        {
            float tz = M.m23;

            if (!hasDepthBaseline)
            {
                baselineZ = tz;
                hasDepthBaseline = true;
            }

            float depthNorm = (baselineZ - tz) / Mathf.Max(1e-4f, depthRangeZ);

            if (Mathf.Abs(depthNorm) < depthDeadzone) depthNorm = 0f;

            depthNorm = Mathf.Clamp(depthNorm, -1f, 1f);

            if (invertDepth) depthNorm = -depthNorm;

            depthEma = Mathf.Lerp(depthEma, depthNorm, depthEmaAlpha);

            targetDollyZ = depthEma * dollyZMax;
        }
        else
        {
            float pitchNorm = Mathf.Clamp(targetPitchDeg / Mathf.Max(1e-3f, pitchMax), -1f, 1f);
            depthEma = Mathf.Lerp(depthEma, -pitchNorm, depthEmaAlpha);
            targetDollyZ = depthEma * dollyZMax;
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
        if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
        if (up.sqrMagnitude < 1e-6f) up = Vector3.up;
        return Quaternion.LookRotation(forward.normalized, up.normalized);
    }
}
