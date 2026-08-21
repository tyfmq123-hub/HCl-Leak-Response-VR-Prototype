using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Logical left/right pair (ChemicalGloves, InnerGloves, ChemicalBoots).
/// Sequence, checklist, and loadout stay on one item id. Physical pieces are
/// independent grabbables that complete that item only when both are equipped.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-300)]
public sealed class PPEGlovePairCoordinator : MonoBehaviour
{
    public static PPEGlovePairCoordinator ChemicalGlovesInstance { get; private set; }
    static readonly System.Collections.Generic.List<PPEGlovePairCoordinator> activeCoordinators =
        new System.Collections.Generic.List<PPEGlovePairCoordinator>();

    [SerializeField] PPEEquipable logicalItem;
    [SerializeField] PPEGlovePairMember leftMember;
    [SerializeField] PPEGlovePairMember rightMember;
    [SerializeField] Transform leftTrackedTarget;
    [SerializeField] Transform rightTrackedTarget;
    [SerializeField] GameObject leftGuidePrefab;
    [SerializeField] GameObject rightGuidePrefab;
    [SerializeField]
    [Tooltip("Legacy fallback only. 04_PPE uses authored Left/Right members.")]
    bool splitCombinedShelfPairOnAwake = false;
    [SerializeField] PPEPairTargetKind targetKind = PPEPairTargetKind.TrackedHands;
    [SerializeField]
    [Tooltip("Gloves require the opposite hand. Boots allow either hand.")]
    bool requireOppositeHand = true;
    [SerializeField, Min(0f)]
    [Tooltip("Fallback shin height if PPEPlayerBodyReference is missing. Quest-tune Lower Leg Height on PPE_PlayerBodyReference.")]
    float lowerLegHeight = 0.38f;
    [SerializeField, Min(0.01f)]
    [Tooltip("Placement distance against the tracked target. Gloves start at 0.12 m.")]
    float placementDistance = 0.12f;
    [SerializeField]
    [Tooltip("When false, gloves and boots equip by proximity only.")]
    bool useRotationValidation;
    [SerializeField, Range(1f, 180f)]
    [Tooltip("Used only when Use Rotation Validation is enabled.")]
    float placementAngleTolerance = 40f;

    bool leftEquipped;
    bool rightEquipped;
    bool logicalEquipCompleted;
    bool debugCompleteSideCalled;
    bool debugCompleteSideSucceeded;
    PPEEquipmentManager cachedEquipmentManager;
    ChemicalGlovesWearableVisual cachedWearable;
    XRInteractionManager cachedInteractionManager;
    PPEPlayerBodyReference cachedBody;
    PPEPlacementGuideManager cachedPlacementGuide;
    static PPEGloveHandTargetFollower cachedLeftFollower;
    static PPEGloveHandTargetFollower cachedRightFollower;
    static PPELowerLegTargetFollower cachedLeftLowerLeg;
    static PPELowerLegTargetFollower cachedRightLowerLeg;

    public PPEEquipable LogicalItem => logicalItem != null ? logicalItem : GetComponent<PPEEquipable>();
    public bool LeftEquipped => leftEquipped;
    public bool RightEquipped => rightEquipped;
    public bool BothEquipped => leftEquipped && rightEquipped;
    public PPEGlovePairMember LeftMember => leftMember;
    public PPEGlovePairMember RightMember => rightMember;
    public float PlacementDistance => placementDistance;
    public float PlacementAngleTolerance => placementAngleTolerance;
    public bool UseRotationValidation => useRotationValidation;
    public PPEPairTargetKind TargetKind => targetKind;
    public bool RequireOppositeHand => requireOppositeHand;
    public float LowerLegHeight => lowerLegHeight;
    public bool DebugCompleteSideCalled => debugCompleteSideCalled;
    public bool DebugCompleteSideSucceeded => debugCompleteSideSucceeded;

