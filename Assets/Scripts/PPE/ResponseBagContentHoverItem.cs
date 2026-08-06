using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Hover-only XR item for a revealed response-bag content model.
/// First valid controller-ray hover inspects via ResponseEquipmentManager.
/// Not grabbable / not PPE-selectable.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(XRSimpleInteractable))]
public class ResponseBagContentHoverItem : MonoBehaviour
{
    public const string InteractionLayerName = "ResponseBagContent";

    [Header("Identity")]
    [SerializeField] string itemId;
    [SerializeField] string koreanDisplayName;

    [Header("References")]
    [SerializeField] Transform contentRoot;
    [SerializeField] XRSimpleInteractable xrSimpleInteractable;
    [SerializeField] Collider itemCollider;
    [SerializeField] PPEInteractableOutline outlineController;
    [SerializeField] ResponseEquipmentManager equipmentManager;
    [SerializeField] ResponseBagItemNameLabel sharedNameLabel;
    [SerializeField] ResponseBagContentsRevealController revealController;

    [Header("Wearable contents")]
    [Tooltip("When true, select only logs — item hand retrieval is not implemented yet.")]
    [SerializeField] bool retrievalNotImplemented;

    [Tooltip("When false, hover shows outline/subtitle but does not mark world-bag inspection.")]
    [SerializeField] bool inspectOnHover = true;

    int rayHoverCount;

    public string ItemId => itemId;
    public string KoreanDisplayName => koreanDisplayName;
    public bool IsRayHovered => rayHoverCount > 0;
    public Transform ContentRoot => contentRoot != null ? contentRoot : transform;

    void Awake() => ResolveRefs();

    void OnEnable()
    {
        ResolveRefs();
        Wire(true);
    }

    void OnDisable()
    {
        Wire(false);
        ForceClearHoverVisuals();
        rayHoverCount = 0;
    }

    public void EditorWire(
        string id,
        string koreanName,
        Transform content,
        XRSimpleInteractable interactable,
        Collider collider,
        PPEInteractableOutline outline,
        ResponseEquipmentManager manager,
        ResponseBagItemNameLabel nameLabel,
        ResponseBagContentsRevealController reveal)
    {
        itemId = id;
        koreanDisplayName = koreanName;
        contentRoot = content;
        xrSimpleInteractable = interactable;
        itemCollider = collider;
        outlineController = outline;
        equipmentManager = manager;
        sharedNameLabel = nameLabel;
        revealController = reveal;
    }

    public void EditorSetWearableDisplayOnly(bool displayOnly)
    {
        retrievalNotImplemented = displayOnly;
        inspectOnHover = !displayOnly;
    }

    public void ClearVisuals()
    {
        ForceClearHoverVisuals();
        rayHoverCount = 0;
    }

    void Wire(bool subscribe)
    {
        if (xrSimpleInteractable == null)
            return;

        xrSimpleInteractable.hoverEntered.RemoveListener(OnHoverEntered);
        xrSimpleInteractable.hoverExited.RemoveListener(OnHoverExited);
        xrSimpleInteractable.selectEntered.RemoveListener(OnSelectEntered);

        if (!subscribe)
            return;

        xrSimpleInteractable.hoverEntered.AddListener(OnHoverEntered);
        xrSimpleInteractable.hoverExited.AddListener(OnHoverExited);
        // Swallow select so sticky PPE selection never latches these items.
        xrSimpleInteractable.selectEntered.AddListener(OnSelectEntered);
    }

    void OnHoverEntered(HoverEnterEventArgs args)
    {
        if (!IsControllerRay(args.interactorObject))
            return;

        if (!IsContentActuallyVisible())
            return;

        rayHoverCount++;
        if (rayHoverCount == 1)
            ApplyHoverVisuals();

        TryInspectOnce();
        revealController?.NotifyContentHoverChanged();
    }

    void OnHoverExited(HoverExitEventArgs args)
    {
        if (!IsControllerRay(args.interactorObject))
            return;

        rayHoverCount = Mathf.Max(0, rayHoverCount - 1);
        if (rayHoverCount == 0)
            ClearHoverVisualsOnly();

        revealController?.NotifyContentHoverChanged();
    }

