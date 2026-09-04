using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Hover + sticky selected outline for PPE roots (XR Ray Interactor only).
/// Selected has priority over hover. Ray selectExited does not clear selection.
/// Direct-hand grab hides all outlines (including required-step pulse) until release.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(XRBaseInteractable))]
public class PPEOutlineHighlight : MonoBehaviour, IXRSelectFilter
{
    static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
    static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");

    const string OutlineShaderName = "PPE/Outline";
    const string DefaultMaterialResource = "PPE_Outline";

    [Header("Hover")]
    [SerializeField] bool enableHoverOutline = true;
    [SerializeField] Color hoverColor = new Color(0.4f, 0.78f, 1f, 1f);
    [SerializeField] [Range(0.5f, 10f)] float hoverWidth = 2.5f;

    [Header("Selected")]
    [SerializeField] bool enableSelectedOutline = true;
    [SerializeField] Color selectedColor = new Color(0.2f, 1f, 0.25f, 1f);
    [SerializeField] [Range(0.5f, 12f)] float selectedWidth = 4.5f;

    [Header("Current Required Step")]
    [SerializeField] Color requiredCyan = new Color(0.05f, 0.95f, 1f, 1f);
    [SerializeField] Color requiredYellow = new Color(1f, 0.88f, 0.05f, 1f);
    [SerializeField] [Range(1f, 12f)] float requiredMinWidth = 5.5f;
    [SerializeField] [Range(1f, 12f)] float requiredMaxWidth = 8f;
    [SerializeField] [Min(0.1f)] float requiredPulseSpeed = 1.25f;

    [Header("Renderers")]
    [SerializeField] bool includeChildRenderers = true;
    [SerializeField] Material outlineMaterialSource;

    XRBaseInteractable interactable;
    readonly List<Renderer> renderers = new List<Renderer>(16);
    Material[][] originalSharedMaterials;
    Material[][] materialsWithOutline;
    Material outlineInstance;
    int rayHoverCount;
    int handGrabCount;
    bool isSelected;
    bool isRequiredStep;
    bool outlineVisible;
    bool cached;
    readonly List<PPEHandGrabbable> childGrabbers = new List<PPEHandGrabbable>(4);

    public bool IsHovered => rayHoverCount > 0;
    public bool IsSelected => isSelected;
    public bool IsRequiredStep => isRequiredStep;

    public bool canProcess =>
        isActiveAndEnabled &&
        childGrabbers.Count > 0 &&
        interactable is XRSimpleInteractable;

    void Awake()
    {
        interactable = GetComponent<XRBaseInteractable>();
        EnsureOutlineMaterial();
        CacheRenderersAndMaterials();
        ApplyOutlineVisual(false);
    }

    void OnEnable()
    {
        if (interactable == null)
            interactable = GetComponent<XRBaseInteractable>();

        if (interactable == null)
        {
            Debug.LogWarning(
                $"PPEOutlineHighlight on '{name}' requires XRBaseInteractable / XRSimpleInteractable.",
                this);
            return;
        }

        interactable.hoverEntered.AddListener(OnHoverEntered);
        interactable.hoverExited.AddListener(OnHoverExited);
        interactable.selectEntered.AddListener(OnSelectEntered);
        interactable.selectExited.AddListener(OnSelectExited);
        WireChildHandGrabs(true);
        StripChildGrabCollidersFromPairRoot();
        PinPairLogicalRootColliders();
        RegisterPairRootRayOnlySelectFilter(true);

        // Restore sticky selected / hover outline after disable→enable.
        RefreshOutlineState();
    }

    void OnDisable()
    {
        if (interactable != null)
        {
            interactable.hoverEntered.RemoveListener(OnHoverEntered);
            interactable.hoverExited.RemoveListener(OnHoverExited);
            interactable.selectEntered.RemoveListener(OnSelectEntered);
            interactable.selectExited.RemoveListener(OnSelectExited);
        }

        RegisterPairRootRayOnlySelectFilter(false);
        WireChildHandGrabs(false);
        rayHoverCount = 0;
        handGrabCount = 0;
        // Keep sticky isSelected flag; only hide renderer materials while disabled.
        ApplyOutlineVisual(false);
    }