    public static PPEGlovePairCoordinator FindForItem(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return null;

        if (ChemicalGlovesInstance != null &&
            ChemicalGlovesInstance.LogicalItem != null &&
            string.Equals(ChemicalGlovesInstance.LogicalItem.ItemId, itemId, System.StringComparison.Ordinal))
            return ChemicalGlovesInstance;

        for (var i = 0; i < activeCoordinators.Count; i++)
        {
            var coordinator = activeCoordinators[i];
            if (coordinator == null || coordinator.LogicalItem == null)
                continue;
            if (string.Equals(coordinator.LogicalItem.ItemId, itemId, System.StringComparison.Ordinal))
                return coordinator;
        }

        return null;
    }

    public Transform GetTrackedTarget(PPEGrabHand side)
    {
        EnsureTrackedTargets();
        return side == PPEGrabHand.Right ? rightTrackedTarget : leftTrackedTarget;
    }

    public void EditorApplyPairSettings(
        PPEPairTargetKind kind,
        bool oppositeHand,
        float distance,
        float angle,
        float shinHeight,
        bool requireRotation = false)
    {
        targetKind = kind;
        requireOppositeHand = oppositeHand;
        placementDistance = Mathf.Max(0.01f, distance);
        placementAngleTolerance = Mathf.Clamp(angle, 1f, 180f);
        useRotationValidation = requireRotation;
        lowerLegHeight = Mathf.Max(0f, shinHeight);
        splitCombinedShelfPairOnAwake = false;
    }

    public void EditorAssignMembers(PPEGlovePairMember left, PPEGlovePairMember right)
    {
        leftMember = left;
        rightMember = right;
    }

    void Awake()
    {
        if (logicalItem == null)
            logicalItem = GetComponent<PPEEquipable>();

        if (logicalItem != null &&
            string.Equals(logicalItem.ItemId, PPEEquipmentManager.IdChemicalGloves, System.StringComparison.Ordinal))
            ChemicalGlovesInstance = this;

        EnsureTrackedTargets();
        BindAuthoredMembersFromChildren();
        if (splitCombinedShelfPairOnAwake)
            EnsurePhysicalMembers();
        else
            PrepareAuthoredMembers();
        DisableLogicalRootGrab();
        EnsureInnerGloveDonning();
    }

    void EnsureInnerGloveDonning()
    {
        if (LogicalItem == null ||
            !string.Equals(LogicalItem.ItemId, PPEEquipmentManager.IdInnerGloves, System.StringComparison.Ordinal))
            return;
        if (GetComponent<PPEInnerGloveDonning>() == null)
            gameObject.AddComponent<PPEInnerGloveDonning>();
    }

    void OnEnable()
    {
        if (!activeCoordinators.Contains(this))
            activeCoordinators.Add(this);
    }

    void OnDisable()
    {
        activeCoordinators.Remove(this);
    }

    void OnDestroy()
    {
        if (ChemicalGlovesInstance == this)
            ChemicalGlovesInstance = null;
    }

    public bool NotifySideEquipped(PPEGlovePairMember member)
    {
        return NotifySideEquipped(member, selectionAlreadyExited: false);
    }

