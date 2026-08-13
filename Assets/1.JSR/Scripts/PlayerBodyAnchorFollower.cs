using UnityEngine;

/// <summary>
/// Body-space anchor that follows the XR Origin without orbiting around the
/// player when the HMD looks or leans left/right. Locomotion and snap-turn come
/// from the XR Origin. Only tracked head height is used for the waist height.
/// </summary>
[DisallowMultipleComponent]
public class PlayerBodyAnchorFollower : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] Transform xrCamera;

    [Header("Offset (relative to horizontal facing)")]
    // Left hip / low waist relative to HMD (meters). The wider X offset keeps
    // the bag body beside the torso after its long axis is rotated front-to-back.
    [SerializeField] Vector3 localPositionOffset = new Vector3(-0.58f, -1.08f, 0.16f);

    [Header("Smoothing")]
    [SerializeField] [Min(0f)] float positionSmoothTime = 0.08f;
    [SerializeField] [Min(0.1f)] float rotationSmoothSpeed = 12f;

    [Header("Options")]
    [SerializeField] bool followCameraHeight = true;
    [Tooltip("Legacy option. Keep disabled so looking left/right does not rotate the bag around the body.")]
    [SerializeField] bool useCameraYawOnly = false;
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
        var origin = GetComponentInParent<Unity.XR.CoreUtils.XROrigin>(true);
        if (origin == null)
        {
            var fallbackRotation = ResolveBodyYawRotation();
            var fallbackHeight = followCameraHeight ? xrCamera.position.y : ResolveLockedHeight();
            var fallbackBase = new Vector3(transform.parent != null ? transform.parent.position.x : 0f,
                fallbackHeight,
                transform.parent != null ? transform.parent.position.z : 0f);
            return fallbackBase + fallbackRotation * localPositionOffset;
        }

        // X/Z must come from the stable body root, never from the HMD. A tracked
        // head rotates and translates around the neck, so Camera.position makes
        // a hip-mounted bag follow the view even when camera yaw is ignored.
        var trackedHeight = followCameraHeight
            ? Mathf.Max(0f, origin.CameraInOriginSpaceHeight)
            : ResolveLockedHeightInOriginSpace(origin);
        var bodyRotation = HorizontalYawRotation(origin.transform);
        var bodyHeadPoint = origin.transform.position + origin.transform.up * trackedHeight;
        return bodyHeadPoint + bodyRotation * localPositionOffset;
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

    float ResolveLockedHeightInOriginSpace(Unity.XR.CoreUtils.XROrigin origin)
    {
        if (!hasLockedHeight)
        {
            lockedHeight = Mathf.Max(0f, origin.CameraInOriginSpaceHeight);
            hasLockedHeight = true;
        }

        return lockedHeight;
    }

    Quaternion ComputeTargetRotation()
    {
        return ResolveBodyYawRotation();
    }

    Quaternion ResolveBodyYawRotation()
    {
        if (useCameraYawOnly)
            return HorizontalYawRotation(xrCamera);

        var origin = GetComponentInParent<Unity.XR.CoreUtils.XROrigin>(true);
        return origin != null
            ? HorizontalYawRotation(origin.transform)
            : HorizontalYawRotation(transform.parent != null ? transform.parent : transform);
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
