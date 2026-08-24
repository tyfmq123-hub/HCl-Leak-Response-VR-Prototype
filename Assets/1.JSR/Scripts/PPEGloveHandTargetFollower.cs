using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public enum PPEGloveTrackingSource
{
    XRHands,
    ControllerFallback,
    AutoActive,
}

public enum PPEResolvedHandTargetSource
{
    None,
    XRHandsPalm,
    ControllerHand,
}

public enum PPEGloveTargetJoint
{
    Palm,
    Wrist,
}

/// <summary>
/// Copies a tracked XR Hands palm/wrist pose onto a PPE-owned glove equip target.
/// Does not parent under or move XR Origin. Used only as a glove placement target.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(50)]
public sealed class PPEGloveHandTargetFollower : MonoBehaviour
{
    [SerializeField] PPEGrabHand hand = PPEGrabHand.Left;
    [SerializeField] PPEGloveTrackingSource trackingSource = PPEGloveTrackingSource.AutoActive;
    [SerializeField] PPEGloveTargetJoint targetJoint = PPEGloveTargetJoint.Palm;
    [SerializeField] Vector3 targetPositionOffset;
    [SerializeField] Vector3 targetRotationOffsetEuler;
    [SerializeField, Min(0f)]
    [Tooltip("Keep the last valid pose this long after a tracking miss.")]
    float trackingLossGraceTime = 0.30f;
    [SerializeField] Transform controllerFallbackSource;
    [SerializeField]
    [Tooltip("Applied only in ControllerHand mode so the target sits at the virtual palm, not the grip pivot.")]
    Vector3 controllerPalmLocalOffset = new Vector3(0f, 0f, 0.07f);
    [SerializeField] bool drawEditorDebugGizmos;
    [Header("Runtime Debug")]
    [SerializeField] PPEResolvedHandTargetSource debugTargetSource;
    [SerializeField] bool debugTargetValid;
    [SerializeField] string debugVisibleHandSource;

    Pose lastRawWorldPose;
    bool hasRawWorldPose;
    string lastLoggedSource;
    bool lastLoggedTracking;
    XRHandSubsystem subscribedSubsystem;
    float nextSubsystemRetryUnscaled = -1f;
    float trackingLostUnscaled = -1f;
    PPEResolvedHandTargetSource lockedSource = PPEResolvedHandTargetSource.None;

    public PPEGrabHand Hand => hand;
    public Transform TrackedSource => controllerFallbackSource;
    public bool IsTracking { get; private set; }
    public bool TargetValid => IsTracking;
    public bool IsInTrackingGrace =>
        hasRawWorldPose &&
        trackingLostUnscaled >= 0f &&
        Time.unscaledTime - trackingLostUnscaled < trackingLossGraceTime;
    public PPEGloveTrackingSource TrackingSource => trackingSource;
    public PPEResolvedHandTargetSource ResolvedSource { get; private set; }
    public string ResolvedSourceLabel
    {
        get
        {
            switch (ResolvedSource)
            {
                case PPEResolvedHandTargetSource.XRHandsPalm:
                    return "XRHandsPalm";
                case PPEResolvedHandTargetSource.ControllerHand:
                    return "ControllerHand";
                default:
                    return "None";
            }
        }
    }
    public Transform ActiveControllerHandSource { get; private set; }
    public PPEGloveTargetJoint TargetJoint => targetJoint;
    public float TrackingLossGraceTime => trackingLossGraceTime;

    public bool TryGetPalmForRelease(float graceSeconds, out Vector3 worldPosition)
    {
        worldPosition = transform.position;
        if (IsTracking)
            return true;
        if (!hasRawWorldPose)
            return false;
        if (trackingLostUnscaled < 0f)
            return true;
        return Time.unscaledTime - trackingLostUnscaled <= Mathf.Max(0f, graceSeconds);
    }

    public void Configure(PPEGrabHand targetHand, Transform source)
    {
        hand = targetHand;
        controllerFallbackSource = source;
        trackingSource = PPEGloveTrackingSource.AutoActive;
    }

    public void UseAutoActive()
    {
        trackingSource = PPEGloveTrackingSource.AutoActive;
    }