    public bool NotifySideEquipped(PPEGlovePairMember member, bool selectionAlreadyExited)
    {
        debugCompleteSideCalled = true;
        debugCompleteSideSucceeded = false;
        if (member == null || LogicalItem == null)
        {
            Debug.LogWarning("[PPE Pair] CompleteSide failed: member or LogicalItem is null.", this);
            return false;
        }
        if (member.GloveSide != PPEGrabHand.Left && member.GloveSide != PPEGrabHand.Right)
            return false;

        var sequence = PPESequenceManager.Resolve();
        if (sequence != null && !sequence.CanEquip(LogicalItem.ItemId))
        {
            sequence.NotifyEquipBlocked(LogicalItem.ItemId);
            Debug.Log(
                "[PPE Pair] CompleteSide blocked by sequence. Item=" + LogicalItem.ItemId +
                " Required=" + sequence.CurrentRequiredStep,
                this);
            return false;
        }

        if (member.GloveSide == PPEGrabHand.Left)
        {
            if (leftEquipped)
                return false;
        }
        else
        {
            if (rightEquipped)
                return false;
        }

        ReleaseMemberInteraction(member, selectionAlreadyExited);

        if (member.GloveSide == PPEGrabHand.Left)
            leftEquipped = true;
        else
            rightEquipped = true;

        if (member.gameObject.activeSelf)
            member.gameObject.SetActive(false);

        var placement = cachedPlacementGuide;
        if (placement == null)
            placement = FindAnyObjectByType<PPEPlacementGuideManager>(FindObjectsInactive.Include);
        cachedPlacementGuide = placement;
        placement?.HideActiveGuides();

        ApplyWearableVisual();
        debugCompleteSideSucceeded = true;
        Debug.Log(
            "[PPE Pair] CompleteSide " + member.GloveSide +
            " Item=" + LogicalItem.ItemId +
            " LeftEquipped=" + leftEquipped +
            " RightEquipped=" + rightEquipped +
            " PhysicalActive=" + member.gameObject.activeSelf,
            this);

        if (!BothEquipped || logicalEquipCompleted)
            return true;

        var manager = ResolveEquipmentManager();
        if (manager == null)
            return false;

        logicalEquipCompleted = manager.Equip(LogicalItem);
        return logicalEquipCompleted;
    }

    public bool NotifyPairSideFromRelease(PPEGlovePairMember member)
    {
        return NotifyInnerGloveSideFromRelease(member);
    }

    public bool NotifyInnerGloveSideFromRelease(PPEGlovePairMember member)
    {
        debugCompleteSideCalled = true;
        debugCompleteSideSucceeded = false;
        if (member == null || LogicalItem == null)
            return false;
        if (member.GloveSide != PPEGrabHand.Left && member.GloveSide != PPEGrabHand.Right)
            return false;

        var sequence = PPESequenceManager.Resolve();
        if (sequence != null && !sequence.CanEquip(LogicalItem.ItemId))
        {
            sequence.NotifyEquipBlocked(LogicalItem.ItemId);
            return false;
        }

        if (member.GloveSide == PPEGrabHand.Left)
        {
            if (leftEquipped)
                return false;
            leftEquipped = true;
        }
        else
        {
            if (rightEquipped)
                return false;
            rightEquipped = true;
        }

        debugCompleteSideSucceeded = true;
        var placement = cachedPlacementGuide;
        if (placement == null)
            placement = FindAnyObjectByType<PPEPlacementGuideManager>(FindObjectsInactive.Include);
        cachedPlacementGuide = placement;
        placement?.HideActiveGuides();
        ApplyWearableVisual();

        Debug.Log(
            "[PPE Pair] Release side=" + member.GloveSide +
            " Item=" + LogicalItem.ItemId +
            " LeftEquipped=" + leftEquipped +
            " RightEquipped=" + rightEquipped,
            this);

        if (BothEquipped && !logicalEquipCompleted)
        {
            var manager = ResolveEquipmentManager();
            if (manager != null)
                logicalEquipCompleted = manager.Equip(LogicalItem);
        }

        StartCoroutine(DisableInnerGloveMemberEndOfFrame(member));
        return true;
    }

