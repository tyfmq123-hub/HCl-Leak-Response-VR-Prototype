using System;
using UnityEngine;
using Unity.XR.CoreUtils;

/// <summary>
/// Stage-local body frame for PPE placement. It uses HMD height and planar
/// room-scale movement, but deliberately ignores HMD look rotation so guides
/// remain attached to the inferred player body instead of following the view.
/// </summary>
[DisallowMultipleComponent]
public sealed class PPEPlayerBodyReference : MonoBehaviour
{
    [Serializable]
    struct AnchorPose
    {
        public PPEEquipSlot slot;
        public Transform anchor;
        [Range(0f, 1.2f)] public float heightRatio;
        public float verticalOffset;
        public Vector2 horizontalOffset;
        public Vector3 localEulerAngles;

        public AnchorPose(
            PPEEquipSlot equipmentSlot,
            Transform target,
            float normalizedHeight,
            float yOffset,
            Vector2 xzOffset,
            Vector3 eulerAngles)
        {
            slot = equipmentSlot;
            anchor = target;
            heightRatio = normalizedHeight;
            verticalOffset = yOffset;
            horizontalOffset = xzOffset;
            localEulerAngles = eulerAngles;
        }
    }

    [Header("Tracking Source")]
    [SerializeField] XROrigin xrOrigin;
    [SerializeField] Transform xrCamera;
    [SerializeField] PPEEquipmentAnchorSet anchorSet;

    [Header("Body Inference")]
    [SerializeField]
    [Tooltip("Follow physical room-scale X/Z movement, but never HMD look rotation.")]
    bool followRoomScalePlanarMovement = true;

    [SerializeField, Min(0f)] float planarPositionSmoothTime = 0.10f;
    [SerializeField, Min(0f)] float heightSmoothTime = 0.08f;
    [SerializeField, Min(0.5f)] float minimumTrackedHeight = 0.75f;
    [SerializeField, Min(0.5f)] float maximumTrackedHeight = 2.20f;

    [Header("Slot Poses (height-relative)")]
    [SerializeField] AnchorPose[] anchorPoses = Array.Empty<AnchorPose>();
    [SerializeField, HideInInspector] int bodyReferenceSetupVersion;

    Vector2 planarVelocity;
    float heightVelocity;
    float currentHeight;
    bool initialized;

    public XROrigin Origin => xrOrigin;
    public Transform XrCamera => xrCamera;
    public PPEEquipmentAnchorSet AnchorSet => anchorSet;
    public float CurrentTrackedHeight => currentHeight;
    public bool FollowsRoomScalePlanarMovement => followRoomScalePlanarMovement;
    public int BodyReferenceSetupVersion => bodyReferenceSetupVersion;

    void OnEnable()
    {
        ResolveTrackingSource();
        SnapToTrackingSource();
    }

    void LateUpdate()
    {
        ResolveTrackingSource();
        if (xrOrigin == null || xrCamera == null)
            return;

        var cameraInOrigin = xrOrigin.transform.InverseTransformPoint(xrCamera.position);
        var targetPlanar = followRoomScalePlanarMovement
            ? new Vector2(cameraInOrigin.x, cameraInOrigin.z)
            : Vector2.zero;
        var targetHeight = ClampTrackedHeight(cameraInOrigin.y);

        if (!initialized)
        {
            SetBodyFrame(targetPlanar, targetHeight);
            initialized = true;
            return;
        }

        var currentPlanar = GetCurrentPlanarPosition();
        if (planarPositionSmoothTime > 0.0001f)
        {
            currentPlanar = Vector2.SmoothDamp(
                currentPlanar,
                targetPlanar,
                ref planarVelocity,
                planarPositionSmoothTime);
        }
        else
        {
            currentPlanar = targetPlanar;
        }

        currentHeight = heightSmoothTime > 0.0001f
            ? Mathf.SmoothDamp(currentHeight, targetHeight, ref heightVelocity, heightSmoothTime)
            : targetHeight;
        SetBodyFrame(currentPlanar, currentHeight);
    }

    public void SnapToTrackingSource()
    {
        ResolveTrackingSource();
        if (xrOrigin == null || xrCamera == null)
            return;

        planarVelocity = Vector2.zero;
        heightVelocity = 0f;
        var cameraInOrigin = xrOrigin.transform.InverseTransformPoint(xrCamera.position);
        var planar = followRoomScalePlanarMovement
            ? new Vector2(cameraInOrigin.x, cameraInOrigin.z)
            : Vector2.zero;
        SetBodyFrame(planar, ClampTrackedHeight(cameraInOrigin.y));
        initialized = true;
    }

