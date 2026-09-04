using System;
using UnityEngine;
using Unity.XR.CoreUtils;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.XR.Hands;

public enum PPEFaceEquipProfile
{
    Respirator,
    FaceProtection,
}

/// <summary>
/// Stage-local dynamic body frame for PPE placement.
/// Follows current HMD X/Z and a stable horizontal heading. Face/head targets
/// use HMD pose with inspector offsets. Hands use XR Hands. Lower-body proxies
/// use body yaw, never HMD pitch/roll.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-50)]
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
    [Tooltip("Follow physical room-scale X/Z movement, but never HMD pitch/roll.")]
    bool followRoomScalePlanarMovement = true;

    [SerializeField, Min(0f)]
    [FormerlySerializedAs("planarPositionSmoothTime")]
    [Tooltip("Light smoothing for body-proxy X/Z. Keep low so walking stays snappy. Do not apply to tracked hands.")]
    float positionFollowSmooth = 0.06f;

    [SerializeField, Min(0f)]
    [Tooltip("Light smoothing for body yaw (horizontal heading only).")]
    float rotationFollowSmooth = 0.08f;

    [SerializeField, Min(0f)] float heightSmoothTime = 0.08f;
    [SerializeField, Min(0.5f)] float minimumTrackedHeight = 0.75f;
    [SerializeField, Min(0.5f)] float maximumTrackedHeight = 2.20f;

    [Header("Head / Face (HMD local offsets)")]
    [SerializeField]
    [FormerlySerializedAs("headPositionOffset")]
    [Tooltip("Helmet Head Position Offset, in active HMD-local meters. The target is invisible.")]
    Vector3 helmetHeadPositionOffset = new Vector3(0f, 0.08f, 0.04f);
    [SerializeField] Vector3 headRotationOffsetEuler;
    [SerializeField] Vector3 respiratorPositionOffset = new Vector3(0f, -0.10f, 0.13f);
    [SerializeField] Vector3 respiratorRotationOffsetEuler;
    [SerializeField] Vector3 faceProtectionPositionOffset = new Vector3(0f, -0.02f, 0.20f);
    [SerializeField] Vector3 faceProtectionRotationOffsetEuler;

    [Header("Torso")]
    [SerializeField, Min(0f)]
    [Tooltip("Meters below current HMD for the suit / chest target.")]
    float torsoVerticalOffset = 0.40f;
    [SerializeField]
    [Tooltip("Meters forward along body yaw, not HMD pitch.")]
    float torsoForwardOffset = 0.05f;

    [Header("Lower Body (boots)")]
    [SerializeField, Min(0f)] float footSpacing = 0.14f;
    [SerializeField, Min(0f)]
    [Tooltip("Reachable shin / below-knee height. Do not place the valid target on the floor.")]
    float lowerLegHeight = 0.38f;
    [SerializeField] float lowerBodyForwardOffset = 0.08f;

    [Header("Debug")]
    [SerializeField]
    [Tooltip("Scene-view gizmos for live Head/Face, Torso, LowerLeg, and Hand targets.")]
    bool drawDynamicTargetGizmos;

    [Header("Slot Poses (height-relative carried equipment)")]
    [SerializeField] AnchorPose[] anchorPoses = Array.Empty<AnchorPose>();
    [SerializeField, HideInInspector] int bodyReferenceSetupVersion;

    const string BodyTargetRootName = "PPE_BodyTargets";
    static Transform bodyTargetRoot;

    Vector2 planarVelocity;
    float heightVelocity;
    float yawVelocity;
    float currentHeight;
    float currentYawDegrees;
    bool initialized;
    bool hasYaw;
    PPEFaceEquipProfile activeFaceProfile = PPEFaceEquipProfile.Respirator;
    bool leftHandTracked;
    bool rightHandTracked;

    public XROrigin Origin => xrOrigin;
    public Transform XrCamera => xrCamera;
    public PPEEquipmentAnchorSet AnchorSet => anchorSet;
    public float CurrentTrackedHeight => currentHeight;
    public bool FollowsRoomScalePlanarMovement => followRoomScalePlanarMovement;
    public int BodyReferenceSetupVersion => bodyReferenceSetupVersion;
    public float PositionFollowSmooth => positionFollowSmooth;
    public float RotationFollowSmooth => rotationFollowSmooth;
    public float FootSpacing => footSpacing;
    public float LowerLegHeight => lowerLegHeight;
    public float LowerBodyForwardOffset => lowerBodyForwardOffset;
    public float TorsoVerticalOffset => torsoVerticalOffset;
    public float TorsoForwardOffset => torsoForwardOffset;
    public Vector3 RespiratorPositionOffset => respiratorPositionOffset;
    public Vector3 FaceProtectionPositionOffset => faceProtectionPositionOffset;
    public Vector3 HelmetHeadPositionOffset => helmetHeadPositionOffset;
    public PPEFaceEquipProfile ActiveFaceProfile => activeFaceProfile;
    public bool DrawDynamicTargetGizmos => drawDynamicTargetGizmos;

    public bool HasTracking =>
        xrOrigin != null &&
        xrCamera != null &&
        xrCamera.gameObject.activeInHierarchy;

    public Quaternion BodyYawRotation => Quaternion.Euler(0f, currentYawDegrees, 0f);

    /// <summary>
    /// PPE-scene host for Face/Torso/LowerLeg targets. Never parented under XR Origin
    /// so App can disable the PPE-scene origin without hiding placement ghosts.
    /// Targets stay empty Transforms. No body meshes are created.
    /// </summary>
    public static Transform GetOrCreateBodyTargetRoot()
    {
        if (bodyTargetRoot != null)
            return bodyTargetRoot;

        var existing = GameObject.Find(BodyTargetRootName);
        if (existing != null)
        {
            bodyTargetRoot = existing.transform;
            return bodyTargetRoot;
        }

        var go = new GameObject(BodyTargetRootName);
        var guideManager = FindAnyObjectByType<PPEPlacementGuideManager>(FindObjectsInactive.Include);
        var sceneObject = guideManager != null
            ? guideManager.gameObject
            : FindAnyObjectByType<PPEEquipmentManager>(FindObjectsInactive.Include)?.gameObject;
        if (sceneObject != null && go.scene != sceneObject.scene)
            SceneManager.MoveGameObjectToScene(go, sceneObject.scene);

        bodyTargetRoot = go.transform;
        return bodyTargetRoot;
    }

    /// <summary>
    /// Keeps this body frame alive after App disables the PPE-scene XR Origin.
    /// Reparents to <see cref="GetOrCreateBodyTargetRoot"/> when the current parent
    /// origin is inactive. Does not parent under Common XR.
    /// </summary>
    public void EnsureActiveInPpeScene()
    {
        var parentOrigin = GetComponentInParent<XROrigin>(true);
        var originInactive = parentOrigin != null && !parentOrigin.gameObject.activeInHierarchy;
        var becameActive = false;
        if (originInactive || !gameObject.activeInHierarchy)
        {
            var host = GetOrCreateBodyTargetRoot();
            if (transform.parent != host)
            {
                transform.SetParent(host, true);
                becameActive = true;
                Debug.Log(
                    "[PPE Body Target] Reparented PPE_PlayerBodyReference to " + host.name +
                    " because the PPE XR Origin is inactive. Face/Torso/LowerLeg stay empty Transforms.",
                    this);
            }

            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
                becameActive = true;
            }
        }

        ResolveTrackingSource();
        if (becameActive && HasTracking)
            SnapToTrackingSource();
    }

    void OnEnable()
    {
        ResolveTrackingSource();
        SnapToTrackingSource();
    }

    void LateUpdate()
    {
        ResolveTrackingSource();
        if (!HasTracking)
            return;

        var cameraInOrigin = xrOrigin.transform.InverseTransformPoint(xrCamera.position);
        var targetPlanar = followRoomScalePlanarMovement
            ? new Vector2(cameraInOrigin.x, cameraInOrigin.z)
            : Vector2.zero;
        var targetHeight = ClampTrackedHeight(cameraInOrigin.y);
        var targetYaw = HorizontalYawDegrees(xrCamera);

        if (!initialized)
        {
            SetBodyFrame(targetPlanar, targetHeight, targetYaw);
            initialized = true;
            return;
        }

        var currentPlanar = GetCurrentPlanarPosition();
        if (positionFollowSmooth > 0.0001f)
        {
            currentPlanar = Vector2.SmoothDamp(
                currentPlanar,
                targetPlanar,
                ref planarVelocity,
                positionFollowSmooth);
        }
        else
        {
            currentPlanar = targetPlanar;
        }

        currentHeight = heightSmoothTime > 0.0001f
            ? Mathf.SmoothDamp(currentHeight, targetHeight, ref heightVelocity, heightSmoothTime)
            : targetHeight;

        if (!hasYaw)
        {
            currentYawDegrees = targetYaw;
            hasYaw = true;
        }
        else if (rotationFollowSmooth > 0.0001f)
        {
            currentYawDegrees = Mathf.SmoothDampAngle(
                currentYawDegrees,
                targetYaw,
                ref yawVelocity,
                rotationFollowSmooth);
        }
        else
        {
            currentYawDegrees = targetYaw;
        }

        SetBodyFrame(currentPlanar, currentHeight, currentYawDegrees);
    }

    public void SnapToTrackingSource()
    {
        ResolveTrackingSource();
        if (!HasTracking)
            return;

        planarVelocity = Vector2.zero;
        heightVelocity = 0f;
        yawVelocity = 0f;
        var cameraInOrigin = xrOrigin.transform.InverseTransformPoint(xrCamera.position);
        var planar = followRoomScalePlanarMovement
            ? new Vector2(cameraInOrigin.x, cameraInOrigin.z)
            : Vector2.zero;
        SetBodyFrame(planar, ClampTrackedHeight(cameraInOrigin.y), HorizontalYawDegrees(xrCamera));
        initialized = true;
    }

    public void SetActiveFaceProfile(PPEFaceEquipProfile profile)
    {
        activeFaceProfile = profile;
    }

    public void SetActiveFaceProfileFrom(PPEEquipable equipable)
    {
        activeFaceProfile = IsFaceProtection(equipable)
            ? PPEFaceEquipProfile.FaceProtection
            : PPEFaceEquipProfile.Respirator;
    }

    public void ClearActiveFaceProfile()
    {
        activeFaceProfile = PPEFaceEquipProfile.Respirator;
    }

    public bool IsLookingDown(float downAngleFromHorizonDegrees)
    {
        if (!HasTracking)
            return false;

        var clamped = Mathf.Clamp(downAngleFromHorizonDegrees, 0f, 90f);
        var threshold = -Mathf.Sin(clamped * Mathf.Deg2Rad);
        return xrCamera.forward.y <= threshold;
    }

    public bool TryGetLowerLegWorldPose(PPEGrabHand side, out Vector3 position, out Quaternion rotation)
    {
        position = transform.position;
        rotation = BodyYawRotation;
        if (!HasTracking && !initialized)
            return false;

        ComputeLowerLegWorldPose(side, out position, out rotation);
        return true;
    }

    public bool TryGetTorsoWorldPose(out Vector3 position, out Quaternion rotation)
    {
        ComputeTorsoWorldPose(out position, out rotation);
        return HasTracking || initialized;
    }

    static bool IsFaceProtection(PPEEquipable equipable)
    {
        if (equipable == null)
            return false;
        if (equipable.Type == PPEType.FaceShield)
            return true;
        return string.Equals(
            equipable.ItemId,
            PPEEquipmentManager.IdFaceShield,
            StringComparison.Ordinal);
    }

    void SetBodyFrame(Vector2 planar, float trackedHeight, float yawDegrees)
    {
        currentHeight = trackedHeight;
        currentYawDegrees = yawDegrees;
        hasYaw = true;

        var worldYaw = Quaternion.Euler(0f, yawDegrees, 0f);
        if (xrOrigin != null && transform.parent == xrOrigin.transform)
        {
            transform.localPosition = new Vector3(planar.x, 0f, planar.y);
            transform.localRotation = Quaternion.Inverse(xrOrigin.transform.rotation) * worldYaw;
        }
        else if (xrOrigin != null)
        {
            var worldPosition = xrOrigin.transform.TransformPoint(new Vector3(planar.x, 0f, planar.y));
            transform.SetPositionAndRotation(worldPosition, worldYaw);
        }

        ApplyDynamicTargets(trackedHeight);
    }

    void ApplyDynamicTargets(float trackedHeight)
    {
        ApplyHeadAndFace();
        ApplyTorso();
        ApplyLowerLegs();
        ApplyTrackedHands();
        ApplyCarriedEquipment(trackedHeight);
    }

    void ApplyHeadAndFace()
    {
        if (!HasTracking || anchorSet == null)
            return;

        ApplyHmdLocalPose(anchorSet.Head, helmetHeadPositionOffset, headRotationOffsetEuler);

        if (activeFaceProfile == PPEFaceEquipProfile.FaceProtection)
            ApplyHmdLocalPose(anchorSet.Face, faceProtectionPositionOffset, faceProtectionRotationOffsetEuler);
        else
            ApplyHmdLocalPose(anchorSet.Face, respiratorPositionOffset, respiratorRotationOffsetEuler);
    }

    void ApplyHmdLocalPose(Transform target, Vector3 localPosition, Vector3 localEuler)
    {
        if (target == null || xrCamera == null)
            return;

        var worldRotation = xrCamera.rotation * Quaternion.Euler(localEuler);
        var worldPosition = xrCamera.position + (xrCamera.rotation * localPosition);
        target.SetPositionAndRotation(worldPosition, worldRotation);
    }

    void ApplyTorso()
    {
        if (anchorSet == null || anchorSet.Body == null)
            return;

        ComputeTorsoWorldPose(out var position, out var rotation);
        anchorSet.Body.SetPositionAndRotation(position, rotation);
    }

    void ApplyLowerLegs()
    {
        if (anchorSet == null)
            return;

        if (anchorSet.LeftFoot != null)
        {
            ComputeLowerLegWorldPose(PPEGrabHand.Left, out var position, out var rotation);
            anchorSet.LeftFoot.SetPositionAndRotation(position, rotation);
        }

        if (anchorSet.RightFoot != null)
        {
            ComputeLowerLegWorldPose(PPEGrabHand.Right, out var position, out var rotation);
            anchorSet.RightFoot.SetPositionAndRotation(position, rotation);
        }
    }

    void ApplyTrackedHands()
    {
        if (anchorSet == null)
            return;

        leftHandTracked = ApplyHandAnchor(anchorSet.LeftHand, PPEGrabHand.Left);
        rightHandTracked = ApplyHandAnchor(anchorSet.RightHand, PPEGrabHand.Right);
    }

    bool ApplyHandAnchor(Transform target, PPEGrabHand hand)
    {
        if (target == null)
            return false;
        if (!PPEXrHandsJointPose.TryGetWorldPose(hand, XRHandJointID.Palm, out var worldPose))
            return false;

        target.SetPositionAndRotation(worldPose.position, worldPose.rotation);
        return true;
    }

    void ApplyCarriedEquipment(float trackedHeight)
    {
        if (anchorPoses == null)
            return;

        for (var i = 0; i < anchorPoses.Length; i++)
        {
            var pose = anchorPoses[i];
            if (pose.anchor == null)
                continue;
            if (pose.slot != PPEEquipSlot.Radio && pose.slot != PPEEquipSlot.GasDetector)
                continue;

            pose.anchor.localPosition = new Vector3(
                pose.horizontalOffset.x,
                trackedHeight * pose.heightRatio + pose.verticalOffset,
                pose.horizontalOffset.y);
            pose.anchor.localRotation = Quaternion.Euler(pose.localEulerAngles);
        }
    }

    void ComputeTorsoWorldPose(out Vector3 position, out Quaternion rotation)
    {
        rotation = BodyYawRotation;
        var headY = xrCamera != null ? xrCamera.position.y : transform.position.y + currentHeight;
        position = new Vector3(transform.position.x, headY, transform.position.z);
        position += Vector3.down * torsoVerticalOffset;
        position += rotation * Vector3.forward * torsoForwardOffset;
    }

    void ComputeLowerLegWorldPose(PPEGrabHand side, out Vector3 position, out Quaternion rotation)
    {
        rotation = BodyYawRotation;
        var lateral = side == PPEGrabHand.Right ? footSpacing : -footSpacing;
        position = transform.position;
        position += rotation * Vector3.right * lateral;
        position += rotation * Vector3.forward * lowerBodyForwardOffset;
        position += Vector3.up * lowerLegHeight;
    }

    Vector2 GetCurrentPlanarPosition()
    {
        if (xrOrigin != null && transform.parent == xrOrigin.transform)
            return new Vector2(transform.localPosition.x, transform.localPosition.z);

        if (xrOrigin != null)
        {
            var local = xrOrigin.transform.InverseTransformPoint(transform.position);
            return new Vector2(local.x, local.z);
        }

        return new Vector2(transform.position.x, transform.position.z);
    }

    float ClampTrackedHeight(float height)
    {
        var max = Mathf.Max(minimumTrackedHeight, maximumTrackedHeight);
        return Mathf.Clamp(height, minimumTrackedHeight, max);
    }

    float HorizontalYawDegrees(Transform source)
    {
        if (source == null)
            return currentYawDegrees;

        var forward = Vector3.ProjectOnPlane(source.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = Vector3.ProjectOnPlane(source.up, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
                return hasYaw ? currentYawDegrees : 0f;
        }

        return Quaternion.LookRotation(forward.normalized, Vector3.up).eulerAngles.y;
    }

    void ResolveTrackingSource()
    {
        if (xrOrigin != null && !xrOrigin.isActiveAndEnabled)
            xrOrigin = null;

        if (xrOrigin == null)
        {
            var parentOrigin = GetComponentInParent<XROrigin>(true);
            if (PPEActiveXrOrigin.IsUsable(parentOrigin))
                xrOrigin = parentOrigin;
            else
                xrOrigin = PPEActiveXrOrigin.Resolve();
        }

        if (xrCamera == null || (xrOrigin != null && xrOrigin.Camera != null && !xrCamera.gameObject.activeInHierarchy))
        {
            if (xrOrigin != null && xrOrigin.Camera != null)
                xrCamera = xrOrigin.Camera.transform;
        }

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
        positionFollowSmooth = 0.06f;
        rotationFollowSmooth = 0.08f;
        bodyReferenceSetupVersion = 2;
        drawDynamicTargetGizmos = false;

        anchorPoses = equipmentAnchors == null
            ? Array.Empty<AnchorPose>()
            : new[]
            {
                new AnchorPose(PPEEquipSlot.Head, equipmentAnchors.Head, 1f, 0.08f, new Vector2(0f, 0.04f), Vector3.zero),
                new AnchorPose(PPEEquipSlot.Face, equipmentAnchors.Face, 1f, -0.10f, new Vector2(0f, 0.13f), Vector3.zero),
                new AnchorPose(PPEEquipSlot.LeftHand, equipmentAnchors.LeftHand, 0.60f, 0f, new Vector2(-0.32f, 0.10f), Vector3.zero),
                new AnchorPose(PPEEquipSlot.RightHand, equipmentAnchors.RightHand, 0.60f, 0f, new Vector2(0.32f, 0.10f), Vector3.zero),
                new AnchorPose(PPEEquipSlot.Body, equipmentAnchors.Body, 0.55f, 0f, new Vector2(0f, 0.02f), Vector3.zero),
                new AnchorPose(PPEEquipSlot.LeftFoot, equipmentAnchors.LeftFoot, 0f, 0.38f, new Vector2(-0.14f, 0.08f), Vector3.zero),
                new AnchorPose(PPEEquipSlot.RightFoot, equipmentAnchors.RightFoot, 0f, 0.38f, new Vector2(0.14f, 0.08f), Vector3.zero),
                new AnchorPose(PPEEquipSlot.Radio, equipmentAnchors.Radio, 0.62f, 0f, new Vector2(-0.18f, 0.05f), Vector3.zero),
                new AnchorPose(PPEEquipSlot.GasDetector, equipmentAnchors.GasDetector, 0.58f, 0f, new Vector2(0.18f, 0.07f), Vector3.zero),
            };

        ApplyCarriedEquipment(1.70f);
    }

    void OnDrawGizmos()
    {
        if (!drawDynamicTargetGizmos)
            return;
        DrawDynamicGizmos();
    }

    void OnDrawGizmosSelected()
    {
        DrawDynamicGizmos();
    }

    void DrawDynamicGizmos()
    {
        if (anchorSet == null)
            anchorSet = GetComponentInChildren<PPEEquipmentAnchorSet>(true);

        DrawLabeledTarget(anchorSet != null ? anchorSet.Head : null, new Color(0.6f, 0.85f, 1f, 0.95f), "Head");
        DrawLabeledTarget(anchorSet != null ? anchorSet.Face : null, new Color(0.35f, 0.95f, 1f, 0.95f), "Face");
        DrawLabeledTarget(anchorSet != null ? anchorSet.Body : null, new Color(0.3f, 1f, 0.45f, 0.95f), "Torso");
        DrawLabeledTarget(anchorSet != null ? anchorSet.LeftFoot : null, new Color(1f, 0.7f, 0.2f, 0.95f), "LeftLowerLeg");
        DrawLabeledTarget(anchorSet != null ? anchorSet.RightFoot : null, new Color(1f, 0.45f, 0.15f, 0.95f), "RightLowerLeg");

        var leftColor = leftHandTracked ? new Color(0.2f, 0.85f, 1f, 0.95f) : new Color(1f, 0.25f, 0.25f, 0.9f);
        var rightColor = rightHandTracked ? new Color(0.2f, 0.85f, 1f, 0.95f) : new Color(1f, 0.25f, 0.25f, 0.9f);
        DrawLabeledTarget(anchorSet != null ? anchorSet.LeftHand : null, leftColor, "LeftHand");
        DrawLabeledTarget(anchorSet != null ? anchorSet.RightHand : null, rightColor, "RightHand");
    }

    static void DrawLabeledTarget(Transform target, Color color, string label)
    {
        if (target == null)
            return;

        Gizmos.color = color;
        Gizmos.DrawWireSphere(target.position, 0.03f);
        Gizmos.DrawRay(target.position, target.forward * 0.08f);
        Gizmos.DrawRay(target.position, target.up * 0.05f);
        UnityEditor.Handles.color = color;
        UnityEditor.Handles.Label(target.position + Vector3.up * 0.05f, label);
    }
#endif
}
