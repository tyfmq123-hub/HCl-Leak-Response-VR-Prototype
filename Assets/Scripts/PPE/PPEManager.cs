using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scene-level registry for PPE items and equipped state.
/// Does not perform wear / unequip behavior — that belongs to later phases.
/// Does not control highlight rendering directly.
/// </summary>
public class PPEManager : MonoBehaviour
{
    public static PPEManager Instance { get; private set; }

    readonly Dictionary<PPEType, List<PPEItem>> registeredItems = new Dictionary<PPEType, List<PPEItem>>();
    readonly Dictionary<PPEType, bool> equippedStates = new Dictionary<PPEType, bool>();

    PPEItem currentHoveredItem;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Multiple PPEManager instances found. Keeping the first one.", this);
            enabled = false;
            return;
        }

        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        currentHoveredItem = null;
        registeredItems.Clear();
    }

    public void RegisterItem(PPEItem item)
    {
        if (item == null)
        {
            Debug.LogWarning("PPEManager.RegisterItem called with null item.", this);
            return;
        }

        if (!registeredItems.TryGetValue(item.Type, out var list))
        {
            list = new List<PPEItem>(2);
            registeredItems[item.Type] = list;
        }

        if (!list.Contains(item))
            list.Add(item);

        if (!equippedStates.ContainsKey(item.Type))
            equippedStates[item.Type] = false;
    }

    public void UnregisterItem(PPEItem item)
    {
        if (item == null)
            return;

        if (!registeredItems.TryGetValue(item.Type, out var list))
            return;

        list.Remove(item);
        if (list.Count == 0)
            registeredItems.Remove(item.Type);

        if (currentHoveredItem == item)
            currentHoveredItem = null;
    }

    public bool IsEquipped(PPEType type)
    {
        return equippedStates.TryGetValue(type, out var equipped) && equipped;
    }

    public void SetEquipped(PPEType type, bool equipped)
    {
        equippedStates[type] = equipped;
    }

    /// <summary>
    /// Returns the first registered item of <paramref name="type"/>, if any.
    /// Prefer <see cref="TryGetItems"/> when multiple instances of the same type exist.
    /// </summary>
    public bool TryGetItem(PPEType type, out PPEItem item)
    {
        if (registeredItems.TryGetValue(type, out var list))
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null)
                {
                    item = list[i];
                    return true;
                }
            }
        }

        item = null;
        return false;
    }

    /// <summary>
    /// Returns all registered items of <paramref name="type"/> (may be empty).
    /// </summary>
    public bool TryGetItems(PPEType type, out IReadOnlyList<PPEItem> items)
    {
        if (registeredItems.TryGetValue(type, out var list) && list.Count > 0)
        {
            items = list;
            return true;
        }

        items = System.Array.Empty<PPEItem>();
        return false;
    }

    /// <summary>
    /// Ensures only one PPE item remains highlighted when the ray switches targets.
    /// </summary>
    public void NotifyHoverEntered(PPEItem item)
    {
        if (item == null)
            return;

        if (currentHoveredItem != null && currentHoveredItem != item)
            currentHoveredItem.ClearHighlight();

        currentHoveredItem = item;
    }

    public void NotifyHoverExited(PPEItem item)
    {
        if (item == null)
            return;

        if (currentHoveredItem == item)
            currentHoveredItem = null;
    }
}