    IEnumerator DisableInnerGloveMemberEndOfFrame(PPEGlovePairMember member)
    {
        yield return null;
        if (member == null)
            yield break;

        var grab = member.GetComponent<PPEHandGrabbable>();
        if (grab != null)
            grab.enabled = false;
        if (member.gameObject.activeSelf)
            member.gameObject.SetActive(false);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [ContextMenu("Debug/Force Complete Left InnerGlove Member")]
    public void DebugForceCompleteLeftInnerGloveMember()
    {
        if (LogicalItem == null ||
            !string.Equals(LogicalItem.ItemId, PPEEquipmentManager.IdInnerGloves, System.StringComparison.Ordinal))
        {
            Debug.LogWarning("[PPE Pair] Force Complete skipped: LogicalItem is not InnerGloves.", this);
            return;
        }

        var result = NotifySideEquipped(leftMember);
        Debug.Log("[PPE Pair] Force Complete Left InnerGlove result=" + result, this);
    }
#endif

    void EnsurePhysicalMembers()
    {
        BindAuthoredMembersFromChildren();
        if (leftMember != null && rightMember != null)
        {
            PrepareAuthoredMembers();
            return;
        }

        var leftVisual = FindGloveVisual(PPEGrabHand.Left);
        var rightVisual = FindGloveVisual(PPEGrabHand.Right);
        if (leftVisual == null || rightVisual == null)
        {
            Debug.LogWarning("[PPE Pair] Could not find separate left/right visuals under " + name, this);
            return;
        }

        var sourceGrab = GetComponent<PPEHandGrabbable>();
        leftMember = CreatePhysicalMember(PPEGrabHand.Left, leftVisual, sourceGrab, leftGuidePrefab);
        rightMember = CreatePhysicalMember(PPEGrabHand.Right, rightVisual, sourceGrab, rightGuidePrefab);

        var placement = cachedPlacementGuide;
        if (placement == null)
            placement = FindAnyObjectByType<PPEPlacementGuideManager>(FindObjectsInactive.Include);
        cachedPlacementGuide = placement;
        placement?.RefreshBindings();
    }

    void BindAuthoredMembersFromChildren()
    {
        var members = GetComponentsInChildren<PPEGlovePairMember>(true);
        for (var i = 0; i < members.Length; i++)
        {
            var member = members[i];
            if (member == null || member.transform.parent != transform)
                continue;
            if (member.GloveSide == PPEGrabHand.Right && rightMember == null)
                rightMember = member;
            else if (member.GloveSide == PPEGrabHand.Left && leftMember == null)
                leftMember = member;
        }
    }

    void PrepareAuthoredMembers()
    {
        if (leftMember != null)
        {
            leftMember.Configure(this, PPEGrabHand.Left, leftMember.VisualRoot, leftGuidePrefab);
            leftMember.EnsurePrimitiveInteractionVolumes();
            leftMember.EnsureEquipReference();
        }

        if (rightMember != null)
        {
            rightMember.Configure(this, PPEGrabHand.Right, rightMember.VisualRoot, rightGuidePrefab);
            rightMember.EnsurePrimitiveInteractionVolumes();
            rightMember.EnsureEquipReference();
        }
    }

    PPEGlovePairMember CreatePhysicalMember(
        PPEGrabHand side,
        Transform visual,
        PPEHandGrabbable sourceGrab,
        GameObject dedicatedGuide)
    {
        var existingMember = GetComponentInChildren<PPEGlovePairMember>(true);
        if (existingMember != null && existingMember.GloveSide == side && existingMember.transform.parent == transform)
            return existingMember;

        var existingRoot = transform.Find(PhysicalMemberName(side));
        GameObject go;
        if (existingRoot != null)
        {
            go = existingRoot.gameObject;
        }
        else
        {
            go = new GameObject(PhysicalMemberName(side));
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
        }

        go.transform.SetPositionAndRotation(visual.position, visual.rotation);

        visual.SetParent(go.transform, true);

        var body = go.GetComponent<Rigidbody>();
        if (body == null)
            body = go.AddComponent<Rigidbody>();
        CopyRigidbody(GetComponent<Rigidbody>(), body);

        var member = go.GetComponent<PPEGlovePairMember>();
        if (member == null)
            member = go.AddComponent<PPEGlovePairMember>();
        member.Configure(this, side, visual, dedicatedGuide);
        member.EnsurePrimitiveInteractionVolumes();

        var stored = go.GetComponent<PPEStoredDisplayState>();
        if (stored == null)
            stored = go.AddComponent<PPEStoredDisplayState>();
        stored.UseAuthoredAssistCollider(
            go.transform.Find(PPEGlovePairMember.AssistColliderName)?.GetComponent<Collider>());

        var grab = go.GetComponent<PPEHandGrabbable>();
        if (grab == null)
            grab = go.AddComponent<PPEHandGrabbable>();
        CopyGrabSettings(sourceGrab, grab);
        grab.colliders.Clear();
        CopyProximityDistance(sourceGrab, grab);
        member.EnsurePrimitiveInteractionVolumes();
        return member;
    }

    void DisableLogicalRootGrab()
    {
        EnsureHoverOnlyInteractable();

        var grab = GetComponent<PPEHandGrabbable>();
        if (grab != null)
        {
            grab.enabled = false;
            grab.colliders.Clear();
            Destroy(grab);
        }

        var stored = GetComponent<PPEStoredDisplayState>();
        if (stored != null)
        {
            stored.DisableGeneratedWorldAabbAssist();
            stored.enabled = false;
        }

        var filter = GetComponent<PPEDirectGrabProximityFilter>();
        if (filter != null)
            Destroy(filter);

        var body = GetComponent<Rigidbody>();
        if (body != null)
            Destroy(body);
    }

    void EnsureHoverOnlyInteractable()
    {
        var grab = GetComponent<PPEHandGrabbable>();
        var simple = GetComponent<XRSimpleInteractable>();
        if (simple == null)
            simple = gameObject.AddComponent<XRSimpleInteractable>();

        if (grab != null)
            simple.interactionLayers = grab.interactionLayers;

        simple.selectMode = InteractableSelectMode.Multiple;
        simple.colliders.Clear();
        simple.enabled = true;
    }

    void EnsureTrackedTargets()
    {
        if (targetKind == PPEPairTargetKind.BodyLowerLeg)
        {
            if (leftTrackedTarget == null)
                leftTrackedTarget = GetOrCreateLowerLegFollower(PPEGrabHand.Left);
            if (rightTrackedTarget == null)
                rightTrackedTarget = GetOrCreateLowerLegFollower(PPEGrabHand.Right);
            return;
        }

        if (leftTrackedTarget == null)
            leftTrackedTarget = GetOrCreateFollower(PPEGrabHand.Left);
        if (rightTrackedTarget == null)
            rightTrackedTarget = GetOrCreateFollower(PPEGrabHand.Right);
    }

    Transform GetOrCreateLowerLegFollower(PPEGrabHand hand)
    {
        var body = ResolveBodyReference();
        var foot = ResolveFootAnchor(hand);
        var height = body != null ? body.LowerLegHeight : lowerLegHeight;

        var cached = hand == PPEGrabHand.Right ? cachedRightLowerLeg : cachedLeftLowerLeg;
        if (cached != null)
        {
            cached.Configure(hand, foot, height, body);
            return cached.transform;
        }

        var existing = FindObjectsByType<PPELowerLegTargetFollower>(FindObjectsInactive.Include);
        for (var i = 0; i < existing.Length; i++)
        {
            if (existing[i] == null || existing[i].Side != hand)
                continue;
            existing[i].Configure(hand, foot, height, body);
            if (hand == PPEGrabHand.Right)
                cachedRightLowerLeg = existing[i];
            else
                cachedLeftLowerLeg = existing[i];
            return existing[i].transform;
        }

        var parent = ResolveTargetParent();
        var go = new GameObject(hand == PPEGrabHand.Right ? "RightBootEquipTarget" : "LeftBootEquipTarget");
        go.transform.SetParent(parent, false);
        var follower = go.AddComponent<PPELowerLegTargetFollower>();
        follower.Configure(hand, foot, height, body);
        if (hand == PPEGrabHand.Right)
            cachedRightLowerLeg = follower;
        else
            cachedLeftLowerLeg = follower;
        return go.transform;
    }

    Transform ResolveFootAnchor(PPEGrabHand hand)
    {
        var body = ResolveBodyReference();
        var anchors = body != null ? body.AnchorSet : null;
        if (anchors == null)
            return null;
        return hand == PPEGrabHand.Right ? anchors.RightFoot : anchors.LeftFoot;
    }

    PPEPlayerBodyReference ResolveBodyReference()
    {
        if (cachedBody == null)
            cachedBody = FindAnyObjectByType<PPEPlayerBodyReference>(FindObjectsInactive.Include);
        return cachedBody;
    }

    Transform ResolveTargetParent()
    {
        var body = ResolveBodyReference();
        if (body != null)
            return body.transform;

        // Stay in the PPE scene. Do not parent under XR Origin.
        return transform;
    }

    string PhysicalMemberName(PPEGrabHand side)
    {
        var id = logicalItem != null ? logicalItem.ItemId : string.Empty;
        var isRight = side == PPEGrabHand.Right;
        if (string.Equals(id, PPEEquipmentManager.IdInnerGloves, System.StringComparison.Ordinal))
            return isRight ? "RightInnerGlovePhysical" : "LeftInnerGlovePhysical";
        if (string.Equals(id, PPEEquipmentManager.IdChemicalBoots, System.StringComparison.Ordinal))
            return isRight ? "RightBootPhysical" : "LeftBootPhysical";
        return isRight ? "RightProtectiveGlovePhysical" : "LeftProtectiveGlovePhysical";
    }

    Transform GetOrCreateFollower(PPEGrabHand hand)
    {
        var parent = PPEHandTargetResolver.GetOrCreateTargetRoot();
        var existing = FindExistingFollower(hand);
        if (existing != null)
        {
            var existingFollower = existing.GetComponent<PPEGloveHandTargetFollower>();
            existingFollower?.UseAutoActive();
            existingFollower?.Configure(hand, PPEHandTargetResolver.ResolveControllerHand(hand));
            if (existing.parent != parent)
                existing.SetParent(parent, true);
            return existing;
        }

        var go = new GameObject(hand == PPEGrabHand.Right ? "RightGloveEquipTarget" : "LeftGloveEquipTarget");
        go.transform.SetParent(parent, false);
        var follower = go.AddComponent<PPEGloveHandTargetFollower>();
        follower.Configure(hand, PPEHandTargetResolver.ResolveControllerHand(hand));
        CacheFollower(follower);
        return go.transform;
    }

    static Transform FindExistingFollower(PPEGrabHand hand)
    {
        var cached = hand == PPEGrabHand.Right ? cachedRightFollower : cachedLeftFollower;
        if (cached != null)
            return cached.transform;

        var followers = FindObjectsByType<PPEGloveHandTargetFollower>(FindObjectsInactive.Include);
        for (var i = 0; i < followers.Length; i++)
        {
            if (followers[i] == null || followers[i].Hand != hand)
                continue;
            CacheFollower(followers[i]);
            return followers[i].transform;
        }

        return null;
    }

    static void CacheFollower(PPEGloveHandTargetFollower follower)
    {
        if (follower == null)
            return;
        if (follower.Hand == PPEGrabHand.Right)
            cachedRightFollower = follower;
        else if (follower.Hand == PPEGrabHand.Left)
            cachedLeftFollower = follower;
    }

    Transform FindGloveVisual(PPEGrabHand side)
    {
        var token = side == PPEGrabHand.Right ? "Right" : "Left";
        var transforms = GetComponentsInChildren<Transform>(true);
        Transform fallback = null;
        for (var i = 0; i < transforms.Length; i++)
        {
            var child = transforms[i];
            if (child == null || child == transform)
                continue;
            if (child.GetComponentInParent<PPEGlovePairMember>() != null)
                continue;
            if (child.name.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            if (child.GetComponent<MeshFilter>() != null || child.GetComponentInChildren<MeshFilter>(true) != null)
            {
                if (child.parent == transform || (child.parent != null && child.parent.name == "Visual"))
                    return child;
                if (fallback == null)
                    fallback = child;
            }
        }

        return fallback;
    }

    void ReleaseMemberInteraction(PPEGlovePairMember member, bool selectionAlreadyExited = false)
    {
        if (member == null)
            return;

        var grab = member.GetComponent<PPEHandGrabbable>();
        if (grab != null)
        {
            if (!selectionAlreadyExited)
            {
                var xr = grab.interactionManager != null
                    ? grab.interactionManager
                    : ResolveInteractionManager();
                if (xr != null)
                {
                    if (grab.isSelected)
                        xr.CancelInteractableSelection((IXRSelectInteractable)grab);
                    if (grab.isHovered)
                        xr.CancelInteractableHover((IXRHoverInteractable)grab);
                }
            }
            grab.enabled = false;
        }
    }

    void DeactivatePhysicalMember(PPEGlovePairMember member)
    {
        ReleaseMemberInteraction(member);
        if (member != null)
            member.gameObject.SetActive(false);
    }

    void ApplyWearableVisual()
    {
        if (logicalItem == null ||
            !string.Equals(logicalItem.ItemId, PPEEquipmentManager.IdChemicalGloves, System.StringComparison.Ordinal))
            return;

        var wearable = ResolveWearable();
        if (wearable == null)
            return;

        wearable.ApplyPpeRoomState(leftEquipped, rightEquipped);
    }

    PPEEquipmentManager ResolveEquipmentManager()
    {
        if (cachedEquipmentManager != null)
            return cachedEquipmentManager;
        cachedEquipmentManager = PPEEquipmentManager.Instance != null
            ? PPEEquipmentManager.Instance
            : FindAnyObjectByType<PPEEquipmentManager>();
        return cachedEquipmentManager;
    }

    ChemicalGlovesWearableVisual ResolveWearable()
    {
        if (cachedWearable != null)
            return cachedWearable;
        cachedWearable = ChemicalGlovesWearableVisual.FindActive();
        return cachedWearable;
    }

    XRInteractionManager ResolveInteractionManager()
    {
        if (cachedInteractionManager != null)
            return cachedInteractionManager;
        cachedInteractionManager = FindAnyObjectByType<XRInteractionManager>();
        return cachedInteractionManager;
    }

    static void CopyProximityDistance(PPEHandGrabbable source, PPEHandGrabbable destination)
    {
        if (source == null || destination == null)
            return;

        var sourceFilter = source.GetComponent<PPEDirectGrabProximityFilter>();
        var destinationFilter = destination.GetComponent<PPEDirectGrabProximityFilter>();
        if (sourceFilter == null || destinationFilter == null)
            return;

        destinationFilter.MaxDirectGrabDistance = sourceFilter.MaxDirectGrabDistance;
    }

    static void CopyRigidbody(Rigidbody source, Rigidbody destination)
    {
        if (destination == null)
            return;

        destination.mass = source != null ? source.mass : 0.35f;
        destination.useGravity = false;
        destination.isKinematic = true;
        destination.interpolation = RigidbodyInterpolation.Interpolate;
        destination.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        destination.linearVelocity = Vector3.zero;
        destination.angularVelocity = Vector3.zero;
    }

    static void CopyGrabSettings(PPEHandGrabbable source, PPEHandGrabbable destination)
    {
        if (destination == null)
            return;

        destination.selectMode = InteractableSelectMode.Single;
        destination.distanceCalculationMode = XRBaseInteractable.DistanceCalculationMode.ColliderPosition;
        destination.movementType = XRGrabInteractable.MovementType.Kinematic;
        destination.trackPosition = true;
        destination.trackRotation = true;
        destination.smoothPosition = true;
        destination.smoothRotation = true;
        destination.attachEaseInTime = 0f;
        destination.useDynamicAttach = true;
        destination.matchAttachPosition = true;
        destination.matchAttachRotation = true;
        destination.throwOnDetach = true;
        destination.forceGravityOnDetach = true;
        if (source == null)
            return;

        destination.interactionLayers = source.interactionLayers;
        destination.distanceCalculationMode = source.distanceCalculationMode;
        destination.movementType = source.movementType;
        destination.useDynamicAttach = source.useDynamicAttach;
        destination.matchAttachPosition = source.matchAttachPosition;
        destination.matchAttachRotation = source.matchAttachRotation;
        destination.throwOnDetach = source.throwOnDetach;
        destination.forceGravityOnDetach = source.forceGravityOnDetach;
        var member = destination.GetComponent<PPEGlovePairMember>();
        if (member != null && member.IsInnerGlove)
        {
            destination.throwOnDetach = false;
            destination.forceGravityOnDetach = false;
        }
    }
}
