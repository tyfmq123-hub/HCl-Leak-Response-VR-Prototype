using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Gaze- or ray-select the hazardous waste container, then Grip-confirm via
/// <see cref="XRInteractionConfirmRouter"/> to mark it prepared (take out).
/// Requires chemical gloves equipped. Checklist uses Prepared (not Equip).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(XRSimpleInteractable))]
public class WasteContainerPrepareInteractable : MonoBehaviour
{
    static readonly string DefaultDisplayName =
        "\uC720\uD574\uD3D0\uAE30\uBB3C \uC218\uAC70\uC6A9\uAE30";

    static readonly string PreparedMessage =
        "\uC720\uD574\uD3D0\uAE30\uBB3C \uC218\uAC70\uC6A9\uAE30\uB97C \uC900\uBE44\uD588\uC2B5\uB2C8\uB2E4.";

    [Header("References")]
    [SerializeField] PPEEquipmentManager equipmentManager;
    [SerializeField] GameObject containerRoot;
    [SerializeField] XRBaseInteractable interactable;
    [SerializeField] PPEOutlineHighlight outlineHighlight;

    [Header("Display")]
    [SerializeField] string displayName = DefaultDisplayName;

    [Header("Prepare")]
    [SerializeField] bool deactivateOnPrepare = true;
    [SerializeField] bool requireChemicalGloves = true;
    [SerializeField] AudioSource completionAudio;

    [Header("Confirm Timing")]
    [SerializeField] [Min(0f)] float confirmDelay = 0.3f;
    [SerializeField] bool confirmInputOwnedByRouter = true;

    bool containerSelected;
    bool preparationInProgress;
    float selectedAtTime = -999f;
    bool capturedAuthoredState;
    bool authoredObjectActive;
    bool authoredRootActive;
    bool authoredInteractableEnabled;
    Transform authoredRootParent;
    Vector3 authoredRootLocalPosition;
    Quaternion authoredRootLocalRotation;
    Vector3 authoredRootLocalScale;

    public string DisplayName =>
        string.IsNullOrWhiteSpace(displayName) ? DefaultDisplayName : displayName;

    public bool IsContainerSelected => IsStickySelected();
    public bool PreparationInProgress => preparationInProgress;

    void Awake()
    {
        ResolveRefs();
        CaptureAuthoredState();
    }

    void OnEnable()
    {
        ResolveRefs();
        WireInteractable(true);
    }

    void OnDisable()
    {
        WireInteractable(false);
        if (!preparationInProgress)
            containerSelected = false;
    }

    void OnDestroy() => WireInteractable(false);

    public void SetConfirmInputOwnedByRouter(bool owned) => confirmInputOwnedByRouter = owned;

    /// <summary>
    /// Full PPE Room retry: restore Prepared=false visual, interaction, and authored pose.
    /// Does not turn WasteContainer into wearable PPE.
    /// </summary>
    public void ResetPreparedState()
    {
        preparationInProgress = false;
        containerSelected = false;
        selectedAtTime = -999f;

        ResolveRefs();
        if (!capturedAuthoredState)
            CaptureAuthoredState();

        if (outlineHighlight != null)
        {
            if (PPESelectionManager.Instance != null &&
                PPESelectionManager.Instance.CurrentSelectedPPE == outlineHighlight)
                PPESelectionManager.Instance.ClearSelection();
            else
                outlineHighlight.SetSelected(false);
        }

        var root = containerRoot != null ? containerRoot : gameObject;
        RestoreAuthoredHierarchy(root);
        RestoreAuthoredActive(root);

        if (interactable != null)
            interactable.enabled = authoredInteractableEnabled;

        if (equipmentManager != null)
            equipmentManager.SetPrepared(PPEEquipmentManager.IdWasteContainer, false);

        ClearXrInteraction(root != null ? root : gameObject);
    }

