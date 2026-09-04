using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// PPE-only XRI select filter. Common NearFarInteractor cast radius is left
/// unchanged; this rejects a far-ray select when the hand attach is not close
/// enough to a grab collider. It never moves the PPE toward the hand.
/// Tracked-hand gloves use the same physical-collider distance as other PPE.
/// NearGrabAssist remains a hover trigger only; it does not enlarge Grip range.
/// </summary>
[DisallowMultipleComponent]
public sealed class PPEDirectGrabProximityFilter : MonoBehaviour, IXRSelectFilter
{
    const float DefaultMaxDirectGrabDistance = 0.06f;

    /// <summary>
    /// Same as other cabinet PPE physical grab range. Kept as a named constant
    /// so glove authoring and SelectAttempt logs stay aligned with serialized filters.
    /// </summary>
    public const float NearGrabHoverSelectDistance = DefaultMaxDirectGrabDistance;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Maximum distance from the selecting interactor attach to a physical grab collider.")]
    float maxDirectGrabDistance = DefaultMaxDirectGrabDistance;

    XRBaseInteractable interactable;
    PPEGlovePairMember cachedGloveMember;
    bool resolvedGloveMember;
    string lastGloveSelectAttemptLog;

    public bool canProcess => isActiveAndEnabled;

    public float MaxDirectGrabDistance
    {
        get => UsesGloveNearGrabVolume ? NearGrabHoverSelectDistance : maxDirectGrabDistance;
        set => maxDirectGrabDistance = Mathf.Max(0.01f, value);
    }

    public bool UsesGloveNearGrabVolume
    {
        get
        {
            var member = ResolveGloveMember();
            return member != null && member.IsTrackedHandGlove;
        }
    }

    void Awake()
    {
        interactable = GetComponent<XRBaseInteractable>();
        if (UsesGloveNearGrabVolume)
            maxDirectGrabDistance = NearGrabHoverSelectDistance;
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

        if (UsesGloveNearGrabVolume)
            return ProcessGloveSelectAttempt(interactor, target);

        return IsNearGrab(interactor, target);
    }

    bool ProcessGloveSelectAttempt(IXRSelectInteractor interactor, IXRSelectInteractable target)
    {
        var member = ResolveGloveMember();
        var attach = ResolveGloveSelectOrigin(interactor, target, member);
        var attachUsable = attach != null && attach.gameObject.activeInHierarchy;
        var handPosition = attachUsable ? attach.position : interactor.transform.position;
        var physicalDistance = DistanceToNamedCollider(target, handPosition, PPEGlovePairMember.PhysicalColliderName, false);
        var assistDistance = DistanceToNamedCollider(target, handPosition, PPEGlovePairMember.AssistColliderName, true);
        var allowed = MaxDirectGrabDistance;
        var nearAssistHit = !float.IsPositiveInfinity(assistDistance) &&
                            assistDistance <= allowed;
        var physicalHit = !float.IsPositiveInfinity(physicalDistance) &&
                          physicalDistance <= allowed;
        // Select uses the physical collider only, matching other PPE. Assist is logged, not an allow path.
        var distance = physicalDistance;
        if (float.IsPositiveInfinity(distance))
            distance = DistanceToGrabSurface(target, handPosition, includeNearGrabAssist: false);

        var resolvedHand = PPEGrabHandResolver.Resolve(interactor, target.transform);
        var pair = member != null ? member.Coordinator : null;
        var bothUnequipped = pair == null || (!pair.LeftEquipped && !pair.RightEquipped);
        var correctHand = member != null && member.IsOppositeHandGrab(resolvedHand);
        var filterAllows = true;
        var firstFailure = "NONE";

        if (member == null || !member.isActiveAndEnabled)
        {
            filterAllows = false;
            firstFailure = "PAIR_STATE_INVALID";
        }
        else if (distance > 0.5f)
        {
            filterAllows = false;
            firstFailure = "NOT_NEAR_INTERACTOR";
        }
        else if (!physicalHit)
        {
            filterAllows = false;
            firstFailure = "TOO_FAR";
        }

        if (filterAllows && !attachUsable)
            firstFailure = "NO_ATTACH_TRANSFORM";
        else if (filterAllows && resolvedHand == PPEGrabHand.Unknown)
            firstFailure = "HAND_UNRESOLVED";
        else if (filterAllows && member.RequireOppositeHand && !correctHand)
            firstFailure = "WRONG_HAND";

        LogGloveSelectAttempt(
            member,
            pair,
            interactor,
            attach,
            resolvedHand,
            bothUnequipped,
            correctHand,
            distance,
            physicalDistance,
            assistDistance,
            nearAssistHit,
            filterAllows,
            firstFailure);

        return filterAllows;
    }