    void Update()
    {
        if (!isRequiredStep || outlineInstance == null || handGrabCount > 0)
            return;

        var pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * requiredPulseSpeed * Mathf.PI * 2f);
        SetOutlineAppearance(
            Color.Lerp(requiredCyan, requiredYellow, pulse),
            Mathf.Lerp(requiredMinWidth, requiredMaxWidth, pulse));
    }

    void OnDestroy()
    {
        if (PPESelectionManager.Instance != null &&
            PPESelectionManager.Instance.CurrentSelectedPPE == this)
        {
            PPESelectionManager.Instance.NotifyTargetDestroyed(this);
        }

        ApplyOutlineVisual(false);

        if (outlineInstance == null)
            return;

        if (Application.isPlaying)
            Destroy(outlineInstance);
        else
            DestroyImmediate(outlineInstance);

        outlineInstance = null;
    }

    void OnHoverEntered(HoverEnterEventArgs args)
    {
        if (!IsRayInteractor(args.interactorObject))
            return;

        rayHoverCount++;
        RefreshOutlineState();
    }

    void OnHoverExited(HoverExitEventArgs args)
    {
        if (!IsRayInteractor(args.interactorObject))
            return;

        rayHoverCount = Mathf.Max(0, rayHoverCount - 1);
        RefreshOutlineState();
    }

    void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (IsDirectGrabInteractor(args.interactorObject))
        {
            RecalculateHandGrabCount();
            RefreshOutlineState();
            return;
        }

        if (PPETTSManager.Instance != null && PPETTSManager.Instance.IsBusy)
            return;

        if (!IsRayInteractor(args.interactorObject))
            return;

        Debug.Log($"[PPE Selected] {name}", this);

        if (PPESelectionManager.Instance != null)
            PPESelectionManager.Instance.SelectPPE(this);
        else
            SetSelected(true);
    }

    void OnSelectExited(SelectExitEventArgs args)
    {
        if (!IsDirectGrabInteractor(args.interactorObject))
            return;

        RecalculateHandGrabCount();
        RefreshOutlineState();
    }

    /// <summary>
    /// Called by <see cref="PPESelectionManager"/> to latch / clear sticky selection visuals.
    /// </summary>
    public void SetSelected(bool selected)
    {
        if (isSelected == selected)
        {
            RefreshOutlineState();
            return;
        }

        isSelected = selected;
        RefreshOutlineState();
    }

    /// <summary>Strong sequence guidance owned separately from hover/selection state.</summary>
    public void SetRequiredStepHighlighted(bool highlighted)
    {
        if (isRequiredStep == highlighted)
        {
            RefreshOutlineState();
            return;
        }

        isRequiredStep = highlighted;
        RefreshOutlineState();
    }

    void RefreshOutlineState()
    {
        if (handGrabCount > 0)
        {
            ApplyOutlineVisual(false);
            return;
        }

        // The required target must remain unmistakable even when another object was
        // hovered or selected out of order. Hover -> select -> equip behavior is unchanged.
        if (isRequiredStep)
        {
            SetOutlineAppearance(requiredCyan, requiredMaxWidth);
            ApplyOutlineVisual(true);
            return;
        }

        // Selected has priority over hover.
        if (isSelected && enableSelectedOutline)
        {
            SetOutlineAppearance(selectedColor, selectedWidth);
            ApplyOutlineVisual(true);
            return;
        }

        if (IsHovered && enableHoverOutline)
        {
            SetOutlineAppearance(hoverColor, hoverWidth);
            ApplyOutlineVisual(true);
            return;
        }

        ApplyOutlineVisual(false);
    }

    static bool IsRayInteractor(IXRInteractor interactor)
    {
        return interactor is XRRayInteractor;
    }

    static bool IsDirectGrabInteractor(IXRInteractor interactor)
    {
        return interactor != null && interactor is not XRRayInteractor;
    }

    public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable _)
    {
        // Pair roots are ray-select / LED only. Hands must grab Left/Right members.
        return IsRayInteractor(interactor);
    }

    void WireChildHandGrabs(bool subscribe)
    {
        if (!subscribe)
        {
            for (var i = 0; i < childGrabbers.Count; i++)
            {
                var grab = childGrabbers[i];
                if (grab == null)
                    continue;
                grab.OnGrabbed -= OnChildHandGrabbed;
                grab.OnReleased -= OnChildHandReleased;
            }

            childGrabbers.Clear();
            return;
        }

        var grabs = GetComponentsInChildren<PPEHandGrabbable>(true);
        for (var i = 0; i < grabs.Length; i++)
        {
            var grab = grabs[i];
            // Same-object grabbers are already covered by this interactable's select events.
            if (grab == null || grab.gameObject == gameObject)
                continue;
            childGrabbers.Add(grab);
            grab.OnGrabbed += OnChildHandGrabbed;
            grab.OnReleased += OnChildHandReleased;
        }
    }

    void OnChildHandGrabbed(PPEGrabHand _)
    {
        RecalculateHandGrabCount();
        RefreshOutlineState();
    }

    void OnChildHandReleased(PPEGrabHand _)
    {
        RecalculateHandGrabCount();
        RefreshOutlineState();
    }

    void RecalculateHandGrabCount()
    {
        var count = CountDirectSelectors(interactable);
        for (var i = 0; i < childGrabbers.Count; i++)
        {
            var grab = childGrabbers[i];
            if (grab == null || grab.gameObject == gameObject)
                continue;
            count += CountDirectSelectors(grab);
        }

        handGrabCount = count;
    }

    static int CountDirectSelectors(XRBaseInteractable target)
    {
        if (target == null || !target.isSelected || target is XRSimpleInteractable)
            return 0;

        var count = 0;
        var selecting = target.interactorsSelecting;
        for (var i = 0; i < selecting.Count; i++)
        {
            if (IsDirectGrabInteractor(selecting[i]))
                count++;
        }

        return count;
    }

    void StripChildGrabCollidersFromPairRoot()
    {
        if (interactable is not XRSimpleInteractable simple || childGrabbers.Count == 0)
            return;

        var ownedByMembers = new HashSet<Collider>();
        for (var i = 0; i < childGrabbers.Count; i++)
        {
            var grab = childGrabbers[i];
            if (grab == null)
                continue;

            var listed = grab.colliders;
            for (var c = 0; c < listed.Count; c++)
            {
                if (listed[c] != null)
                    ownedByMembers.Add(listed[c]);
            }

            var nested = grab.GetComponentsInChildren<Collider>(true);
            for (var c = 0; c < nested.Length; c++)
            {
                if (nested[c] != null)
                    ownedByMembers.Add(nested[c]);
            }
        }

        for (var i = simple.colliders.Count - 1; i >= 0; i--)
        {
            var col = simple.colliders[i];
            if (col == null || ownedByMembers.Contains(col))
                simple.colliders.RemoveAt(i);
        }
    }

    void PinPairLogicalRootColliders()
    {
        if (interactable is not XRSimpleInteractable simple)
            return;

        if (GetComponent<PPEGlovePairCoordinator>() == null)
            return;

        var dummy = EnsurePairLogicalRootDummyCollider();
        if (!simple.colliders.Contains(dummy))
            simple.colliders.Add(dummy);

        for (var i = simple.colliders.Count - 1; i >= 0; i--)
        {
            if (simple.colliders[i] != dummy)
                simple.colliders.RemoveAt(i);
        }

        dummy.enabled = false;
        dummy.isTrigger = true;
    }

    Collider EnsurePairLogicalRootDummyCollider()
    {
        var existing = transform.Find(PPEGlovePairCoordinator.LogicalRootColliderLockName);
        if (existing == null)
        {
            var go = new GameObject(PPEGlovePairCoordinator.LogicalRootColliderLockName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            existing = go.transform;
        }

        var box = existing.GetComponent<BoxCollider>();
        if (box == null)
            box = existing.gameObject.AddComponent<BoxCollider>();

        box.size = Vector3.one * 0.001f;
        box.center = Vector3.zero;
        box.isTrigger = true;
        box.enabled = false;
        return box;
    }

    void RegisterPairRootRayOnlySelectFilter(bool subscribe)
    {
        if (interactable == null || !Application.isPlaying)
            return;

        var filters = interactable.selectFilters;
        var already = false;
        for (var i = 0; i < filters.count; i++)
        {
            if (ReferenceEquals(filters.GetAt(i), this))
            {
                already = true;
                break;
            }
        }

        if (subscribe)
        {
            if (!already)
                interactable.selectFilters.Add(this);
            return;
        }

        if (already)
            interactable.selectFilters.Remove(this);
    }

    void SetOutlineAppearance(Color color, float width)
    {
        if (outlineInstance == null)
            return;

        outlineInstance.SetColor(OutlineColorId, color);
        outlineInstance.SetFloat(OutlineWidthId, width);
    }

    void EnsureOutlineMaterial()
    {
        if (outlineInstance != null)
            return;

        var source = outlineMaterialSource;
        if (source == null)
            source = Resources.Load<Material>(DefaultMaterialResource);

        if (source == null)
        {
            var shader = Shader.Find(OutlineShaderName);
            if (shader == null)
            {
                Debug.LogError(
                    $"PPEOutlineHighlight: URP shader '{OutlineShaderName}' not found. " +
                    "Project is expected to use URP with Assets/1.JSR/Shaders/PPE/PPEOutline.shader.",
                    this);
                return;
            }

            outlineInstance = new Material(shader) { name = "PPE_Outline (Runtime)" };
        }
        else
        {
            outlineInstance = new Material(source) { name = source.name + " (Instance)" };
        }

        outlineInstance.hideFlags = HideFlags.HideAndDontSave;
        SetOutlineAppearance(hoverColor, hoverWidth);
    }

    void CacheRenderersAndMaterials()
    {
        renderers.Clear();

        if (includeChildRenderers)
            GetComponentsInChildren(true, renderers);
        else
            renderers.AddRange(GetComponents<Renderer>());

        for (var i = renderers.Count - 1; i >= 0; i--)
        {
            var r = renderers[i];
            if (r == null)
            {
                renderers.RemoveAt(i);
                continue;
            }

            // Legacy PPE cube highlight — never outline this as if it were the item mesh.
            if (r.gameObject.name == "HighlightVisual")
            {
                renderers.RemoveAt(i);
                continue;
            }

            if (r is MeshRenderer || r is SkinnedMeshRenderer)
                continue;
            renderers.RemoveAt(i);
        }

        originalSharedMaterials = new Material[renderers.Count][];
        materialsWithOutline = new Material[renderers.Count][];

        for (var i = 0; i < renderers.Count; i++)
        {
            var shared = renderers[i].sharedMaterials;
            originalSharedMaterials[i] = shared;

            if (outlineInstance == null)
            {
                materialsWithOutline[i] = shared;
                continue;
            }

            var withOutline = new Material[shared.Length + 1];
            for (var m = 0; m < shared.Length; m++)
                withOutline[m] = shared[m];
            withOutline[shared.Length] = outlineInstance;
            materialsWithOutline[i] = withOutline;
        }

        cached = true;
    }

    void ApplyOutlineVisual(bool visible)
    {
        if (!cached)
        {
            EnsureOutlineMaterial();
            CacheRenderersAndMaterials();
        }

        if (outlineVisible == visible)
            return;

        if (visible)
        {
            if (outlineInstance == null)
            {
                EnsureOutlineMaterial();
                CacheRenderersAndMaterials();
            }

            if (outlineInstance == null)
                return;

            for (var i = 0; i < renderers.Count; i++)
            {
                var r = renderers[i];
                if (r == null || materialsWithOutline[i] == null)
                    continue;
                r.sharedMaterials = materialsWithOutline[i];
            }

            outlineVisible = true;
        }
        else
        {
            for (var i = 0; i < renderers.Count; i++)
            {
                var r = renderers[i];
                if (r == null || originalSharedMaterials[i] == null)
                    continue;
                r.sharedMaterials = originalSharedMaterials[i];
            }

            outlineVisible = false;
        }
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!Application.isPlaying || !outlineVisible)
            return;

        RefreshOutlineState();
    }
#endif
}
