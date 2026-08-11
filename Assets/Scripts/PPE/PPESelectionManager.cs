using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Maintains a single sticky PPE selection and notifies the preview controller.
/// Grip once: select + preview. Grip again (via XRInteractionConfirmRouter): equip.
/// While a PPE is selected, other PPE selects are ignored until Right-hand B cancel.
/// Left-hand Y is reserved for PPE checklist visibility toggle.
/// </summary>
public class PPESelectionManager : MonoBehaviour
{
    public static PPESelectionManager Instance { get; private set; }

    [SerializeField]
    PPEOutlineHighlight currentSelectedPPE;

    [SerializeField]
    PPEPreviewController previewController;

    [Header("Cancel Input")]
    [Tooltip("Clears selection. Defaults to Right-hand SecondaryButton (Quest B).")]
    [SerializeField]
    InputActionProperty cancelAction;

    [Header("Equip Confirm (legacy — owned by XRInteractionConfirmRouter when present)")]
    [Tooltip("Kept for Inspector reference handoff to the router. Not subscribed when router owns confirm.")]
    [SerializeField]
    InputActionProperty equipConfirmAction;

    [SerializeField]
    [Min(0f)]
    float equipConfirmDelay = 0.3f;

    [SerializeField]
    bool equipConfirmInputOwnedByRouter = true;

    InputAction ownedCancelAction;
    InputAction ownedEquipConfirmAction;
    bool cancelSubscribed;
    bool equipConfirmSubscribed;
    float selectedAtTime = -999f;

