using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// InnerGloves donning. Ghost is guidance only. Equip happens on natural
/// XR SelectExited / release near the target palm. No dwell auto-equip.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(110)]
public sealed class PPEInnerGloveDonning : MonoBehaviour
{
    const float DefaultReleaseDistance = 0.15f;
    const float DefaultTrackingReleaseGrace = 0.30f;
    const float DebugLooseReleaseDistance = 0.50f;

    [SerializeField] PPEGlovePairCoordinator coordinator;
    [SerializeField, Min(0.04f)]
    [Tooltip("EquipReference to target XR Palm distance that Ready and release-equip both use.")]
    float equipReleaseDistance = DefaultReleaseDistance;
    [SerializeField, Min(0f)]
    [Tooltip("If XR Hands Palm was lost within this window, last valid palm pose may still equip.")]
    float trackingReleaseGrace = DefaultTrackingReleaseGrace;
    [SerializeField]
    [Tooltip("Development only. When enabled, any opposite-hand release within 0.50 m equips. Leave OFF.")]
    bool debugLooseReleaseDistance;
    [SerializeField]
    [Tooltip("Show Equip Debug Visuals. Editor/Development only. Off for training.")]
    bool showDevelopmentDebug;

    Transform leftZone;
    Transform rightZone;
    SphereCollider leftZoneCollider;
    SphereCollider rightZoneCollider;
    Renderer leftZoneRenderer;
    Renderer rightZoneRenderer;
    Renderer leftRefRenderer;
    Renderer rightRefRenderer;

    PPEGlovePairMember activeMember;
    PPEHandGrabbable activeGrab;
    bool insideReleaseDistance;
    string debugState = "IDLE";
    float debugDistance = float.PositiveInfinity;
    TextMeshPro overlayLabel;
    PPEPlacementGuideManager cachedPlacement;

    public float InnerGloveEquipRadius => equipReleaseDistance;
    public string DebugState => debugState;
    public string FirstFalseState => debugState;
    public bool InsideZone => insideReleaseDistance;
    public float CurrentDistance => debugDistance;

    void Awake()
    {
        if (coordinator == null)
            coordinator = GetComponent<PPEGlovePairCoordinator>();
    }

    void LateUpdate()
    {
        if (coordinator == null || coordinator.LogicalItem == null ||
            !string.Equals(coordinator.LogicalItem.ItemId, PPEEquipmentManager.IdInnerGloves, System.StringComparison.Ordinal))
        {
            HideDebug();
            return;
        }

        EnsureZones();
        ResolveHeldMember();
        EvaluateGuidance();
        UpdateZoneVisuals();
        UpdateDebugOverlay();
    }

    public static bool TryEquipInnerGloveOnRelease(PPEHandGrabbable grabbable, PPEGrabHand capturedHoldingHand)
    {
        if (grabbable == null)
            return false;

        var member = grabbable.GetComponent<PPEGlovePairMember>();
        if (member == null || !member.IsInnerGlove)
            return false;

        var donning = member.Coordinator != null
            ? member.Coordinator.GetComponent<PPEInnerGloveDonning>()
            : FindAnyObjectByType<PPEInnerGloveDonning>(FindObjectsInactive.Include);
        if (donning == null)
        {
            Debug.Log(
                "[InnerGlove Release] Member=" + member.GloveSide +
                " CapturedHolding=" + capturedHoldingHand +
                " Result=NO_DONNING_COMPONENT",
                member);
            return false;
        }

        return donning.TryEquipInnerGloveOnRelease(member, capturedHoldingHand);
    }