    void OnEnable()
    {
        if (trackingSource != PPEGloveTrackingSource.ControllerFallback)
            trackingSource = PPEGloveTrackingSource.AutoActive;
        BindSubsystem(PPEXrHandsJointPose.ResolveRunningSubsystem());
        RefreshTrackingAndPose();
    }

    void OnDisable()
    {
        UnbindSubsystem();
        trackingLostUnscaled = -1f;
        hasRawWorldPose = false;
        SetTracking(false, logChange: false);
    }

    void LateUpdate()
    {
        if (trackingSource != PPEGloveTrackingSource.ControllerFallback &&
            (subscribedSubsystem == null || !subscribedSubsystem.running))
        {
            TryBindSubsystemThrottled();
        }

        RefreshTrackingAndPose();
        PPEHandSourceDiagnostics.LogOnceIfNeeded(this);
    }

    public void SnapToTrackedSource()
    {
        lockedSource = PPEResolvedHandTargetSource.None;
        RefreshTrackingAndPose();
    }

    public void RefreshForRelease()
    {
        RefreshTrackingAndPose();
    }

    void RefreshTrackingAndPose()
    {
        if (trackingSource == PPEGloveTrackingSource.XRHands)
        {
            if (TryApplyXrHandsPose())
                return;
            HandleLost("XRHands pose missing");
            return;
        }

        if (trackingSource == PPEGloveTrackingSource.ControllerFallback)
        {
            if (TryApplyControllerHandPose())
                return;
            HandleLost("ControllerHand pose missing");
            return;
        }

        RefreshAutoActive();
    }

    void RefreshAutoActive()
    {
        var xrValid = TryReadXrHandsPose(out var xrPose);
        var controllerValid = TryResolveControllerHandPose(out var controllerPose, out var controllerSource);

        var chosen = lockedSource;
        if (chosen == PPEResolvedHandTargetSource.XRHandsPalm && !xrValid)
            chosen = PPEResolvedHandTargetSource.None;
        if (chosen == PPEResolvedHandTargetSource.ControllerHand && !controllerValid)
            chosen = PPEResolvedHandTargetSource.None;

        if (chosen == PPEResolvedHandTargetSource.None)
        {
            if (xrValid)
                chosen = PPEResolvedHandTargetSource.XRHandsPalm;
            else if (controllerValid)
                chosen = PPEResolvedHandTargetSource.ControllerHand;
        }

        lockedSource = chosen;
        if (chosen == PPEResolvedHandTargetSource.XRHandsPalm)
        {
            ApplyResolvedPose(PPEResolvedHandTargetSource.XRHandsPalm, xrPose, "XRHandsPalm");
            return;
        }

        if (chosen == PPEResolvedHandTargetSource.ControllerHand)
        {
            ActiveControllerHandSource = controllerSource;
            ApplyResolvedPose(PPEResolvedHandTargetSource.ControllerHand, controllerPose, "ControllerHand");
            return;
        }

        HandleLost("No active XRHands or ControllerHand pose");
    }

    void TryBindSubsystemThrottled()
    {
        if (Time.unscaledTime < nextSubsystemRetryUnscaled)
            return;

        nextSubsystemRetryUnscaled = Time.unscaledTime + 0.5f;
        BindSubsystem(PPEXrHandsJointPose.ResolveRunningSubsystem());
    }

    void BindSubsystem(XRHandSubsystem subsystem)
    {
        if (subsystem == subscribedSubsystem)
            return;

        UnbindSubsystem();
        subscribedSubsystem = subsystem;
        if (subscribedSubsystem == null)
            return;

        subscribedSubsystem.updatedHands += OnUpdatedHands;
        RefreshTrackingAndPose();
    }

    void UnbindSubsystem()
    {
        if (subscribedSubsystem == null)
            return;

        subscribedSubsystem.updatedHands -= OnUpdatedHands;
        subscribedSubsystem = null;
    }

    void OnUpdatedHands(
        XRHandSubsystem subsystem,
        XRHandSubsystem.UpdateSuccessFlags successFlags,
        XRHandSubsystem.UpdateType updateType)
    {
        if (trackingSource == PPEGloveTrackingSource.ControllerFallback)
            return;
        if (updateType == XRHandSubsystem.UpdateType.Dynamic)
            return;

        RefreshTrackingAndPose();
    }