    void CaptureAuthoredState()
    {
        ResolveRefs();
        var root = containerRoot != null ? containerRoot : gameObject;
        authoredObjectActive = gameObject.activeSelf;
        authoredRootActive = root != null && root.activeSelf;
        authoredInteractableEnabled = interactable == null || interactable.enabled;
        if (root != null)
        {
            authoredRootParent = root.transform.parent;
            authoredRootLocalPosition = root.transform.localPosition;
            authoredRootLocalRotation = root.transform.localRotation;
            authoredRootLocalScale = root.transform.localScale;
        }

        capturedAuthoredState = true;
    }

    void RestoreAuthoredHierarchy(GameObject root)
    {
        if (root == null)
            return;

        if (root.transform.parent != authoredRootParent)
            root.transform.SetParent(authoredRootParent, false);

        root.transform.localPosition = authoredRootLocalPosition;
        root.transform.localRotation = authoredRootLocalRotation;
        root.transform.localScale = authoredRootLocalScale == Vector3.zero
            ? Vector3.one
            : authoredRootLocalScale;
    }

    void RestoreAuthoredActive(GameObject root)
    {
        if (root != null && root.activeSelf != authoredRootActive)
            root.SetActive(authoredRootActive);
        if (gameObject.activeSelf != authoredObjectActive)
            gameObject.SetActive(authoredObjectActive);
    }

    /// <summary>Used by camera gaze when rays are off (gloves worn).</summary>
    public void NotifyGazeSelected()
    {
        ResolveRefs();
        if (equipmentManager != null && equipmentManager.IsPrepared(PPEEquipmentManager.IdWasteContainer))
            return;

        containerSelected = true;
        selectedAtTime = Time.unscaledTime;

        if (outlineHighlight != null && PPESelectionManager.Instance != null)
            PPESelectionManager.Instance.SelectPPE(outlineHighlight);
    }

    public bool CanConfirmPreparation()
    {
        ResolveRefs();

        if (preparationInProgress)
            return false;

        if (!IsStickySelected())
            return false;

        if (Time.unscaledTime - GetEffectiveSelectedAtTime() < confirmDelay)
            return false;

        if (equipmentManager == null)
            return false;

        if (equipmentManager.IsPrepared(PPEEquipmentManager.IdWasteContainer))
            return false;

        if (requireChemicalGloves && !equipmentManager.IsEquipped(PPEEquipmentManager.IdChemicalGloves))
            return false;

        var root = containerRoot != null ? containerRoot : gameObject;
        if (root == null || !root.activeInHierarchy)
            return false;

        return true;
    }

    public bool TryPrepareFromConfirmRouter()
    {
        if (!CanConfirmPreparation())
            return false;

        return BeginPreparation();
    }

    void ResolveRefs()
    {
        if (interactable == null)
            interactable = GetComponent<XRBaseInteractable>();
        if (outlineHighlight == null)
            outlineHighlight = GetComponent<PPEOutlineHighlight>();
        if (containerRoot == null)
            containerRoot = gameObject;
        if (equipmentManager == null)
        {
            equipmentManager = PPEEquipmentManager.Instance != null
                ? PPEEquipmentManager.Instance
                : FindAnyObjectByType<PPEEquipmentManager>();
        }
    }

    void WireInteractable(bool subscribe)
    {
        if (interactable == null)
            return;

        interactable.selectEntered.RemoveListener(OnSelectEntered);
        interactable.selectExited.RemoveListener(OnSelectExited);
        interactable.activated.RemoveListener(OnActivated);

        if (!subscribe)
            return;

        interactable.selectEntered.AddListener(OnSelectEntered);
        interactable.selectExited.AddListener(OnSelectExited);
        if (!confirmInputOwnedByRouter)
            interactable.activated.AddListener(OnActivated);
    }

    void OnSelectEntered(SelectEnterEventArgs _)
    {
        if (equipmentManager != null && equipmentManager.IsPrepared(PPEEquipmentManager.IdWasteContainer))
            return;

        containerSelected = true;
        selectedAtTime = Time.unscaledTime;
    }

    void OnSelectExited(SelectExitEventArgs _)
    {
        if (preparationInProgress)
            return;
        if (outlineHighlight != null && outlineHighlight.IsSelected)
            return;
    }