    public bool IsNearGrab(IXRSelectInteractor interactor, IXRSelectInteractable target)
    {
        return EvaluateDistance(interactor, target) <= MaxDirectGrabDistance;
    }

    public float EvaluateDistance(IXRSelectInteractor interactor, IXRSelectInteractable target)
    {
        if (interactor == null || target == null)
            return float.PositiveInfinity;
        var attach = ResolveSelectAttach(interactor, target);
        var handPosition = attach != null ? attach.position : interactor.transform.position;
        return DistanceToGrabSurface(target, handPosition, includeNearGrabAssist: false);
    }

    public static Transform ResolveSelectAttach(IXRSelectInteractor interactor, IXRSelectInteractable target)
    {
        if (interactor is IXRInteractor xrInteractor)
            return xrInteractor.GetAttachTransform(target);
        return interactor != null ? interactor.transform : null;
    }

    static Transform ResolveGloveSelectOrigin(
        IXRSelectInteractor interactor,
        IXRSelectInteractable target,
        PPEGlovePairMember member)
    {
        if (IsProtectiveGloveMember(member) &&
            interactor is NearFarInteractor nearFar)
        {
            var caster = nearFar.nearInteractionCaster;
            var origin = caster != null ? caster.effectiveCastOrigin : null;
            if (origin != null && origin.gameObject.activeInHierarchy)
                return origin;
            origin = caster != null ? caster.castOrigin : null;
            if (origin != null && origin.gameObject.activeInHierarchy)
                return origin;
        }

        return ResolveSelectAttach(interactor, target);
    }