    bool TryApplyXrHandsPose()
    {
        if (!TryReadXrHandsPose(out var pose))
            return false;
        ApplyResolvedPose(PPEResolvedHandTargetSource.XRHandsPalm, pose, "XRHandsPalm");
        return true;
    }

    bool TryReadXrHandsPose(out Pose pose)
    {
        pose = Pose.identity;
        var subsystem = subscribedSubsystem ?? PPEXrHandsJointPose.ResolveRunningSubsystem();
        return PPEXrHandsJointPose.TryGetWorldPose(
            subsystem,
            hand,
            ToJointId(targetJoint),
            default,
            out pose);
    }

    bool TryApplyControllerHandPose()
    {
        if (!TryResolveControllerHandPose(out var pose, out var source))
            return false;
        ActiveControllerHandSource = source;
        ApplyResolvedPose(PPEResolvedHandTargetSource.ControllerHand, pose, "ControllerHand");
        return true;
    }

    bool TryResolveControllerHandPose(out Pose pose, out Transform source)
    {
        pose = Pose.identity;
        source = null;
        if (controllerFallbackSource != null && controllerFallbackSource.gameObject.activeInHierarchy)
            source = controllerFallbackSource;
        if (source == null)
            source = PPEHandTargetResolver.ResolveControllerHand(hand);
        if (source == null || !source.gameObject.activeInHierarchy)
            return false;

        pose = new Pose(source.TransformPoint(controllerPalmLocalOffset), source.rotation);
        ActiveControllerHandSource = source;
        return true;
    }

    void ApplyResolvedPose(PPEResolvedHandTargetSource source, Pose rawWorld, string logName)
    {
        trackingLostUnscaled = -1f;
        ResolvedSource = source;
        lockedSource = source;
        SetTracking(true);
        LogSourceOnce(logName);
        ApplyWorldPose(rawWorld.position, rawWorld.rotation, storeRaw: true);
        UpdateDebugFields();
    }

    void HandleLost(string reason)
    {
        if (hasRawWorldPose && trackingLossGraceTime > 0f)
        {
            if (trackingLostUnscaled < 0f)
                trackingLostUnscaled = Time.unscaledTime;

            if (Time.unscaledTime - trackingLostUnscaled < trackingLossGraceTime)
            {
                SetTracking(true);
                LogSourceOnce(ResolvedSourceLabel + " grace — holding last pose");
                ApplyWorldPose(lastRawWorldPose.position, lastRawWorldPose.rotation, storeRaw: false);
                UpdateDebugFields();
                return;
            }
        }

        trackingLostUnscaled = -1f;
        hasRawWorldPose = false;
        lockedSource = PPEResolvedHandTargetSource.None;
        ResolvedSource = PPEResolvedHandTargetSource.None;
        ActiveControllerHandSource = null;
        SetTracking(false);
        LogSourceOnce(reason);
        UpdateDebugFields();
    }

    void UpdateDebugFields()
    {
        debugTargetSource = ResolvedSource;
        debugTargetValid = TargetValid;
        var visible = ActiveControllerHandSource != null
            ? ActiveControllerHandSource
            : PPEHandTargetResolver.ResolveControllerHand(hand);
        debugVisibleHandSource = visible != null
            ? visible.name + " (" + PPEHandSourceDiagnostics.BuildPath(visible) + ")"
            : "(none)";
    }

    void ApplyWorldPose(Vector3 rawPosition, Quaternion rawRotation, bool storeRaw)
    {
        if (storeRaw)
        {
            lastRawWorldPose = new Pose(rawPosition, rawRotation);
            hasRawWorldPose = true;
        }

        var worldRotation = rawRotation * Quaternion.Euler(targetRotationOffsetEuler);
        var worldPosition = rawPosition + (rawRotation * targetPositionOffset);
        transform.SetPositionAndRotation(worldPosition, worldRotation);
    }

    void SetTracking(bool tracking, bool logChange = true)
    {
        if (IsTracking == tracking)
            return;
        IsTracking = tracking;
        if (logChange)
            LogTrackingLostOrRestored(tracking);
    }

    void LogSourceOnce(string source)
    {
        if (lastLoggedSource == source)
            return;
        lastLoggedSource = source;
        Debug.Log("[GloveHandTarget] " + HandLabel() + " source = " + source, this);
    }