    bool TryEquipInnerGloveOnRelease(PPEGlovePairMember member, PPEGrabHand capturedHolding)
    {
        if (member == null || coordinator == null)
            return false;

        var maxDistance = EffectiveReleaseDistance();
        var sequenceAllowed = IsSequenceValid();
        var correctHolding = member.IsOppositeHandGrab(capturedHolding);
        var targetValid = TryGetReleasePalm(member, out var palm);
        var equipRef = member.EnsureEquipReference();
        var distance = equipRef != null && targetValid
            ? Vector3.Distance(equipRef.position, palm)
            : float.PositiveInfinity;
        debugDistance = distance;

        string result;
        if (!correctHolding)
            result = "WRONG_HOLDING_HAND";
        else if (!targetValid)
            result = "NO_VALID_TARGET_POSE";
        else if (!sequenceAllowed)
            result = "SEQUENCE_BLOCKED";
        else if (distance > maxDistance)
            result = "TOO_FAR";
        else
            result = member.GloveSide == PPEGrabHand.Right ? "EQUIP_RIGHT" : "EQUIP_LEFT";

        PPEGloveReleaseDiagnostics.Log(
            member,
            capturedHolding,
            palm,
            equipRef,
            distance,
            maxDistance,
            targetValid,
            result);

        if (result != "EQUIP_LEFT" && result != "EQUIP_RIGHT")
            return false;

        var equipped = coordinator.NotifyInnerGloveSideFromRelease(member);
        if (!equipped)
        {
            Debug.Log(
                "[InnerGlove Release] Member=" + member.GloveSide +
                " CapturedHolding=" + capturedHolding +
                " Result=NOTIFY_SIDE_FAILED" +
                " LeftEquipped=" + coordinator.LeftEquipped +
                " RightEquipped=" + coordinator.RightEquipped,
                member);
            return false;
        }

        member.GetComponent<PPEStoredDisplayState>()?.NotifyEquipped();
        return true;
    }

    float EffectiveReleaseDistance()
    {
        if (debugLooseReleaseDistance && (Application.isEditor || Debug.isDebugBuild))
            return DebugLooseReleaseDistance;
        return equipReleaseDistance;
    }

    bool TryGetReleasePalm(PPEGlovePairMember member, out Vector3 palm)
    {
        palm = default;
        var target = member != null ? member.TargetTransform : null;
        if (target == null)
            return false;

        var follower = target.GetComponent<PPEGloveHandTargetFollower>();
        if (follower != null)
        {
            follower.RefreshForRelease();
            return follower.TryGetPalmForRelease(trackingReleaseGrace, out palm);
        }

        palm = target.position;
        return member.IsTargetTracking;
    }

    void ResolveHeldMember()
    {
        activeMember = null;
        activeGrab = null;
        TryBindHeld(coordinator.LeftMember);
        if (activeMember == null)
            TryBindHeld(coordinator.RightMember);
    }

    void TryBindHeld(PPEGlovePairMember member)
    {
        if (member == null || !member.gameObject.activeInHierarchy)
            return;
        var grab = member.GetComponent<PPEHandGrabbable>();
        if (grab == null || !grab.IsGrabbed)
            return;
        activeMember = member;
        activeGrab = grab;
    }

    void EvaluateGuidance()
    {
        if (activeMember == null || activeGrab == null)
        {
            insideReleaseDistance = false;
            debugState = "IDLE";
            debugDistance = float.PositiveInfinity;
            return;
        }

        var holding = activeGrab.GrabSessionHoldingHand != PPEGrabHand.Unknown
            ? activeGrab.GrabSessionHoldingHand
            : activeGrab.CurrentHand;
        var sequenceValid = IsSequenceValid();
        var targetValid = TryGetReleasePalm(activeMember, out var palm);
        var equipRef = activeMember.EnsureEquipReference();
        debugDistance = equipRef != null && targetValid
            ? Vector3.Distance(equipRef.position, palm)
            : float.PositiveInfinity;

        if (activeMember.IsSameHandGrab(holding) || !activeMember.IsOppositeHandGrab(holding))
        {
            insideReleaseDistance = false;
            SetGhost(false, false);
            debugState = "WRONG HAND";
            return;
        }

        if (!targetValid)
        {
            insideReleaseDistance = false;
            SetGhost(false, false);
            debugState = "TRACKING INVALID";
            return;
        }

        insideReleaseDistance = debugDistance <= EffectiveReleaseDistance();
        if (!insideReleaseDistance)
        {
            SetGhost(true, false);
            debugState = "TOO FAR";
            return;
        }

        SetGhost(true, true);
        debugState = sequenceValid ? "READY" : "SEQUENCE BLOCKED";
    }

    void SetGhost(bool visible, bool ready)
    {
        var placement = ResolvePlacement();
        placement?.SetInnerGloveGhostDisplay(visible, ready);
    }

    bool IsSequenceValid()
    {
        var sequence = PPESequenceManager.Resolve();
        return sequence == null || sequence.CanEquip(PPEEquipmentManager.IdInnerGloves);
    }

