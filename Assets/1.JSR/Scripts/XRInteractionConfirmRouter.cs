using HClTraining.SharedXR;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Owns PPE-room Grip routing. Placement is primary for wearable PPE, while an
/// Inspector-controlled legacy button fallback remains available during rollout.
/// </summary>
[DefaultExecutionOrder(-50)]
[DisallowMultipleComponent]
public class XRInteractionConfirmRouter : MonoBehaviour
{
    enum ControllerHand
    {
        Unknown,
        Left,
        Right,
    }

    public static XRInteractionConfirmRouter Instance { get; private set; }

    [Header("Targets")]
    [SerializeField] ResponseBagPrepareInteractable responseBagPrepareInteractable;
    [SerializeField] WasteContainerPrepareInteractable wasteContainerPrepareInteractable;
    [SerializeField] PPESelectionManager ppeSelectionManager;

    [Header("Confirm Input")]
    [Tooltip("Reuse the assigned confirm InputActionProperty. Router is the sole subscriber.")]
    [SerializeField] InputActionProperty confirmAction;

    [SerializeField]
    [Tooltip("Legacy Ray+Grip wearable Equip. Must stay off in 04_PPE. Pair PPE would skip Left/Right physical completion.")]
    bool allowLegacyWearableEquipFallback = false;

    [SerializeField, Min(0f)]
    float legacyEquipConfirmDelay = 0.3f;

