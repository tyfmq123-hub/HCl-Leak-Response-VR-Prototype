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
    [SerializeField] Vector3 localPositionOffset = new Vector3(-0.38f, -0.90f, 0.10f);

    [Header("Smoothing")]
    [SerializeField] [Min(0f)] float positionSmoothTime = 0.08f;

    [Header("Options")]
    [SerializeField] bool followCameraHeight = true;
    [Tooltip("Integration mode: keep an exact local pose under the Shared XR Origin and ignore all HMD translation/rotation.")]
    [SerializeField] bool useStableOriginBodyFrame;
    [Tooltip("Legacy option. Keep disabled so looking left/right does not rotate the bag around the body.")]
    [SerializeField] bool useCameraYawOnly = false;
    [SerializeField] bool snapOnEnable = true;

    float localHeightVelocity;
    float lockedHeight;
    bool hasLockedHeight;

    public Transform XrCamera => xrCamera;
    public Vector3 LocalPositionOffset => localPositionOffset;

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

        var origin = GetComponentInParent<Unity.XR.CoreUtils.XROrigin>(true);
        if (origin != null && transform.parent != null)
        {
            if (useStableOriginBodyFrame)
            {
                // Locomotion moves the XR Origin, but real room-scale walking only moves
                // the tracked camera inside it. Follow that tracked X/Z exactly so worn
                // equipment cannot remain behind in world space as the player walks.
                var cameraLocal = transform.parent.InverseTransformPoint(xrCamera.position);
                transform.localPosition = new Vector3(
                    cameraLocal.x + localPositionOffset.x,
                    localPositionOffset.y,
                    cameraLocal.z + localPositionOffset.z);
                transform.localRotation = Quaternion.identity;
                return;
            }

            // This object is already parented under the XR Origin. Keep its body-space
            // X/Z and yaw fixed locally so locomotion and snap-turn move it exactly once.
            // Smoothing world position here causes the parent motion and follower motion
            // to fight, which appears as the bag drifting farther away after movement.
            var targetLocalPosition = ComputeTargetLocalPosition(origin, transform.parent);
            if (positionSmoothTime > 0.0001f)
            {
                targetLocalPosition.y = Mathf.SmoothDamp(
                    transform.localPosition.y,
                    targetLocalPosition.y,
                    ref localHeightVelocity,
                    positionSmoothTime);
            }

            transform.localPosition = targetLocalPosition;
            transform.localRotation = ComputeTargetLocalRotation(transform.parent);
            return;
        }

        transform.SetPositionAndRotation(ComputeTargetPosition(), ComputeTargetRotation());
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

        localHeightVelocity = 0f;
        var origin = GetComponentInParent<Unity.XR.CoreUtils.XROrigin>(true);
        if (origin != null && transform.parent != null)
        {
            if (useStableOriginBodyFrame)
            {
                var cameraLocal = transform.parent.InverseTransformPoint(xrCamera.position);
                transform.localPosition = new Vector3(
                    cameraLocal.x + localPositionOffset.x,
                    localPositionOffset.y,
                    cameraLocal.z + localPositionOffset.z);
                transform.localRotation = Quaternion.identity;
                return;
            }

            transform.localPosition = ComputeTargetLocalPosition(origin, transform.parent);
            transform.localRotation = ComputeTargetLocalRotation(transform.parent);
            return;
        }

        transform.SetPositionAndRotation(ComputeTargetPosition(), ComputeTargetRotation());
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

    Vector3 ComputeTargetLocalPosition(Unity.XR.CoreUtils.XROrigin origin, Transform parent)
    {
        var trackedHeight = followCameraHeight
            ? Mathf.Max(0f, origin.CameraInOriginSpaceHeight)
            : ResolveLockedHeightInOriginSpace(origin);
        var worldHeadPoint = origin.transform.TransformPoint(Vector3.up * trackedHeight);
        var worldTarget = worldHeadPoint + ResolveBodyYawRotation() * localPositionOffset;
        return parent.InverseTransformPoint(worldTarget);
    }

    Quaternion ComputeTargetLocalRotation(Transform parent)
    {
        return Quaternion.Inverse(parent.rotation) * ResolveBodyYawRotation();
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
