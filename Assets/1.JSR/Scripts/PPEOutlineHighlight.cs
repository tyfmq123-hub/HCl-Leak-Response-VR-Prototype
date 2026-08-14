using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Hover + sticky selected outline for PPE roots (XR Ray Interactor only).
/// Selected has priority over hover. selectExited does not clear selection.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(XRBaseInteractable))]
public class PPEOutlineHighlight : MonoBehaviour
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
    bool isSelected;
    bool isRequiredStep;
    bool outlineVisible;
    bool cached;

    public bool IsHovered => rayHoverCount > 0;
    public bool IsSelected => isSelected;
    public bool IsRequiredStep => isRequiredStep;

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
        // selectExited intentionally ignored — Select is click-style sticky selection.

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
        }

        rayHoverCount = 0;
        // Keep sticky isSelected flag; only hide renderer materials while disabled.
        ApplyOutlineVisual(false);
    }

    void Update()
    {
        if (!isRequiredStep || outlineInstance == null)
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