    public PPEOutlineHighlight CurrentSelectedPPE => currentSelectedPPE;
    public float SelectedAtTime => selectedAtTime;
    public float EquipConfirmDelay => equipConfirmDelay;
    public InputActionProperty EquipConfirmActionProperty => equipConfirmAction;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Multiple PPESelectionManager instances found. Keeping the first one.", this);
            enabled = false;
            return;
        }

        Instance = this;

        if (previewController == null)
            previewController = GetComponent<PPEPreviewController>();
        if (previewController == null)
            previewController = FindAnyObjectByType<PPEPreviewController>();

        EnsureCancelAction();
        // Only build fallback action if router is absent at Awake.
        if (!equipConfirmInputOwnedByRouter)
            EnsureEquipConfirmAction();
    }

    void OnEnable()
    {
        EnsureCancelAction();

        var cancel = GetCancelAction();
        if (cancel != null)
        {
            cancel.performed += OnCancelPerformed;
            cancel.Enable();
            cancelSubscribed = true;
        }

        // Prefer shared router when present; otherwise keep legacy Grip subscription.
        var routerPresent = XRInteractionConfirmRouter.Instance != null
            || FindAnyObjectByType<XRInteractionConfirmRouter>() != null;
        equipConfirmInputOwnedByRouter = routerPresent;

        if (!equipConfirmInputOwnedByRouter)
        {
            EnsureEquipConfirmAction();
            WireLegacyEquipConfirm(true);
        }
        else
        {
            WireLegacyEquipConfirm(false);
        }
    }

    void OnDisable()
    {
        var cancel = GetCancelAction();
        if (cancel != null && cancelSubscribed)
        {
            cancel.performed -= OnCancelPerformed;
            if (ownedCancelAction != null)
                cancel.Disable();
        }

        cancelSubscribed = false;
        WireLegacyEquipConfirm(false);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        if (ownedCancelAction != null)
        {
            ownedCancelAction.Dispose();
            ownedCancelAction = null;
        }

        if (ownedEquipConfirmAction != null)
        {
            ownedEquipConfirmAction.Dispose();
            ownedEquipConfirmAction = null;
        }
    }

    public void SetEquipConfirmInputOwnedByRouter(bool owned)
    {
        equipConfirmInputOwnedByRouter = owned;
        if (owned)
            WireLegacyEquipConfirm(false);
        else if (isActiveAndEnabled)
        {
            EnsureEquipConfirmAction();
            WireLegacyEquipConfirm(true);
        }
    }

    /// <summary>True when sticky PPE selection can be equipped after confirm delay.</summary>
    public bool HasValidEquipSelection(float confirmDelayOverride = -1f)
    {
        if (currentSelectedPPE == null)
            return false;

        var delay = confirmDelayOverride >= 0f ? confirmDelayOverride : equipConfirmDelay;
        if (Time.unscaledTime - selectedAtTime < delay)
            return false;

        var equipable = currentSelectedPPE.GetComponent<PPEEquipable>();
        if (equipable == null)
            equipable = currentSelectedPPE.GetComponentInParent<PPEEquipable>();

        return equipable != null && equipable.CanEquip();
    }

    void WireLegacyEquipConfirm(bool subscribe)
    {
        var equipConfirm = GetEquipConfirmAction();
        if (equipConfirm == null)
            return;

        if (equipConfirmSubscribed)
        {
            equipConfirm.performed -= OnEquipConfirmPerformed;
            if (ownedEquipConfirmAction != null)
                equipConfirm.Disable();
            equipConfirmSubscribed = false;
        }

        if (!subscribe || equipConfirmInputOwnedByRouter)
            return;

        equipConfirm.performed += OnEquipConfirmPerformed;
        equipConfirm.Enable();
        equipConfirmSubscribed = true;
    }

    void OnCancelPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed)
            return;

        ClearSelection();
    }

    void OnEquipConfirmPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed)
            return;

        if (Time.unscaledTime - selectedAtTime < equipConfirmDelay)
            return;

        TryEquipCurrentSelection();
    }

    InputAction GetCancelAction()
    {
        if (cancelAction.reference != null)
            return cancelAction.action;
        if (cancelAction.action != null && cancelAction.action.bindings.Count > 0)
            return cancelAction.action;
        return ownedCancelAction;
    }

    InputAction GetEquipConfirmAction()
    {
        if (equipConfirmAction.reference != null)
            return equipConfirmAction.action;
        if (equipConfirmAction.action != null && equipConfirmAction.action.bindings.Count > 0)
            return equipConfirmAction.action;
        return ownedEquipConfirmAction;
    }

    void EnsureCancelAction()
    {
        if (GetCancelAction() != null)
            return;

        ownedCancelAction = new InputAction(
            name: "PPE Cancel Selection",
            type: InputActionType.Button);

        ownedCancelAction.AddBinding("<XRController>{RightHand}/{SecondaryButton}");
        ownedCancelAction.AddBinding("<OculusTouchController>{RightHand}/secondaryButton");
    }

    void EnsureEquipConfirmAction()
    {
        if (GetEquipConfirmAction() != null)
            return;

        ownedEquipConfirmAction = new InputAction(
            name: "PPE Equip Confirm",
            type: InputActionType.Button);

        ownedEquipConfirmAction.AddBinding("<XRController>{RightHand}/{GripButton}");
        ownedEquipConfirmAction.AddBinding("<XRController>{LeftHand}/{GripButton}");
        ownedEquipConfirmAction.AddBinding("<OculusTouchController>{RightHand}/gripPressed");
        ownedEquipConfirmAction.AddBinding("<OculusTouchController>{LeftHand}/gripPressed");
    }

    public void SelectPPE(PPEOutlineHighlight target) => TrySelectPPE(target);

    /// <summary>
    /// Sticky-selects <paramref name="target"/>. While another PPE is already previewed,
    /// selection is locked until Right B cancel (<see cref="ClearSelection"/>).
    /// </summary>
    public bool TrySelectPPE(PPEOutlineHighlight target)
    {
        if (target == null)
            return false;

        if (currentSelectedPPE != null && currentSelectedPPE != target)
        {
            ShowMustCancelBeforeSwitchFeedback();
            return false;
        }

        var isBag = IsResponseBagHighlight(target);

        // Wearable PPE and response-bag preview/panel are mutually exclusive.
        if (!isBag)
            CloseResponseBagContentsPanelIfOpen();

        if (currentSelectedPPE == target)
        {
            // Keep sticky highlight — equip only via XRInteractionConfirmRouter Grip confirm.
            currentSelectedPPE.SetSelected(true);
            return true;
        }

        var previous = currentSelectedPPE;
        currentSelectedPPE = target;
        selectedAtTime = Time.unscaledTime;

        if (previous != null)
            previous.SetSelected(false);

        currentSelectedPPE.SetSelected(true);

        if (previewController != null)
            previewController.HandleSelectionChanged(previous, currentSelectedPPE);

        var response = ResponseEquipmentManager.Instance != null
            ? ResponseEquipmentManager.Instance
            : FindAnyObjectByType<ResponseEquipmentManager>();

        // Bag preview stays empty inside — contents UI opens with Y while selected.
        if (response != null)
        {
            response.HideGearPanel();
            response.HideBagContents();
        }

        if (isBag && response != null && !response.ResponseBagPrepared)
        {
            var feedback = FindAnyObjectByType<PPEEquipFeedbackUI>(FindObjectsInactive.Include);
            feedback?.ShowMessage(
                "Y\uB85C \uB0B4\uBD80 \uD655\uC778 \uD6C4 Grip\uC73C\uB85C \uC900\uBE44\uD558\uC138\uC694.");
        }

        return true;
    }

    static void ShowMustCancelBeforeSwitchFeedback()
    {
        var feedback = FindAnyObjectByType<PPEEquipFeedbackUI>(FindObjectsInactive.Include);
        feedback?.ShowMessage(
            "B\uB85C \uC120\uD0DD\uC744 \uCDE8\uC18C\uD55C \uB4A4 \uB2E4\uB978 \uBCF4\uD638\uAD6C\uB97C \uC120\uD0DD\uD558\uC138\uC694.");
    }

    static bool IsResponseBagHighlight(PPEOutlineHighlight target)
    {
        if (target == null)
            return false;
        return target.GetComponent<ResponseBagPrepareInteractable>() != null
            || target.GetComponentInParent<ResponseBagPrepareInteractable>() != null;
    }

    static void CloseResponseBagContentsPanelIfOpen()
    {
        var manager = ResponseEquipmentManager.Instance != null
            ? ResponseEquipmentManager.Instance
            : FindAnyObjectByType<ResponseEquipmentManager>();
        if (manager != null)
            manager.HideGearPanel();

        var panel = manager != null ? manager.ContentsPanel : null;
        if (panel == null)
            panel = FindAnyObjectByType<ResponseBagContentsPanel>(FindObjectsInactive.Include);
        if (panel != null && panel.IsOpen)
            panel.HidePanel();
    }

    public bool TryEquipCurrentSelection()
    {
        if (currentSelectedPPE == null)
            return false;

        var equipable = currentSelectedPPE.GetComponent<PPEEquipable>();
        if (equipable == null)
            equipable = currentSelectedPPE.GetComponentInParent<PPEEquipable>();

        if (equipable == null)
        {
            Debug.LogWarning(
                $"[PPE Selection] '{currentSelectedPPE.name}' has no PPEEquipable — cannot equip.",
                currentSelectedPPE);
            return false;
        }

        var manager = PPEEquipmentManager.Instance != null
            ? PPEEquipmentManager.Instance
            : FindAnyObjectByType<PPEEquipmentManager>();

        if (manager == null)
        {
            Debug.LogWarning("[PPE Selection] No PPEEquipmentManager in the scene.", this);
            return false;
        }

        return manager.Equip(equipable);
    }

    public void ClearSelection()
    {
        if (currentSelectedPPE == null)
            return;

        var previous = currentSelectedPPE;
        var wasBag = IsResponseBagHighlight(previous);
        currentSelectedPPE = null;
        previous.SetSelected(false);

        if (previewController != null)
            previewController.HandleSelectionCleared(previous);

        if (wasBag)
        {
            var response = ResponseEquipmentManager.Instance != null
                ? ResponseEquipmentManager.Instance
                : FindAnyObjectByType<ResponseEquipmentManager>();
            if (response != null)
            {
                response.HideGearPanel();
                response.HideBagContents();
            }
        }
    }

    public void NotifyEquipped(PPEOutlineHighlight target)
    {
        if (target == null)
            return;

        if (currentSelectedPPE == target)
        {
            currentSelectedPPE = null;
            target.SetSelected(false);
        }

        if (previewController != null)
            previewController.PrepareForEquipDeactivate(target);
    }

    public void NotifyTargetDestroyed(PPEOutlineHighlight target)
    {
        if (currentSelectedPPE != target)
            return;

        currentSelectedPPE = null;

        if (previewController != null)
            previewController.AbandonPreviewWithoutRestore(target);
    }
}