    void LogTrackingLostOrRestored(bool tracking)
    {
        if (lastLoggedTracking == tracking)
            return;
        lastLoggedTracking = tracking;
        Debug.Log(
            tracking
                ? "[GloveHandTarget] " + HandLabel() + " hand tracking restored"
                : "[GloveHandTarget] " + HandLabel() + " hand tracking lost",
            this);
    }

    string HandLabel() => hand == PPEGrabHand.Right ? "Right" : "Left";

    static XRHandJointID ToJointId(PPEGloveTargetJoint joint)
    {
        return joint == PPEGloveTargetJoint.Wrist ? XRHandJointID.Wrist : XRHandJointID.Palm;
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!drawEditorDebugGizmos)
            return;
        DrawDebugGizmos();
    }

    void OnDrawGizmosSelected()
    {
        DrawDebugGizmos();
    }

    void DrawDebugGizmos()
    {
        if (hasRawWorldPose)
        {
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
            Gizmos.DrawWireSphere(lastRawWorldPose.position, 0.012f);
            Gizmos.DrawRay(lastRawWorldPose.position, lastRawWorldPose.rotation * Vector3.forward * 0.05f);
        }

        Gizmos.color = IsTracking ? new Color(0.2f, 0.85f, 1f, 0.9f) : new Color(1f, 0.25f, 0.25f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, 0.016f);
        Gizmos.DrawRay(transform.position, transform.forward * 0.06f);
    }
#endif
}

/// <summary>
/// Reads Unity XR Hands joint poses and converts tracking-origin space to world space.
/// </summary>
public static class PPEXrHandsJointPose
{
    static readonly List<XRHandSubsystem> subsystems = new List<XRHandSubsystem>(2);
    static XRHandSubsystem cachedSubsystem;
    static Transform cachedOriginTransform;

    public static XRHandSubsystem ResolveRunningSubsystem()
    {
        if (cachedSubsystem != null && cachedSubsystem.running)
            return cachedSubsystem;

        cachedSubsystem = null;
        subsystems.Clear();
        SubsystemManager.GetSubsystems(subsystems);
        for (var i = 0; i < subsystems.Count; i++)
        {
            var subsystem = subsystems[i];
            if (subsystem != null && subsystem.running)
            {
                cachedSubsystem = subsystem;
                return cachedSubsystem;
            }
        }

        return null;
    }

    public static bool TryGetWorldPose(PPEGrabHand hand, XRHandJointID jointId, out Pose worldPose)
    {
        return TryGetWorldPose(
            ResolveRunningSubsystem(),
            hand,
            jointId,
            default,
            out worldPose);
    }

    public static bool TryGetWorldPose(
        XRHandSubsystem subsystem,
        PPEGrabHand hand,
        XRHandJointID jointId,
        XRHandSubsystem.UpdateSuccessFlags successFlags,
        out Pose worldPose)
    {
        worldPose = Pose.identity;
        if (subsystem == null || !subsystem.running)
            return false;
        if (hand != PPEGrabHand.Left && hand != PPEGrabHand.Right)
            return false;

        var jointsFlag = hand == PPEGrabHand.Right
            ? XRHandSubsystem.UpdateSuccessFlags.RightHandJoints
            : XRHandSubsystem.UpdateSuccessFlags.LeftHandJoints;
        if (successFlags != default && (successFlags & jointsFlag) == 0)
            return false;

        var xrHand = hand == PPEGrabHand.Right ? subsystem.rightHand : subsystem.leftHand;
        if (!xrHand.isTracked)
            return false;

        var joint = xrHand.GetJoint(jointId);
        if ((joint.trackingState & XRHandJointTrackingState.Pose) == 0)
            return false;
        if (!joint.TryGetPose(out var trackingPose))
            return false;

        var origin = ResolveOriginTransform();
        if (origin == null)
            return false;

        worldPose = new Pose(
            origin.TransformPoint(trackingPose.position),
            origin.rotation * trackingPose.rotation);
        return true;
    }

    static Transform ResolveOriginTransform()
    {
        if (cachedOriginTransform != null && cachedOriginTransform.gameObject.activeInHierarchy)
            return cachedOriginTransform;

        cachedOriginTransform = PPEActiveXrOrigin.ResolveOriginTransform();
        return cachedOriginTransform;
    }
}

