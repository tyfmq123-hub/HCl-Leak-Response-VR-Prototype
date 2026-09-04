using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public enum PPEPlacementGuideState
{
    NotReady,
    Ready,
}

/// <summary>
/// Shows placement guides while PPE is grabbed, evaluates the authored distance
/// and rotation tolerances against live body/hand targets, and routes successful
/// placement through the existing <see cref="PPEEquipmentManager.Equip"/> contract.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public sealed class PPEPlacementGuideManager : MonoBehaviour
{
    sealed class GrabBinding
    {
        public PPEHandGrabbable Grabbable;
        public Action<PPEGrabHand> Grabbed;
        public Action<PPEGrabHand> Released;
    }

    sealed class ActiveGuide
    {
        public PPEEquipSlot Slot;
        public Transform Anchor;
        public GameObject Root;
        public Renderer[] Renderers;
        public PPEPlacementGuideState State;
        public Material[] CachedNotReadyMaterials;
        public Material[] CachedReadyMaterials;
        public Material[] CachedStaticMaterials;
        public float[] CachedStaticBaseAlphas;
    }

    const float InnerGloveReadyEnterDistance = 0.12f;
    const float InnerGloveReadyExitDistance = 0.15f;

    static readonly PPEEquipSlot[] ConcreteSlots =
    {
        PPEEquipSlot.Head,
        PPEEquipSlot.Face,
        PPEEquipSlot.LeftHand,
        PPEEquipSlot.RightHand,
        PPEEquipSlot.Body,
        PPEEquipSlot.LeftFoot,
        PPEEquipSlot.RightFoot,
        PPEEquipSlot.Radio,
        PPEEquipSlot.GasDetector,
    };

    [Header("Placement Anchors")]
    [SerializeField] PPEEquipmentAnchorSet anchorSet;
    [SerializeField] PPEPlayerBodyReference bodyReference;

    [Header("Boot Guide")]
    [SerializeField, Range(10f, 80f)]
    [Tooltip("HMD pitch below the horizon (degrees) required to show boot ghosts. Target pose still updates while hidden.")]
    float bootGuideLookDownAngle = 30f;

    [Header("Face Ghost Comfort")]
    [SerializeField]
    [Tooltip("Ghost-only extra distance along the Face anchor forward (HMD forward). Release-to-Equip still uses the Face target.")]
    float faceGhostVisualForwardOffset = 0.15f;

    [SerializeField]
    float faceGhostVisualVerticalOffset;

    [Header("Face / Suit Static Ghost")]
    [Header("Suit Ghost Comfort")]
    [SerializeField]
    [Tooltip("Ghost-only extra distance along torso forward. Does not move the Body validation target.")]
    float suitGhostVisualForwardOffset = 0.12f;

    [SerializeField]
    [Tooltip("Ghost-only downward shift so a full-body suit does not sit in the HMD.")]
    float suitGhostVisualDownOffset = 0.08f;

    [Header("Ready Visual Hysteresis")]
    [SerializeField]
    [Tooltip("Ready visual stays on until distance exceeds Equip Distance by this amount. Does not change Release-to-Equip.")]
    float readyExitDistanceExtra = 0.03f;

    [Header("Ghost Materials")]
    [SerializeField] Material notReadyMaterial;
    [SerializeField] Material readyMaterial;

    [Header("Fallback")]
    [SerializeField]
    [Tooltip("Build a renderer-only ghost from the grabbed PPE when Guide Prefab is not assigned.")]
    bool usePpeVisualWhenGuidePrefabMissing = true;

    [Header("Placement Equip")]
    [SerializeField]
    [Tooltip("Routes a valid placement through PPEEquipmentManager.Equip. Wearable PPE has no button-equip path.")]
    bool autoEquipEnabled = true;

    [SerializeField]
    [Min(0f)]
    [Tooltip("How long placement must remain valid before Equip. 0.20 s feels immediate but intentional.")]
    float readyHoldDuration = 0.20f;

    [Header("Development Debug")]
    [SerializeField]
    [Tooltip("Log placement snapshot only when values change. Not every frame.")]
    bool logPlacementDebugOnChange;

    [SerializeField]
    [Tooltip("Show Equip Debug Visuals. Runtime zone spheres, HMD overlay, radius rings. Editor/Development only.")]
    bool showEquipDebugVisuals;

    [SerializeField]
    [Tooltip("World-space placement debug overlay. Requires Show Equip Debug Visuals. Off for training.")]
    bool showPlacementDebugOverlay;

    [SerializeField] string debugStatus = "IDLE";
    [SerializeField] string debugItem;
    [SerializeField] string debugHoldingHand;
    [SerializeField] string debugTarget;
    [SerializeField] bool debugTargetValid;
    [SerializeField] float debugDistance;
    [SerializeField] float debugMaxDistance;
    [SerializeField] float debugAngle;
    [SerializeField] float debugMaxAngle;
    [SerializeField] bool debugDistanceValid;
    [SerializeField] bool debugAngleValid;
    [SerializeField] bool debugSequenceValid;
    [SerializeField] bool debugReady;
    [SerializeField] bool debugUseRotationValidation;
    [SerializeField] bool debugGuideActive;
    [SerializeField] bool debugTracking;
    [SerializeField] int debugGuideRendererCount;
    [SerializeField] float debugDwellElapsed;
    [SerializeField] string debugSequenceStep;
    [SerializeField] bool debugLeftEquipped;
    [SerializeField] bool debugCompleteSideCalled;

    [SerializeField, HideInInspector] int placementSetupVersion;

    readonly List<GrabBinding> bindings = new List<GrabBinding>();
    readonly List<ActiveGuide> activeGuides = new List<ActiveGuide>();
    PPEHandGrabbable activeGrabbable;
    PPEEquipable activeEquipable;
    PPEGlovePairMember activeGloveMember;
    PPEEquipSlot readyCandidateSlot = PPEEquipSlot.None;
    float readyCandidateSince = -1f;
    bool visualReadyLatched;
    Transform debugEnterSphere;
    Transform debugExitSphere;

    public event Action<PPEEquipSlot, PPEPlacementGuideState> GuideStateChanged;

    public PPEEquipable ActivePpe => activeEquipable;
    public bool IsGuideVisible => activeGuides.Count > 0;
    public Material NotReadyMaterial => notReadyMaterial;
    public Material ReadyMaterial => readyMaterial;
    public bool UsePpeVisualFallback => usePpeVisualWhenGuidePrefabMissing;
    public bool AutoEquipEnabled => autoEquipEnabled;
    public float ReadyHoldDuration => readyHoldDuration;
    public float BootGuideLookDownAngle => bootGuideLookDownAngle;
    public int PlacementSetupVersion => placementSetupVersion;
    public PPEEquipSlot ReadyCandidateSlot => readyCandidateSlot;
    public float CurrentDistance { get; private set; } = float.PositiveInfinity;
    public float CurrentAngle { get; private set; } = float.PositiveInfinity;
    public string DebugStatus => debugStatus;
    public bool ShowEquipDebugVisuals
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return showEquipDebugVisuals;
#else
            return false;
#endif
        }
    }

    string lastLoggedDebugKey;
    TextMeshPro overlayLabel;

    void OnEnable()
    {
        ResolveAnchorSet();
        ResolveBodyReference();
        if (bodyReference != null)
            bodyReference.EnsureActiveInPpeScene();
        BindGrabbables();
    }

    void OnDisable()
    {
        UnbindGrabbables();
        ChemicalGlovesWearableVisual.FindActive()?.CancelTargetGhostPresentations();
        HideGuides();
    }

    void LateUpdate()
    {
        ResolveBodyReference();
        if (bodyReference != null)
            bodyReference.EnsureActiveInPpeScene();

        if (!autoEquipEnabled || activeGrabbable == null || activeEquipable == null ||
            !activeGrabbable.IsGrabbed)
        {
            if (activeGloveMember != null && (activeGrabbable == null || !activeGrabbable.IsGrabbed))
                SetTargetHandGhostPresentation(activeGloveMember, false);
            ResetReadyCandidate();
            ClearDebugIfIdle();
            UpdateInnerGloveDebugSpheres();
            UpdateDebugOverlay();
            return;
        }

        EvaluateHeldPlacement();
        RefreshActiveGuideCalibrationPoses();
        if (!IsActiveInnerGloves())
        {
            UpdateInnerGloveDebugSpheres();
            UpdateDebugOverlay();
        }
    }

    void EvaluateHeldPlacement()
    {
        var holdingHand = activeGrabbable.CurrentHand;
        var wrongHand = activeGloveMember != null && activeGloveMember.IsSameHandGrab(holdingHand);
        var targetValid = !wrongHand && HasLiveTarget();
        var useRotation = activeGloveMember != null
            ? activeGloveMember.UseRotationValidation
            : activeEquipable.UseRotationValidation;
        var maxDistance = activeGloveMember != null
            ? activeGloveMember.PlacementDistance
            : activeEquipable.EquipDistance;
        var maxAngle = activeGloveMember != null
            ? activeGloveMember.PlacementAngleTolerance
            : activeEquipable.EquipAngleTolerance;

        var placementRef = ResolvePlacementReference();
        var sequenceValid = IsSequenceValid(activeEquipable.ItemId);

        if (IsActiveInnerGloves())
        {
            EvaluateInnerGloveGhostVisibilityOnly(wrongHand, targetValid);
            return;
        }

        if (wrongHand)
        {
            SetTargetHandGhostPresentation(activeGloveMember, false);
            SetActiveGuidesVisible(false);
            SetAllGuidesReady(false);
            ResetReadyCandidate(keepDebug: true);
            PublishDebug(
                "WRONG HAND",
                holdingHand,
                false,
                float.PositiveInfinity,
                maxDistance,
                float.PositiveInfinity,
                maxAngle,
                false,
                !useRotation,
                sequenceValid,
                false,
                useRotation);
            return;
        }

        if (!targetValid)
        {
            SetTargetHandGhostPresentation(activeGloveMember, false);
            if (IsBodyWornGuide())
            {
                SetActiveGuidesVisible(ShouldShowGuideVisual());
                SetAllGuidesReady(false);
            }
            else
            {
                SetActiveGuidesVisible(false);
                SetAllGuidesReady(false);
            }

            ResetReadyCandidate(keepDebug: true);
            PublishDebug(
                "TRACKING LOST",
                holdingHand,
                false,
                float.PositiveInfinity,
                maxDistance,
                float.PositiveInfinity,
                maxAngle,
                false,
                !useRotation,
                sequenceValid,
                false,
                useRotation);
            return;
        }

        if (activeGuides.Count == 0)
        {
            ResetReadyCandidate(keepDebug: true);
            PublishDebug(
                "TOO FAR",
                holdingHand,
                true,
                float.PositiveInfinity,
                maxDistance,
                float.PositiveInfinity,
                maxAngle,
                false,
                !useRotation,
                sequenceValid,
                false,
                useRotation);
            return;
        }

        var enterDistance = IsActiveInnerGloves() ? InnerGloveReadyEnterDistance : maxDistance;
        var exitDistance = IsActiveInnerGloves()
            ? InnerGloveReadyExitDistance
            : maxDistance + readyExitDistanceExtra;

        SetTargetHandGhostPresentation(activeGloveMember, true);
        SetActiveGuidesVisible(ShouldShowGuideVisual());

        ActiveGuide firstReady = null;
        var nearestDistance = float.PositiveInfinity;
        var nearestAngle = float.PositiveInfinity;
        var nearestDistanceValid = false;
        var nearestAngleValid = false;

        for (var i = 0; i < activeGuides.Count; i++)
        {
            var guide = activeGuides[i];
            if (guide == null || guide.Anchor == null)
                continue;

            var sampleFrom = placementRef != null ? placementRef : activeGrabbable.transform;
            var evaluation = PPEPlacementValidation.Evaluate(
                sampleFrom,
                guide.Anchor,
                targetValid: true,
                sequenceValid: sequenceValid,
                maxDistance: visualReadyLatched ? exitDistance : enterDistance,
                useRotationValidation: useRotation,
                maxAngle: maxAngle);
            var distance = evaluation.Distance;
            var angle = evaluation.Angle;
            var distanceValid = evaluation.DistanceValid;
            var angleValid = evaluation.AngleValid;
            var placementValid = evaluation.PoseValid;

            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestAngle = angle;
                nearestDistanceValid = distanceValid;
                nearestAngleValid = angleValid;
            }

            SetGuideState(guide, placementValid ? PPEPlacementGuideState.Ready : PPEPlacementGuideState.NotReady);
            if (placementValid && firstReady == null)
                firstReady = guide;
        }

        visualReadyLatched = firstReady != null;

        CurrentDistance = nearestDistance;
        CurrentAngle = nearestAngle;

        var readyVisual = firstReady != null;
        string status;
        if (!nearestDistanceValid)
            status = "TOO FAR";
        else if (useRotation && !nearestAngleValid)
            status = "ANGLE";
        else if (!sequenceValid)
            status = "SEQUENCE BLOCKED";
        else
            status = "READY";

        PublishDebug(
            status,
            holdingHand,
            true,
            nearestDistance,
            enterDistance,
            nearestAngle,
            maxAngle,
            nearestDistanceValid,
            nearestAngleValid,
            sequenceValid,
            readyVisual,
            useRotation);

        if (!readyVisual)
        {
            ResetReadyCandidate(keepDebug: true);
            return;
        }

        if (UsesReleaseToEquip())
            return;

        if (readyCandidateSlot != firstReady.Slot)
        {
            readyCandidateSlot = firstReady.Slot;
            readyCandidateSince = Time.time;
            return;
        }

        if (!sequenceValid)
            return;

        if (Time.time - readyCandidateSince < readyHoldDuration)
            return;

        TryEquipAtAnchor(firstReady.Anchor);
    }

    Transform ResolvePlacementReference()
    {
        if (activeGloveMember != null)
            return activeGloveMember.EnsureEquipReference();
        if (activeEquipable != null)
            return activeEquipable.EnsurePlacementReference();
        return activeGrabbable != null ? activeGrabbable.transform : null;
    }

    bool IsSequenceValid(string itemId)
    {
        var sequence = PPESequenceManager.Resolve();
        return sequence == null || sequence.CanEquip(itemId);
    }

    void PublishDebug(
        string status,
        PPEGrabHand holdingHand,
        bool targetValid,
        float distance,
        float maxDistance,
        float angle,
        float maxAngle,
        bool distanceValid,
        bool angleValid,
        bool sequenceValid,
        bool ready,
        bool useRotation)
    {
        var itemLabel = BuildItemLabel();
        var targetLabel = BuildTargetLabel();
        debugStatus = status;
        debugItem = itemLabel;
        debugHoldingHand = holdingHand.ToString();
        debugTarget = targetLabel;
        debugTargetValid = targetValid;
        debugDistance = distance;
        debugMaxDistance = maxDistance;
        debugAngle = angle;
        debugMaxAngle = maxAngle;
        debugDistanceValid = distanceValid;
        debugAngleValid = angleValid;
        debugSequenceValid = sequenceValid;
        debugReady = ready;
        debugUseRotationValidation = useRotation;
        debugGuideActive = activeGuides.Count > 0 &&
            activeGuides[0] != null &&
            activeGuides[0].Root != null &&
            activeGuides[0].Root.activeInHierarchy;
        debugTracking = targetValid;
        debugGuideRendererCount = 0;
        for (var i = 0; i < activeGuides.Count; i++)
        {
            if (activeGuides[i] != null && activeGuides[i].Renderers != null)
                debugGuideRendererCount += activeGuides[i].Renderers.Length;
        }

        debugDwellElapsed = readyCandidateSince >= 0f
            ? Mathf.Max(0f, Time.time - readyCandidateSince)
            : 0f;
        var sequence = PPESequenceManager.Resolve();
        debugSequenceStep = sequence != null && sequence.CurrentRequiredStep != null
            ? sequence.CurrentRequiredStep.Value.ToString()
            : "(none)";
        var coordinator = activeGloveMember != null ? activeGloveMember.Coordinator : null;
        debugLeftEquipped = coordinator != null && coordinator.LeftEquipped;
        debugCompleteSideCalled = coordinator != null && coordinator.DebugCompleteSideCalled;

        if (!logPlacementDebugOnChange || (!Application.isEditor && !Debug.isDebugBuild))
            return;

        var key = string.Join("|",
            status,
            itemLabel,
            holdingHand,
            targetValid,
            distanceValid,
            angleValid,
            sequenceValid,
            ready,
            distance.ToString("0.000"),
            angle.ToString("0.0"));
        if (key == lastLoggedDebugKey)
            return;

        lastLoggedDebugKey = key;
        Debug.Log(
            "[PPE Placement]\n" +
            "Item: " + itemLabel + "\n" +
            "HoldingHand: " + holdingHand + "\n" +
            "Target: " + targetLabel + "\n" +
            "TargetValid: " + targetValid + "\n\n" +
            "Distance: " + (float.IsInfinity(distance) ? "inf" : distance.ToString("0.000")) + "\n" +
            "MaxDistance: " + maxDistance.ToString("0.000") + "\n\n" +
            "Angle: " + (float.IsInfinity(angle) ? "inf" : angle.ToString("0.0")) + "\n" +
            "MaxAngle: " + maxAngle.ToString("0.0") + "\n" +
            "UseRotationValidation: " + useRotation + "\n\n" +
            "DistanceValid: " + distanceValid + "\n" +
            "AngleValid: " + angleValid + "\n" +
            "SequenceValid: " + sequenceValid + "\n\n" +
            "READY: " + ready + "\n" +
            "Status: " + status,
            this);
    }

    string BuildItemLabel()
    {
        if (activeGloveMember != null && activeEquipable != null)
            return activeEquipable.ItemId + " " + activeGloveMember.GloveSide;
        return activeEquipable != null ? activeEquipable.ItemId : "(none)";
    }

    string BuildTargetLabel()
    {
        if (activeGloveMember == null)
        {
            if (activeEquipable == null)
                return "(none)";
            if (activeEquipable.UsesSlot(PPEEquipSlot.Face))
                return "HMD Face";
            if (activeEquipable.UsesSlot(PPEEquipSlot.Body))
                return "Dynamic Torso";
            if (activeEquipable.UsesSlot(PPEEquipSlot.Head))
                return "HMD Head";
            return activeEquipable.EquipSlots.ToString();
        }

        if (activeGloveMember.TargetKind == PPEPairTargetKind.BodyLowerLeg)
            return activeGloveMember.GloveSide == PPEGrabHand.Right
                ? "Right LowerLeg"
                : "Left LowerLeg";

        return activeGloveMember.GloveSide == PPEGrabHand.Right
            ? "Right XR Palm"
            : "Left XR Palm";
    }

    void ClearDebugIfIdle()
    {
        if (debugStatus == "IDLE")
            return;
        debugStatus = "IDLE";
        debugItem = string.Empty;
        debugReady = false;
        debugDwellElapsed = 0f;
        lastLoggedDebugKey = null;
    }

    bool ShouldShowDebugOverlay()
    {
        return ShowEquipDebugVisuals && showPlacementDebugOverlay;
    }

    bool UsesReleaseToEquip()
    {
        if (activeGloveMember != null)
            return true;
        return PPEReleaseEquip.IsWearableReleaseItem(activeEquipable);
    }

    public bool TryGetReleaseTarget(PPEHandGrabbable grabbable, out Transform target, out string label)
    {
        target = null;
        label = "None";
        if (grabbable == null || activeGrabbable != grabbable)
            return false;

        if (activeGloveMember != null)
        {
            target = activeGloveMember.TargetTransform;
            label = BuildReleaseTargetLabel();
            return target != null;
        }

        if (SuppressesGhostVisual(activeEquipable))
        {
            target = ResolveInvisibleReleaseTarget(activeEquipable);
            label = BuildReleaseTargetLabel();
            return target != null;
        }

        for (var i = 0; i < activeGuides.Count; i++)
        {
            var guide = activeGuides[i];
            if (guide == null || guide.Anchor == null)
                continue;
            target = guide.Anchor;
            label = BuildReleaseTargetLabel();
            return true;
        }

        return false;
    }

    string BuildReleaseTargetLabel()
    {
        if (activeGloveMember != null)
        {
            if (activeGloveMember.TargetKind == PPEPairTargetKind.BodyLowerLeg)
                return activeGloveMember.GloveSide == PPEGrabHand.Right ? "RightLowerLeg" : "LeftLowerLeg";
            return activeGloveMember.GloveSide == PPEGrabHand.Right ? "RightHand" : "LeftHand";
        }

        if (activeEquipable != null && activeEquipable.UsesSlot(PPEEquipSlot.Face))
            return "Face";
        if (activeEquipable != null && activeEquipable.UsesSlot(PPEEquipSlot.Body))
            return "Torso";
        if (activeEquipable != null && activeEquipable.UsesSlot(PPEEquipSlot.Head))
            return "Head";
        return BuildTargetLabel();
    }

    public void EquipAfterRelease(PPEEquipable item)
    {
        StartCoroutine(EquipAfterReleaseRoutine(item));
    }

    IEnumerator EquipAfterReleaseRoutine(PPEEquipable item)
    {
        yield return null;
        HideActiveGuides();
        if (item == null)
            yield break;
        var manager = PPEEquipmentManager.Instance != null
            ? PPEEquipmentManager.Instance
            : FindAnyObjectByType<PPEEquipmentManager>();
        if (manager != null && manager.Equip(item))
            AttachEquippedHelmetToHead(item);
    }

    void AttachEquippedHelmetToHead(PPEEquipable item)
    {
        if (item == null ||
            item.DeactivateOnEquip ||
            !string.Equals(item.ItemId, PPEEquipmentManager.IdHelmet, StringComparison.Ordinal))
            return;

        ResolveBodyReference();
        ResolveAnchorSet();
        var anchors = anchorSet != null ? anchorSet : (bodyReference != null ? bodyReference.AnchorSet : null);
        var head = anchors != null ? anchors.Head : null;
        if (head == null)
            return;

        // Preserve the validated release pose while making the worn Helmet follow
        // the live HMD-driven Head target. No visible target or Helmet Ghost is created.
        item.transform.SetParent(head, true);
    }

    string BuildOverlayText()
    {
        var distance = float.IsInfinity(debugDistance) ? "inf" : debugDistance.ToString("0.000");
        var enter = debugMaxDistance > 0f ? debugMaxDistance.ToString("0.000") : "0.120";
        return
            "Held: " + (string.IsNullOrEmpty(debugItem) ? "(none)" : debugItem) + "\n" +
            "Holding: " + debugHoldingHand + "\n" +
            "Target: " + debugTarget + "\n" +
            "Tracking: " + (debugTracking ? "TRUE" : "FALSE") + "\n" +
            "Distance: " + distance + " / " + enter + "\n" +
            "Sequence: " + debugSequenceStep + " / " + (debugSequenceValid ? "TRUE" : "FALSE") + "\n" +
            "Guide: " + (debugGuideActive ? "ACTIVE" : "HIDDEN") + "\n" +
            "Placement: " + debugStatus + "\n" +
            "Dwell: " + debugDwellElapsed.ToString("0.00") + " → " + readyHoldDuration.ToString("0.00") + "\n" +
            "LeftEquipped: " + (debugLeftEquipped ? "TRUE" : "FALSE") + "\n" +
            "CompleteSide Called: " + (debugCompleteSideCalled ? "TRUE" : "FALSE");
    }

    void UpdateDebugOverlay()
    {
        if (!ShouldShowDebugOverlay() || debugStatus == "IDLE")
        {
            if (overlayLabel != null && overlayLabel.gameObject.activeSelf)
                overlayLabel.gameObject.SetActive(false);
            return;
        }

        EnsureOverlayLabel();
        if (overlayLabel == null)
            return;

        overlayLabel.gameObject.SetActive(true);
        overlayLabel.text = BuildOverlayText();
        overlayLabel.color = debugReady ? new Color(0.2f, 1f, 0.4f, 1f) : new Color(1f, 0.85f, 0.2f, 1f);

        var cam = bodyReference != null ? bodyReference.XrCamera : null;
        if (cam == null && Camera.main != null)
            cam = Camera.main.transform;
        if (cam == null)
            return;

        overlayLabel.transform.position = cam.position + cam.forward * 1.1f + cam.up * 0.22f;
        overlayLabel.transform.rotation = Quaternion.LookRotation(overlayLabel.transform.position - cam.position, Vector3.up);
    }

    void EnsureOverlayLabel()
    {
        if (overlayLabel != null)
            return;

        var go = new GameObject("PPE_PlacementDebugOverlay");
        go.transform.SetParent(transform, false);
        go.transform.localScale = Vector3.one;
        overlayLabel = go.AddComponent<TextMeshPro>();
        overlayLabel.fontSize = 0.11f;
        overlayLabel.alignment = TextAlignmentOptions.TopLeft;
        overlayLabel.rectTransform.sizeDelta = new Vector2(1.7f, 1.35f);
        overlayLabel.textWrappingMode = TextWrappingModes.NoWrap;
        overlayLabel.raycastTarget = false;
    }

    void UpdateInnerGloveDebugSpheres()
    {
        if (!ShouldShowDebugOverlay() || !IsActiveInnerGloves() || activeGloveMember == null)
        {
            SetInnerGloveDebugSpheresVisible(false);
            return;
        }

        var target = activeGloveMember.TargetTransform;
        if (target == null)
        {
            SetInnerGloveDebugSpheresVisible(false);
            return;
        }

        EnsureInnerGloveDebugSpheres();
        if (debugEnterSphere == null || debugExitSphere == null)
            return;

        debugEnterSphere.gameObject.SetActive(true);
        debugExitSphere.gameObject.SetActive(true);
        debugEnterSphere.position = target.position;
        debugExitSphere.position = target.position;
        var ready = debugStatus == "READY";
        SetDebugSphereColor(debugEnterSphere, ready ? new Color(0.15f, 1f, 0.35f, 0.28f) : new Color(1f, 0.55f, 0.1f, 0.18f));
        SetDebugSphereColor(debugExitSphere, ready ? new Color(0.15f, 1f, 0.35f, 0.08f) : new Color(1f, 0.85f, 0.2f, 0.08f));
    }

    void EnsureInnerGloveDebugSpheres()
    {
        if (debugEnterSphere == null)
            debugEnterSphere = CreateDebugSphere("PPE_InnerGloveReadyEnter", InnerGloveReadyEnterDistance * 2f);
        if (debugExitSphere == null)
            debugExitSphere = CreateDebugSphere("PPE_InnerGloveReadyExit", InnerGloveReadyExitDistance * 2f);
    }

    Transform CreateDebugSphere(string objectName, float diameter)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = objectName;
        go.transform.SetParent(transform, false);
        UnityEngine.Object.Destroy(go.GetComponent<Collider>());
        go.transform.localScale = Vector3.one * diameter;
        var renderer = go.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                var material = new Material(shader);
                if (material.HasProperty("_Surface"))
                    material.SetFloat("_Surface", 1f);
                if (material.HasProperty("_BaseColor"))
                    material.SetColor("_BaseColor", new Color(1f, 0.6f, 0.1f, 0.2f));
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.renderQueue = 3000;
                renderer.sharedMaterial = material;
            }
        }

        return go.transform;
    }

    static void SetDebugSphereColor(Transform sphere, Color color)
    {
        if (sphere == null)
            return;
        var renderer = sphere.GetComponent<Renderer>();
        if (renderer == null || renderer.sharedMaterial == null)
            return;
        var material = renderer.sharedMaterial;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
    }

    void SetInnerGloveDebugSpheresVisible(bool visible)
    {
        if (debugEnterSphere != null)
            debugEnterSphere.gameObject.SetActive(visible);
        if (debugExitSphere != null)
            debugExitSphere.gameObject.SetActive(visible);
    }

    /// <summary>Rebuilds subscriptions, for example after spawning PPE at runtime.</summary>
    public void RefreshBindings()
    {
        UnbindGrabbables();
        BindGrabbables();
    }

    /// <summary>Changes every active guide without evaluating placement.</summary>
    public void SetAllGuidesReady(bool ready)
    {
        for (var i = 0; i < activeGuides.Count; i++)
            SetGuideState(activeGuides[i], ready ? PPEPlacementGuideState.Ready : PPEPlacementGuideState.NotReady);
    }

    /// <summary>Changes one concrete slot guide without evaluating placement.</summary>
    public bool SetSlotReady(PPEEquipSlot slot, bool ready)
    {
        var changed = false;
        for (var i = 0; i < activeGuides.Count; i++)
        {
            var guide = activeGuides[i];
            if (guide.Slot != slot)
                continue;
            SetGuideState(guide, ready ? PPEPlacementGuideState.Ready : PPEPlacementGuideState.NotReady);
            changed = true;
        }
        return changed;
    }

    void BindGrabbables()
    {
        var grabbables = FindObjectsByType<PPEHandGrabbable>(FindObjectsInactive.Include);
        foreach (var grabbable in grabbables)
        {
            if (grabbable == null)
                continue;
            if (grabbable.GetComponent<PPEEquipable>() == null &&
                grabbable.GetComponent<PPEGlovePairMember>() == null)
                continue;

            var captured = grabbable;
            var binding = new GrabBinding
            {
                Grabbable = captured,
                Grabbed = _ => ShowGuides(captured),
                Released = _ => HandleReleased(captured),
            };
            captured.OnGrabbed += binding.Grabbed;
            captured.OnReleased += binding.Released;
            bindings.Add(binding);
        }
    }

    void UnbindGrabbables()
    {
        foreach (var binding in bindings)
        {
            if (binding.Grabbable == null)
                continue;
            binding.Grabbable.OnGrabbed -= binding.Grabbed;
            binding.Grabbable.OnReleased -= binding.Released;
        }
        bindings.Clear();
    }

    void ShowGuides(PPEHandGrabbable grabbable)
    {
        if (activeGloveMember != null)
            SetTargetHandGhostPresentation(activeGloveMember, false);
        HideGuides();
        if (grabbable == null)
            return;

        ResolveAnchorSet();
        ResolveBodyReference();
        if (bodyReference != null)
            bodyReference.EnsureActiveInPpeScene();

        var gloveMember = grabbable.GetComponent<PPEGlovePairMember>();
        if (gloveMember != null)
        {
            ShowGloveMemberGuides(grabbable, gloveMember);
            PPEEquipGuidanceHints.NotifyGrabbed(
                gloveMember.LogicalEquipable,
                gloveMember,
                grabbable.CurrentHand);
            return;
        }

        if (anchorSet == null)
        {
            LogWearableGuideDiagnostics(grabbable, null, null, null, "anchorSet is null");
            return;
        }

        var equipable = grabbable.GetComponent<PPEEquipable>();
        if (equipable == null || equipable.EquipSlots == PPEEquipSlot.None)
        {
            LogWearableGuideDiagnostics(grabbable, equipable, null, null, "equipable missing or EquipSlots=None");
            return;
        }

        activeGrabbable = grabbable;
        activeEquipable = equipable;
        activeGloveMember = null;
        if (bodyReference != null)
            bodyReference.SetActiveFaceProfileFrom(equipable);
        activeEquipable.EnsurePlacementReference();

        if (SuppressesGhostVisual(equipable))
        {
            PPEEquipGuidanceHints.NotifyGrabbed(equipable, null);
            return;
        }

        foreach (var slot in ConcreteSlots)
        {
            if (!equipable.UsesSlot(slot) || !anchorSet.TryGetAnchor(slot, out var anchor))
                continue;

            var root = CreateGuide(equipable, anchor, slot, null);
            if (root == null)
            {
                LogWearableGuideDiagnostics(grabbable, equipable, anchor, null, "CreateGuide returned null for " + slot);
                continue;
            }

            var guide = new ActiveGuide
            {
                Slot = slot,
                Anchor = anchor,
                Root = root,
                Renderers = root.GetComponentsInChildren<Renderer>(true),
                State = PPEPlacementGuideState.NotReady,
            };
            activeGuides.Add(guide);
            ApplyGuideAppearance(guide, false);
            if (root.activeSelf != true)
                root.SetActive(true);
            GuideStateChanged?.Invoke(slot, PPEPlacementGuideState.NotReady);
            LogWearableGuideDiagnostics(grabbable, equipable, anchor, root, null);
        }

        PPEEquipGuidanceHints.NotifyGrabbed(equipable, null);
    }

    void ShowGloveMemberGuides(PPEHandGrabbable grabbable, PPEGlovePairMember member)
    {
        activeGrabbable = grabbable;
        activeGloveMember = member;
        activeEquipable = member.LogicalEquipable;
        ResolveBodyReference();
        if (bodyReference != null)
            bodyReference.EnsureActiveInPpeScene();
        if (activeEquipable == null)
        {
            if (member != null && member.IsInnerGlove)
                LogInnerOrGloveGrab(
                    grabbable,
                    member,
                    null,
                    member.IsSameHandGrab(grabbable.CurrentHand));
            return;
        }

        member.EnsureEquipReference();

        var wrongHand = member.IsSameHandGrab(grabbable.CurrentHand);
        var follower = member.TargetTransform != null
            ? member.TargetTransform.GetComponent<PPEGloveHandTargetFollower>()
            : null;
        if (follower != null)
            follower.SnapToTrackedSource();

        if (wrongHand)
        {
            LogInnerOrGloveGrab(grabbable, member, null, true);
            return;
        }

        if (member.IsInnerGlove && !member.IsOppositeHandGrab(grabbable.CurrentHand))
        {
            LogInnerOrGloveGrab(grabbable, member, null, false);
            return;
        }

        var anchor = member.TargetTransform;
        if (anchor == null)
        {
            LogInnerOrGloveGrab(grabbable, member, null, false);
            return;
        }

        var slot = member.TargetSlot;
        var root = CreateGuide(activeEquipable, anchor, slot, member);
        LogInnerOrGloveGrab(grabbable, member, root, false);
        if (root == null)
            return;

        var guide = new ActiveGuide
        {
            Slot = slot,
            Anchor = anchor,
            Root = root,
            Renderers = root.GetComponentsInChildren<Renderer>(true),
            State = PPEPlacementGuideState.NotReady,
        };
        activeGuides.Add(guide);
        ApplyGuideAppearance(guide, false);
        if (member.TargetKind == PPEPairTargetKind.BodyLowerLeg && !ShouldShowGuideVisual())
            guide.Root.SetActive(false);
        else if (guide.Root != null && !guide.Root.activeSelf)
            guide.Root.SetActive(true);
        SetTargetHandGhostPresentation(member, true);
        GuideStateChanged?.Invoke(slot, PPEPlacementGuideState.NotReady);
        if (member.TargetKind == PPEPairTargetKind.BodyLowerLeg)
            LogWearableGuideDiagnostics(grabbable, activeEquipable, anchor, root, null);
    }

    void HandleReleased(PPEHandGrabbable grabbable)
    {
        if (grabbable == activeGrabbable)
        {
            var releasedMember = activeGloveMember;
            HideGuides();
            SetTargetHandGhostPresentation(releasedMember, false);
        }
    }

    static void SetTargetHandGhostPresentation(PPEGlovePairMember member, bool visible)
    {
        if (member == null || !member.IsTrackedHandGlove)
            return;
        var item = member.LogicalEquipable;
        if (item == null)
            return;
        var isSupportedGlove =
            string.Equals(item.ItemId, PPEEquipmentManager.IdInnerGloves, StringComparison.Ordinal) ||
            string.Equals(item.ItemId, PPEEquipmentManager.IdChemicalGloves, StringComparison.Ordinal);
        if (!isSupportedGlove)
            return;
        ChemicalGlovesWearableVisual.FindActive()?.SetTargetGhostPresentation(member.GloveSide, visible);
    }

    GameObject CreateGuide(
        PPEEquipable equipable,
        Transform anchor,
        PPEEquipSlot slot,
        PPEGlovePairMember gloveMember)
    {
        if (gloveMember == null && SuppressesGhostVisual(equipable))
            return null;

        GameObject root;
        var dedicatedGuide = gloveMember != null ? gloveMember.GuidePrefab : null;
        if (dedicatedGuide == null && equipable != null)
            dedicatedGuide = equipable.GuidePrefab;

        if (dedicatedGuide != null)
        {
            root = Instantiate(dedicatedGuide, anchor);
            root.name = $"PPE_Guide_{equipable.ItemId}_{slot}";
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
        }
        else if (usePpeVisualWhenGuidePrefabMissing)
        {
            var source = gloveMember != null ? gloveMember.VisualRoot.gameObject : equipable.gameObject;
            root = BuildRendererOnlyGhost(source, anchor, slot, gloveMember);
        }
        else
        {
            return null;
        }

        ApplyGuideLocalPose(root, equipable, gloveMember);
        if (gloveMember != null && gloveMember.TargetKind == PPEPairTargetKind.TrackedHands)
            SanitizeGloveGuideVisualOnly(root);
        SanitizeGuideRenderers(root);

        SetLayerRecursively(root.transform, LayerMask.NameToLayer("Default") >= 0 ? 0 : anchor.gameObject.layer);
        return root;
    }

    static GameObject BuildRendererOnlyGhost(
        GameObject source,
        Transform anchor,
        PPEEquipSlot slot,
        PPEGlovePairMember gloveMember)
    {
        var root = new GameObject($"PPE_Guide_{source.name}_{slot}");
        root.transform.SetParent(anchor, false);
        var visualParent = root.transform;
        if (gloveMember != null && gloveMember.IsTrackedHandGlove)
        {
            var visualOrigin = new GameObject(PPEGlovePairMember.GhostVisualOriginName);
            visualOrigin.transform.SetParent(root.transform, false);
            visualParent = visualOrigin.transform;
        }

        visualParent.localScale = source.transform.lossyScale;
        CopyVisualComponents(source.transform, visualParent);

        foreach (Transform child in source.transform)
            CloneVisualHierarchy(child, visualParent);
        return root;
    }

    static void CloneVisualHierarchy(Transform source, Transform parent)
    {
        var clone = new GameObject(source.name);
        clone.transform.SetParent(parent, false);
        clone.transform.localPosition = source.localPosition;
        clone.transform.localRotation = source.localRotation;
        clone.transform.localScale = source.localScale;
        clone.SetActive(source.gameObject.activeSelf);
        CopyVisualComponents(source, clone.transform);

        foreach (Transform child in source)
            CloneVisualHierarchy(child, clone.transform);
    }

    static void CopyVisualComponents(Transform source, Transform destination)
    {
        var sourceFilter = source.GetComponent<MeshFilter>();
        var sourceRenderer = source.GetComponent<MeshRenderer>();
        if (sourceFilter != null && sourceRenderer != null && sourceFilter.sharedMesh != null)
        {
            AddGhostMesh(destination.gameObject, sourceFilter.sharedMesh, sourceRenderer.sharedMaterials);
            return;
        }

        // ProtectiveGloves use SkinnedMeshRenderer. Copy shared bind-pose mesh so
        // the ghost matches held size without bones or per-frame BakeMesh.
        var skin = source.GetComponent<SkinnedMeshRenderer>();
        if (skin != null && skin.sharedMesh != null)
            AddGhostMesh(destination.gameObject, skin.sharedMesh, skin.sharedMaterials);
    }

    static void AddGhostMesh(GameObject destination, Mesh mesh, Material[] materials)
    {
        if (destination.GetComponent<MeshFilter>() != null)
            return;

        destination.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = destination.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = materials;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    void SetGuideState(ActiveGuide guide, PPEPlacementGuideState state)
    {
        if (guide == null || guide.Root == null || guide.State == state)
            return;
        guide.State = state;
        ApplyGuideAppearance(guide, state == PPEPlacementGuideState.Ready);
        GuideStateChanged?.Invoke(guide.Slot, state);
    }

    void ApplyGuideAppearance(ActiveGuide guide, bool ready)
    {
        if (guide == null)
            return;

        if (activeGloveMember != null &&
            activeGloveMember.TargetKind == PPEPairTargetKind.TrackedHands)
        {
            ApplyGloveGhostMaterials(guide, ready);
            return;
        }

        ApplyMaterial(guide.Renderers, ready ? readyMaterial : notReadyMaterial);
    }

    void ApplyGloveGhostMaterials(ActiveGuide guide, bool ready)
    {
        if (guide.Renderers == null)
            return;

        EnsureGloveMaterialCache(guide);
        var cached = ready ? guide.CachedReadyMaterials : guide.CachedNotReadyMaterials;
        var renderers = guide.Renderers;
        if (cached == null)
            return;

        for (var i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null)
                continue;

            renderer.enabled = true;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterials = new[] { cached[i] };
        }
    }

    void EnsureGloveMaterialCache(ActiveGuide guide)
    {
        if (guide.CachedNotReadyMaterials != null && guide.CachedReadyMaterials != null)
            return;

        DestroyGuideMaterials(guide);
        var softBlue = new Color(0.32f, 0.72f, 1f, 1f);
        guide.CachedNotReadyMaterials = CloneGhostMaterials(
            guide.Renderers, notReadyMaterial, softBlue, 0.26f, false);
        guide.CachedReadyMaterials = CloneGhostMaterials(
            guide.Renderers, notReadyMaterial, softBlue, 0.40f, false);
    }

    static Material[] CloneGhostMaterials(
        Renderer[] renderers,
        Material template,
        Color fillColor,
        float alpha,
        bool doubleSided)
    {
        if (renderers == null || template == null)
            return null;

        var result = new Material[renderers.Length];
        for (var i = 0; i < renderers.Length; i++)
        {
            var material = new Material(template);
            if (doubleSided && material.HasProperty("_Cull"))
                material.SetFloat("_Cull", 0f);
            if (material.HasProperty("_EmissionColor"))
                material.SetColor("_EmissionColor", Color.black);
            material.DisableKeyword("_EMISSION");
            if (material.HasProperty("_OutlineWidth"))
                material.SetFloat("_OutlineWidth", 0f);
            if (material.HasProperty("_OutlineColor"))
                material.SetColor("_OutlineColor", Color.clear);
            if (material.HasProperty("_RimColor"))
                material.SetColor("_RimColor", Color.clear);
            if (material.HasProperty("_RimPower"))
                material.SetFloat("_RimPower", 0f);
            if (material.HasProperty("_FresnelPower"))
                material.SetFloat("_FresnelPower", 0f);
            if (material.HasProperty("_BaseColor"))
            {
                var color = fillColor;
                color.a = alpha;
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                var color = fillColor;
                color.a = alpha;
                material.SetColor("_Color", color);
            }

            result[i] = material;
        }

        return result;
    }

    static void DestroyGuideMaterials(ActiveGuide guide)
    {
        if (guide == null)
            return;
        DestroyMaterialArray(guide.CachedNotReadyMaterials);
        DestroyMaterialArray(guide.CachedReadyMaterials);
        DestroyMaterialArray(guide.CachedStaticMaterials);
        guide.CachedNotReadyMaterials = null;
        guide.CachedReadyMaterials = null;
        guide.CachedStaticMaterials = null;
        guide.CachedStaticBaseAlphas = null;
    }

    static void DestroyMaterialArray(Material[] materials)
    {
        if (materials == null)
            return;
        for (var i = 0; i < materials.Length; i++)
        {
            if (materials[i] != null)
                Destroy(materials[i]);
        }
    }

    void LogInnerOrGloveGrab(
        PPEHandGrabbable grabbable,
        PPEGlovePairMember member,
        GameObject guideRoot,
        bool wrongHand)
    {
        if (member != null && member.IsInnerGlove)
        {
            PPEInnerGloveRuntimeLog.LogGrab(grabbable, member, guideRoot, wrongHand);
            return;
        }

        LogGloveGrabDiagnostics(grabbable, member, guideRoot, wrongHand);
    }

    void LogGloveGrabDiagnostics(
        PPEHandGrabbable grabbable,
        PPEGlovePairMember member,
        GameObject guideRoot,
        bool wrongHand)
    {
        var follower = member != null && member.TargetTransform != null
            ? member.TargetTransform.GetComponent<PPEGloveHandTargetFollower>()
            : null;
        var renderers = guideRoot != null ? guideRoot.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
        var sequence = member != null && member.LogicalEquipable != null &&
                       IsSequenceValid(member.LogicalEquipable.ItemId);

        var sequenceManager = PPESequenceManager.Resolve();
        var required = sequenceManager != null ? sequenceManager.CurrentRequiredStep : null;
        var logical = member != null ? member.LogicalEquipable : null;
        var coordinator = member != null ? member.Coordinator : null;
        var equipRef = member != null ? member.EnsureEquipReference() : null;
        var palm = member != null ? member.TargetTransform : null;
        var distance = equipRef != null && palm != null
            ? Vector3.Distance(equipRef.position, palm.position)
            : float.PositiveInfinity;

        Debug.Log(
            "[PPE Glove Grab]\n" +
            "Physical Member: " + (member != null ? member.name : "(null)") + "\n" +
            "Side: " + (member != null ? member.GloveSide.ToString() : "(null)") + "\n" +
            "Coordinator: " + (coordinator != null ? coordinator.name : "(null)") + "\n" +
            "LogicalItemId: " + (logical != null ? logical.ItemId : "(null)") + "\n" +
            "Selecting Interactor Hand: " + (grabbable != null ? grabbable.CurrentHand.ToString() : "(null)") + "\n" +
            "Holding Hand: " + (grabbable != null ? grabbable.CurrentHand.ToString() : "(null)") + "\n" +
            "Expected Target Hand: " + (member != null ? member.TargetHand.ToString() : "(null)") + "\n" +
            "Target Follower Found: " + (follower != null) + "\n" +
            "Target Tracking Valid: " + (follower != null && follower.IsTracking) + "\n" +
            "Guide Created: " + (guideRoot != null) + "\n" +
            "Guide Active: " + (guideRoot != null && guideRoot.activeInHierarchy) + "\n" +
            "Guide Renderer Count: " + renderers.Length + "\n" +
            "Guide World Position: " + (guideRoot != null ? guideRoot.transform.position.ToString("F3") : "(none)") + "\n" +
            "Guide World Scale: " + (guideRoot != null ? guideRoot.transform.lossyScale.ToString("F3") : "(none)") + "\n" +
            "EquipReference: " + (equipRef != null ? equipRef.name : "(none)") + "\n" +
            "EquipReference World: " + (equipRef != null ? equipRef.position.ToString("F3") : "(none)") + "\n" +
            "Target Palm World: " + (palm != null ? palm.position.ToString("F3") : "(none)") + "\n" +
            "Current Distance: " + (float.IsInfinity(distance) ? "inf" : distance.ToString("0.000")) + "\n" +
            "Enter Distance: 0.120\n" +
            "Sequence CurrentRequiredStep: " + (required != null ? required.Value.ToString() : "(none)") + "\n" +
            "Sequence CanEquip(InnerGloves): " + sequence + "\n" +
            "Wrong Hand: " + wrongHand + "\n" +
            "Sequence Valid: " + sequence,
            member != null ? member : this);
    }

    static void ApplyMaterial(Renderer[] renderers, Material material)
    {
        if (material == null || renderers == null)
            return;

        foreach (var renderer in renderers)
        {
            if (renderer == null)
                continue;
            var count = Mathf.Max(1, renderer.sharedMaterials.Length);
            var materials = new Material[count];
            for (var i = 0; i < count; i++)
                materials[i] = material;
            renderer.sharedMaterials = materials;
        }
    }

    void HideGuides()
    {
        foreach (var guide in activeGuides)
        {
            DestroyGuideMaterials(guide);
            if (guide.Root != null)
                Destroy(guide.Root);
        }
        activeGuides.Clear();
        activeGrabbable = null;
        activeEquipable = null;
        activeGloveMember = null;
        visualReadyLatched = false;
        if (bodyReference != null)
            bodyReference.ClearActiveFaceProfile();
        ResetReadyCandidate();
        SetInnerGloveDebugSpheresVisible(false);
    }

    public void HideActiveGuides()
    {
        HideGuides();
    }

    void EvaluateInnerGloveGhostVisibilityOnly(bool wrongHand, bool targetValid)
    {
        if (activeGloveMember != null &&
            activeGloveMember.DevelopmentRuntimeCalibrationMode &&
            targetValid &&
            activeGuides.Count > 0)
        {
            SetActiveGuidesVisible(true);
            return;
        }

        if (wrongHand || !targetValid || activeGuides.Count == 0)
        {
            SetActiveGuidesVisible(false);
            SetAllGuidesReady(false);
            return;
        }

        SetActiveGuidesVisible(true);
    }

    public void SetInnerGloveGhostDisplay(bool visible, bool ready)
    {
        if (!IsActiveInnerGloves())
            return;
        SetActiveGuidesVisible(visible && activeGuides.Count > 0);
        SetAllGuidesReady(ready);
    }

    bool IsActiveInnerGloves()
    {
        return activeGloveMember != null && activeGloveMember.IsInnerGlove;
    }

    bool HasLiveTarget()
    {
        if (activeGloveMember != null)
            return activeGloveMember.IsTargetTracking;
        return bodyReference == null || bodyReference.HasTracking;
    }

    bool ShouldShowGuideVisual()
    {
        if (activeGloveMember != null &&
            activeGloveMember.TargetKind == PPEPairTargetKind.BodyLowerLeg)
        {
            ResolveBodyReference();
            if (bodyReference == null)
                return true;
            return bodyReference.IsLookingDown(bootGuideLookDownAngle);
        }

        return true;
    }

    bool IsBodyWornGuide()
    {
        if (activeGloveMember != null)
            return activeGloveMember.TargetKind == PPEPairTargetKind.BodyLowerLeg;
        return true;
    }

    void LogWearableGuideDiagnostics(
        PPEHandGrabbable grabbable,
        PPEEquipable equipable,
        Transform target,
        GameObject guideRoot,
        string failure)
    {
        var itemId = equipable != null ? equipable.ItemId : "(none)";
        var source = activeGloveMember != null && activeGloveMember.VisualRoot != null
            ? activeGloveMember.VisualRoot.gameObject
            : equipable != null ? equipable.gameObject : null;
        var sourceRenderers = source != null
            ? source.GetComponentsInChildren<Renderer>(true)
            : Array.Empty<Renderer>();
        var guideRenderers = guideRoot != null
            ? guideRoot.GetComponentsInChildren<Renderer>(true)
            : Array.Empty<Renderer>();
        var enabledRenderers = 0;
        for (var i = 0; i < guideRenderers.Length; i++)
        {
            if (guideRenderers[i] != null && guideRenderers[i].enabled)
                enabledRenderers++;
        }

        var lookingDown = bodyReference != null && bodyReference.IsLookingDown(bootGuideLookDownAngle);
        var targetKind = activeGloveMember != null
            ? activeGloveMember.TargetKind.ToString()
            : (equipable != null ? equipable.EquipSlots.ToString() : "none");

        Debug.Log(
            "[PPE Guide]\n" +
            "1 Grab event received: " + (grabbable != null && grabbable.IsGrabbed) + "\n" +
            "2 Guide creation called: " + (failure == null || guideRoot != null) + "\n" +
            "3 Source visual/renderer found: " + (source != null) +
            " count=" + sourceRenderers.Length +
            " name=" + (source != null ? source.name : "(null)") + "\n" +
            "4 Guide GameObject created: " + (guideRoot != null) +
            (guideRoot != null ? " name=" + guideRoot.name : "") + "\n" +
            "5 Guide renderer enabled: " + enabledRenderers + "/" + guideRenderers.Length + "\n" +
            "6 Target Transform resolved: " + (target != null) +
            (target != null ? " name=" + target.name : "") + "\n" +
            "7 TargetValid: " + (activeGloveMember != null
                ? activeGloveMember.IsTargetTracking.ToString()
                : (bodyReference != null && bodyReference.HasTracking).ToString()) + "\n" +
            "8 Guide parent/target: " + (guideRoot != null && guideRoot.transform.parent != null
                ? guideRoot.transform.parent.name
                : "(none)") + " kind=" + targetKind + "\n" +
            "9 Guide world position: " + (guideRoot != null
                ? guideRoot.transform.position.ToString("F3")
                : "(none)") + "\n" +
            "10 Guide active state: self=" + (guideRoot != null && guideRoot.activeSelf) +
            " hierarchy=" + (guideRoot != null && guideRoot.activeInHierarchy) + "\n" +
            "ItemId=" + itemId + "\n" +
            "BodyActive=" + (bodyReference != null && bodyReference.isActiveAndEnabled) + "\n" +
            "LookingDown=" + lookingDown + "\n" +
            "Failure=" + (failure ?? "none"),
            guideRoot != null ? guideRoot : this);
    }

    void SetActiveGuidesVisible(bool visible)
    {
        for (var i = 0; i < activeGuides.Count; i++)
        {
            var guide = activeGuides[i];
            if (guide == null || guide.Root == null)
                continue;
            if (guide.Root.activeSelf != visible)
                guide.Root.SetActive(visible);
        }
    }

    bool TryEquipAtAnchor(Transform anchor)
    {
        if (anchor == null || activeGrabbable == null || activeEquipable == null)
            return false;

        var manager = PPEEquipmentManager.Instance != null
            ? PPEEquipmentManager.Instance
            : FindAnyObjectByType<PPEEquipmentManager>();
        if (manager == null)
        {
            Debug.LogWarning("[PPE Placement] PPEEquipmentManager is missing; placement equip was skipped.", this);
            return false;
        }

        var grabbable = activeGrabbable;
        var equipable = activeEquipable;
        var gloveMember = activeGloveMember;
        if (gloveMember != null)
        {
            var completed = gloveMember.TryCompletePlacement(
                grabbable != null ? grabbable.CurrentHand : PPEGrabHand.Unknown);
            Debug.Log(
                "[PPE Placement] CompleteMemberSide attempted " + gloveMember.GloveSide +
                " result=" + completed +
                " holding=" + (grabbable != null ? grabbable.CurrentHand.ToString() : "null") +
                " tracking=" + gloveMember.IsTargetTracking,
                gloveMember);
            if (!completed)
                return false;
            HideGuides();
            return true;
        }

        if (!manager.Equip(equipable))
            return false;

        FinalizeEquippedPose(equipable, grabbable, anchor);
        return true;
    }

    static void FinalizeEquippedPose(
        PPEEquipable equipable,
        PPEHandGrabbable grabbable,
        Transform anchor)
    {
        if (equipable == null || anchor == null)
            return;

        var root = equipable.transform;
        var worldScale = root.lossyScale;

        var rigidbody = root.GetComponent<Rigidbody>();
        if (rigidbody != null)
        {
            rigidbody.linearVelocity = Vector3.zero;
            rigidbody.angularVelocity = Vector3.zero;
            rigidbody.useGravity = false;
            rigidbody.isKinematic = true;
            rigidbody.detectCollisions = false;
        }

        if (grabbable != null)
            grabbable.enabled = false;

        root.SetParent(anchor, false);
        root.localPosition = Vector3.zero;
        root.localRotation = Quaternion.identity;
        root.localScale = DivideScale(worldScale, anchor.lossyScale);
    }

    static Vector3 DivideScale(Vector3 worldScale, Vector3 parentScale)
    {
        return new Vector3(
            Mathf.Abs(parentScale.x) > 0.0001f ? worldScale.x / parentScale.x : worldScale.x,
            Mathf.Abs(parentScale.y) > 0.0001f ? worldScale.y / parentScale.y : worldScale.y,
            Mathf.Abs(parentScale.z) > 0.0001f ? worldScale.z / parentScale.z : worldScale.z);
    }

    void ResetReadyCandidate(bool keepDebug = false)
    {
        readyCandidateSlot = PPEEquipSlot.None;
        readyCandidateSince = -1f;
        visualReadyLatched = false;
        if (keepDebug)
            return;
        CurrentDistance = float.PositiveInfinity;
        CurrentAngle = float.PositiveInfinity;
    }

    void ResolveAnchorSet()
    {
        if (anchorSet == null)
            anchorSet = FindAnyObjectByType<PPEEquipmentAnchorSet>(FindObjectsInactive.Include);
    }

    void ResolveBodyReference()
    {
        if (bodyReference == null)
            bodyReference = FindAnyObjectByType<PPEPlayerBodyReference>(FindObjectsInactive.Include);
    }

    void ApplyGuideLocalPose(GameObject root, PPEEquipable equipable, PPEGlovePairMember gloveMember)
    {
        if (root == null)
            return;

        var localPosition = Vector3.zero;
        var localRotation = Quaternion.identity;

        if (gloveMember != null)
        {
            var calib = gloveMember.EnsureGhostPoseCalibration();
            if (gloveMember.IsTrackedHandGlove)
            {
                localPosition = calib.localPosition;
                var visualOrigin = root.transform.Find(PPEGlovePairMember.GhostVisualOriginName);
                if (visualOrigin != null)
                {
                    visualOrigin.localPosition = gloveMember.GhostVisualOriginOffset;
                    visualOrigin.localRotation = calib.localRotation;
                }
            }
            else
            {
                localPosition = calib.localPosition;
                localRotation = gloveMember.GhostVisualLocalRotation;
            }
        }
        else if (equipable != null)
        {
            var calib = equipable.EnsureGhostPoseCalibration();
            localPosition = calib.localPosition;
            localRotation = calib.localRotation;
            if (equipable.UsesSlot(PPEEquipSlot.Face) || equipable.UsesSlot(PPEEquipSlot.Head))
                localPosition += new Vector3(0f, faceGhostVisualVerticalOffset, faceGhostVisualForwardOffset);
            else if (equipable.UsesSlot(PPEEquipSlot.Body))
                localPosition += new Vector3(0f, -suitGhostVisualDownOffset, suitGhostVisualForwardOffset);
        }

        root.transform.localPosition = localPosition;
        root.transform.localRotation = localRotation;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (gloveMember != null &&
            gloveMember.IsTrackedHandGlove &&
            gloveMember.DevelopmentRuntimeCalibrationMode &&
            root.transform.parent != null)
        {
            var targetPalm = root.transform.parent.position;
            Debug.DrawLine(targetPalm, root.transform.position, Color.magenta, 0f, false);
            Debug.DrawRay(targetPalm, Vector3.up * 0.035f, Color.cyan, 0f, false);
            Debug.DrawRay(root.transform.position, Vector3.up * 0.035f, Color.magenta, 0f, false);
        }
#endif
    }

    void RefreshActiveGuideCalibrationPoses()
    {
        for (var i = 0; i < activeGuides.Count; i++)
        {
            var guide = activeGuides[i];
            if (guide == null || guide.Root == null)
                continue;
            ApplyGuideLocalPose(guide.Root, activeEquipable, activeGloveMember);
        }
    }

    static void SanitizeGuideRenderers(GameObject root)
    {
        if (root == null)
            return;

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        for (var i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null)
                continue;
            renderer.enabled = true;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        var colliders = root.GetComponentsInChildren<Collider>(true);
        for (var i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = false;
        }
    }

    static void SanitizeGloveGuideVisualOnly(GameObject root)
    {
        if (root == null)
            return;

        var transforms = root.GetComponentsInChildren<Transform>(true);
        for (var i = 0; i < transforms.Length; i++)
        {
            var child = transforms[i];
            if (child == null || child == root.transform)
                continue;
            var childName = child.name;
            if (childName.IndexOf("HighlightVisual", StringComparison.OrdinalIgnoreCase) >= 0 ||
                childName.IndexOf("Outline", StringComparison.OrdinalIgnoreCase) >= 0)
                child.gameObject.SetActive(false);
        }

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        for (var i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null)
                continue;

            // Physical PPE can have an outline material appended while hovered or
            // selected. Never preserve that material-slot count on a glove Ghost.
            renderer.sharedMaterials = Array.Empty<Material>();
        }

        var colliders = root.GetComponentsInChildren<Collider>(true);
        for (var i = 0; i < colliders.Length; i++)
            if (colliders[i] != null)
                colliders[i].enabled = false;

        var rigidbodies = root.GetComponentsInChildren<Rigidbody>(true);
        for (var i = 0; i < rigidbodies.Length; i++)
        {
            var body = rigidbodies[i];
            if (body == null)
                continue;
            body.detectCollisions = false;
            body.isKinematic = true;
        }

        var interactables = root.GetComponentsInChildren<XRBaseInteractable>(true);
        for (var i = 0; i < interactables.Length; i++)
            if (interactables[i] != null)
                interactables[i].enabled = false;

        var lineRenderers = root.GetComponentsInChildren<LineRenderer>(true);
        for (var i = 0; i < lineRenderers.Length; i++)
            if (lineRenderers[i] != null)
                lineRenderers[i].enabled = false;

        var behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
        for (var i = 0; i < behaviours.Length; i++)
        {
            var behaviour = behaviours[i];
            if (behaviour == null)
                continue;
            var typeName = behaviour.GetType().Name;
            if (typeName.IndexOf("Outline", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("Highlight", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("Hover", StringComparison.OrdinalIgnoreCase) >= 0)
                behaviour.enabled = false;
        }

        // Disabling an outline component can restore its cached source
        // materials in OnDisable, so collapse the material array last.
        for (var i = 0; i < renderers.Length; i++)
            if (renderers[i] != null)
                renderers[i].sharedMaterials = Array.Empty<Material>();
    }

    static bool SuppressesGhostVisual(PPEEquipable equipable)
    {
        if (equipable == null)
            return false;
        return equipable.UsesSlot(PPEEquipSlot.Face) ||
               equipable.UsesSlot(PPEEquipSlot.Head) ||
               equipable.UsesSlot(PPEEquipSlot.Body);
    }

    Transform ResolveInvisibleReleaseTarget(PPEEquipable equipable)
    {
        if (equipable == null)
            return null;

        ResolveAnchorSet();
        var anchors = anchorSet != null ? anchorSet : (bodyReference != null ? bodyReference.AnchorSet : null);
        if (anchors == null)
            return null;

        for (var i = 0; i < ConcreteSlots.Length; i++)
        {
            var slot = ConcreteSlots[i];
            if (!equipable.UsesSlot(slot))
                continue;
            if (anchors.TryGetAnchor(slot, out var anchor) && anchor != null)
                return anchor;
        }

        return null;
    }

    static void SetLayerRecursively(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        foreach (Transform child in root)
            SetLayerRecursively(child, layer);
    }

#if UNITY_EDITOR
    public void EditorWire(
        PPEEquipmentAnchorSet equipmentAnchors,
        Material notReady,
        Material ready,
        bool usePpeVisualFallback,
        bool enableAutoEquip = true,
        float placementReadyHoldDuration = 0.15f)
    {
        anchorSet = equipmentAnchors;
        notReadyMaterial = notReady;
        readyMaterial = ready;
        usePpeVisualWhenGuidePrefabMissing = usePpeVisualFallback;
        autoEquipEnabled = enableAutoEquip;
        readyHoldDuration = Mathf.Max(0f, placementReadyHoldDuration > 0.16f ? placementReadyHoldDuration : 0.20f);
        bootGuideLookDownAngle = 30f;
        placementSetupVersion = 3;
        logPlacementDebugOnChange = false;
        showPlacementDebugOverlay = false;
        bodyReference = FindAnyObjectByType<PPEPlayerBodyReference>(FindObjectsInactive.Include);
    }
#endif
}
