using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Single owner of the shared confirm InputAction.
/// Routes one press to either response-bag prepare or PPE equip — never both.
/// </summary>
[DefaultExecutionOrder(-50)]
[DisallowMultipleComponent]
public class XRInteractionConfirmRouter : MonoBehaviour
{
    public static XRInteractionConfirmRouter Instance { get; private set; }

    [Header("Targets")]
    [SerializeField] ResponseBagPrepareInteractable responseBagPrepareInteractable;
    [SerializeField] PPESelectionManager ppeSelectionManager;

    [Header("Confirm Input")]
    [Tooltip("Reuse the assigned confirm InputActionProperty. Router is the sole subscriber.")]
    [SerializeField] InputActionProperty confirmAction;

    [SerializeField]
    [Min(0f)]
    float equipConfirmDelay = 0.3f;

    InputAction ownedConfirmAction;
    bool confirmSubscribed;
    float lastRoutedAt = -999f;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[Confirm Router] Multiple instances — keeping the first.", this);
            enabled = false;
            return;
        }

        Instance = this;
        ResolveRefs();
        BorrowConfirmPropertyFromSelectionIfEmpty();
        EnsureConfirmAction();
    }

    void OnEnable()
    {
        ResolveRefs();
        BorrowConfirmPropertyFromSelectionIfEmpty();
        EnsureConfirmAction();
        WireConfirm(true);

        // Prevent parallel Grip subscriptions on the same action.
        if (ppeSelectionManager != null)
            ppeSelectionManager.SetEquipConfirmInputOwnedByRouter(true);

        if (responseBagPrepareInteractable != null)
            responseBagPrepareInteractable.SetConfirmInputOwnedByRouter(true);
    }

    void OnDisable()
    {
        WireConfirm(false);

        if (ppeSelectionManager != null)
            ppeSelectionManager.SetEquipConfirmInputOwnedByRouter(false);

        if (responseBagPrepareInteractable != null)
            responseBagPrepareInteractable.SetConfirmInputOwnedByRouter(false);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        WireConfirm(false);

        if (ownedConfirmAction != null)
        {
            ownedConfirmAction.Dispose();
            ownedConfirmAction = null;
        }
    }

    public void EditorWire(
        ResponseBagPrepareInteractable bag,
        PPESelectionManager selection)
    {
        responseBagPrepareInteractable = bag;
        ppeSelectionManager = selection;
        if (selection != null)
            confirmAction = selection.EquipConfirmActionProperty;
    }

    void ResolveRefs()
    {
        if (responseBagPrepareInteractable == null)
        {
            responseBagPrepareInteractable =
                FindAnyObjectByType<ResponseBagPrepareInteractable>(FindObjectsInactive.Exclude);
        }

        if (ppeSelectionManager == null)
        {
            ppeSelectionManager = PPESelectionManager.Instance != null
                ? PPESelectionManager.Instance
                : FindAnyObjectByType<PPESelectionManager>();
        }
    }

    void BorrowConfirmPropertyFromSelectionIfEmpty()
    {
        if (GetAssignedConfirmAction() != null || ppeSelectionManager == null)
            return;

        var fromSelection = ppeSelectionManager.EquipConfirmActionProperty;
        if (fromSelection.reference != null
            || (fromSelection.action != null && fromSelection.action.bindings.Count > 0))
        {
            confirmAction = fromSelection;
        }
    }

    void EnsureConfirmAction()
    {
        if (GetConfirmAction() != null)
            return;

        // Fallback only when no InputActionProperty is assigned on router or selection.
        // Uses the same historical bindings as PPESelectionManager (not a new button mapping).
        ownedConfirmAction = new InputAction(
            name: "XR Shared Confirm",
            type: InputActionType.Button);
        ownedConfirmAction.AddBinding("<XRController>{RightHand}/{GripButton}");
        ownedConfirmAction.AddBinding("<XRController>{LeftHand}/{GripButton}");
        ownedConfirmAction.AddBinding("<OculusTouchController>{RightHand}/gripPressed");
        ownedConfirmAction.AddBinding("<OculusTouchController>{LeftHand}/gripPressed");
    }

    InputAction GetAssignedConfirmAction()
    {
        if (confirmAction.reference != null)
            return confirmAction.action;
        if (confirmAction.action != null && confirmAction.action.bindings.Count > 0)
            return confirmAction.action;
        return null;
    }

    InputAction GetConfirmAction()
    {
        var assigned = GetAssignedConfirmAction();
        if (assigned != null)
            return assigned;
        return ownedConfirmAction;
    }

    void WireConfirm(bool subscribe)
    {
        var action = GetConfirmAction();
        if (action == null)
            return;

        if (confirmSubscribed)
        {
            action.performed -= OnConfirmPerformed;
            if (ownedConfirmAction != null)
                action.Disable();
            confirmSubscribed = false;
        }

        if (!subscribe)
            return;

        action.performed += OnConfirmPerformed;
        action.Enable();
        confirmSubscribed = true;
    }

    void OnConfirmPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed)
            return;

        // Debounce accidental double-fire within one press.
        if (Time.unscaledTime - lastRoutedAt < 0.05f)
            return;

        ResolveRefs();

        // First Grip while hovering a *different* outline = switch sticky select (never equip that press).
        if (TrySelectHoveredOutline())
        {
            lastRoutedAt = Time.unscaledTime;
            return;
        }

        // Do not equip the old sticky item while the ray is still aiming at a different outline.
        if (IsHoveringConflictingOutline())
            return;

        // Priority 1: world response bag sticky selection.
        if (responseBagPrepareInteractable != null
            && responseBagPrepareInteractable.CanConfirmPreparation())
        {
            if (responseBagPrepareInteractable.TryPrepareFromConfirmRouter())
                lastRoutedAt = Time.unscaledTime;
            return;
        }

        // Priority 2: PPE equip.
        if (ppeSelectionManager != null
            && ppeSelectionManager.HasValidEquipSelection(equipConfirmDelay))
        {
            if (ppeSelectionManager.TryEquipCurrentSelection())
                lastRoutedAt = Time.unscaledTime;
        }
    }

    bool TrySelectHoveredOutline()
    {
        if (ppeSelectionManager == null)
            return false;

        var hovered = FindHoveredOutline();
        if (hovered == null)
            return false;

        // Already sticky-selected — let confirm/equip path handle this Grip.
        if (hovered.IsSelected && ppeSelectionManager.CurrentSelectedPPE == hovered)
            return false;

        // Switching to another outline (PPE <-> PPE, or PPE <-> bag): select only, no equip.
        ppeSelectionManager.SelectPPE(hovered);
        return true;
    }

    bool IsHoveringConflictingOutline()
    {
        var hovered = FindHoveredOutline();
        if (hovered == null)
            return false;

        if (ppeSelectionManager == null || ppeSelectionManager.CurrentSelectedPPE == null)
            return false;

        return hovered != ppeSelectionManager.CurrentSelectedPPE;
    }

    static PPEOutlineHighlight FindHoveredOutline()
    {
        var rays = FindObjectsByType<XRRayInteractor>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (var r = 0; r < rays.Length; r++)
        {
            var ray = rays[r];
            if (ray == null || !ray.isActiveAndEnabled)
                continue;

            var hoveredInteractables = ray.interactablesHovered;
            for (var i = 0; i < hoveredInteractables.Count; i++)
            {
                var interactable = hoveredInteractables[i];
                if (interactable == null)
                    continue;

                var t = interactable.transform;
                if (t == null)
                    continue;

                var outline = t.GetComponent<PPEOutlineHighlight>();
                if (outline == null)
                    outline = t.GetComponentInParent<PPEOutlineHighlight>();
                if (outline != null && outline.isActiveAndEnabled)
                    return outline;
            }
        }

        var highlights = FindObjectsByType<PPEOutlineHighlight>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (var i = 0; i < highlights.Length; i++)
        {
            var h = highlights[i];
            if (h != null && h.isActiveAndEnabled && h.IsHovered)
                return h;
        }

        return null;
    }
}
