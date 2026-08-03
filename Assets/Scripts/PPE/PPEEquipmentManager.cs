using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Scene-level PPE equipment state. Tracks equipped Item IDs independently
/// from transform / visibility / XR interaction bookkeeping.
/// </summary>
public class PPEEquipmentManager : MonoBehaviour
{
    public static PPEEquipmentManager Instance { get; private set; }

    [SerializeField]
    List<string> equippedItemIds = new List<string>();

    readonly HashSet<string> equippedLookup = new HashSet<string>();

    /// <summary>Raised after a successful <see cref="Equip"/>.</summary>
    public event Action<PPEEquipable> EquipmentSucceeded;

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
            return false;

        if (!item.CanEquip())
            return false;

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

        EquipmentSucceeded?.Invoke(item);

        if (item.DeactivateOnEquip)
            item.gameObject.SetActive(false);

        return true;
    }

    /// <summary>Clears manager state and resets all <see cref="PPEEquipable"/> instances in the scene.</summary>
    public void ResetAllEquipment()
    {
        equippedLookup.Clear();
        equippedItemIds.Clear();

        var items = FindObjectsByType<PPEEquipable>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i] != null)
            {
                items[i].ResetEquipState();
                if (!items[i].gameObject.activeSelf)
                    items[i].gameObject.SetActive(true);
            }
        }
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
            manager = FindFirstObjectByType<XRInteractionManager>();
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
            : FindFirstObjectByType<PPESelectionManager>();

        if (selection != null)
            selection.NotifyEquipped(highlight);
        else
        {
            var preview = FindFirstObjectByType<PPEPreviewController>();
            if (preview != null && highlight != null)
                preview.PrepareForEquipDeactivate(highlight);
        }
    }

    void RebuildLookupFromSerializedList()
    {
        equippedLookup.Clear();
        if (equippedItemIds == null)
            return;

        for (int i = 0; i < equippedItemIds.Count; i++)
        {
            var id = equippedItemIds[i];
            if (!string.IsNullOrWhiteSpace(id))
                equippedLookup.Add(id);
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
            : FindFirstObjectByType<PPESelectionManager>();

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
}
