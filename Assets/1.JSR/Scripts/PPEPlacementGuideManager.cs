using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

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

    [Header("Glove Ghost")]
    [SerializeField]
    [Tooltip("Hand-local offset so the InnerGlove ghost sits just outside the tracked palm, not inside the hand mesh.")]
    Vector3 gloveGuidePositionOffset = new Vector3(0f, 0.02f, 0.03f);

    [SerializeField]
    Vector3 gloveGuideRotationOffsetEuler;

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
    bool logPlacementDebugOnChange = true;

    [SerializeField]
    [Tooltip("Show Equip Debug Visuals. Runtime zone spheres, HMD overlay, radius rings. Editor/Development only.")]
    bool showEquipDebugVisuals;

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
    bool equipAttemptedForCandidate;
    bool innerGloveReadyLatched;
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
        BindGrabbables();
    }

    void OnDisable()
    {
        UnbindGrabbables();
        HideGuides();
    }

    void LateUpdate()
    {
        if (!autoEquipEnabled || activeGrabbable == null || activeEquipable == null ||
            !activeGrabbable.IsGrabbed)
        {
            ResetReadyCandidate();
            ClearDebugIfIdle();
            UpdateInnerGloveDebugSpheres();
            UpdateDebugOverlay();
            return;
        }

        EvaluateHeldPlacement();
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
            SetActiveGuidesVisible(false);
            SetAllGuidesReady(false);
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
        var exitDistance = IsActiveInnerGloves() ? InnerGloveReadyExitDistance : maxDistance;

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
            var distance = Vector3.Distance(sampleFrom.position, guide.Anchor.position);
            var angle = Quaternion.Angle(sampleFrom.rotation, guide.Anchor.rotation);
            var distanceValid = innerGloveReadyLatched
                ? distance <= exitDistance
                : distance <= enterDistance;
            var angleValid = !useRotation || angle <= maxAngle;
            var placementValid = distanceValid && angleValid;

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

        if (IsActiveInnerGloves())
            innerGloveReadyLatched = firstReady != null;

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
            equipAttemptedForCandidate = false;
            return;
        }

        if (!sequenceValid)
            return;

        if (Time.time - readyCandidateSince < readyHoldDuration)
            return;

        if (TryEquipAtAnchor(firstReady.Anchor))
            equipAttemptedForCandidate = true;
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
        return ShowEquipDebugVisuals;
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
        manager?.Equip(item);
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
        overlayLabel.enableWordWrapping = false;
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
        var material = renderer.material;
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
        HideGuides();
        if (grabbable == null)
            return;

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

        ResolveAnchorSet();
        ResolveBodyReference();
        if (anchorSet == null)
            return;

        var equipable = grabbable.GetComponent<PPEEquipable>();
        if (equipable == null || equipable.EquipSlots == PPEEquipSlot.None)
            return;

        activeGrabbable = grabbable;
        activeEquipable = equipable;
        activeGloveMember = null;
        if (bodyReference != null)
            bodyReference.SetActiveFaceProfileFrom(equipable);
        activeEquipable.EnsurePlacementReference();

        foreach (var slot in ConcreteSlots)
        {
            if (!equipable.UsesSlot(slot) || !anchorSet.TryGetAnchor(slot, out var anchor))
                continue;

            var root = CreateGuide(equipable, anchor, slot, null);
            if (root == null)
                continue;

            var guide = new ActiveGuide
            {
                Slot = slot,
                Anchor = anchor,
                Root = root,
                Renderers = root.GetComponentsInChildren<Renderer>(true),
                State = PPEPlacementGuideState.NotReady,
            };
            activeGuides.Add(guide);
            ApplyMaterial(guide.Renderers, notReadyMaterial);
            GuideStateChanged?.Invoke(slot, PPEPlacementGuideState.NotReady);
        }

        PPEEquipGuidanceHints.NotifyGrabbed(equipable, null);
    }

    void ShowGloveMemberGuides(PPEHandGrabbable grabbable, PPEGlovePairMember member)
    {
        activeGrabbable = grabbable;
        activeGloveMember = member;
        activeEquipable = member.LogicalEquipable;
        ResolveBodyReference();
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
        ApplyMaterial(guide.Renderers, notReadyMaterial);
        if (member.TargetKind == PPEPairTargetKind.TrackedHands)
            MakeGloveGhostVisible(guide.Renderers);
        if (member.TargetKind == PPEPairTargetKind.BodyLowerLeg && !ShouldShowGuideVisual())
            guide.Root.SetActive(false);
        GuideStateChanged?.Invoke(slot, PPEPlacementGuideState.NotReady);
    }

    void HandleReleased(PPEHandGrabbable grabbable)
    {
        if (grabbable == activeGrabbable)
            HideGuides();
    }

    GameObject CreateGuide(
        PPEEquipable equipable,
        Transform anchor,
        PPEEquipSlot slot,
        PPEGlovePairMember gloveMember)
    {
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
            root = BuildRendererOnlyGhost(source, anchor, slot);
        }
        else
        {
            return null;
        }

        if (gloveMember != null && gloveMember.TargetKind == PPEPairTargetKind.TrackedHands)
        {
            root.transform.localPosition = gloveGuidePositionOffset;
            root.transform.localRotation = Quaternion.Euler(gloveGuideRotationOffsetEuler);
        }

        SetLayerRecursively(root.transform, LayerMask.NameToLayer("Default") >= 0 ? 0 : anchor.gameObject.layer);
        return root;
    }

    static GameObject BuildRendererOnlyGhost(GameObject source, Transform anchor, PPEEquipSlot slot)
    {
        var root = new GameObject($"PPE_Guide_{source.name}_{slot}");
        root.transform.SetParent(anchor, false);
        root.transform.localScale = source.transform.lossyScale;
        CopyVisualComponents(source.transform, root.transform);

        foreach (Transform child in source.transform)
            CloneVisualHierarchy(child, root.transform);
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

        // ProtectiveGloves use SkinnedMeshRenderer. Bake bind-pose mesh so the
        // ghost has the same world size as the held object without copying bones.
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
        ApplyMaterial(guide.Renderers, state == PPEPlacementGuideState.Ready ? readyMaterial : notReadyMaterial);
        if (activeGloveMember != null && activeGloveMember.TargetKind == PPEPairTargetKind.TrackedHands)
            MakeGloveGhostVisible(guide.Renderers, state == PPEPlacementGuideState.Ready);
        GuideStateChanged?.Invoke(guide.Slot, state);
    }

    static void MakeGloveGhostVisible(Renderer[] renderers, bool ready = false)
    {
        if (renderers == null)
            return;

        var minAlpha = ready ? 0.72f : 0.55f;
        for (var i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null)
                continue;

            renderer.enabled = true;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var materials = renderer.materials;
            for (var m = 0; m < materials.Length; m++)
            {
                var material = materials[m];
                if (material == null)
                    continue;
                if (material.HasProperty("_Cull"))
                    material.SetFloat("_Cull", 0f);
                if (material.HasProperty("_BaseColor"))
                {
                    var color = material.GetColor("_BaseColor");
                    color.a = Mathf.Max(color.a, minAlpha);
                    material.SetColor("_BaseColor", color);
                }

                if (material.HasProperty("_Color"))
                {
                    var color = material.GetColor("_Color");
                    color.a = Mathf.Max(color.a, minAlpha);
                    material.SetColor("_Color", color);
                }
            }

            renderer.materials = materials;
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
            if (guide.Root != null)
                Destroy(guide.Root);
        }
        activeGuides.Clear();
        activeGrabbable = null;
        activeEquipable = null;
        activeGloveMember = null;
        innerGloveReadyLatched = false;
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
        equipAttemptedForCandidate = false;
        innerGloveReadyLatched = false;
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
        logPlacementDebugOnChange = true;
        bodyReference = FindAnyObjectByType<PPEPlayerBodyReference>(FindObjectsInactive.Include);
    }
#endif
}
