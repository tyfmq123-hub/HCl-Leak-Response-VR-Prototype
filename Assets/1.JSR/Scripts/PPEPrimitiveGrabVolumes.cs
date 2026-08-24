using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Builds a physical grab collider plus a slightly larger NearGrabAssist from
/// the item's visual mesh bounds. Used by single wearable PPE (not glove pairs).
/// Does not move the item or change sequence/equip rules.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-220)]
public sealed class PPEPrimitiveGrabVolumes : MonoBehaviour
{
    public const string PhysicalColliderName = "PhysicalCollider";
    public const string AssistColliderName = "NearGrabAssist";

    [SerializeField, Range(0.04f, 0.07f)] float assistDistance = 0.05f;
    [SerializeField] Collider physicalCollider;
    [SerializeField] Collider assistCollider;

    public Collider PhysicalCollider => physicalCollider;
    public Collider AssistCollider => assistCollider;

    void Awake()
    {
        Rebuild();
    }

    public static PPEPrimitiveGrabVolumes EnsureForWearable(GameObject host)
    {
        if (host == null)
            return null;
        if (host.GetComponent<PPEGlovePairMember>() != null)
            return null;
        if (host.GetComponent<PPEGlovePairCoordinator>() != null)
            return null;

        var grab = host.GetComponent<PPEHandGrabbable>();
        if (grab == null)
            return null;

        var equipable = host.GetComponent<PPEEquipable>();
        if (equipable != null &&
            string.Equals(equipable.ItemId, PPEEquipmentManager.IdResponseBag, System.StringComparison.Ordinal))
            return null;

        var volumes = host.GetComponent<PPEPrimitiveGrabVolumes>();
        if (volumes == null)
            volumes = host.AddComponent<PPEPrimitiveGrabVolumes>();
        volumes.Rebuild();
        return volumes;
    }

    public void Rebuild()
    {
        DisableDenseConvexMeshColliders();
        DisableRootPhysicalBoxIfReplaced();
        physicalCollider = EnsurePhysicalCollider();
        assistCollider = EnsureAssistCollider();

        var stored = GetComponent<PPEStoredDisplayState>();
        if (stored != null)
        {
            stored.UseAuthoredAssistCollider(assistCollider);
            stored.IgnorePlayerLocomotionCollisions();
        }

        var grab = GetComponent<PPEHandGrabbable>();
        RegisterCollider(grab, physicalCollider);
        RegisterCollider(grab, assistCollider);
        RemoveDisabledFromGrab(grab);
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
        size = Vector3.Max(size, new Vector3(0.06f, 0.04f, 0.08f));

        var box = root.GetComponent<BoxCollider>();
        if (box == null)
            box = root.AddComponent<BoxCollider>();
        box.center = center;
        box.size = size;
        box.isTrigger = false;
        box.enabled = true;
        physicalCollider = box;
        return box;
    }

    Collider EnsureAssistCollider()
    {
        var existing = transform.Find(AssistColliderName);
        var root = existing != null ? existing.gameObject : new GameObject(AssistColliderName);
        if (existing == null)
        {
            root.transform.SetParent(transform, false);
            root.layer = gameObject.layer;
        }

        ComputeLocalVisualBounds(out var center, out var size);
        var padded = Vector3.Max(size + Vector3.one * (assistDistance * 2f), new Vector3(0.08f, 0.06f, 0.10f));

        var box = root.GetComponent<BoxCollider>();
        if (box == null)
            box = root.AddComponent<BoxCollider>();
        box.center = center;
        box.size = padded;
        box.isTrigger = true;
        box.enabled = true;
        assistCollider = box;
        return box;
    }

    void DisableRootPhysicalBoxIfReplaced()
    {
        var rootBox = GetComponent<BoxCollider>();
        if (rootBox == null)
            return;
        rootBox.enabled = false;
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
        var visual = transform.Find("Visual");
        var source = visual != null ? visual : transform;
        var meshFilters = source.GetComponentsInChildren<MeshFilter>(true);
        var hasBounds = false;
        var min = Vector3.zero;
        var max = Vector3.zero;

        for (var i = 0; i < meshFilters.Length; i++)
        {
            var filter = meshFilters[i];
            if (filter == null || filter.sharedMesh == null)
                continue;
            EncapsulateLocalCorners(filter.transform, filter.sharedMesh.bounds, ref hasBounds, ref min, ref max);
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
            localSize = new Vector3(0.12f, 0.05f, 0.22f);
            return;
        }

        localCenter = (min + max) * 0.5f;
        localSize = Vector3.Max(max - min, new Vector3(0.04f, 0.03f, 0.04f));
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
            var local = transform.InverseTransformPoint(world);
            if (!hasBounds)
            {
                min = max = local;
                hasBounds = true;
            }
            else
            {
                min = Vector3.Min(min, local);
                max = Vector3.Max(max, local);
            }
        }
    }

    static void RegisterCollider(XRGrabInteractable grab, Collider col)
    {
        if (grab == null || col == null)
            return;
        if (!grab.colliders.Contains(col))
            grab.colliders.Add(col);
    }

    static void RemoveDisabledFromGrab(XRGrabInteractable grab)
    {
        if (grab == null)
            return;
        for (var i = grab.colliders.Count - 1; i >= 0; i--)
        {
            var col = grab.colliders[i];
            if (col == null || !col.enabled)
                grab.colliders.RemoveAt(i);
        }
    }
}