    void OnActivated(ActivateEventArgs _)
    {
        if (CanConfirmPreparation())
            TryPrepareFromConfirmRouter();
    }

    bool IsStickySelected()
    {
        if (outlineHighlight != null && outlineHighlight.IsSelected)
            return true;
        return containerSelected;
    }

    float GetEffectiveSelectedAtTime()
    {
        if (outlineHighlight != null
            && outlineHighlight.IsSelected
            && PPESelectionManager.Instance != null
            && PPESelectionManager.Instance.CurrentSelectedPPE == outlineHighlight)
        {
            return PPESelectionManager.Instance.SelectedAtTime;
        }

        return selectedAtTime;
    }

    bool BeginPreparation()
    {
        if (preparationInProgress)
            return false;

        ResolveRefs();
        preparationInProgress = true;

        try
        {
            if (equipmentManager == null)
                return false;

            var sequence = PPESequenceManager.Resolve();
            if (sequence != null && !sequence.CanEquip(PPEEquipmentManager.IdWasteContainer))
            {
                sequence.NotifyEquipBlocked(PPEEquipmentManager.IdWasteContainer);
                return false;
            }

            if (!equipmentManager.SetPrepared(PPEEquipmentManager.IdWasteContainer, true))
                return false;

            var root = containerRoot != null ? containerRoot : gameObject;
            var label = DisplayName;
            Debug.Log($"[Waste Container] Prepared / take-out: {label}", root);

            if (completionAudio != null)
                completionAudio.Play();

            ShowPreparedFeedback();

            if (outlineHighlight != null && PPESelectionManager.Instance != null)
                PPESelectionManager.Instance.NotifyEquipped(outlineHighlight);

            ClearXrInteraction(root);

            if (deactivateOnPrepare && root != null)
                root.SetActive(false);

            containerSelected = false;
            PPESequenceManager.Resolve()?.CompleteEquip(PPEEquipmentManager.IdWasteContainer);
            return true;
        }
        finally
        {
            preparationInProgress = false;
        }
    }

    static void ShowPreparedFeedback()
    {
        var feedback = FindAnyObjectByType<PPEEquipFeedbackUI>(FindObjectsInactive.Include);
        feedback?.ShowMessage(PreparedMessage);
    }

    static void ClearXrInteraction(GameObject root)
    {
        if (root == null)
            return;

        var interactable = root.GetComponent<XRBaseInteractable>();
        if (interactable == null)
            interactable = root.GetComponentInChildren<XRBaseInteractable>(true);
        if (interactable == null)
            return;

        var manager = interactable.interactionManager;
        if (manager == null)
            manager = FindAnyObjectByType<UnityEngine.XR.Interaction.Toolkit.XRInteractionManager>();
        if (manager == null)
            return;

        if (interactable is IXRSelectInteractable selectable && selectable.isSelected)
            manager.CancelInteractableSelection(selectable);

        if (interactable is IXRHoverInteractable hoverable && hoverable.isHovered)
            manager.CancelInteractableHover(hoverable);
    }

    public void EditorWire(
        PPEEquipmentManager manager,
        GameObject root,
        XRBaseInteractable xrInteractable)
    {
        equipmentManager = manager;
        containerRoot = root != null ? root : gameObject;
        interactable = xrInteractable != null ? xrInteractable : GetComponent<XRBaseInteractable>();
        outlineHighlight = GetComponent<PPEOutlineHighlight>();
        displayName = DefaultDisplayName;
        deactivateOnPrepare = true;
        requireChemicalGloves = true;
        confirmInputOwnedByRouter = true;
    }

#if UNITY_EDITOR
    [ContextMenu("Test Prepare (Guarded)")]
    void ContextTestPrepare()
    {
        containerSelected = true;
        selectedAtTime = Time.unscaledTime - confirmDelay;
        if (outlineHighlight != null)
            outlineHighlight.SetSelected(true);
        requireChemicalGloves = false;
        TryPrepareFromConfirmRouter();
        requireChemicalGloves = true;
    }
#endif
}
