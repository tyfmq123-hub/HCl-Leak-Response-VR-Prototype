using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public enum PPEPairTargetKind
{
    TrackedHands,
    BodyLowerLeg,
}

/// <summary>
/// One physical left/right piece belonging to a logical pair
/// (InnerGloves, ChemicalGloves, or ChemicalBoots).
/// Does not create its own sequence step or loadout id.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-250)]
public sealed class PPEGlovePairMember : MonoBehaviour
{
    public const string PhysicalColliderName = "PhysicalCollider";
    public const string AssistColliderName = "NearGrabAssist";
    public const string EquipReferenceName = "EquipReference";
    public const string GhostPoseCalibrationName = "GhostPoseCalibration";
    public const string GhostVisualOriginName = "GhostVisualOrigin";

    [SerializeField] PPEGlovePairCoordinator coordinator;
    [SerializeField] PPEGrabHand gloveSide = PPEGrabHand.Left;
    [SerializeField] Transform visualRoot;
    [SerializeField] Collider physicalCollider;
    [SerializeField] Collider assistCollider;
    [SerializeField, Range(0.04f, 0.07f)] float assistDistance = 0.05f;
    [SerializeField] GameObject guidePrefab;
    [SerializeField]
    [Tooltip("Palm / opening point used for placement distance. Created at the visual center when empty.")]
    Transform equipReference;
    [SerializeField]
    [Tooltip("Gloves require the opposite hand. Boots may be grabbed with either hand.")]
    bool requireOppositeHand = true;
    [SerializeField] PPEPairTargetKind targetKind = PPEPairTargetKind.TrackedHands;

    [Header("Ghost Pose Calibration")]
    [SerializeField]
    [Tooltip("Child Transform you rotate in the Inspector until the Ghost sits on the target. Runtime Ghost uses this local pose relative to the live target (palm or body yaw).")]
    Transform ghostPoseCalibration;
    [SerializeField]
    [Tooltip("Boot Ghost only. Applied after GhostPoseCalibration as a local yaw so look-down facing is ㅗ, not ㅜ. Does not rotate XR Origin, camera, or the lower-leg target.")]
    Vector3 bootModelAxisCorrectionEuler;
    [SerializeField]
    [Tooltip("Seeded onto GhostPoseCalibration when that child is first created.")]
    Vector3 guidePositionOffset;
    [SerializeField]
    [Tooltip("Seeded onto GhostPoseCalibration when that child is first created. Gloves: palm-relative. Boots: body-yaw-relative.")]
    Vector3 guideRotationOffsetEuler;
    [SerializeField, HideInInspector]
    [Tooltip("Mesh-origin compensation kept separate from the palm-relative visual offset. Not a runtime hand-position calibration.")]
    Vector3 ghostVisualOriginOffset;
    [SerializeField]
    [Tooltip("Development builds only: keeps this held glove Ghost visible while tuning GhostPoseCalibration in Play Mode.")]
    bool developmentRuntimeCalibrationMode;

    PPEGloveHandTargetFollower cachedTargetFollower;

