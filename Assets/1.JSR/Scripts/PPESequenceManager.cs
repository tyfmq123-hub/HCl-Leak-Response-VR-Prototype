using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Strict equip/prepare-order gate for the PPE preparation room.
/// Wearable donning and response-equipment preparation are separate phases.
/// Does not own select / preview / grab / attach — only CanEquip / CompleteEquip.
/// </summary>
[DisallowMultipleComponent]
public class PPESequenceManager : MonoBehaviour
{
    public const string IdInnerGloves = "InnerGloves";

    static readonly PPESequenceStep[] DefaultDonningOrder =
    {
        PPESequenceStep.InnerGloves,
        PPESequenceStep.ProtectiveSuit,
        PPESequenceStep.ChemicalBoots,
        PPESequenceStep.Respirator,
        PPESequenceStep.FaceProtection,
        PPESequenceStep.ProtectiveGloves,
    };

    static readonly PPESequenceStep[] DefaultEquipmentPreparationOrder =
    {
        PPESequenceStep.ResponseBag,
        PPESequenceStep.WasteContainer,
    };

    static readonly PPESequenceStep[] EmptyOrder = Array.Empty<PPESequenceStep>();

    public static PPESequenceManager Instance { get; private set; }

    [SerializeField]
    [Tooltip("Wearable PPE order. Helmet can be inserted later; it is not in the production default.")]
    List<PPESequenceStep> donningOrder = new List<PPESequenceStep>();

    [SerializeField]
    [Tooltip("Response equipment preparation order. These are not wearable PPE steps.")]
    List<PPESequenceStep> equipmentPreparationOrder = new List<PPESequenceStep>();

    [SerializeField]
    [HideInInspector]
    [Tooltip("Legacy mixed order. Kept so existing 04_PPE scene data can migrate into the two phase lists.")]
    List<PPESequenceStep> sequenceOrder = new List<PPESequenceStep>();

    [SerializeField]
    [Tooltip("Show a toast when placement/equip is blocked by sequence (or bag not checked).")]
    bool showBlockedFeedback = true;

    [SerializeField]
    [Tooltip("Korean/English toast when the wrong PPE is equipped out of order.")]
    string blockedMessage =
        "\uC21C\uC11C\uC5D0 \uB9DE\uB294 PPE\uB97C \uBA3C\uC800 \uCC29\uC6A9\uD558\uC138\uC694.";

    [SerializeField]
    [Tooltip("Toast when Response Bag equip is attempted before contents are checked (Y).")]
    string bagNotCheckedMessage =
        "Y\uB85C \uB300\uC751\uAC00\uBC29 \uB0B4\uBD80\uB97C \uBA3C\uC800 \uD655\uC778\uD558\uC138\uC694.";

    PPETrainingPhase currentPhase = PPETrainingPhase.Donning;
    int phaseStepIndex;
    bool bagContentsChecked;
    bool sequenceComplete;
    bool resetInProgress;

    readonly HashSet<PPESequenceStep> completedSteps = new HashSet<PPESequenceStep>();

    /// <summary>Authoritative training phase. Do not infer this from list indices.</summary>
    public PPETrainingPhase CurrentPhase => currentPhase;

    /// <summary>True after both donning and equipment preparation are complete.</summary>
    public bool IsSequenceComplete => sequenceComplete;

    /// <summary>True after every configured donning step has completed.</summary>
    public bool IsDonningComplete =>
        currentPhase == PPETrainingPhase.EquipmentPreparation ||
        currentPhase == PPETrainingPhase.Complete;

    /// <summary>True after the player confirmed bag contents once (Y inspect).</summary>
    public bool BagContentsChecked => bagContentsChecked;

    /// <summary>Authoritative completion state used by non-interactive room guidance UI.</summary>
    public bool IsStepCompleted(PPESequenceStep step) => completedSteps.Contains(step);

    /// <summary>Current required step, or null when the sequence is complete.</summary>
    public PPESequenceStep? CurrentRequiredStep
    {
        get
        {
            if (sequenceComplete || currentPhase == PPETrainingPhase.Complete)
                return null;

            var order = GetActivePhaseOrder();
            if (phaseStepIndex < 0 || phaseStepIndex >= order.Count)
                return null;
            return order[phaseStepIndex];
        }
    }

