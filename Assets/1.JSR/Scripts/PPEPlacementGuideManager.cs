using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public enum PPEPlacementGuideState
{
    NotReady,
    Ready,
}

/// <summary>
/// Shows placement guides while PPE is grabbed, evaluates the authored distance
/// and rotation tolerances, and routes successful placement through the existing
/// <see cref="PPEEquipmentManager.Equip"/> contract.
/// </summary>
[DisallowMultipleComponent]
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
    [Tooltip("How long distance and rotation must remain valid before Equip is requested.")]
    float readyHoldDuration = 0.15f;

    [SerializeField, HideInInspector] int placementSetupVersion;

    readonly List<GrabBinding> bindings = new List<GrabBinding>();
    readonly List<ActiveGuide> activeGuides = new List<ActiveGuide>();
    PPEHandGrabbable activeGrabbable;
    PPEEquipable activeEquipable;
    PPEEquipSlot readyCandidateSlot = PPEEquipSlot.None;
    float readyCandidateSince = -1f;
    bool equipAttemptedForCandidate;

    public event Action<PPEEquipSlot, PPEPlacementGuideState> GuideStateChanged;

    public PPEEquipable ActivePpe => activeEquipable;
    public bool IsGuideVisible => activeGuides.Count > 0;
    public Material NotReadyMaterial => notReadyMaterial;
    public Material ReadyMaterial => readyMaterial;
    public bool UsePpeVisualFallback => usePpeVisualWhenGuidePrefabMissing;
    public bool AutoEquipEnabled => autoEquipEnabled;
    public float ReadyHoldDuration => readyHoldDuration;
    public int PlacementSetupVersion => placementSetupVersion;
    public PPEEquipSlot ReadyCandidateSlot => readyCandidateSlot;
    public float CurrentDistance { get; private set; } = float.PositiveInfinity;
    public float CurrentAngle { get; private set; } = float.PositiveInfinity;

    void OnEnable()
    {
        ResolveAnchorSet();
        BindGrabbables();
    }

    void OnDisable()
    {
        UnbindGrabbables();
        HideGuides();
    }

    void Update()
    {
        if (!autoEquipEnabled || activeGrabbable == null || activeEquipable == null ||
            !activeGrabbable.IsGrabbed || activeGuides.Count == 0)
        {
            ResetReadyCandidate();
            return;
        }

        ActiveGuide firstReady = null;
        var nearestDistance = float.PositiveInfinity;
        var nearestAngle = float.PositiveInfinity;

        for (var i = 0; i < activeGuides.Count; i++)
        {
            var guide = activeGuides[i];
            if (guide == null || guide.Anchor == null)
                continue;

            var distance = Vector3.Distance(activeGrabbable.transform.position, guide.Anchor.position);
            var angle = Quaternion.Angle(activeGrabbable.transform.rotation, guide.Anchor.rotation);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestAngle = angle;
            }

            var ready = distance <= activeEquipable.EquipDistance &&
                        angle <= activeEquipable.EquipAngleTolerance;
            SetGuideState(guide, ready ? PPEPlacementGuideState.Ready : PPEPlacementGuideState.NotReady);
            if (ready && firstReady == null)
                firstReady = guide;
        }

        CurrentDistance = nearestDistance;
        CurrentAngle = nearestAngle;

        if (firstReady == null)
        {
            ResetReadyCandidate();
            return;
        }

        if (readyCandidateSlot != firstReady.Slot)
        {
            readyCandidateSlot = firstReady.Slot;
            readyCandidateSince = Time.time;
            equipAttemptedForCandidate = false;
            return;
        }

        if (equipAttemptedForCandidate || Time.time - readyCandidateSince < readyHoldDuration)
            return;

        equipAttemptedForCandidate = true;
        TryEquipAtAnchor(firstReady.Anchor);
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
            if (grabbable == null || grabbable.GetComponent<PPEEquipable>() == null)
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
        ResolveAnchorSet();
        if (anchorSet == null || grabbable == null)
            return;

        var equipable = grabbable.GetComponent<PPEEquipable>();
        if (equipable == null || equipable.EquipSlots == PPEEquipSlot.None)
            return;

        activeGrabbable = grabbable;
        activeEquipable = equipable;

        foreach (var slot in ConcreteSlots)
        {
            if (!equipable.UsesSlot(slot) || !anchorSet.TryGetAnchor(slot, out var anchor))
                continue;

            var root = CreateGuide(equipable, anchor, slot);
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
    }

    void HandleReleased(PPEHandGrabbable grabbable)
    {
        if (grabbable == activeGrabbable)
            HideGuides();
    }

    GameObject CreateGuide(PPEEquipable equipable, Transform anchor, PPEEquipSlot slot)
    {
        GameObject root;
        if (equipable.GuidePrefab != null)
        {
            root = Instantiate(equipable.GuidePrefab, anchor);
            root.name = $"PPE_Guide_{equipable.ItemId}_{slot}";
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
        }
        else if (usePpeVisualWhenGuidePrefabMissing)
        {
            root = BuildRendererOnlyGhost(equipable.gameObject, anchor, slot);
        }
        else
        {
            return null;
        }

        SetLayerRecursively(root.transform, anchor.gameObject.layer);
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
            destination.gameObject.AddComponent<MeshFilter>().sharedMesh = sourceFilter.sharedMesh;
            var renderer = destination.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = sourceRenderer.sharedMaterials;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
    }

    void SetGuideState(ActiveGuide guide, PPEPlacementGuideState state)
    {
        if (guide == null || guide.Root == null || guide.State == state)
            return;
        guide.State = state;
        ApplyMaterial(guide.Renderers, state == PPEPlacementGuideState.Ready ? readyMaterial : notReadyMaterial);
        GuideStateChanged?.Invoke(guide.Slot, state);
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
        ResetReadyCandidate();
    }

    void TryEquipAtAnchor(Transform anchor)
    {
        if (anchor == null || activeGrabbable == null || activeEquipable == null)
            return;

        var manager = PPEEquipmentManager.Instance != null
            ? PPEEquipmentManager.Instance
            : FindAnyObjectByType<PPEEquipmentManager>();
        if (manager == null)
        {
            Debug.LogWarning("[PPE Placement] PPEEquipmentManager is missing; placement equip was skipped.", this);
            return;
        }

        // Equip owns state, checklist/sequence, feedback, and XR selection release.
        // Keep local references because a successful Equip can deactivate the root.
        var grabbable = activeGrabbable;
        var equipable = activeEquipable;
        if (!manager.Equip(equipable))
            return;

        FinalizeEquippedPose(equipable, grabbable, anchor);
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

    void ResetReadyCandidate()
    {
        readyCandidateSlot = PPEEquipSlot.None;
        readyCandidateSince = -1f;
        equipAttemptedForCandidate = false;
        CurrentDistance = float.PositiveInfinity;
        CurrentAngle = float.PositiveInfinity;
    }

    void ResolveAnchorSet()
    {
        if (anchorSet == null)
            anchorSet = FindAnyObjectByType<PPEEquipmentAnchorSet>(FindObjectsInactive.Include);
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
        readyHoldDuration = Mathf.Max(0f, placementReadyHoldDuration);
        placementSetupVersion = 1;
    }
#endif
}
