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

    PPEGloveHandTargetFollower cachedTargetFollower;

    public PPEGlovePairCoordinator Coordinator => coordinator;
    public PPEGrabHand GloveSide => gloveSide;
    public Transform VisualRoot => visualRoot != null ? visualRoot : transform;
    public Transform EquipReference => equipReference;
    public Transform PlacementReference =>
        equipReference != null ? equipReference : transform;
    public GameObject GuidePrefab => guidePrefab;
    public bool RequireOppositeHand => requireOppositeHand;
    public PPEPairTargetKind TargetKind => targetKind;
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
        if (equipReference == null)
            return;
        Gizmos.color = new Color(0.15f, 0.95f, 1f, 0.95f);
        Gizmos.DrawWireSphere(equipReference.position, 0.012f);
        Gizmos.DrawLine(transform.position, equipReference.position);
    }
#endif
}
