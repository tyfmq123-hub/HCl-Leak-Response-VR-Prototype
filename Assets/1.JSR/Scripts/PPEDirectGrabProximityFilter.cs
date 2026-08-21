using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// PPE-only XRI select filter. Common NearFarInteractor cast radius is left
/// unchanged; this rejects a select when the hand is not close enough to a
/// physical grab collider. It never moves the PPE toward the hand.
/// </summary>
[DisallowMultipleComponent]
public sealed class PPEDirectGrabProximityFilter : MonoBehaviour, IXRSelectFilter
{
    const float DefaultMaxDirectGrabDistance = 0.06f;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Maximum distance from the hand/interactor to a physical PPE collider. Start around 0.05–0.07 m.")]
    float maxDirectGrabDistance = DefaultMaxDirectGrabDistance;

    XRBaseInteractable interactable;

    public bool canProcess => isActiveAndEnabled;

    public float MaxDirectGrabDistance
    {
        get => maxDirectGrabDistance;
        set => maxDirectGrabDistance = Mathf.Max(0.01f, value);
    }

    void Awake()
    {
        interactable = GetComponent<XRBaseInteractable>();
    }

    void OnEnable()
    {
        RegisterWithInteractable();
    }

    public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable target)
    {
        if (interactor == null || target == null)
            return false;

        // Keep an already-held grab. Distance is only used to accept the initial select.
        if (target.isSelected && target.interactorsSelecting.Contains(interactor))
            return true;

        var handPosition = GetHandPosition(interactor, target);
        var distance = DistanceToPhysicalGrabSurface(target, handPosition);
        return distance <= maxDirectGrabDistance;
    }

    public void RegisterWithInteractable()
    {
        if (interactable == null)
            interactable = GetComponent<XRBaseInteractable>();
        if (interactable == null)
            return;

        if (!interactable.startingSelectFilters.Contains(this))
            interactable.startingSelectFilters.Add(this);
    }

    public void RegisterRuntimeSelectFilter()
    {
        if (interactable == null)
            interactable = GetComponent<XRBaseInteractable>();
        if (interactable == null || !Application.isPlaying)
            return;

        if (!SelectFiltersContains(interactable, this))
            interactable.selectFilters.Add(this);
    }

    static bool SelectFiltersContains(XRBaseInteractable host, IXRSelectFilter filter)
    {
        if (host == null || filter == null)
            return false;

        var filters = host.selectFilters;
        for (var i = 0; i < filters.count; i++)
        {
            if (filters.GetAt(i) == filter)
                return true;
        }

        return false;
    }

    static Vector3 GetHandPosition(IXRSelectInteractor interactor, IXRSelectInteractable target)
    {
        var selectingHand = PPEGrabHandResolver.Resolve(interactor, target.transform);
        if (selectingHand != PPEGrabHand.Unknown &&
            PPEXrHandsJointPose.TryGetWorldPose(selectingHand, XRHandJointID.Palm, out var palmPose))
        {
            return palmPose.position;
        }

        if (interactor is IXRInteractor xrInteractor)
        {
            var attach = xrInteractor.GetAttachTransform(target);
            if (attach != null)
                return attach.position;
        }

        return interactor.transform.position;
    }

    static float DistanceToPhysicalGrabSurface(IXRSelectInteractable target, Vector3 handPosition)
    {
        var closest = float.PositiveInfinity;
        var colliders = target.colliders;
        if (colliders != null)
        {
            for (var i = 0; i < colliders.Count; i++)
            {
                var col = colliders[i];
                if (!IsPhysicalGrabCollider(col))
                    continue;
                closest = Mathf.Min(closest, DistanceToCollider(col, handPosition));
            }
        }

        if (!float.IsPositiveInfinity(closest))
            return closest;

        var host = target.transform;
        var fallback = host.GetComponentsInChildren<Collider>(true);
        for (var i = 0; i < fallback.Length; i++)
        {
            var col = fallback[i];
            if (!IsPhysicalGrabCollider(col))
                continue;
            closest = Mathf.Min(closest, DistanceToCollider(col, handPosition));
        }

        return float.IsPositiveInfinity(closest)
            ? Vector3.Distance(handPosition, host.position)
            : closest;
    }

    static bool IsPhysicalGrabCollider(Collider col)
    {
        if (col == null || !col.enabled || !col.gameObject.activeInHierarchy)
            return false;
        if (col.isTrigger)
            return false;
        if (col.name.IndexOf("NearGrabAssist", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return false;
        return true;
    }

    static float DistanceToCollider(Collider col, Vector3 handPosition)
    {
        // ClosestPoint is unreliable on non-uniformly scaled colliders.
        // Prefer a point on the collider world AABB, then ClosestPoint when scale is uniform.
        var lossy = col.transform.lossyScale;
        var nonUniform = Mathf.Abs(lossy.x - lossy.y) > 0.01f ||
                         Mathf.Abs(lossy.y - lossy.z) > 0.01f ||
                         Mathf.Abs(lossy.x - lossy.z) > 0.01f;
        if (nonUniform)
            return DistanceToBounds(col.bounds, handPosition);

        var closestPoint = col.ClosestPoint(handPosition);
        return Vector3.Distance(handPosition, closestPoint);
    }

    static float DistanceToBounds(Bounds bounds, Vector3 handPosition)
    {
        var closest = bounds.ClosestPoint(handPosition);
        return Vector3.Distance(handPosition, closest);
    }

#if UNITY_EDITOR
    public void EditorApplyDefaults()
    {
        maxDirectGrabDistance = DefaultMaxDirectGrabDistance;
    }

    public void EditorApplyDefaults(float distance)
    {
        maxDirectGrabDistance = Mathf.Max(0.01f, distance);
    }
#endif
}
