using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Scene-level PPE equipment + preparation state. Tracks Item IDs independently
/// from transform / visibility / XR interaction bookkeeping.
/// </summary>
public class PPEEquipmentManager : MonoBehaviour
{
    public const string IdHelmet = "Helmet";
    public const string IdFaceShield = "ChemicalFaceShield";
    public const string IdFaceRespirator = "FaceRespirator";
    public const string IdChemicalSuit = "ChemicalSuit";
    public const string IdChemicalGloves = "ChemicalGloves";
    public const string IdChemicalBoots = "ChemicalSafetyBoots";
    public const string IdResponseBag = "ResponseBag";
    public const string IdWasteContainer = "WasteContainer";
    /// <summary>Optional first sequence step — assign a PPEEquipable with this ItemId when present.</summary>
    public const string IdInnerGloves = PPESequenceManager.IdInnerGloves;

    public static PPEEquipmentManager Instance { get; private set; }

    [SerializeField]
    List<string> equippedItemIds = new List<string>();

    [SerializeField]
    [Tooltip("Non-wearable preparation items (response bag, waste container, etc.).")]
    List<string> preparedItemIds = new List<string>();

    readonly HashSet<string> equippedLookup = new HashSet<string>();
    readonly HashSet<string> preparedLookup = new HashSet<string>();

    /// <summary>Raised after a successful <see cref="Equip"/>.</summary>
    public event Action<PPEEquipable> EquipmentSucceeded;

    /// <summary>Raised after <see cref="SetPrepared"/> changes preparation state.</summary>
    public event Action<string, bool> PreparationChanged;

    /// <summary>Raised after any equip / preparation / reset that checklist UIs should refresh from.
    /// UI must query this manager — do not cache completion in the panel.
    /// </summary>
    public event Action StateChanged;

    /// <summary>Copies equipped ids into <paramref name="destination"/> (clears it first).</summary>
    public void CopyEquippedIdsTo(List<string> destination)
    {
        if (destination == null)
            return;

        destination.Clear();
        if (equippedItemIds == null)
            return;

        for (var i = 0; i < equippedItemIds.Count; i++)
        {
            var id = equippedItemIds[i];
            if (!string.IsNullOrWhiteSpace(id))
                destination.Add(id);
        }
    }

    /// <summary>Copies prepared ids into <paramref name="destination"/> (clears it first).</summary>
    public void CopyPreparedIdsTo(List<string> destination)
    {
        if (destination == null)
            return;

        destination.Clear();
        if (preparedItemIds == null)
            return;

        for (var i = 0; i < preparedItemIds.Count; i++)
        {
            var id = preparedItemIds[i];
            if (!string.IsNullOrWhiteSpace(id))
                destination.Add(id);
        }
    }

    static void SyncLoadoutData()
    {
        PPELoadoutData.SyncFromScene();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Multiple PPEEquipmentManager instances found. Keeping the first one.", this);
            enabled = false;
            return;
        }

        Instance = this;
        RebuildLookupFromSerializedList();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void OnValidate()
    {
        RebuildLookupFromSerializedList();
    }

    /// <summary>True if <paramref name="itemId"/> is in the equipped collection.</summary>
    public bool IsEquipped(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return false;

        return equippedLookup.Contains(itemId);
    }

    /// <summary>
    /// Marks ResponseBag equipped in lookup lists after Grip-prepare (without full Equip()).
    /// </summary>
    public void MirrorResponseBagEquipped()
    {
        equippedLookup.Add(IdResponseBag);
        if (!equippedItemIds.Contains(IdResponseBag))
            equippedItemIds.Add(IdResponseBag);

        SyncLoadoutData();
    }

    /// <summary>Removes an equipped id without resetting the whole inventory (used by response-bag reset).</summary>
    public void ClearEquippedId(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return;

        equippedLookup.Remove(itemId);
        equippedItemIds.Remove(itemId);
        SyncLoadoutData();
    }

    /// <summary>True if a non-wearable preparation item is marked ready.</summary>
    public bool IsPrepared(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return false;

        return preparedLookup.Contains(itemId);
    }

    /// <summary>
    /// Marks or clears preparation for ResponseBag / WasteContainer (and similar IDs).
    /// Does not affect PPE wearables — those use <see cref="Equip"/>.
    /// </summary>
    public bool SetPrepared(string itemId, bool prepared)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return false;

        var changed = false;
        if (prepared)
        {
            if (preparedLookup.Add(itemId))
            {
                if (!preparedItemIds.Contains(itemId))
                    preparedItemIds.Add(itemId);
                changed = true;
            }
        }
        else
        {
            if (preparedLookup.Remove(itemId))
            {
                preparedItemIds.Remove(itemId);
                changed = true;
            }
        }

        if (!changed)
            return false;