    /// <summary>Fired when the required step advances (or resets).</summary>
    public event Action<PPESequenceStep?> OnRequiredStepChanged;

    /// <summary>Fired when the training phase changes.</summary>
    public event Action<PPETrainingPhase, PPETrainingPhase> OnPhaseChanged;

    /// <summary>Fired once when the full PPE preparation sequence finishes.</summary>
    public event Action OnPPESequenceCompleted;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[PPE Sequence] Multiple PPESequenceManager instances. Keeping the first.", this);
            enabled = false;
            return;
        }

        Instance = this;
        EnsurePhaseOrders();
        currentPhase = PPETrainingPhase.Donning;
        phaseStepIndex = 0;
        bagContentsChecked = false;
        sequenceComplete = false;
        completedSteps.Clear();
        LogCurrentState();
    }

    void Start()
    {
        WarnIfCurrentStepMissingInScene();
        RefreshRequiredStepHighlights();
    }

    void WarnIfCurrentStepMissingInScene()
    {
        var required = CurrentRequiredStep;
        if (required == null)
            return;

        var itemId = ToItemId(required.Value);
        if (string.IsNullOrEmpty(itemId))
            return;

        var found = false;
        var equipables = FindObjectsByType<PPEEquipable>(FindObjectsInactive.Include);
        for (var i = 0; i < equipables.Length; i++)
        {
            if (equipables[i] != null
                && string.Equals(equipables[i].ItemId, itemId, StringComparison.Ordinal))
            {
                found = true;
                break;
            }
        }

        if (!found && required.Value == PPESequenceStep.WasteContainer)
        {
            // Waste uses prepare interactable, not always PPEEquipable.
            found = FindAnyObjectByType<WasteContainerPrepareInteractable>(FindObjectsInactive.Include) != null;
        }

        if (!found)
        {
            Debug.LogWarning(
                $"[PPE Sequence] No scene object for required step '{required.Value}' (ItemId='{itemId}'). " +
                "Add a PPEEquipable with that ItemId, or remove the step from the phase lists on PPESequenceManager.",
                this);
        }
    }

    void OnDestroy()
    {
        // Only finalize an already-existing loadout. Never Ensure()/spawn DDOL from OnDestroy —
        // that leaves "PPELoadoutData" behind when stopping Play Mode.
        if (Application.isPlaying
            && Instance == this
            && PPELoadoutData.Instance != null
            && PPELoadoutData.Instance.Source == PPELoadoutSource.None)
        {
            PPELoadoutPresets.CommitPpeSceneResult();
        }

        if (Instance == this)
            Instance = null;

        PPEHandGrabbable.SuppressReleaseEquip = false;
    }

    void OnValidate()
    {
        EnsurePhaseOrders();
    }

    void EnsurePhaseOrders()
    {
        if (donningOrder == null)
            donningOrder = new List<PPESequenceStep>();
        if (equipmentPreparationOrder == null)
            equipmentPreparationOrder = new List<PPESequenceStep>();
        if (sequenceOrder == null)
            sequenceOrder = new List<PPESequenceStep>();

        var donningEmpty = donningOrder.Count == 0;
        var equipmentEmpty = equipmentPreparationOrder.Count == 0;
        if (donningEmpty && equipmentEmpty && sequenceOrder.Count > 0)
            MigrateLegacySequenceOrder();

        if (donningOrder.Count == 0)
            donningOrder.AddRange(DefaultDonningOrder);
        if (equipmentPreparationOrder.Count == 0)
            equipmentPreparationOrder.AddRange(DefaultEquipmentPreparationOrder);
    }

    void MigrateLegacySequenceOrder()
    {
        donningOrder.Clear();
        equipmentPreparationOrder.Clear();
        for (var i = 0; i < sequenceOrder.Count; i++)
        {
            var step = sequenceOrder[i];
            if (IsBuiltInEquipmentPreparationStep(step))
                equipmentPreparationOrder.Add(step);
            else
                donningOrder.Add(step);
        }

        Debug.Log(
            $"[PPE Sequence] Migrated legacy sequenceOrder ({sequenceOrder.Count}) into " +
            $"donning ({donningOrder.Count}) and equipment preparation ({equipmentPreparationOrder.Count}).",
            this);
    }

    IReadOnlyList<PPESequenceStep> GetActivePhaseOrder()
    {
        EnsurePhaseOrders();
        switch (currentPhase)
        {
            case PPETrainingPhase.Donning:
                return donningOrder;
            case PPETrainingPhase.EquipmentPreparation:
                return equipmentPreparationOrder;
            default:
                return EmptyOrder;
        }
    }

    /// <summary>True when either configured phase list contains this step.</summary>
    public bool ContainsStep(PPESequenceStep step)
    {
        EnsurePhaseOrders();
        return ContainsIn(donningOrder, step) || ContainsIn(equipmentPreparationOrder, step);
    }

    public bool IsDonningStep(PPESequenceStep step)
    {
        EnsurePhaseOrders();
        return ContainsIn(donningOrder, step);
    }

    public bool IsEquipmentPreparationStep(PPESequenceStep step)
    {
        EnsurePhaseOrders();
        return ContainsIn(equipmentPreparationOrder, step);
    }

    static bool IsBuiltInEquipmentPreparationStep(PPESequenceStep step)
    {
        return step == PPESequenceStep.ResponseBag || step == PPESequenceStep.WasteContainer;
    }

    static bool ContainsIn(List<PPESequenceStep> order, PPESequenceStep step)
    {
        if (order == null)
            return false;
        for (var i = 0; i < order.Count; i++)
        {
            if (order[i] == step)
                return true;
        }

        return false;
    }

    /// <summary>True only when this step is the current required equip/prepare target.</summary>
    public bool CanEquip(PPESequenceStep type)
    {
        if (sequenceComplete || currentPhase == PPETrainingPhase.Complete)
            return false;

        if (completedSteps.Contains(type))
            return false;

        var required = CurrentRequiredStep;
        if (required == null || required.Value != type)
            return false;

        if (type == PPESequenceStep.ResponseBag && !bagContentsChecked)
            return false;

        return true;
    }

    /// <summary>True only when this item id maps to the current required step (and bag rules).</summary>
    public bool CanEquip(string itemId)
    {
        if (!TryGetStep(itemId, out var step))
            return false;

        return CanEquip(step);
    }

    /// <summary>
    /// Call after a successful equip/prepare. Advances only for the current required step.
    /// Duplicate / wrong-step calls are ignored. Completing ProtectiveGloves still requires
    /// the caller to treat left/right physical gloves as one logical step.
    /// </summary>
    public bool CompleteEquip(PPESequenceStep type)
    {
        if (sequenceComplete || currentPhase == PPETrainingPhase.Complete)
            return false;

        if (completedSteps.Contains(type))
            return false;

        var required = CurrentRequiredStep;
        if (required == null || required.Value != type)
            return false;

        if (type == PPESequenceStep.ResponseBag && !bagContentsChecked)
            return false;

        completedSteps.Add(type);
        var completedPhase = currentPhase;
        var order = GetActivePhaseOrder();
        phaseStepIndex++;

        var tts = PPETTSManager.Instance;
        tts?.NotifyStepCompleted(type);

        if (phaseStepIndex < order.Count)
        {
            RefreshRequiredStepHighlights();
            OnRequiredStepChanged?.Invoke(order[phaseStepIndex]);
            PPELoadoutData.SyncFromScene();
            LogCurrentState();
            Debug.Log($"[PPE Sequence] Step complete: {type}. Next: {order[phaseStepIndex]}", this);
            return true;
        }

        if (completedPhase == PPETrainingPhase.Donning)
        {
            EnterEquipmentPreparation();
            return true;
        }

        FinishSequence();
        return true;
    }

    /// <summary>Item-id overload for existing equip scripts.</summary>
    public bool CompleteEquip(string itemId)
    {
        if (!TryGetStep(itemId, out var step))
            return false;

        return CompleteEquip(step);
    }

    void EnterEquipmentPreparation()
    {
        var previous = currentPhase;
        currentPhase = PPETrainingPhase.EquipmentPreparation;
        phaseStepIndex = 0;
        EnsurePhaseOrders();

        Debug.Log("[PPE Sequence] Donning completed. Phase changed: Donning → EquipmentPreparation", this);
        OnPhaseChanged?.Invoke(previous, currentPhase);
        RefreshRequiredStepHighlights();
        OnRequiredStepChanged?.Invoke(CurrentRequiredStep);
        PPELoadoutData.SyncFromScene();
        LogCurrentState();
    }

    void FinishSequence()
    {
        var previous = currentPhase;
        currentPhase = PPETrainingPhase.Complete;
        sequenceComplete = true;
        phaseStepIndex = 0;

        Debug.Log("[PPE Sequence] EquipmentPreparation completed. Preparation sequence complete.", this);
        OnPhaseChanged?.Invoke(previous, currentPhase);
        RefreshRequiredStepHighlights();
        OnRequiredStepChanged?.Invoke(null);
        // Finalize before completed listeners so they see Source=PpeSceneResult.
        PPELoadoutPresets.CommitPpeSceneResult();
        OnPPESequenceCompleted?.Invoke();
    }

    /// <summary>
    /// Marks bag contents as checked once. Safe to call repeatedly after the first confirm.
    /// Does not advance the sequence by itself.
    /// </summary>
    public void NotifyBagContentsChecked()
    {
        if (bagContentsChecked)
            return;

        bagContentsChecked = true;
        PPELoadoutData.SyncFromScene();
        Debug.Log("[PPE Sequence] Response bag contents checked.", this);
    }

    /// <summary>Optional feedback when an equip attempt is blocked by sequence rules.</summary>
    public void NotifyEquipBlocked(string itemId)
    {
        if (PPETTSManager.Instance != null && PPETTSManager.Instance.IsBusy)
            return;

        if (TryGetStep(itemId, out var telemetryStep))
        {
            HClTraining.SharedXR.SharedTrainingTelemetry.Current.ContentViewed(
                "PPE.EquipBlocked." + telemetryStep);
        }

        PPETTSManager.Instance?.NotifyEquipBlocked(itemId);

        if (!showBlockedFeedback)
            return;

        var message = blockedMessage;
        if (TryGetStep(itemId, out var step)
            && step == PPESequenceStep.ResponseBag
            && CurrentRequiredStep == PPESequenceStep.ResponseBag
            && !bagContentsChecked)
        {
            message = bagNotCheckedMessage;
        }

        var feedback = FindAnyObjectByType<PPEEquipFeedbackUI>(FindObjectsInactive.Include);
        feedback?.ShowMessage(message);
    }

    /// <summary>Editor / debug: restart the sequence without touching PPE visuals.</summary>
    public void ResetSequence()
    {
        currentPhase = PPETrainingPhase.Donning;
        phaseStepIndex = 0;
        bagContentsChecked = false;
        sequenceComplete = false;
        completedSteps.Clear();
        EnsurePhaseOrders();
        RefreshRequiredStepHighlights();
        OnRequiredStepChanged?.Invoke(CurrentRequiredStep);
        PPELoadoutData.SyncFromScene();
        LogCurrentState();
        Debug.Log("[PPE Sequence] Sequence reset.", this);
    }

    /// <summary>
    /// Full PPE Room training reset: physical PPE, pair flags, ghosts, equipment,
    /// and sequence. Does not recreate authored objects. Invalid in-world drops
    /// are restored only through this path, not on a missed release.
    /// </summary>
    [ContextMenu("Reset PPE Room Training")]
    public void ResetPpeRoomTraining()
    {
        if (resetInProgress)
            return;

        resetInProgress = true;
        PPEHandGrabbable.SuppressReleaseEquip = true;
        try
        {
            CancelWearableSelections();

            var selection = PPESelectionManager.Instance != null
                ? PPESelectionManager.Instance
                : FindAnyObjectByType<PPESelectionManager>(FindObjectsInactive.Include);
            selection?.ClearSelection();

            var placement = FindAnyObjectByType<PPEPlacementGuideManager>(FindObjectsInactive.Include);
            if (placement != null)
            {
                placement.StopAllCoroutines();
                placement.HideActiveGuides();
            }

            var equipment = PPEEquipmentManager.Instance != null
                ? PPEEquipmentManager.Instance
                : FindAnyObjectByType<PPEEquipmentManager>(FindObjectsInactive.Include);
            equipment?.ResetAllEquipment();

            var coordinators = FindObjectsByType<PPEGlovePairCoordinator>(
                FindObjectsInactive.Include);
            for (var i = 0; i < coordinators.Length; i++)
            {
                if (coordinators[i] != null)
                    coordinators[i].ResetPair();
            }

            var storedStates = FindObjectsByType<PPEStoredDisplayState>(
                FindObjectsInactive.Include);
            for (var i = 0; i < storedStates.Length; i++)
            {
                var stored = storedStates[i];
                if (stored == null)
                    continue;
                if (stored.GetComponent<PPEGlovePairCoordinator>() != null)
                    continue;
                stored.RestoreToStoredPose();
            }

            ResetSequence();
        }
        finally
        {
            PPEHandGrabbable.SuppressReleaseEquip = false;
            resetInProgress = false;
        }

        Debug.Log("[PPE Sequence] Full PPE Room training reset.", this);
    }

    static void CancelWearableSelections()
    {
        var grabs = FindObjectsByType<PPEHandGrabbable>(FindObjectsInactive.Include);
        for (var i = 0; i < grabs.Length; i++)
        {
            var grab = grabs[i];
            if (grab == null)
                continue;

            var manager = grab.interactionManager;
            if (manager == null)
                manager = FindAnyObjectByType<XRInteractionManager>();
            if (manager == null)
                continue;

            IXRSelectInteractable selectable = grab;
            IXRHoverInteractable hoverable = grab;
            if (selectable.isSelected)
                manager.CancelInteractableSelection(selectable);
            if (hoverable.isHovered)
                manager.CancelInteractableHover(hoverable);
        }
    }

    void LogCurrentState()
    {
        var required = CurrentRequiredStep;
        var order = GetActivePhaseOrder();
        if (currentPhase == PPETrainingPhase.Complete || sequenceComplete)
        {
            Debug.Log("[PPE Sequence] Phase: Complete", this);
            return;
        }

        Debug.Log(
            $"[PPE Sequence] Phase: {currentPhase} Step: {phaseStepIndex + 1}/{order.Count} Required: {required}",
            this);
    }

    public static string ToItemId(PPESequenceStep step)
    {
        switch (step)
        {
            case PPESequenceStep.Helmet:
                return PPEEquipmentManager.IdHelmet;
            case PPESequenceStep.InnerGloves:
                return IdInnerGloves;
            case PPESequenceStep.ProtectiveSuit:
                return PPEEquipmentManager.IdChemicalSuit;
            case PPESequenceStep.ChemicalBoots:
                return PPEEquipmentManager.IdChemicalBoots;
            case PPESequenceStep.Respirator:
                return PPEEquipmentManager.IdFaceRespirator;
            case PPESequenceStep.ResponseBag:
                return PPEEquipmentManager.IdResponseBag;
            case PPESequenceStep.FaceProtection:
                return PPEEquipmentManager.IdFaceShield;
            case PPESequenceStep.ProtectiveGloves:
                return PPEEquipmentManager.IdChemicalGloves;
            case PPESequenceStep.WasteContainer:
                return PPEEquipmentManager.IdWasteContainer;
            default:
                return string.Empty;
        }
    }

    void RefreshRequiredStepHighlights()
    {
        var highlights = FindObjectsByType<PPEOutlineHighlight>(FindObjectsInactive.Include);
        Array.Sort(highlights, (left, right) =>
        {
            var leftId = left != null ? left.GetEntityId().GetHashCode() : 0;
            var rightId = right != null ? right.GetEntityId().GetHashCode() : 0;
            return leftId.CompareTo(rightId);
        });
        var required = CurrentRequiredStep;
        var claimedCurrentTarget = false;

        for (var i = 0; i < highlights.Length; i++)
        {
            var highlight = highlights[i];
            if (highlight == null)
                continue;

            var matches = required.HasValue &&
                          TryResolveHighlightStep(highlight, out var step) &&
                          step == required.Value;
            var isUsableCurrentTarget = matches && !claimedCurrentTarget &&
                                        highlight.gameObject.activeInHierarchy &&
                                        highlight.enabled;
            highlight.SetRequiredStepHighlighted(isUsableCurrentTarget);
            claimedCurrentTarget |= isUsableCurrentTarget;
        }
    }

    static bool TryResolveHighlightStep(PPEOutlineHighlight highlight, out PPESequenceStep step)
    {
        step = default;
        if (highlight == null)
            return false;

        var equipable = highlight.GetComponent<PPEEquipable>();
        if (equipable == null)
            equipable = highlight.GetComponentInParent<PPEEquipable>(true);
        if (equipable == null)
            equipable = highlight.GetComponentInChildren<PPEEquipable>(true);
        if (equipable != null)
            return TryGetStep(equipable.ItemId, out step);

        if (highlight.GetComponent<WasteContainerPrepareInteractable>() != null ||
            highlight.GetComponentInParent<WasteContainerPrepareInteractable>(true) != null)
        {
            step = PPESequenceStep.WasteContainer;
            return true;
        }

        if (highlight.GetComponent<ResponseBagPrepareInteractable>() != null ||
            highlight.GetComponentInParent<ResponseBagPrepareInteractable>(true) != null)
        {
            step = PPESequenceStep.ResponseBag;
            return true;
        }

        return false;
    }

    public static bool TryGetStep(string itemId, out PPESequenceStep step)
    {
        step = default;
        if (string.IsNullOrWhiteSpace(itemId))
            return false;

        if (string.Equals(itemId, PPEEquipmentManager.IdHelmet, StringComparison.Ordinal))
        {
            step = PPESequenceStep.Helmet;
            return true;
        }

        if (string.Equals(itemId, IdInnerGloves, StringComparison.Ordinal))
        {
            step = PPESequenceStep.InnerGloves;
            return true;
        }

        if (string.Equals(itemId, PPEEquipmentManager.IdChemicalSuit, StringComparison.Ordinal))
        {
            step = PPESequenceStep.ProtectiveSuit;
            return true;
        }

        if (string.Equals(itemId, PPEEquipmentManager.IdChemicalBoots, StringComparison.Ordinal))
        {
            step = PPESequenceStep.ChemicalBoots;
            return true;
        }

        if (string.Equals(itemId, PPEEquipmentManager.IdFaceRespirator, StringComparison.Ordinal))
        {
            step = PPESequenceStep.Respirator;
            return true;
        }

        if (string.Equals(itemId, PPEEquipmentManager.IdResponseBag, StringComparison.Ordinal))
        {
            step = PPESequenceStep.ResponseBag;
            return true;
        }

        if (string.Equals(itemId, PPEEquipmentManager.IdFaceShield, StringComparison.Ordinal))
        {
            step = PPESequenceStep.FaceProtection;
            return true;
        }

        if (string.Equals(itemId, PPEEquipmentManager.IdChemicalGloves, StringComparison.Ordinal))
        {
            step = PPESequenceStep.ProtectiveGloves;
            return true;
        }

        if (string.Equals(itemId, PPEEquipmentManager.IdWasteContainer, StringComparison.Ordinal))
        {
            step = PPESequenceStep.WasteContainer;
            return true;
        }

        return false;
    }

    /// <summary>Resolve manager without requiring callers to null-check Instance alone.</summary>
    public static PPESequenceManager Resolve()
    {
        if (Instance != null)
            return Instance;

        return FindAnyObjectByType<PPESequenceManager>(FindObjectsInactive.Include);
    }
}