    static bool IsProtectiveGloveMember(PPEGlovePairMember member)
    {
        var item = member != null ? member.LogicalEquipable : null;
        return item != null &&
               string.Equals(
                   item.ItemId,
                   PPEEquipmentManager.IdChemicalGloves,
                   System.StringComparison.Ordinal);
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

    PPEGlovePairMember ResolveGloveMember()
    {
        if (!resolvedGloveMember)
        {
            cachedGloveMember = GetComponent<PPEGlovePairMember>();
            resolvedGloveMember = true;
        }

        return cachedGloveMember;
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

    void LogGloveSelectAttempt(
        PPEGlovePairMember member,
        PPEGlovePairCoordinator pair,
        IXRSelectInteractor interactor,
        Transform attach,
        PPEGrabHand resolvedHand,
        bool firstGlove,
        bool correctHand,
        float distance,
        float physicalDistance,
        float assistDistance,
        bool nearAssistHit,
        bool filterAllows,
        string firstFailure)
    {
        var interactorGo = interactor != null ? interactor.transform.gameObject : null;
        var key =
            (firstGlove ? "first|" : "second|") +
            (member != null ? member.GloveSide.ToString() : "?") + "|" +
            (interactorGo != null ? interactorGo.name : "none") + "|" +
            resolvedHand + "|" +
            (pair != null && pair.LeftEquipped) + "|" +
            (pair != null && pair.RightEquipped) + "|" +
            correctHand + "|" +
            filterAllows + "|" +
            firstFailure;
        if (key == lastGloveSelectAttemptLog)
            return;
        lastGloveSelectAttemptLog = key;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var title = firstGlove ? "[First Glove SelectAttempt]" : "[Glove SelectAttempt]";
        Debug.Log(
            title + "\n" +
            "GloveSide=" + (member != null ? member.GloveSide.ToString() : "?") + "\n" +
            "InteractorType=" + (interactor != null ? interactor.GetType().Name : "none") + "\n" +
            "InteractorName=" + (interactorGo != null ? interactorGo.name : "none") + "\n" +
            "ResolvedHand=" + resolvedHand + "\n" +
            "AttachTransform=" + (attach != null ? attach.name : "none") + "\n" +
            "OriginSource=" + OriginSourceLabel(interactor, member, attach) + "\n" +
            "Distance=" + FormatDistance(distance) + "\n" +
            "AllowedDistance=" + MaxDirectGrabDistance.ToString("F3") + "\n" +
            "NearAssistHit=" + nearAssistHit + "\n" +
            "CorrectHand=" + correctHand + "\n" +
            "LeftEquipped=" + (pair != null && pair.LeftEquipped) + "\n" +
            "RightEquipped=" + (pair != null && pair.RightEquipped) + "\n" +
            "FilterAllows=" + filterAllows + "\n" +
            "FirstFailure=" + firstFailure,
            member != null ? member : (Component)this);
#endif
    }

    static string OriginSourceLabel(
        IXRSelectInteractor interactor,
        PPEGlovePairMember member,
        Transform attach)
    {
        if (!IsProtectiveGloveMember(member) || interactor is not NearFarInteractor nearFar)
            return "Attach";

        var caster = nearFar.nearInteractionCaster;
        if (caster != null &&
            (attach == caster.effectiveCastOrigin || attach == caster.castOrigin))
            return "NearCaster";

        return "Attach";
    }

    static string FormatDistance(float distance)
    {
        return float.IsPositiveInfinity(distance) ? "n/a" : distance.ToString("F3");
    }

    static float DistanceToNamedCollider(
        IXRSelectInteractable target,
        Vector3 handPosition,
        string nameToken,
        bool allowTrigger)
    {
        var closest = float.PositiveInfinity;
        if (target == null)
            return closest;

        var colliders = target.colliders;
        if (colliders != null)
        {
            for (var i = 0; i < colliders.Count; i++)
                closest = Mathf.Min(closest, NamedColliderDistance(colliders[i], handPosition, nameToken, allowTrigger));
        }

        if (!float.IsPositiveInfinity(closest))
            return closest;

        var fallback = target.transform.GetComponentsInChildren<Collider>(true);
        for (var i = 0; i < fallback.Length; i++)
            closest = Mathf.Min(closest, NamedColliderDistance(fallback[i], handPosition, nameToken, allowTrigger));
        return closest;
    }

    static float NamedColliderDistance(
        Collider col,
        Vector3 handPosition,
        string nameToken,
        bool allowTrigger)
    {
        if (col == null || !col.enabled || !col.gameObject.activeInHierarchy)
            return float.PositiveInfinity;
        if (col.name.IndexOf(nameToken, System.StringComparison.OrdinalIgnoreCase) < 0)
            return float.PositiveInfinity;
        if (col.isTrigger && !allowTrigger)
            return float.PositiveInfinity;
        return DistanceToCollider(col, handPosition);
    }

    static float DistanceToGrabSurface(
        IXRSelectInteractable target,
        Vector3 handPosition,
        bool includeNearGrabAssist)
    {
        var closest = float.PositiveInfinity;
        var colliders = target.colliders;
        if (colliders != null)
        {
            for (var i = 0; i < colliders.Count; i++)
            {
                var col = colliders[i];
                if (!IsSelectDistanceCollider(col, includeNearGrabAssist))
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
            if (!IsSelectDistanceCollider(col, includeNearGrabAssist))
                continue;
            closest = Mathf.Min(closest, DistanceToCollider(col, handPosition));
        }

        return float.IsPositiveInfinity(closest)
            ? Vector3.Distance(handPosition, host.position)
            : closest;
    }

    static bool IsSelectDistanceCollider(Collider col, bool includeNearGrabAssist)
    {
        if (col == null || !col.enabled || !col.gameObject.activeInHierarchy)
            return false;

        // Assist stays a trigger. It is measured for glove select distance only
        // so Highlight and Grip share a volume. It is never a physics grab collider.
        if (includeNearGrabAssist &&
            col.name.IndexOf("NearGrabAssist", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        return !col.isTrigger;
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
