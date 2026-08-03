using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Maintains a single sticky PPE selection and notifies the preview controller.
/// Grip once: select + preview. Grip again: equip current selection.
/// Right-hand B / left-hand Y (SecondaryButton) clears the current selection.
/// </summary>
public class PPESelectionManager : MonoBehaviour
{
    public static PPESelectionManager Instance { get; private set; }

    [SerializeField]
    PPEOutlineHighlight currentSelectedPPE;

    [SerializeField]
    PPEPreviewController previewController;

    [Header("Cancel Input")]
    [Tooltip("Clears selection. Defaults to XR SecondaryButton (Quest B / Y).")]
    [SerializeField]
    InputActionProperty cancelAction;

    [Header("Equip Confirm (Grip)")]
    [Tooltip("Second Grip press equips the currently selected PPE. Defaults to XR GripButton.")]
    [SerializeField]
    InputActionProperty equipConfirmAction;

    [SerializeField]
    [Min(0f)]
    float equipConfirmDelay = 0.3f;

    InputAction ownedCancelAction;
    InputAction ownedEquipConfirmAction;
    bool cancelSubscribed;
    bool equipConfirmSubscribed;
    float selectedAtTime = -999f;

    public PPEOutlineHighlight CurrentSelectedPPE => currentSelectedPPE;

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
            previewController = FindFirstObjectByType<PPEPreviewController>();

        EnsureCancelAction();
        EnsureEquipConfirmAction();
    }

    void OnEnable()
    {
        EnsureCancelAction();
        EnsureEquipConfirmAction();

        var cancel = GetCancelAction();
        if (cancel != null)
        {
            cancel.performed += OnCancelPerformed;
            cancel.Enable();
            cancelSubscribed = true;
        }

        var equipConfirm = GetEquipConfirmAction();
        if (equipConfirm != null)
        {
            equipConfirm.performed += OnEquipConfirmPerformed;
            equipConfirm.Enable();
            equipConfirmSubscribed = true;
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

        var equipConfirm = GetEquipConfirmAction();
        if (equipConfirm != null && equipConfirmSubscribed)
        {
            equipConfirm.performed -= OnEquipConfirmPerformed;
            if (ownedEquipConfirmAction != null)
                equipConfirm.Disable();
        }

        equipConfirmSubscribed = false;
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

        // Ignore the same Grip pulse that just selected / started preview.
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
        ownedCancelAction.AddBinding("<XRController>{LeftHand}/{SecondaryButton}");
        ownedCancelAction.AddBinding("<OculusTouchController>{RightHand}/secondaryButton");
        ownedCancelAction.AddBinding("<OculusTouchController>{LeftHand}/secondaryButton");
    }

    void EnsureEquipConfirmAction()
    {
        if (GetEquipConfirmAction() != null)
            return;

        // Same physical button as XRI Select (Grip) — used as "confirm equip".
        ownedEquipConfirmAction = new InputAction(
            name: "PPE Equip Confirm",
            type: InputActionType.Button);

        ownedEquipConfirmAction.AddBinding("<XRController>{RightHand}/{GripButton}");
        ownedEquipConfirmAction.AddBinding("<XRController>{LeftHand}/{GripButton}");
        ownedEquipConfirmAction.AddBinding("<OculusTouchController>{RightHand}/gripPressed");
        ownedEquipConfirmAction.AddBinding("<OculusTouchController>{LeftHand}/gripPressed");
    }

    /// <summary>
    /// Selects <paramref name="target"/> as the only selected PPE.
    /// Selecting the same target again attempts to equip it.
    /// </summary>
    public void SelectPPE(PPEOutlineHighlight target)
    {
        if (target == null)
            return;

        if (currentSelectedPPE == target)
        {
            currentSelectedPPE.SetSelected(true);
            TryEquipCurrentSelection();
            return;
        }

        var previous = currentSelectedPPE;
        currentSelectedPPE = target;
        selectedAtTime = Time.unscaledTime;

        if (previous != null)
            previous.SetSelected(false);

        currentSelectedPPE.SetSelected(true);

        if (previewController != null)
            previewController.HandleSelectionChanged(previous, currentSelectedPPE);
    }

    /// <summary>
    /// Equips the current sticky selection via <see cref="PPEEquipmentManager"/>.
    /// </summary>
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
            : FindFirstObjectByType<PPEEquipmentManager>();

        if (manager == null)
        {
            Debug.LogWarning("[PPE Selection] No PPEEquipmentManager in the scene.", this);
            return false;
        }

        return manager.Equip(equipable);
    }

    /// <summary>
    /// Clears the current sticky selection and restores hover/none visuals.
    /// </summary>
    public void ClearSelection()
    {
        if (currentSelectedPPE == null)
            return;

        var previous = currentSelectedPPE;
        currentSelectedPPE = null;
        previous.SetSelected(false);

        if (previewController != null)
            previewController.HandleSelectionCleared(previous);
    }

    /// <summary>
    /// Clears sticky selection for an equipped item without playing the return animation.
    /// Instantly restores slot parenting so the root can be deactivated safely.
    /// </summary>
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

    /// <summary>
    /// Called when a selected highlight component is destroyed.
    /// </summary>
    public void NotifyTargetDestroyed(PPEOutlineHighlight target)
    {
        if (currentSelectedPPE != target)
            return;

        currentSelectedPPE = null;

        // Do not animate/reparent — the GameObject is already being destroyed.
        if (previewController != null)
            previewController.AbandonPreviewWithoutRestore(target);
    }
}