    /// <summary>
    /// Avoid ghost subtitles when the mesh is hidden/inactive or the collider is a huge leftover (e.g. cabinet).
    /// </summary>
    bool IsContentActuallyVisible()
    {
        var root = ContentRoot;
        if (root == null || !root.gameObject.activeInHierarchy)
            return false;

        if (!gameObject.activeInHierarchy)
            return false;

        if (itemCollider != null)
        {
            if (!itemCollider.enabled || !itemCollider.gameObject.activeInHierarchy)
                return false;

            var size = itemCollider.bounds.size;
            var maxAxis = Mathf.Max(size.x, size.y, size.z);
            // Cabinet-sized ghost colliders were firing "흡착패드" across the room.
            if (maxAxis > 0.55f)
                return false;
        }

        var renderers = root.GetComponentsInChildren<Renderer>(false);
        for (var i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                continue;
            if (r is MeshRenderer || r is SkinnedMeshRenderer)
                return true;
        }

        return false;
    }

    void OnSelectEntered(SelectEnterEventArgs args)
    {
        // Content items are hover-inspect only — cancel accidental select.
        if (xrSimpleInteractable == null)
            return;

        var manager = xrSimpleInteractable.interactionManager;
        if (manager != null && xrSimpleInteractable is IXRSelectInteractable selectable && selectable.isSelected)
            manager.CancelInteractableSelection(selectable);

        if (retrievalNotImplemented)
            Debug.Log("[Response Bag] Item retrieval is not implemented yet.", this);
    }

    void TryInspectOnce()
    {
        if (!inspectOnHover)
            return;

        ResolveRefs();
        if (equipmentManager == null || string.IsNullOrWhiteSpace(itemId))
            return;

        if (equipmentManager.ResponseBagPrepared)
            return;

        if (!equipmentManager.InspectResponseBagItem(itemId))
            return;

        ShowInspectedFeedback();
    }

    void ApplyHoverVisuals()
    {
        if (sharedNameLabel != null)
            sharedNameLabel.ShowSubtitle(koreanDisplayName);

        if (outlineController == null)
            outlineController = GetComponent<PPEInteractableOutline>();
    }

    void ClearHoverVisualsOnly()
    {
        if (sharedNameLabel != null)
            sharedNameLabel.Hide();
    }

    void ForceClearHoverVisuals()
    {
        if (sharedNameLabel != null)
            sharedNameLabel.HideImmediate();
        if (outlineController != null)
            outlineController.ForceClearOutline();
    }

    void ShowInspectedFeedback()
    {
        var feedback = FindAnyObjectByType<PPEEquipFeedbackUI>(FindObjectsInactive.Include);
        if (feedback == null)
            return;

        string message = null;
        if (itemId == ResponseEquipmentManager.IdGasDetector)
            message = "\uD734\uB300\uC6A9 HCl \uAC00\uC2A4\uCE21\uC815\uAE30\uB97C \uD655\uC778\uD588\uC2B5\uB2C8\uB2E4.";
        else if (itemId == ResponseEquipmentManager.IdAbsorbentSock)
            message = "\uCF00\uBBF8\uCEEC \uD761\uCC29\uC0AD\uC744 \uD655\uC778\uD588\uC2B5\uB2C8\uB2E4.";
        else if (itemId == ResponseEquipmentManager.IdAbsorbentPad)
            message = "\uD654\uD559\uBB3C\uC9C8\uC6A9 \uD761\uCC29\uD328\uB4DC\uB97C \uD655\uC778\uD588\uC2B5\uB2C8\uB2E4.";

        if (!string.IsNullOrEmpty(message))
            feedback.ShowMessage(message);
    }

    void ResolveRefs()
    {
        if (xrSimpleInteractable == null)
            xrSimpleInteractable = GetComponent<XRSimpleInteractable>();

        if (outlineController == null)
            outlineController = GetComponent<PPEInteractableOutline>();

        if (contentRoot == null)
            contentRoot = transform;

        if (itemCollider == null)
            itemCollider = GetComponent<Collider>();

        if (equipmentManager == null)
        {
            equipmentManager = ResponseEquipmentManager.Instance != null
                ? ResponseEquipmentManager.Instance
                : FindAnyObjectByType<ResponseEquipmentManager>();
        }

        if (revealController == null)
            revealController = GetComponentInParent<ResponseBagContentsRevealController>(true);

        if (sharedNameLabel == null)
            sharedNameLabel = FindAnyObjectByType<ResponseBagItemNameLabel>(FindObjectsInactive.Include);
    }

    static bool IsControllerRay(IXRInteractor interactor)
    {
        return interactor is XRRayInteractor;
    }
}
