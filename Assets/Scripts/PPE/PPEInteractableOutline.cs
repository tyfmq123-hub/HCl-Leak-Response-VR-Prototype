using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Event-driven XR hover/select outline for PPE roots.
/// Appends a separate outline material; never permanently modifies sharedMaterials.
/// Compatible with XRGrabInteractable, XRSimpleInteractable, and XRBaseInteractable
/// (Ray + Direct interactors via hover/select events).
/// </summary>
[DisallowMultipleComponent]
public class PPEInteractableOutline : MonoBehaviour
{
    static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
    static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");

    const string OutlineShaderName = "PPE/Outline";
    const string DefaultMaterialResource = "PPE_Outline";

    [Header("Hover")]
    [SerializeField] bool enableHoverOutline = true;
    [SerializeField] bool rayInteractorOnly;
    [SerializeField] Color hoverColor = new Color(1f, 0.92f, 0.016f, 1f);
    [SerializeField] [Range(0.5f, 10f)] float hoverWidth = 2.5f;

    [Header("Selected")]
    [SerializeField] bool enableSelectedOutline = true;
    [SerializeField] Color selectedColor = new Color(0.2f, 1f, 0.25f, 1f);
    [SerializeField] [Range(0.5f, 12f)] float selectedWidth = 4.5f;

    [Header("Renderers")]
    [SerializeField] bool includeChildRenderers = true;
    [SerializeField] Material outlineMaterialSource;

    XRBaseInteractable interactable;
    readonly List<Renderer> renderers = new List<Renderer>(16);
    Material[][] originalSharedMaterials;
    Material outlineInstance;
    bool isHovered;
    bool isSelected;
    int hoverCount;
    int selectCount;
    bool outlineVisible;

    public bool IsHovered => isHovered;
    public bool IsSelected => isSelected;

    public void EditorSetHoverYellowRayOnly()
    {
        enableHoverOutline = true;
        enableSelectedOutline = false;
        rayInteractorOnly = true;
        hoverColor = new Color(1f, 0.92f, 0.016f, 1f);
        hoverWidth = 2.5f;
    }

    /// <summary>Hide outline and reset hover/select counters (reset / contents hide).</summary>
    public void ForceClearOutline()
    {
        hoverCount = 0;
        selectCount = 0;
        isHovered = false;
        isSelected = false;
        ApplyOutlineVisual(false);
    }

    void Awake()
    {
        CacheRenderers();
        EnsureOutlineMaterial();
        ApplyOutlineVisual(false);
    }

    void OnEnable()
    {
        interactable = GetComponent<XRBaseInteractable>();
        if (interactable == null)
        {
            Debug.LogWarning(
                $"PPEInteractableOutline on '{name}' requires XRBaseInteractable " +
                "(XRGrabInteractable / XRSimpleInteractable).",
                this);
            return;
        }

        interactable.hoverEntered.AddListener(OnHoverEntered);
        interactable.hoverExited.AddListener(OnHoverExited);
        interactable.selectEntered.AddListener(OnSelectEntered);
        interactable.selectExited.AddListener(OnSelectExited);
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

        hoverCount = 0;
        selectCount = 0;
        isHovered = false;
        isSelected = false;
        ApplyOutlineVisual(false);
    }

    void OnDestroy()
    {
        ApplyOutlineVisual(false);
        if (outlineInstance != null)
        {
            if (Application.isPlaying)
                Destroy(outlineInstance);
            else
                DestroyImmediate(outlineInstance);
            outlineInstance = null;
        }
    }

    void OnHoverEntered(HoverEnterEventArgs args)
    {
        if (rayInteractorOnly && !IsRayInteractor(args.interactorObject))
            return;

        hoverCount++;
        isHovered = hoverCount > 0;
        RefreshOutlineState();
    }