    void EnsureZones()
    {
        leftZone = EnsureZone(
            PPEGrabHand.Left,
            "LeftInnerGloveEquipZone",
            leftZone,
            ref leftZoneCollider,
            ref leftZoneRenderer);
        rightZone = EnsureZone(
            PPEGrabHand.Right,
            "RightInnerGloveEquipZone",
            rightZone,
            ref rightZoneCollider,
            ref rightZoneRenderer);
    }

    Transform EnsureZone(
        PPEGrabHand side,
        string objectName,
        Transform existing,
        ref SphereCollider collider,
        ref Renderer renderer)
    {
        var parent = coordinator != null ? coordinator.GetTrackedTarget(side) : null;
        if (parent == null)
            return existing;

        if (existing == null)
        {
            var go = new GameObject(objectName);
            existing = go.transform;
            collider = go.AddComponent<SphereCollider>();
            collider.isTrigger = true;
            collider.radius = equipReleaseDistance;
            go.layer = 2;
        }

        if (existing.parent != parent)
            existing.SetParent(parent, false);
        existing.localPosition = Vector3.zero;
        existing.localRotation = Quaternion.identity;
        existing.localScale = Vector3.one;
        if (collider != null)
            collider.radius = equipReleaseDistance;

        if (ShouldShowDebug() && renderer == null)
            renderer = AddDebugSphere(existing, equipReleaseDistance * 2f, new Color(0.2f, 0.85f, 1f, 0.22f));

        return existing;
    }

    static Renderer AddDebugSphere(Transform parent, float diameter, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = parent.name + "Debug";
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localScale = Vector3.one * diameter;
        Object.Destroy(go.GetComponent<Collider>());
        var renderer = go.GetComponent<Renderer>();
        if (renderer == null)
            return null;
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
                material.SetColor("_BaseColor", color);
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.renderQueue = 3000;
            renderer.sharedMaterial = material;
        }