    public PPEGlovePairCoordinator Coordinator => coordinator;
    public PPEGrabHand GloveSide => gloveSide;
    public Transform VisualRoot => visualRoot != null ? visualRoot : transform;
    public Collider PhysicalCollider => physicalCollider;
    public Collider AssistCollider => assistCollider;
    public Transform EquipReference => equipReference;
    public Transform PlacementReference =>
        equipReference != null ? equipReference : transform;
    public GameObject GuidePrefab => guidePrefab;
    public bool RequireOppositeHand => requireOppositeHand;
    public PPEPairTargetKind TargetKind => targetKind;
    public Transform GhostPoseCalibration => EnsureGhostPoseCalibration();
    public Transform GhostPoseCalibrationOrNull =>
        ghostPoseCalibration != null ? ghostPoseCalibration : transform.Find(GhostPoseCalibrationName);
    public Vector3 GuidePositionOffset =>
        ghostPoseCalibration != null ? ghostPoseCalibration.localPosition : guidePositionOffset;
    public Vector3 GuideRotationOffsetEuler =>
        ghostPoseCalibration != null ? ghostPoseCalibration.localEulerAngles : guideRotationOffsetEuler;
    public Quaternion GhostCalibrationLocalRotation =>
        ghostPoseCalibration != null ? ghostPoseCalibration.localRotation : Quaternion.Euler(guideRotationOffsetEuler);
    public Quaternion GhostVisualLocalRotation
    {
        get
        {
            var calibration = GhostCalibrationLocalRotation;
            if (targetKind != PPEPairTargetKind.BodyLowerLeg)
                return calibration;
            return calibration * Quaternion.Euler(bootModelAxisCorrectionEuler);
        }
    }
    public Vector3 BootModelAxisCorrectionEuler => bootModelAxisCorrectionEuler;
    public Vector3 GhostCalibrationLocalPosition =>
        ghostPoseCalibration != null ? ghostPoseCalibration.localPosition : guidePositionOffset;
    public Vector3 GhostVisualOriginOffset => ghostVisualOriginOffset;
    public bool DevelopmentRuntimeCalibrationMode
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return developmentRuntimeCalibrationMode;
#else
            return false;
#endif
        }
    }
    public PPEEquipable LogicalEquipable => coordinator != null ? coordinator.LogicalItem : null;

    public PPEEquipSlot TargetSlot
    {
        get
        {
            if (targetKind == PPEPairTargetKind.BodyLowerLeg)
                return gloveSide == PPEGrabHand.Right ? PPEEquipSlot.RightFoot : PPEEquipSlot.LeftFoot;
            return gloveSide == PPEGrabHand.Right ? PPEEquipSlot.RightHand : PPEEquipSlot.LeftHand;
        }
    }

    public PPEGrabHand TargetHand => gloveSide;

    public Transform TargetTransform =>
        coordinator != null ? coordinator.GetTrackedTarget(gloveSide) : null;

    public float PlacementDistance =>
        coordinator != null ? coordinator.PlacementDistance : 0.12f;

    public float PlacementAngleTolerance =>
        coordinator != null ? coordinator.PlacementAngleTolerance : 40f;

    public bool UseRotationValidation =>
        coordinator != null && coordinator.UseRotationValidation;

    public bool IsTargetTracking
    {
        get
        {
            if (targetKind == PPEPairTargetKind.BodyLowerLeg)
            {
                var lowerLeg = ResolveLowerLegFollower();
                return lowerLeg != null && lowerLeg.IsTracking;
            }

            var follower = ResolveTargetFollower();
            return follower != null && follower.IsTracking;
        }
    }

    public bool IsSameHandGrab(PPEGrabHand grabbingHand)
    {
        if (!requireOppositeHand)
            return false;
        return grabbingHand != PPEGrabHand.Unknown && grabbingHand == gloveSide;
    }

    public bool IsOppositeHandGrab(PPEGrabHand grabbingHand)
    {
        if (!requireOppositeHand)
            return grabbingHand != PPEGrabHand.Unknown;
        if (gloveSide == PPEGrabHand.Left)
            return grabbingHand == PPEGrabHand.Right;
        if (gloveSide == PPEGrabHand.Right)
            return grabbingHand == PPEGrabHand.Left;
        return false;
    }

    public Transform EnsureGhostPoseCalibration()
    {
        if (ghostPoseCalibration != null)
            return ghostPoseCalibration;

        var existing = transform.Find(GhostPoseCalibrationName);
        if (existing != null)
        {
            ghostPoseCalibration = existing;
            return ghostPoseCalibration;
        }

        var go = new GameObject(GhostPoseCalibrationName);
        go.transform.SetParent(transform, false);
        var seedPosition = guidePositionOffset;
        if (targetKind == PPEPairTargetKind.TrackedHands)
            seedPosition = Vector3.zero;
        go.transform.localPosition = seedPosition;
        go.transform.localRotation = Quaternion.Euler(guideRotationOffsetEuler);
        ghostPoseCalibration = go.transform;
        return ghostPoseCalibration;
    }

    public bool IsTrackedHandGlove => targetKind == PPEPairTargetKind.TrackedHands;