/// <summary>
/// Authoritative controller-hand pose for PPE glove targets.
/// Uses the active XR Origin NearFar/Direct attach transform. Never searches
/// active <c>Hand</c> visuals, which may be worn-glove meshes.
/// </summary>
public static class PPEHandTargetResolver
{
    const string TargetRootName = "PPE_HandTargets";

    static Transform targetRoot;
    static Transform cachedLeftControllerHand;
    static Transform cachedRightControllerHand;

    public static Transform GetOrCreateTargetRoot()
    {
        if (targetRoot != null)
            return targetRoot;

        var existing = GameObject.Find(TargetRootName);
        if (existing != null)
        {
            targetRoot = existing.transform;
            return targetRoot;
        }

        var go = new GameObject(TargetRootName);
        var manager = Object.FindAnyObjectByType<PPEEquipmentManager>(FindObjectsInactive.Include);
        if (manager != null && go.scene != manager.gameObject.scene)
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, manager.gameObject.scene);
        targetRoot = go.transform;
        return targetRoot;
    }

    public static Transform ResolveControllerHand(PPEGrabHand hand)
    {
        if (hand != PPEGrabHand.Left && hand != PPEGrabHand.Right)
            return null;

        var cached = hand == PPEGrabHand.Right ? cachedRightControllerHand : cachedLeftControllerHand;
        if (IsUsableInteractionSource(cached))
            return cached;

        Transform resolved = null;
        var origin = FindActiveOrigin();
        var controller = ResolveActiveController(origin, hand);
        if (controller != null)
        {
            var interactor = FindActiveNearFar(controller);
            if (interactor != null)
            {
                if (IsUsableInteractionSource(interactor.attachTransform))
                    resolved = interactor.attachTransform;
                else if (IsUsableInteractionSource(interactor.transform))
                    resolved = interactor.transform;
            }

            if (resolved == null && IsUsableInteractionSource(controller))
                resolved = controller;
        }

        if (resolved == null)
            return null;

        CacheControllerHand(hand, resolved);
        return resolved;
    }

    static void CacheControllerHand(PPEGrabHand hand, Transform source)
    {
        if (hand == PPEGrabHand.Right)
            cachedRightControllerHand = source;
        else
            cachedLeftControllerHand = source;
    }

    static bool IsWearableVisual(Transform transform)
    {
        return PPEWearableVisualOnly.IsInHierarchy(transform);
    }

    static bool IsUsableInteractionSource(Transform transform)
    {
        return IsUsable(transform) && !IsWearableVisual(transform);
    }

    static XROrigin FindActiveOrigin()
    {
        return PPEActiveXrOrigin.Resolve();
    }

    static Transform ResolveActiveController(XROrigin origin, PPEGrabHand hand)
    {
        if (origin == null || !origin.isActiveAndEnabled)
            return null;

        var expected = hand == PPEGrabHand.Left ? "Left Controller" : "Right Controller";
        var offset = origin.CameraFloorOffsetObject != null
            ? origin.CameraFloorOffsetObject.transform
            : origin.Camera != null ? origin.Camera.transform.parent : origin.transform;
        var fromOffset = offset != null ? FindNamedOnce(offset, expected) : null;
        var controller = fromOffset != null ? fromOffset : FindNamedOnce(origin.transform, expected);
        if (controller == null || !controller.gameObject.activeInHierarchy)
            return null;
        return controller;
    }

    static XRBaseInteractor FindActiveNearFar(Transform controller)
    {
        if (controller == null)
            return null;

        var nearFar = controller.GetComponentsInChildren<NearFarInteractor>(false);
        for (var i = 0; i < nearFar.Length; i++)
        {
            var interactor = nearFar[i];
            if (interactor != null && interactor.isActiveAndEnabled && interactor.gameObject.activeInHierarchy)
                return interactor;
        }

        var direct = controller.GetComponentsInChildren<XRDirectInteractor>(false);
        for (var i = 0; i < direct.Length; i++)
        {
            var interactor = direct[i];
            if (interactor != null && interactor.isActiveAndEnabled && interactor.gameObject.activeInHierarchy)
                return interactor;
        }

        return null;
    }

    static Transform FindNamedOnce(Transform root, string name)
    {
        if (root == null)
            return null;

        var direct = root.Find(name);
        if (direct != null)
            return direct;

        var children = root.GetComponentsInChildren<Transform>(true);
        for (var i = 0; i < children.Length; i++)
        {
            var child = children[i];
            if (child != null && child.name == name)
                return child;
        }

        return null;
    }

    static bool IsUsable(Transform transform)
    {
        return transform != null && transform.gameObject.activeInHierarchy;
    }
}

