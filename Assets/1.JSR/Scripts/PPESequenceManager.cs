using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Strict equip-order gate for the PPE preparation room.
/// Does not own select / preview / grab / attach — only CanEquip / CompleteEquip.
/// </summary>
[DisallowMultipleComponent]
public class PPESequenceManager : MonoBehaviour
{
    public const string IdInnerGloves = "InnerGloves";

    static readonly PPESequenceStep[] DefaultOrder =
    {
        PPESequenceStep.InnerGloves,
        PPESequenceStep.ProtectiveSuit,
        PPESequenceStep.ChemicalBoots,
        PPESequenceStep.Respirator,
        PPESequenceStep.ResponseBag,
        PPESequenceStep.FaceProtection,
        PPESequenceStep.ProtectiveGloves,
        PPESequenceStep.WasteContainer,
    };

    public static PPESequenceManager Instance { get; private set; }

    [SerializeField]
    [Tooltip("Equip order. Empty uses the built-in default sequence.")]
    List<PPESequenceStep> sequenceOrder = new List<PPESequenceStep>();

    [SerializeField]
    [Tooltip("Show a toast when Grip/equip is blocked by sequence (or bag not checked).")]
    bool showBlockedFeedback = true;

    [SerializeField]
    [Tooltip("Korean/English toast when the wrong PPE is equipped out of order.")]
    string blockedMessage =
        "\uC21C\uC11C\uC5D0 \uB9DE\uB294 PPE\uB97C \uBA3C\uC800 \uCC29\uC6A9\uD558\uC138\uC694.";

    [SerializeField]
    [Tooltip("Toast when Response Bag equip is attempted before contents are checked (Y).")]
    string bagNotCheckedMessage =
        "Y\uB85C \uB300\uC751\uAC00\uBC29 \uB0B4\uBD80\uB97C \uBA3C\uC800 \uD655\uC778\uD558\uC138\uC694.";

    int currentIndex;
    bool bagContentsChecked;
    bool sequenceComplete;

    readonly HashSet<PPESequenceStep> completedSteps = new HashSet<PPESequenceStep>();

    /// <summary>True after Hazardous Waste Collection Container is successfully completed.</summary>
    public bool IsSequenceComplete => sequenceComplete;

    /// <summary>True after the player confirmed bag contents once (Y inspect).</summary>
    public bool BagContentsChecked => bagContentsChecked;

    /// <summary>Current required step, or null when the sequence is complete.</summary>
    public PPESequenceStep? CurrentRequiredStep
    {
        get
        {
            var order = GetOrder();
            if (sequenceComplete || currentIndex < 0 || currentIndex >= order.Count)
                return null;
            return order[currentIndex];
        }
    }

    /// <summary>Fired when the required step advances (or resets).</summary>
    public event Action<PPESequenceStep?> OnRequiredStepChanged;

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
        EnsureDefaultOrder();
        currentIndex = 0;
        bagContentsChecked = false;
        sequenceComplete = false;
        completedSteps.Clear();
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
        var equipables = FindObjectsByType<PPEEquipable>(FindObjectsInactive.Include, FindObjectsSortMode.None);
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
                "Add a PPEEquipable with that ItemId, or remove the step from Sequence Order on PPESequenceManager.",
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
    }

    void OnValidate()
    {
        EnsureDefaultOrder();
    }

    void EnsureDefaultOrder()
    {
        if (sequenceOrder == null)
            sequenceOrder = new List<PPESequenceStep>();

        if (sequenceOrder.Count == 0)
            sequenceOrder.AddRange(DefaultOrder);
    }

    IReadOnlyList<PPESequenceStep> GetOrder()
    {
        EnsureDefaultOrder();
        return sequenceOrder;
    }

    /// <summary>True only when this step is the current required equip target.</summary>
    public bool CanEquip(PPESequenceStep type)
    {
        if (PPETTSManager.Instance != null && PPETTSManager.Instance.IsBusy)
            return false;

        if (sequenceComplete)
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
    /// Duplicate / wrong-step calls are ignored.
    /// </summary>
    public bool CompleteEquip(PPESequenceStep type)
    {
        if (PPETTSManager.Instance != null && PPETTSManager.Instance.IsBusy)
            return false;

        if (sequenceComplete)
            return false;

        if (completedSteps.Contains(type))
            return false;

        var required = CurrentRequiredStep;
        if (required == null || required.Value != type)
            return false;

        if (type == PPESequenceStep.ResponseBag && !bagContentsChecked)
            return false;

        completedSteps.Add(type);
        currentIndex++;

        var tts = PPETTSManager.Instance;
        tts?.NotifyStepCompleted(type);

        var order = GetOrder();
        if (currentIndex >= order.Count)
        {
            sequenceComplete = true;
            RefreshRequiredStepHighlights();
            OnRequiredStepChanged?.Invoke(null);
            // Finalize before completed listeners so they see Source=PpeSceneResult.
            PPELoadoutPresets.CommitPpeSceneResult();
            OnPPESequenceCompleted?.Invoke();
            Debug.Log("[PPE Sequence] Preparation sequence complete.", this);
            return true;
        }

        RefreshRequiredStepHighlights();
        OnRequiredStepChanged?.Invoke(order[currentIndex]);
        PPELoadoutData.SyncFromScene();
        Debug.Log($"[PPE Sequence] Step complete: {type}. Next: {order[currentIndex]}", this);
        return true;
    }

    /// <summary>Item-id overload for existing equip scripts.</summary>
    public bool CompleteEquip(string itemId)
    {
        if (!TryGetStep(itemId, out var step))
            return false;

        return CompleteEquip(step);
    }

    /// <summary>
    /// Marks bag contents as checked once. Safe to call repeatedly after the first confirm.
    /// Does not advance the sequence by itself.
    /// </summary>
    public void NotifyBagContentsChecked()
    {
        if (PPETTSManager.Instance != null && PPETTSManager.Instance.IsBusy)
            return;

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
        currentIndex = 0;
        bagContentsChecked = false;
        sequenceComplete = false;
        completedSteps.Clear();
        EnsureDefaultOrder();
        RefreshRequiredStepHighlights();
        OnRequiredStepChanged?.Invoke(CurrentRequiredStep);
        PPELoadoutData.SyncFromScene();
        Debug.Log("[PPE Sequence] Sequence reset.", this);
    }

    public static string ToItemId(PPESequenceStep step)
    {
        switch (step)
        {
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
        var highlights = FindObjectsByType<PPEOutlineHighlight>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.InstanceID);
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