#if UNITY_EDITOR
    public void EditorSetGhostVisualOriginOffset(Vector3 localOffset)
    {
        ghostVisualOriginOffset = localOffset;
    }

    public void EditorResetGhostCalibration()
    {
        var calib = EnsureGhostPoseCalibration();
        if (calib == null)
            return;
        calib.localPosition = Vector3.zero;
        calib.localRotation = Quaternion.identity;
        guidePositionOffset = calib.localPosition;
        guideRotationOffsetEuler = Vector3.zero;
    }

    public void EditorCopyMirroredCalibrationFrom(PPEGlovePairMember source)
    {
        if (source == null || source == this)
            return;
        var src = source.EnsureGhostPoseCalibration();
        var dst = EnsureGhostPoseCalibration();
        if (src == null || dst == null)
            return;
        var p = src.localPosition;
        var q = src.localRotation;
        dst.localPosition = new Vector3(-p.x, p.y, p.z);
        dst.localRotation = new Quaternion(q.x, -q.y, -q.z, q.w);
        guidePositionOffset = dst.localPosition;
        guideRotationOffsetEuler = dst.localEulerAngles;
    }
#endif

    void Awake()
    {
        EnsureGhostPoseCalibration();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null)
                return;
            EnsureGhostPoseCalibration();
        };
    }