    void SetBodyFrame(Vector2 planar, float trackedHeight)
    {
        currentHeight = trackedHeight;
        if (transform.parent == xrOrigin.transform)
        {
            transform.localPosition = new Vector3(planar.x, 0f, planar.y);
            transform.localRotation = Quaternion.identity;
        }
        else
        {
            var worldPosition = xrOrigin.transform.TransformPoint(new Vector3(planar.x, 0f, planar.y));
            transform.SetPositionAndRotation(worldPosition, xrOrigin.transform.rotation);
        }

        ApplyAnchorPoses(trackedHeight);
    }

    void ApplyAnchorPoses(float trackedHeight)
    {
        if (anchorPoses == null)
            return;

        for (var i = 0; i < anchorPoses.Length; i++)
        {
            var pose = anchorPoses[i];
            if (pose.anchor == null)
                continue;

            pose.anchor.localPosition = new Vector3(
                pose.horizontalOffset.x,
                trackedHeight * pose.heightRatio + pose.verticalOffset,
                pose.horizontalOffset.y);
            pose.anchor.localRotation = Quaternion.Euler(pose.localEulerAngles);
        }
    }

    Vector2 GetCurrentPlanarPosition()
    {
        if (transform.parent == xrOrigin.transform)
            return new Vector2(transform.localPosition.x, transform.localPosition.z);

        var local = xrOrigin.transform.InverseTransformPoint(transform.position);
        return new Vector2(local.x, local.z);
    }

    float ClampTrackedHeight(float height)
    {
        var max = Mathf.Max(minimumTrackedHeight, maximumTrackedHeight);
        return Mathf.Clamp(height, minimumTrackedHeight, max);
    }

    void ResolveTrackingSource()
    {
        if (xrOrigin == null)
            xrOrigin = GetComponentInParent<XROrigin>(true);
        if (xrOrigin == null)
            xrOrigin = FindAnyObjectByType<XROrigin>();

        if (xrCamera == null && xrOrigin != null && xrOrigin.Camera != null)
            xrCamera = xrOrigin.Camera.transform;
        if (xrCamera == null && Camera.main != null)
            xrCamera = Camera.main.transform;
        if (anchorSet == null)
            anchorSet = GetComponentInChildren<PPEEquipmentAnchorSet>(true);
    }

#if UNITY_EDITOR
    public void EditorWire(XROrigin origin, Transform camera, PPEEquipmentAnchorSet equipmentAnchors)
    {
        xrOrigin = origin;
        xrCamera = camera;
        anchorSet = equipmentAnchors;
        followRoomScalePlanarMovement = true;
        bodyReferenceSetupVersion = 1;

        anchorPoses = equipmentAnchors == null
            ? Array.Empty<AnchorPose>()
            : new[]
            {
                // Head/face remain at HMD height, but do not inherit HMD pitch/yaw.
                new AnchorPose(PPEEquipSlot.Head, equipmentAnchors.Head, 1f, 0.08f, new Vector2(0f, 0.04f), Vector3.zero),
                new AnchorPose(PPEEquipSlot.Face, equipmentAnchors.Face, 1f, -0.10f, new Vector2(0f, 0.13f), Vector3.zero),
                // Hands/body use proportional standing or crouching height.
                new AnchorPose(PPEEquipSlot.LeftHand, equipmentAnchors.LeftHand, 0.60f, 0f, new Vector2(-0.32f, 0.10f), Vector3.zero),
                new AnchorPose(PPEEquipSlot.RightHand, equipmentAnchors.RightHand, 0.60f, 0f, new Vector2(0.32f, 0.10f), Vector3.zero),
                new AnchorPose(PPEEquipSlot.Body, equipmentAnchors.Body, 0.55f, 0f, new Vector2(0f, 0.02f), Vector3.zero),
                // Foot anchors stay close to the XR Origin floor regardless of HMD height.
                new AnchorPose(PPEEquipSlot.LeftFoot, equipmentAnchors.LeftFoot, 0f, 0.06f, new Vector2(-0.14f, 0.08f), Vector3.zero),
                new AnchorPose(PPEEquipSlot.RightFoot, equipmentAnchors.RightFoot, 0f, 0.06f, new Vector2(0.14f, 0.08f), Vector3.zero),
                new AnchorPose(PPEEquipSlot.Radio, equipmentAnchors.Radio, 0.62f, 0f, new Vector2(-0.18f, 0.05f), Vector3.zero),
                new AnchorPose(PPEEquipSlot.GasDetector, equipmentAnchors.GasDetector, 0.58f, 0f, new Vector2(0.18f, 0.07f), Vector3.zero),
            };

        // Author a representative standing pose in Edit Mode. Runtime replaces
        // it with the tracked HMD height without using HMD look rotation.
        ApplyAnchorPoses(1.70f);
    }
#endif
}
