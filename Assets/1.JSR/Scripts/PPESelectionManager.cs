using HClTraining.SharedXR;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Maintains a single sticky PPE selection and notifies the preview controller.
/// Ray/Grip selection identifies and previews PPE. Placement is the primary equip path;
/// XRInteractionConfirmRouter may retain the legacy button path as an Inspector fallback.
/// While a PPE is selected, other PPE selects are ignored until Right-hand B cancel.
/// Left-hand X is reserved for PPE checklist visibility toggle.
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

    InputAction ownedCancelAction;
    bool cancelSubscribed;
    SharedXRInputContract sharedInput;
    float selectedAtTime = -999f;

    public PPEOutlineHighlight CurrentSelectedPPE => currentSelectedPPE;
    public float SelectedAtTime => selectedAtTime;

    /// <summary>True when sticky PPE selection can use the legacy confirm fallback.</summary>
    public bool HasValidEquipSelection(float confirmDelay)
    {
        if (currentSelectedPPE == null || Time.unscaledTime - selectedAtTime < confirmDelay)
            return false;

        var equipable = currentSelectedPPE.GetComponent<PPEEquipable>();
        if (equipable == null)
            equipable = currentSelectedPPE.GetComponentInParent<PPEEquipable>();
        return equipable != null && equipable.CanEquip();
    }

    /// <summary>Routes legacy fallback through the same equipment/state system.</summary>
    public bool TryEquipCurrentSelection()
    {
        if (currentSelectedPPE == null)
            return false;

        var equipable = currentSelectedPPE.GetComponent<PPEEquipable>();
        if (equipable == null)
            equipable = currentSelectedPPE.GetComponentInParent<PPEEquipable>();
        var manager = PPEEquipmentManager.Instance != null
            ? PPEEquipmentManager.Instance
            : FindAnyObjectByType<PPEEquipmentManager>();
        if (equipable == null || manager == null)
            return false;

        var pair = equipable.GetComponent<PPEGlovePairCoordinator>();
        if (pair != null && !pair.BothEquipped)
            return false;

        return manager.Equip(equipable);
    }

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
    }

    void OnEnable()
    {
        EnsureCancelAction();

        sharedInput = SharedXRInputContract.Instance;
        if (sharedInput != null)
        {
            sharedInput.Performed += OnSharedInputPerformed;
        }
        else
        {
            var cancel = GetCancelAction();
            if (cancel != null)
            {
                cancel.performed += OnCancelPerformed;
                cancel.Enable();
                cancelSubscribed = true;
            }
        }

    }

    void OnDisable()
    {
        if (sharedInput != null)
            sharedInput.Performed -= OnSharedInputPerformed;
        sharedInput = null;

        var cancel = GetCancelAction();
        if (cancel != null && cancelSubscribed)
        {
            cancel.performed -= OnCancelPerformed;
            if (ownedCancelAction != null)
                cancel.Disable();
        }

        cancelSubscribed = false;
    }

    void OnSharedInputPerformed(SharedXRHand hand, SharedXRControl control)
    {
        if (hand == SharedXRHand.Right && control == SharedXRControl.SecondaryButton)
            ClearSelection();
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

    }

    void OnCancelPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed)
            return;

        ClearSelection();
    }

    InputAction GetCancelAction()
    {
        if (cancelAction.reference != null)
            return cancelAction.action;
        if (cancelAction.action != null && cancelAction.action.bindings.Count > 0)
            return cancelAction.action;
        return ownedCancelAction;
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

    public void SelectPPE(PPEOutlineHighlight target) => TrySelectPPE(target);

    /// <summary>
    /// Sticky-selects <paramref name="target"/> for ray identification and preview.
    /// While another PPE is already previewed, selection is locked until Right B cancel.
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
            // Keep sticky highlight. Repeated selection never equips wearable PPE.
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