#endif

    public void Configure(
        PPEGlovePairCoordinator pair,
        PPEGrabHand side,
        Transform visual,
        GameObject dedicatedGuide)
    {
        coordinator = pair;
        gloveSide = side;
        visualRoot = visual;
        guidePrefab = dedicatedGuide;
        if (pair != null)
        {
            requireOppositeHand = pair.RequireOppositeHand;
            targetKind = pair.TargetKind;
        }
    }

    public void EditorSetVisualRoot(Transform visual)
    {
        visualRoot = visual;
    }

    public void EnsurePrimitiveInteractionVolumes()
    {
        DisableDenseConvexMeshColliders();
        physicalCollider = EnsurePhysicalCollider();
        assistCollider = EnsureAssistCollider();

        var stored = GetComponent<PPEStoredDisplayState>();
        if (stored != null)
            stored.UseAuthoredAssistCollider(assistCollider);

        var grab = GetComponent<PPEHandGrabbable>();
        RegisterCollider(grab, physicalCollider);
        RegisterCollider(grab, assistCollider);
        EnsureEquipReference();
    }

    public bool IsInnerGlove
    {
        get
        {
            var item = LogicalEquipable;
            return item != null &&
                   string.Equals(item.ItemId, PPEEquipmentManager.IdInnerGloves, System.StringComparison.Ordinal);
        }
    }

    public Transform EnsureEquipReference()
    {
        if (equipReference != null)
            return equipReference;

        var existing = transform.Find(EquipReferenceName);
        if (existing != null)
        {
            equipReference = existing;
            return existing;
        }

        equipReference = PPEEquipReferenceUtility.Ensure(transform, VisualRoot);
        if (IsInnerGlove)
            PPEEquipReferenceUtility.PlaceAtGlovePalmOpening(equipReference, VisualRoot);
        return PlacementReference;
    }

    public bool TryCompletePlacement(PPEGrabHand grabbingHand)
    {
        if (coordinator == null)
            return false;
        if (IsSameHandGrab(grabbingHand))
            return false;
        if (!IsTargetTracking)
            return false;

        return coordinator.NotifySideEquipped(this);
    }

    PPEGloveHandTargetFollower ResolveTargetFollower()
    {
        if (cachedTargetFollower != null)
            return cachedTargetFollower;

        var target = TargetTransform;
        cachedTargetFollower = target != null
            ? target.GetComponent<PPEGloveHandTargetFollower>()
            : null;
        return cachedTargetFollower;
    }

    PPELowerLegTargetFollower ResolveLowerLegFollower()
    {
        var target = TargetTransform;
        return target != null ? target.GetComponent<PPELowerLegTargetFollower>() : null;
    }

    Collider EnsurePhysicalCollider()
    {
        if (physicalCollider != null && physicalCollider.enabled && !physicalCollider.isTrigger)
            return physicalCollider;

        var existing = transform.Find(PhysicalColliderName);
        var root = existing != null ? existing.gameObject : new GameObject(PhysicalColliderName);
        if (existing == null)
        {
            root.transform.SetParent(transform, false);
            root.layer = gameObject.layer;
        }

        ComputeLocalVisualBounds(out var center, out var size);
        var capsule = root.GetComponent<CapsuleCollider>();
        if (capsule == null)
            capsule = root.AddComponent<CapsuleCollider>();

        var longest = LongestAxis(size);
        capsule.direction = longest;
        capsule.center = center;
        capsule.height = Mathf.Max(GetAxis(size, longest), 0.04f);
        capsule.radius = Mathf.Max(SecondaryRadius(size, longest) * 0.5f, 0.015f);
        capsule.isTrigger = false;
        capsule.enabled = true;
        physicalCollider = capsule;
        return capsule;
    }

    Collider EnsureAssistCollider()
    {
        if (assistCollider != null)
            return assistCollider;

        var existing = transform.Find(AssistColliderName);
        var root = existing != null ? existing.gameObject : new GameObject(AssistColliderName);
        if (existing == null)
        {
            root.transform.SetParent(transform, false);
            root.layer = gameObject.layer;
        }

        ComputeLocalVisualBounds(out var center, out var size);
        var padded = size + Vector3.one * (assistDistance * 2f);

        var box = root.GetComponent<BoxCollider>();
        if (box == null)
            box = root.AddComponent<BoxCollider>();
        box.center = center;
        box.size = Vector3.Max(padded, Vector3.one * 0.04f);
        box.isTrigger = true;
        box.enabled = true;
        assistCollider = box;
        return box;
    }

    void DisableDenseConvexMeshColliders()
    {
        var meshColliders = GetComponentsInChildren<MeshCollider>(true);
        for (var i = 0; i < meshColliders.Length; i++)
        {
            var meshCollider = meshColliders[i];
            if (meshCollider == null || !meshCollider.convex || meshCollider.sharedMesh == null)
                continue;
            if (meshCollider.sharedMesh.vertexCount <= 255)
                continue;
            meshCollider.enabled = false;
        }
    }

    void ComputeLocalVisualBounds(out Vector3 localCenter, out Vector3 localSize)
    {
        var source = VisualRoot;
        var meshFilters = source.GetComponentsInChildren<MeshFilter>(true);
        var hasBounds = false;
        var min = Vector3.zero;
        var max = Vector3.zero;

        for (var i = 0; i < meshFilters.Length; i++)
        {
            var filter = meshFilters[i];
            if (filter == null || filter.sharedMesh == null)
                continue;

            var meshBounds = filter.sharedMesh.bounds;
            EncapsulateLocalCorners(filter.transform, meshBounds, ref hasBounds, ref min, ref max);
        }

        if (!hasBounds)
        {
            var renderers = source.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;
                EncapsulateLocalCorners(renderer.transform, renderer.localBounds, ref hasBounds, ref min, ref max);
            }
        }

        if (!hasBounds)
        {
            localCenter = Vector3.zero;
            localSize = new Vector3(0.08f, 0.18f, 0.08f);
            return;
        }

        localCenter = (min + max) * 0.5f;
        localSize = max - min;
        localSize = Vector3.Max(localSize, new Vector3(0.03f, 0.03f, 0.03f));
    }

    void EncapsulateLocalCorners(
        Transform source,
        Bounds sourceLocalBounds,
        ref bool hasBounds,
        ref Vector3 min,
        ref Vector3 max)
    {
        var extents = sourceLocalBounds.extents;
        var center = sourceLocalBounds.center;
        for (var x = -1; x <= 1; x += 2)
        for (var y = -1; y <= 1; y += 2)
        for (var z = -1; z <= 1; z += 2)
        {
            var localCorner = center + Vector3.Scale(extents, new Vector3(x, y, z));
            var world = source.TransformPoint(localCorner);
            var memberLocal = transform.InverseTransformPoint(world);
            if (!hasBounds)
            {
                min = max = memberLocal;
                hasBounds = true;
            }
            else
            {
                min = Vector3.Min(min, memberLocal);
                max = Vector3.Max(max, memberLocal);
            }
        }
    }

    static int LongestAxis(Vector3 size)
    {
        if (size.y >= size.x && size.y >= size.z)
            return 1;
        if (size.z >= size.x)
            return 2;
        return 0;
    }

    static float GetAxis(Vector3 size, int axis)
    {
        switch (axis)
        {
            case 1: return size.y;
            case 2: return size.z;
            default: return size.x;
        }
    }

    static float SecondaryRadius(Vector3 size, int longestAxis)
    {
        switch (longestAxis)
        {
            case 1: return Mathf.Max(size.x, size.z);
            case 2: return Mathf.Max(size.x, size.y);
            default: return Mathf.Max(size.y, size.z);
        }
    }

    static void RegisterCollider(XRGrabInteractable grab, Collider col)
    {
        if (grab == null || col == null)
            return;
        if (!grab.colliders.Contains(col))
            grab.colliders.Add(col);
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (equipReference != null)
        {
            Gizmos.color = new Color(0.15f, 0.95f, 1f, 0.95f);
            Gizmos.DrawWireSphere(equipReference.position, 0.012f);
            Gizmos.DrawLine(transform.position, equipReference.position);
        }

        DrawGhostPosePreview();
    }

    void DrawGhostPosePreview()
    {
        // Tracked-hand glove preview is owned by the editor calibration tool,
        // which draws the actual visible hand mesh and Palm-local handles.
        if (targetKind == PPEPairTargetKind.TrackedHands)
            return;

        var calib = GhostPoseCalibrationOrNull;
        if (calib == null)
            return;

        var target = TargetTransform;
        Vector3 previewPos;
        Quaternion previewRot;
        if (target != null)
        {
            previewPos = target.TransformPoint(calib.localPosition);
            previewRot = target.rotation * calib.localRotation;
        }
        else
        {
            previewPos = transform.TransformPoint(calib.localPosition);
            previewRot = transform.rotation * calib.localRotation;
        }

        Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.9f);
        Gizmos.matrix = Matrix4x4.TRS(previewPos, previewRot, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(0.06f, 0.04f, 0.12f));
        Gizmos.color = Color.red;
        Gizmos.DrawLine(Vector3.zero, Vector3.right * 0.08f);
        Gizmos.color = Color.green;
        Gizmos.DrawLine(Vector3.zero, Vector3.up * 0.08f);
        Gizmos.color = Color.blue;
        Gizmos.DrawLine(Vector3.zero, Vector3.forward * 0.08f);
        Gizmos.matrix = Matrix4x4.identity;

        var source = VisualRoot;
        if (source == null)
            return;
        var filters = source.GetComponentsInChildren<MeshFilter>(true);
        Gizmos.color = new Color(0.35f, 0.9f, 1f, 0.18f);
        for (var i = 0; i < filters.Length; i++)
        {
            var filter = filters[i];
            if (filter == null || filter.sharedMesh == null)
                continue;
            var scale = filter.transform.lossyScale;
            Gizmos.DrawMesh(filter.sharedMesh, previewPos, previewRot, scale);
        }
    }
#endif
}
