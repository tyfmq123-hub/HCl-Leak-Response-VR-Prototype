using UnityEngine;

/// <summary>
/// Body-space anchor that follows the XR camera position and horizontal yaw only.
/// Pitch/roll from looking up/down or tilting the head are ignored.
/// </summary>
[DisallowMultipleComponent]
public class PlayerBodyAnchorFollower : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] Transform xrCamera;

    [Header("Offset (relative to horizontal facing)")]
    // Left waist, slightly lower and forward — easy to see when looking down-left.
    // Left hip / low waist relative to HMD (meters).
    [SerializeField] Vector3 localPositionOffset = new Vector3(-0.36f, -1.08f, 0.16f);

    [Header("Smoothing")]
    [SerializeField] [Min(0f)] float positionSmoothTime = 0.08f;
    [SerializeField] [Min(0.1f)] float rotationSmoothSpeed = 12f;

    [Header("Options")]
    [SerializeField] bool followCameraHeight = true;
    [SerializeField] bool useCameraYawOnly = true;
    [SerializeField] bool snapOnEnable = true;

    Vector3 positionVelocity;
    float lockedHeight;
    bool hasLockedHeight;

    public Transform XrCamera => xrCamera;

    void OnEnable()
    {
        ResolveCamera();
        if (snapOnEnable)
            SnapToTarget();
    }

    void LateUpdate()
    {
        ResolveCamera();
        if (xrCamera == null)
            return;

        var targetPos = ComputeTargetPosition();
        var targetRot = ComputeTargetRotation();

        if (positionSmoothTime <= 0.0001f)
            transform.position = targetPos;
        else
            transform.position = Vector3.SmoothDamp(
                transform.position,
                targetPos,
                ref positionVelocity,
                positionSmoothTime);

        if (rotationSmoothSpeed <= 0.0001f)
            transform.rotation = targetRot;
        else
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRot,
                1f - Mathf.Exp(-rotationSmoothSpeed * Time.deltaTime));
    }

    public void EditorWire(Transform camera)
    {
        xrCamera = camera;
    }

    public void SnapToTarget()
    {
        ResolveCamera();
        if (xrCamera == null)
            return;

        positionVelocity = Vector3.zero;
        transform.position = ComputeTargetPosition();
        transform.rotation = ComputeTargetRotation();
    }

    void ResolveCamera()
    {
        if (xrCamera != null)
            return;

        var origin = FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
        if (origin != null && origin.Camera != null)
        {
            xrCamera = origin.Camera.transform;
            return;
        }

        if (Camera.main != null)
            xrCamera = Camera.main.transform;
    }

    Vector3 ComputeTargetPosition()
    {
        var camPos = xrCamera.position;
        var basePos = followCameraHeight
            ? camPos
            : new Vector3(camPos.x, ResolveLockedHeight(), camPos.z);

        var yawRot = useCameraYawOnly ? HorizontalYawRotation(xrCamera) : xrCamera.rotation;
        return basePos + yawRot * localPositionOffset;
    }

    float ResolveLockedHeight()
    {
        if (!hasLockedHeight)
        {
            lockedHeight = xrCamera.position.y;
            hasLockedHeight = true;
        }

        return lockedHeight;
    }

    Quaternion ComputeTargetRotation()
    {
        if (!useCameraYawOnly)
            return xrCamera.rotation;

        return HorizontalYawRotation(xrCamera);
    }

    static Quaternion HorizontalYawRotation(Transform cam)
    {
        var forward = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f)
        {
            // Looking nearly straight up/down — fall back to projected up or right.
            forward = Vector3.ProjectOnPlane(cam.up, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.ProjectOnPlane(cam.right, Vector3.up);
        }

        if (forward.sqrMagnitude < 0.0001f)
            return Quaternion.identity;

        return Quaternion.LookRotation(forward.normalized, Vector3.up);
    }
}