    InputAction ownedConfirmAction;
    bool confirmSubscribed;
    SharedXRInputContract sharedInput;
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
        EnsureConfirmAction();
    }

    void OnEnable()
    {
        ResolveRefs();
        ConfigurePpeRayTargeting();
        EnsureConfirmAction();
        sharedInput = SharedXRInputContract.Instance;
        if (sharedInput != null)
            sharedInput.Performed += OnSharedInputPerformed;
        else
            WireConfirm(true);

        if (responseBagPrepareInteractable != null)
            responseBagPrepareInteractable.SetConfirmInputOwnedByRouter(true);

        if (wasteContainerPrepareInteractable != null)
            wasteContainerPrepareInteractable.SetConfirmInputOwnedByRouter(true);
    }

    void OnDisable()
    {
        if (sharedInput != null)
            sharedInput.Performed -= OnSharedInputPerformed;
        sharedInput = null;
        WireConfirm(false);

        if (responseBagPrepareInteractable != null)
            responseBagPrepareInteractable.SetConfirmInputOwnedByRouter(false);

        if (wasteContainerPrepareInteractable != null)
            wasteContainerPrepareInteractable.SetConfirmInputOwnedByRouter(false);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        if (sharedInput != null)
            sharedInput.Performed -= OnSharedInputPerformed;
        sharedInput = null;
        WireConfirm(false);

        if (ownedConfirmAction != null)
        {
            ownedConfirmAction.Dispose();
            ownedConfirmAction = null;
        }
    }

    public void EditorWire(
        ResponseBagPrepareInteractable bag,
        PPESelectionManager selection,
        WasteContainerPrepareInteractable waste = null)
    {
        responseBagPrepareInteractable = bag;
        ppeSelectionManager = selection;
        wasteContainerPrepareInteractable = waste;
    }

    void ResolveRefs()
    {
        if (responseBagPrepareInteractable == null)
        {
            responseBagPrepareInteractable =
                FindAnyObjectByType<ResponseBagPrepareInteractable>(FindObjectsInactive.Exclude);
        }

        if (wasteContainerPrepareInteractable == null)
        {
            wasteContainerPrepareInteractable =
                FindAnyObjectByType<WasteContainerPrepareInteractable>(FindObjectsInactive.Exclude);
        }

        if (ppeSelectionManager == null)
        {
            ppeSelectionManager = PPESelectionManager.Instance != null
                ? PPESelectionManager.Instance
                : FindAnyObjectByType<PPESelectionManager>();
        }
    }

    void EnsureConfirmAction()
    {
        if (GetConfirmAction() != null)
            return;

        // PPE-room Grip remains useful for ray selection and non-wearable bag/container preparation.
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

        RouteConfirm(ResolveInputHand(context));
    }

    void OnSharedInputPerformed(SharedXRHand hand, SharedXRControl control)
    {
        if (control != SharedXRControl.Select)
            return;
        RouteConfirm(hand == SharedXRHand.Left ? ControllerHand.Left : ControllerHand.Right);
    }

    void RouteConfirm(ControllerHand inputHand)
    {
        if (PPETTSManager.Instance != null && PPETTSManager.Instance.IsBusy)
            return;

        // Debounce accidental double-fire within one press.
        if (Time.unscaledTime - lastRoutedAt < 0.05f)
            return;

        ResolveRefs();

        // Response bag is a physical one-step action after all three items are
        // packed. Near/Far hand interactors are not XRRayInteractor instances, so
        // route their hovered Grip before the generic sticky PPE selection flow.
        if (responseBagPrepareInteractable != null
            && responseBagPrepareInteractable.TryPrepareFromPackingGripHover())
        {
            lastRoutedAt = Time.unscaledTime;
            return;
        }

        // First Grip while hovering a *different* outline = switch sticky select (never equip that press).
        if (TrySelectHoveredOutline(inputHand))
        {
            lastRoutedAt = Time.unscaledTime;
            return;
        }

        // Do not equip the old sticky item while the ray is still aiming at a different outline.
        if (IsHoveringConflictingOutline(inputHand))
            return;

        // Priority 1: world response bag sticky selection.
        if (responseBagPrepareInteractable != null
            && responseBagPrepareInteractable.CanConfirmPreparation())
        {
            if (responseBagPrepareInteractable.TryPrepareFromConfirmRouter())
                lastRoutedAt = Time.unscaledTime;
            return;
        }

        // Priority 2: hazardous waste container (gaze/ray sticky + Grip take-out).
        if (wasteContainerPrepareInteractable != null
            && wasteContainerPrepareInteractable.CanConfirmPreparation())
        {
            if (wasteContainerPrepareInteractable.TryPrepareFromConfirmRouter())
                lastRoutedAt = Time.unscaledTime;
            return;
        }

        // Priority 3: legacy wearable fallback. It calls the same Equip method as
        // placement and can be disabled after headset validation is complete.
        if (allowLegacyWearableEquipFallback
            && ppeSelectionManager != null
            && ppeSelectionManager.HasValidEquipSelection(legacyEquipConfirmDelay)
            && ppeSelectionManager.TryEquipCurrentSelection())
        {
            lastRoutedAt = Time.unscaledTime;
        }
    }

    bool TrySelectHoveredOutline(ControllerHand inputHand)
    {
        if (ppeSelectionManager == null)
            return false;

        var hovered = FindHoveredOutline(inputHand);
        if (hovered == null)
            return false;

        // Already sticky-selected — repeated Grip does not equip wearable PPE.
        if (hovered.IsSelected && ppeSelectionManager.CurrentSelectedPPE == hovered)
            return false;

        // Switching to another outline (PPE <-> PPE, or PPE <-> bag): select only, no equip.
        ppeSelectionManager.SelectPPE(hovered);
        return true;
    }

    bool IsHoveringConflictingOutline(ControllerHand inputHand)
    {
        var hovered = FindHoveredOutline(inputHand);
        if (hovered == null)
            return false;

        if (ppeSelectionManager == null || ppeSelectionManager.CurrentSelectedPPE == null)
            return false;

        return hovered != ppeSelectionManager.CurrentSelectedPPE;
    }

    static PPEOutlineHighlight FindHoveredOutline(ControllerHand inputHand)
    {
        var rays = FindObjectsByType<XRRayInteractor>(FindObjectsInactive.Exclude);
        PPEOutlineHighlight found = null;
        for (var r = 0; r < rays.Length; r++)
        {
            var ray = rays[r];
            if (ray == null || !ray.isActiveAndEnabled || !RayMatchesHand(ray, inputHand))
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
                if (outline == null || !outline.isActiveAndEnabled)
                    continue;
                if (found == null)
                    found = outline;
                else if (found != outline)
                    return null;
            }
        }

        if (found != null || inputHand != ControllerHand.Unknown)
            return found;

        var highlights = FindObjectsByType<PPEOutlineHighlight>(FindObjectsInactive.Exclude);

        for (var i = 0; i < highlights.Length; i++)
        {
            var h = highlights[i];
            if (h == null || !h.isActiveAndEnabled || !h.IsHovered)
                continue;
            if (found == null)
                found = h;
            else if (found != h)
                return null;
        }

        return found;
    }

    static void ConfigurePpeRayTargeting()
    {
        var rays = FindObjectsByType<XRRayInteractor>(FindObjectsInactive.Exclude);
        for (var i = 0; i < rays.Length; i++)
        {
            var ray = rays[i];
            if (ray != null)
                ray.hitClosestOnly = true;
        }
    }

    static ControllerHand ResolveInputHand(InputAction.CallbackContext context)
    {
        var bindingIndex = context.action != null && context.control != null
            ? context.action.GetBindingIndexForControl(context.control)
            : -1;
        if (bindingIndex >= 0 && bindingIndex < context.action.bindings.Count)
        {
            var bindingPath = context.action.bindings[bindingIndex].effectivePath;
            var fromBinding = ResolveHandText(bindingPath);
            if (fromBinding != ControllerHand.Unknown)
                return fromBinding;
        }

        var device = context.control?.device;
        if (device == null)
            return ControllerHand.Unknown;

        var usages = device.usages;
        for (var i = 0; i < usages.Count; i++)
        {
            var fromUsage = ResolveHandText(usages[i].ToString());
            if (fromUsage != ControllerHand.Unknown)
                return fromUsage;
        }

        return ResolveHandText(device.name + " " + device.displayName + " " + context.control.path);
    }

    static ControllerHand ResolveHandText(string value)
    {
        if (string.IsNullOrEmpty(value))
            return ControllerHand.Unknown;
        if (value.IndexOf("RightHand", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            value.IndexOf("Right Hand", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return ControllerHand.Right;
        }
        if (value.IndexOf("LeftHand", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            value.IndexOf("Left Hand", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return ControllerHand.Left;
        }
        return ControllerHand.Unknown;
    }

    static bool RayMatchesHand(XRRayInteractor ray, ControllerHand inputHand)
    {
        if (inputHand == ControllerHand.Unknown)
            return true;

        var expectedName = inputHand == ControllerHand.Left ? "Left Controller" : "Right Controller";
        for (var current = ray.transform; current != null; current = current.parent)
        {
            if (current.name == expectedName)
                return true;
            if (current.name == "Right Controller" || current.name == "Left Controller")
                return false;
        }
        return false;
    }
}
