using UnityEngine;

/// <summary>
/// Creates a stable placement point at the visual center of a PPE piece.
/// Distance is measured from this point to the live target, not an FBX pivot.
/// </summary>
public static class PPEEquipReferenceUtility
{
    public const string ChildName = "EquipReference";

    public static Transform Ensure(Transform owner, Transform visualRoot)
    {
        if (owner == null)
            return null;

        var existing = owner.Find(ChildName);
        if (existing != null)
            return existing;

        var go = new GameObject(ChildName);
        go.transform.SetParent(owner, false);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        go.transform.localPosition = ComputeVisualCenterLocal(owner, visualRoot != null ? visualRoot : owner);
        return go.transform;
    }

    /// <summary>
    /// Upper torso / chest of a hanging or held suit, not the full-mesh AABB center.
    /// </summary>
    public static void PlaceAtSuitChest(Transform reference, Transform visualRoot)
    {
        if (reference == null || visualRoot == null)
            return;

        var owner = reference.parent != null ? reference.parent : visualRoot;
        var filters = visualRoot.GetComponentsInChildren<MeshFilter>(true);
        var hasBounds = false;
        var min = Vector3.zero;
        var max = Vector3.zero;
        for (var i = 0; i < filters.Length; i++)
        {
            var filter = filters[i];
            if (filter == null || filter.sharedMesh == null)
                continue;
            Encapsulate(owner, filter.transform, filter.sharedMesh.bounds, ref hasBounds, ref min, ref max);
        }

        if (!hasBounds)
        {
            reference.localPosition = new Vector3(0f, 0.55f, 0f);
            return;
        }

        var size = max - min;
        reference.localPosition = new Vector3(
            (min.x + max.x) * 0.5f,
            min.y + size.y * 0.72f,
            (min.z + max.z) * 0.5f);
        reference.localRotation = Quaternion.identity;
    }

    /// <summary>
    /// Places the reference at the glove opening / palm, not the fingertip AABB center.
    /// Mesh +Z is fingers; min Z is the wrist opening.
    /// </summary>
    public static void PlaceAtGlovePalmOpening(Transform reference, Transform visualRoot)
    {
        if (reference == null || visualRoot == null)
            return;

        var filter = visualRoot.GetComponentInChildren<MeshFilter>(true);
        if (filter == null || filter.sharedMesh == null)
            return;

        var bounds = filter.sharedMesh.bounds;
        var opening = new Vector3(
            bounds.center.x,
            bounds.center.y,
            bounds.min.z + bounds.size.z * 0.18f);
        reference.position = filter.transform.TransformPoint(opening);
        reference.rotation = filter.transform.rotation;
    }

    static Vector3 ComputeVisualCenterLocal(Transform owner, Transform visualRoot)
    {
        var filters = visualRoot.GetComponentsInChildren<MeshFilter>(true);
        var hasBounds = false;
        var min = Vector3.zero;
        var max = Vector3.zero;

        for (var i = 0; i < filters.Length; i++)
        {
            var filter = filters[i];
            if (filter == null || filter.sharedMesh == null)
                continue;
            Encapsulate(owner, filter.transform, filter.sharedMesh.bounds, ref hasBounds, ref min, ref max);
        }

        if (!hasBounds)
        {
            var renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;
                Encapsulate(owner, renderer.transform, renderer.localBounds, ref hasBounds, ref min, ref max);
            }
        }

        return hasBounds ? (min + max) * 0.5f : Vector3.zero;
    }

    static void Encapsulate(
        Transform owner,
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
            var memberLocal = owner.InverseTransformPoint(source.TransformPoint(localCorner));
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
}