        return renderer;
    }

    void UpdateZoneVisuals()
    {
        var show = ShouldShowDebug() && activeMember != null;
        var ready = debugState == "READY";
        var leftActive = show && activeMember != null && activeMember.GloveSide == PPEGrabHand.Left;
        var rightActive = show && activeMember != null && activeMember.GloveSide == PPEGrabHand.Right;
        SetRendererVisible(leftZoneRenderer, leftActive);
        SetRendererVisible(rightZoneRenderer, rightActive);
        if (leftActive)
            SetRendererColor(leftZoneRenderer, ready ? new Color(0.15f, 1f, 0.35f, 0.32f) : new Color(0.2f, 0.85f, 1f, 0.22f));
        if (rightActive)
            SetRendererColor(rightZoneRenderer, ready ? new Color(0.15f, 1f, 0.35f, 0.32f) : new Color(0.2f, 0.85f, 1f, 0.22f));

        UpdateEquipReferenceMarker(coordinator != null ? coordinator.LeftMember : null, ref leftRefRenderer, leftActive);
        UpdateEquipReferenceMarker(coordinator != null ? coordinator.RightMember : null, ref rightRefRenderer, rightActive);
    }

    void UpdateEquipReferenceMarker(PPEGlovePairMember member, ref Renderer marker, bool visible)
    {
        if (!ShouldShowDebug() || member == null)
        {
            SetRendererVisible(marker, false);
            return;
        }

        var reference = member.EnsureEquipReference();
        if (reference == null)
            return;
        if (marker == null)
            marker = AddDebugSphere(reference, 0.02f, new Color(1f, 0.85f, 0.15f, 0.85f));
        SetRendererVisible(marker, visible);
    }

    static void SetRendererVisible(Renderer renderer, bool visible)
    {
        if (renderer != null && renderer.gameObject.activeSelf != visible)
            renderer.gameObject.SetActive(visible);
    }

    static void SetRendererColor(Renderer renderer, Color color)
    {
        if (renderer == null)
            return;
        var material = renderer.material;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
    }

    bool ShouldShowDebug()
    {
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
        return false;
#else
        var placement = ResolvePlacement();
        if (placement != null)
            return placement.ShowEquipDebugVisuals;
        return showDevelopmentDebug && (Application.isEditor || Debug.isDebugBuild);
#endif
    }

    void UpdateDebugOverlay()
    {
        if (!ShouldShowDebug() || activeMember == null)
        {
            if (overlayLabel != null && overlayLabel.gameObject.activeSelf)
                overlayLabel.gameObject.SetActive(false);
            return;
        }

        if (overlayLabel == null)
        {
            var go = new GameObject("PPE_InnerGloveDebugOverlay");
            go.transform.SetParent(transform, false);
            overlayLabel = go.AddComponent<TextMeshPro>();
            overlayLabel.fontSize = 0.09f;
            overlayLabel.alignment = TextAlignmentOptions.TopLeft;
            overlayLabel.rectTransform.sizeDelta = new Vector2(1.8f, 1.6f);
            overlayLabel.textWrappingMode = TextWrappingModes.NoWrap;
            overlayLabel.raycastTarget = false;
        }

        overlayLabel.gameObject.SetActive(true);
        overlayLabel.text = BuildOverlayText();
        overlayLabel.color = debugState == "READY"
            ? new Color(0.2f, 1f, 0.4f, 1f)
            : new Color(1f, 0.85f, 0.2f, 1f);

        var body = FindAnyObjectByType<PPEPlayerBodyReference>(FindObjectsInactive.Exclude);
        var cam = body != null ? body.XrCamera : null;
        if (cam == null && Camera.main != null)
            cam = Camera.main.transform;
        if (cam == null)
            return;
        overlayLabel.transform.position = cam.position + cam.forward * 1.1f + cam.up * 0.18f;
        overlayLabel.transform.rotation = Quaternion.LookRotation(
            overlayLabel.transform.position - cam.position,
            Vector3.up);
    }

    string BuildOverlayText()
    {
        var holding = activeGrab != null ? activeGrab.CurrentHand.ToString() : "(none)";
        var side = activeMember != null ? activeMember.GloveSide.ToString() : "(none)";
        var tracking = activeMember != null && TryGetReleasePalm(activeMember, out _);
        var sequenceOk = IsSequenceValid();
        return
            "INNER GLOVE DEBUG\n\n" +
            "Mode: RELEASE TO EQUIP\n" +
            "Member: " + side + "\n" +
            "Holding: " + holding + "\n" +
            "Target: " + side + "\n" +
            "WrongHand: " + (activeMember != null && activeMember.IsSameHandGrab(activeGrab != null ? activeGrab.CurrentHand : PPEGrabHand.Unknown) ? "TRUE" : "FALSE") + "\n" +
            "Tracking: " + (tracking ? "TRUE" : "FALSE") + "\n" +
            "TargetSource: " + (activeMember != null && activeMember.TargetTransform != null
                ? (activeMember.TargetTransform.GetComponent<PPEGloveHandTargetFollower>() != null
                    ? activeMember.TargetTransform.GetComponent<PPEGloveHandTargetFollower>().ResolvedSourceLabel
                    : "None")
                : "None") + "\n\n" +
            "Distance: " + (float.IsInfinity(debugDistance) ? "inf" : debugDistance.ToString("0.00")) + "\n" +
            "Release: " + EffectiveReleaseDistance().ToString("0.00") + "\n\n" +
            "Ready: " + (insideReleaseDistance ? "TRUE" : "FALSE") + "\n" +
            "Sequence: " + (sequenceOk ? "TRUE" : "FALSE") + "\n" +
            "State: " + debugState + "\n\n" +
            "LeftEquipped: " + (coordinator != null && coordinator.LeftEquipped ? "TRUE" : "FALSE") + "\n" +
            "RightEquipped: " + (coordinator != null && coordinator.RightEquipped ? "TRUE" : "FALSE");
    }

    void HideDebug()
    {
        SetRendererVisible(leftZoneRenderer, false);
        SetRendererVisible(rightZoneRenderer, false);
        if (overlayLabel != null && overlayLabel.gameObject.activeSelf)
            overlayLabel.gameObject.SetActive(false);
    }

    PPEPlacementGuideManager ResolvePlacement()
    {
        if (cachedPlacement != null)
            return cachedPlacement;
        cachedPlacement = FindAnyObjectByType<PPEPlacementGuideManager>(FindObjectsInactive.Include);
        return cachedPlacement;
    }
}
