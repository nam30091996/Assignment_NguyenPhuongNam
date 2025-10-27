<a id="readme-top"></a>
<br />
<div align="center">
  <h1 align="center">VR-like Head Tracking (Unity + MediaPipe)</h1>
</div>


<!-- TABLE OF CONTENTS -->
<details>
  <summary>Table of Contents</summary>
  <ol>
    <li>
      <a href="#about-the-project">About The Project</a>
      <ul>
        <li><a href="#built-with">Built With</a></li>
      </ul>
    </li>
    <li><a href="#description-of-development-approach-and-tracking-technique">Description of Development Approach and Tracking Technique</a></li>
    <li><a href="#feature-summary-and-testing-environment">Feature Summary and Testing Environment</a></li>
  </ol>
</details>


<!-- ABOUT THE PROJECT -->
## About The Project

https://github.com/user-attachments/assets/1c3a2b27-7eb1-4fb9-ba34-2e167ceba03f

A VR-like experience that runs on a regular monitor. The Unity camera rotates with the user’s head and performs forward/backward movement while staying within a 5 × 5 m virtual space with a 1 × 1 m empty center. It uses a webcam (no HMD or external sensors required).</br>
Users will experience a vast open grassland, accompanied by soothing music and natural ambient sounds.


### Built With

* <a href="https://unity.com/releases/editor/whats-new/6000.0.59f2#notes"><strong>Unity 6000.0.59f2</strong></a>
* <a href="https://github.com/othneildrew/Best-README-Template"><strong>MediaPipeUnityPlugin</strong></a>
* <a href="https://assetstore.unity.com/packages/2d/textures-materials/sky/fantasy-skybox-free-18353"><strong>Fantasy Skybox FREE Asset</strong></a>
* <a href="https://assetstore.unity.com/packages/3d/characters/animals/animals-free-animated-low-poly-3d-models-260727"><strong>Animals FREE - Animated Low Poly 3D Models</strong></a>
* <a href="https://assetstore.unity.com/packages/vfx/particles/all-nature-vfx-175535"><strong>All Nature VFX</strong></a>



<!-- DEV APPROACH -->
## Description of Development Approach and Tracking Technique

This section explains how the system converts MediaPipe Face Landmarker outputs into a stable, bounded camera motion in Unity. The design optimizes for the assignment’s requirement — **basic directional cues (left/right, front/back)** — while delivering smooth, believable motion and staying inside a **5 × 5 m** play area.

---

### 1) Pose Source (MediaPipe Face Landmarker)
- Use the default `FaceLandmarkerRunner.cs` from `MediaPipeUnityPlugin` in **LIVE_STREAM** mode (webcam).
- Every time MediaPipe renders the landmarks, the `FaceLandmarkerResult` is **forwarded** to my **HeadPoseFromLandmarks** script for pose processing:

```csharp
[SerializeField] private HeadPoseFromLandmarks poseController;

private void OnFaceLandmarkDetectionOutput(FaceLandmarkerResult result, Image image, long timestamp)
{
  _faceLandmarkerResultAnnotationController.DrawLater(result);
  if (poseController != null) poseController.OnLandmarkResult(result);
}
```

---

### 2) Orientation → Camera Rotation
- `HeadPoseFromLandmarks` receives the `FaceLandmarkerResult` sent by `FaceLandmarkerRunner`.
- Read the face pose matrix from `result.facialTransformationMatrixes[0]`, extract forward/up from the Z/Y columns, build a quaternion, then get Euler angles:
  ```csharp
  var M = result.facialTransformationMatrixes[0];
  Vector3 forward = new Vector3(M.m02, M.m12, M.m22);
  Vector3 up      = new Vector3(M.m01, M.m11, M.m21);
  
  var q = SafeLookRotation(forward, up);
  var e = q.eulerAngles;
  ```
- From Euler, compute yaw/pitch/roll (normalized to [-180..180]) and clamp them to safe limits before applying to the camera:
  ```csharp
  float yaw   = Normalize180(e.y);
  float pitch = Normalize180(e.x);
  float roll  = Normalize180(e.z);
  
  targetYawDeg   = Mathf.Clamp(yaw,   -yawMax,   yawMax);
  targetPitchDeg = Mathf.Clamp(pitch, -pitchMax, pitchMax);
  targetRollDeg  = Mathf.Clamp(roll,  -rollMax,  rollMax);
  ```

<!-- FEATURES & TESTING -->
## Feature Summary and Testing Environment

### Feature Summary
- Head-driven camera: **yaw/pitch/roll** from face pose; **no HMD** required.  
- Subtle **forward/back dolly** from pose **Z translation**.  
- **Parallax** tied to yaw for depth cues.
- **Smoothing, deadzones, calibration** for stable motion.

### Testing Environment
- **OS**: Windows 11 (x64)  
- **Unity**: Unity 6000.0.59f2
- **Webcam**: 720p @ 30 FPS (good room lighting)  
- **Inference**: CPU (PC plugin)  
- **Display**: Standard monitor (no HMD)



<!-- MARKDOWN LINKS & IMAGES -->
[unity-shield]: https://img.shields.io/badge/Unity-2022.3%20LTS-6ea8fe?style=for-the-badge
[unity-url]: https://unity.com/
[mediapipe-shield]: https://img.shields.io/badge/MediaPipe-Face%20Landmarker-8bd8bd?style=for-the-badge
[mediapipe-url]: https://developers.google.com/mediapipe
[platform-shield]: https://img.shields.io/badge/Platform-Windows%2010%2F11-64748b?style=for-the-badge
[platform-url]: #
[license-shield]: https://img.shields.io/badge/License-Evaluation%20Only-ef4444?style=for-the-badge
[license-url]: #
[product-screenshot]: demo/demo.mp4