    void OnHoverExited(HoverExitEventArgs args)
    {
        if (rayInteractorOnly && !IsRayInteractor(args.interactorObject))
            return;

        hoverCount = Mathf.Max(0, hoverCount - 1);
        isHovered = hoverCount > 0;
        RefreshOutlineState();
    }

    void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (rayInteractorOnly && !IsRayInteractor(args.interactorObject))
            return;

        selectCount++;
        isSelected = selectCount > 0;
        RefreshOutlineState();
    }

    void OnSelectExited(SelectExitEventArgs args)
    {
        if (rayInteractorOnly && !IsRayInteractor(args.interactorObject))
            return;

        selectCount = Mathf.Max(0, selectCount - 1);
        isSelected = selectCount > 0;
        RefreshOutlineState();
    }

    static bool IsRayInteractor(IXRInteractor interactor)
    {
        return interactor is XRRayInteractor;
    }

    void RefreshOutlineState()
    {
        if (isSelected && enableSelectedOutline)
        {
            SetOutlineAppearance(selectedColor, selectedWidth);
            ApplyOutlineVisual(true);
            return;
        }

        if (isHovered && enableHoverOutline)
        {
            SetOutlineAppearance(hoverColor, hoverWidth);
            ApplyOutlineVisual(true);
            return;
        }

        ApplyOutlineVisual(false);
    }

    void SetOutlineAppearance(Color color, float width)
    {
        if (outlineInstance == null)
            return;
        outlineInstance.SetColor(OutlineColorId, color);
        outlineInstance.SetFloat(OutlineWidthId, width);
    }

    void CacheRenderers()
    {
        renderers.Clear();
        if (includeChildRenderers)
            GetComponentsInChildren(true, renderers);
        else
        {
            var self = GetComponents<Renderer>();
            renderers.AddRange(self);
        }

        // Keep only mesh-based renderers.
        for (var i = renderers.Count - 1; i >= 0; i--)
        {
            var r = renderers[i];
            if (r is MeshRenderer || r is SkinnedMeshRenderer)
                continue;
            renderers.RemoveAt(i);
        }

        originalSharedMaterials = new Material[renderers.Count][];
        for (var i = 0; i < renderers.Count; i++)
            originalSharedMaterials[i] = renderers[i].sharedMaterials;
    }

    void EnsureOutlineMaterial()
    {
        if (outlineInstance != null)
            return;

        Material source = outlineMaterialSource;
        if (source == null)
            source = Resources.Load<Material>(DefaultMaterialResource);

        if (source == null)
        {
            var shader = Shader.Find(OutlineShaderName);
            if (shader == null)
            {
                Debug.LogError(
                    $"PPEInteractableOutline: shader '{OutlineShaderName}' not found.",
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

    void ApplyOutlineVisual(bool visible)
    {
        if (outlineVisible == visible)
            return;

        if (renderers.Count == 0 || originalSharedMaterials == null)
            CacheRenderers();

        if (visible)
        {
            if (outlineInstance == null)
                EnsureOutlineMaterial();
            if (outlineInstance == null)
                return;

            for (var i = 0; i < renderers.Count; i++)
            {
                var r = renderers[i];
                if (r == null)
                    continue;

                var shared = originalSharedMaterials[i] ?? r.sharedMaterials;
                if (ContainsMaterial(shared, outlineInstance))
                {
                    r.sharedMaterials = shared;
                    continue;
                }

                var withOutline = new Material[shared.Length + 1];
                for (var m = 0; m < shared.Length; m++)
                    withOutline[m] = shared[m];
                withOutline[shared.Length] = outlineInstance;
                r.sharedMaterials = withOutline;
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

    static bool ContainsMaterial(Material[] mats, Material target)
    {
        if (mats == null || target == null)
            return false;
        for (var i = 0; i < mats.Length; i++)
        {
            if (mats[i] == target)
                return true;
        }

        return false;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!Application.isPlaying)
            return;
        RefreshOutlineState();
    }
#endif
}