        PreparationChanged?.Invoke(itemId, prepared);
        RaiseStateChanged();
        return true;
    }

    /// <summary>
    /// Marks <paramref name="item"/> equipped if allowed.
    /// Clears XR hover/select, restores preview ownership, raises
    /// <see cref="EquipmentSucceeded"/>, then safely deactivates the PPE root when requested.
    /// </summary>
    public bool Equip(PPEEquipable item)
    {
        if (item == null)
            return false;

        if (string.IsNullOrWhiteSpace(item.ItemId))
            return false;

        if (IsEquipped(item.ItemId))
        {
            PPETTSManager.Instance?.NotifyAlreadyEquipped();
            return false;
        }

        if (!item.CanEquip())
            return false;

        if (IsIncompletePairLogicalEquip(item))
        {
            Debug.LogWarning(
                "[PPE Equipment] Refused logical pair Equip before both physical sides. Item=" +
                item.ItemId,
                item);
            return false;
        }

        // Sequence gate: select/preview stay allowed; only real equip is blocked.
        var sequence = PPESequenceManager.Resolve();
        if (sequence != null && !sequence.CanEquip(item.ItemId))
        {
            sequence.NotifyEquipBlocked(item.ItemId);
            return false;
        }

        if (item.ItemId == IdResponseBag)
            return EquipResponseBagViaContentsFlow(item);

        if (!equippedLookup.Add(item.ItemId))
            return false;

        if (!equippedItemIds.Contains(item.ItemId))
            equippedItemIds.Add(item.ItemId);

        item.MarkAsEquipped();

        var label = string.IsNullOrWhiteSpace(item.DisplayName) ? item.ItemId : item.DisplayName;
        Debug.Log($"[PPE Equipped] {label}", item);

        // Exit XR select/hover before deactivate so interactors do not keep stale targets.
        ClearXrInteraction(item);

        PrepareSelectedItemForDeactivation(item);

        if (item.ItemId == IdResponseBag)
            SyncResponseBagPreparedFromEquip(item);

        EquipmentSucceeded?.Invoke(item);
        RaiseStateChanged();

        if (item.DeactivateOnEquip)
            item.gameObject.SetActive(false);

        sequence?.CompleteEquip(item.ItemId);
        return true;
    }

    static bool EquipResponseBagViaContentsFlow(PPEEquipable item)
    {
        var response = ResponseEquipmentManager.Instance != null
            ? ResponseEquipmentManager.Instance
            : FindAnyObjectByType<ResponseEquipmentManager>();

        if (response == null)
        {
            Debug.LogWarning("[PPE Equipment] ResponseEquipmentManager missing — cannot equip response bag.");
            return false;
        }

        // Sequence + bag-contents gate (CanEquip already checked in Equip, but re-check for safety).
        var sequence = PPESequenceManager.Resolve();
        if (sequence != null && !sequence.CanEquip(IdResponseBag))
        {
            sequence.NotifyEquipBlocked(IdResponseBag);
            return false;
        }

        if (!response.TryCompleteResponseBagPreparation())
            return false;

        // Mirror PPE equipped bookkeeping after ResponseEquipmentManager prepares.
        var ppe = Instance;
        if (ppe != null)
            ppe.MirrorResponseBagEquipped();

        if (item != null && !item.IsEquipped)
            item.MarkAsEquipped();

        // Sync preview restore + XR cleanup (PrepareResponseBag already does most of this,
        // but Equip path historically skipped NotifyEquipped / EquipmentSucceeded).
        if (item != null)
        {
            ClearXrInteraction(item);

            var highlight = item.GetComponent<PPEOutlineHighlight>();
            if (highlight == null)
                highlight = item.GetComponentInChildren<PPEOutlineHighlight>(true);
            if (highlight != null && PPESelectionManager.Instance != null)
                PPESelectionManager.Instance.NotifyEquipped(highlight);

            // Do not raise EquipmentSucceeded — ResponseEquipmentManager already showed the
            // prepared toast; PPEEquipFeedbackUI would show a second "착용" message.
            if (ppe != null)
                ppe.RaiseStateChanged();
        }

        // PrepareResponseBag also Completes — duplicate CompleteEquip is ignored safely.
        sequence?.CompleteEquip(IdResponseBag);
        return true;
    }

    static void SyncResponseBagPreparedFromEquip(PPEEquipable item)
    {
        var response = ResponseEquipmentManager.Instance != null
            ? ResponseEquipmentManager.Instance
            : FindAnyObjectByType<ResponseEquipmentManager>();

        if (response == null)
        {
            Debug.LogWarning(
                "[PPE Equipment] ResponseEquipmentManager missing — checklist ResponseBagPrepared not synced.");
            return;
        }

        response.NotifyPreparedFromPpeEquip(item != null ? item.gameObject : null);
    }

    /// <summary>Clears manager state and resets all <see cref="PPEEquipable"/> instances in the scene.</summary>
    public void ResetAllEquipment()
    {
        equippedLookup.Clear();
        equippedItemIds.Clear();
        preparedLookup.Clear();
        preparedItemIds.Clear();

        var items = FindObjectsByType<PPEEquipable>(FindObjectsInactive.Include);
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i] != null)
            {
                items[i].ResetEquipState();
                if (!items[i].gameObject.activeSelf)
                    items[i].gameObject.SetActive(true);
            }
        }

        var response = ResponseEquipmentManager.Instance != null
            ? ResponseEquipmentManager.Instance
            : FindAnyObjectByType<ResponseEquipmentManager>();
        if (response != null)
            response.ResetResponseBag();

        var wasteContainers = FindObjectsByType<WasteContainerPrepareInteractable>(
            FindObjectsInactive.Include);
        for (var i = 0; i < wasteContainers.Length; i++)
        {
            if (wasteContainers[i] != null)
                wasteContainers[i].ResetPreparedState();
        }

        RaiseStateChanged();
    }

    void RaiseStateChanged()
    {
        StateChanged?.Invoke();
        SyncLoadoutData();
    }

    static void ClearXrInteraction(PPEEquipable item)
    {
        if (item == null)
            return;

        var interactable = item.GetComponent<XRBaseInteractable>();
        if (interactable == null)
            interactable = item.GetComponentInChildren<XRBaseInteractable>(true);
        if (interactable == null)
            return;

        var manager = interactable.interactionManager;
        if (manager == null)
            manager = FindAnyObjectByType<XRInteractionManager>();
        if (manager == null)
            return;

        if (interactable is IXRSelectInteractable selectable && selectable.isSelected)
            manager.CancelInteractableSelection(selectable);

        if (interactable is IXRHoverInteractable hoverable && hoverable.isHovered)
            manager.CancelInteractableHover(hoverable);
    }

    static void PrepareSelectedItemForDeactivation(PPEEquipable item)
    {
        if (item == null)
            return;

        var highlight = item.GetComponent<PPEOutlineHighlight>();
        if (highlight == null)
            highlight = item.GetComponentInChildren<PPEOutlineHighlight>(true);

        var selection = PPESelectionManager.Instance != null
            ? PPESelectionManager.Instance
            : FindAnyObjectByType<PPESelectionManager>();

        if (selection != null)
            selection.NotifyEquipped(highlight);
        else
        {
            var preview = FindAnyObjectByType<PPEPreviewController>();
            if (preview != null && highlight != null)
                preview.PrepareForEquipDeactivate(highlight);
        }
    }

    /// <summary>
    /// Logical pair roots (InnerGloves, ChemicalBoots, ChemicalGloves) must not Equip
    /// until both physical sides are on. Blocks Ray+Grip even if the legacy fallback is on.
    /// </summary>
    static bool IsIncompletePairLogicalEquip(PPEEquipable item)
    {
        if (item == null)
            return false;

        var pair = item.GetComponent<PPEGlovePairCoordinator>();
        if (pair == null)
            return false;
        if (pair.LogicalItem != null && pair.LogicalItem != item)
            return false;
        return !pair.BothEquipped;
    }

    void RebuildLookupFromSerializedList()
    {
        equippedLookup.Clear();
        if (equippedItemIds != null)
        {
            for (int i = 0; i < equippedItemIds.Count; i++)
            {
                var id = equippedItemIds[i];
                if (!string.IsNullOrWhiteSpace(id))
                    equippedLookup.Add(id);
            }
        }

        preparedLookup.Clear();
        if (preparedItemIds != null)
        {
            for (int i = 0; i < preparedItemIds.Count; i++)
            {
                var id = preparedItemIds[i];
                if (!string.IsNullOrWhiteSpace(id))
                    preparedLookup.Add(id);
            }
        }
    }

    /// <summary>
    /// Temporary test: equip the currently selected PPE from <see cref="PPESelectionManager"/>.
    /// </summary>
    [ContextMenu("Test Equip Current Selection")]
    public void TestEquipCurrentSelection()
    {
        var selection = PPESelectionManager.Instance != null
            ? PPESelectionManager.Instance
            : FindAnyObjectByType<PPESelectionManager>();

        if (selection == null)
        {
            Debug.LogWarning("[PPE Equipment] No PPESelectionManager found.", this);
            return;
        }

        var selected = selection.CurrentSelectedPPE;
        if (selected == null)
        {
            Debug.LogWarning("[PPE Equipment] No PPE currently selected. Select a PPE with the ray first.", this);
            return;
        }

        var equipable = selected.GetComponent<PPEEquipable>();
        if (equipable == null)
            equipable = selected.GetComponentInParent<PPEEquipable>();

        if (equipable == null)
        {
            Debug.LogWarning(
                $"[PPE Equipment] Selected '{selected.name}' has no PPEEquipable on root.",
                selected);
            return;
        }

        var ok = Equip(equipable);
        Debug.Log(
            ok
                ? $"[PPE Equipment] Test equip succeeded: {equipable.DisplayName} ({equipable.ItemId})"
                : $"[PPE Equipment] Test equip failed (already equipped or cannot equip): {equipable.DisplayName} ({equipable.ItemId})",
            equipable);
    }

#if UNITY_EDITOR
    [ContextMenu("Test Prepare ResponseBag")]
    void EditorTestPrepareResponseBag() => SetPrepared(IdResponseBag, true);

    [ContextMenu("Test Prepare WasteContainer")]
    void EditorTestPrepareWasteContainer() => SetPrepared(IdWasteContainer, true);
#endif
}