/// <summary>
/// Compatibility wrapper. New code should call PPEHandTargetResolver.
/// </summary>
public static class PPETrackedXrHandResolver
{
    public static Transform Find(PPEGrabHand hand)
    {
        return PPEHandTargetResolver.ResolveControllerHand(hand);
    }

    public static Transform FindActiveHandPose(PPEGrabHand hand)
    {
        return PPEHandTargetResolver.ResolveControllerHand(hand);
    }
}

public static class PPEHandSourceDiagnostics
{
    static bool logged;
    static float firstAttemptUnscaled = -1f;

    public static void LogOnceIfNeeded(PPEGloveHandTargetFollower caller)
    {
        if (logged || caller == null || caller.Hand != PPEGrabHand.Left)
            return;
        if (firstAttemptUnscaled < 0f)
            firstAttemptUnscaled = Time.unscaledTime;

        var leftPose = PPEHandTargetResolver.ResolveControllerHand(PPEGrabHand.Left);
        var rightPose = PPEHandTargetResolver.ResolveControllerHand(PPEGrabHand.Right);
        var hasVisible = leftPose != null || rightPose != null;
        if (!hasVisible && Time.unscaledTime - firstAttemptUnscaled < 0.75f)
            return;

        logged = true;
        var left = caller;
        var right = FindOther(PPEGrabHand.Right);
        Debug.Log(
            "[PPE Hand Source]\n" +
            FormatHand("Left", left) +
            FormatHand("Right", right),
            caller);
    }

    public static string BuildPath(Transform transform)
    {
        if (transform == null)
            return "(null)";
        var names = new System.Collections.Generic.List<string>(8);
        for (var current = transform; current != null; current = current.parent)
            names.Add(current.name);
        names.Reverse();
        return string.Join("/", names);
    }

    static string FormatHand(string label, PPEGloveHandTargetFollower follower)
    {
        var xrTracked = false;
        var xrPalm = false;
        var subsystem = PPEXrHandsJointPose.ResolveRunningSubsystem();
        if (subsystem != null && subsystem.running)
        {
            var xrHand = label == "Right" ? subsystem.rightHand : subsystem.leftHand;
            xrTracked = xrHand.isTracked;
            xrPalm = PPEXrHandsJointPose.TryGetWorldPose(
                label == "Right" ? PPEGrabHand.Right : PPEGrabHand.Left,
                XRHandJointID.Palm,
                out _);
        }

        var node = label == "Right" ? XRNode.RightHand : XRNode.LeftHand;
        var device = InputDevices.GetDeviceAtXRNode(node);
        var controllerTracked = device.isValid &&
                                (!device.TryGetFeatureValue(CommonUsages.isTracked, out var tracked) || tracked);
        var pose = PPEHandTargetResolver.ResolveControllerHand(
            label == "Right" ? PPEGrabHand.Right : PPEGrabHand.Left);
        var nearFar = pose != null ? pose.GetComponentInParent<NearFarInteractor>() : null;
        var interactor = nearFar != null ? nearFar.name : "(none)";

        return
            "\n" + label + ":\n" +
            "XRHandsTracked = " + xrTracked + "\n" +
            "XRHandsPalmValid = " + xrPalm + "\n" +
            "ControllerTracked = " + controllerTracked + "\n" +
            "ActiveInteractor = " + interactor + "\n" +
            "VisibleHandSource = " + (pose != null ? pose.name + " (" + BuildPath(pose) + ")" : "(none)") + "\n" +
            "ResolvedTargetSource = " + (follower != null ? follower.ResolvedSourceLabel : "None") + "\n" +
            "TargetValid = " + (follower != null && follower.TargetValid) + "\n";
    }

    static PPEGloveHandTargetFollower FindOther(PPEGrabHand hand)
    {
        var followers = Object.FindObjectsByType<PPEGloveHandTargetFollower>(FindObjectsInactive.Exclude);
        for (var i = 0; i < followers.Length; i++)
        {
            if (followers[i] != null && followers[i].Hand == hand)
                return followers[i];
        }

        return null;
    }
}
