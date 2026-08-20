using System;
using UnityEngine;

/// <summary>
/// Per-item equipment metadata on a top-level PPE root.
/// Stores equip state and placement definition, but does not move objects,
/// evaluate placement, or drive XR interaction.
/// </summary>
[DisallowMultipleComponent]
public class PPEEquipable : MonoBehaviour
{
    [SerializeField]
    string itemId = "";

    [SerializeField]
    string displayName = "";

    [Header("Equipment Placement")]
    [SerializeField]
    PPEType ppeType = PPEType.Gloves;

    [SerializeField]
    [Tooltip("One or more player equipment destinations used by this PPE.")]
    PPEEquipSlot equipSlots = PPEEquipSlot.None;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Maximum distance in meters for a future placement/equip check.")]
    float equipDistance = 0.2f;

    [SerializeField]
    [Range(0f, 180f)]
    [Tooltip("Maximum orientation difference in degrees for a future equip check.")]
    float equipAngleTolerance = 35f;

    [SerializeField]
    [Tooltip("Optional ghost/guide visual. STEP 3 stores the reference but does not instantiate it.")]
    GameObject guidePrefab;

    [SerializeField]
    bool isEquipped;

    [SerializeField]
    [Tooltip("When true, the PPE root is SetActive(false) after a successful Equip.")]
    bool deactivateOnEquip = true;

    [SerializeField]
    GameObject equippedVisual;

    [SerializeField]
    AudioSource equippedAudio;

    /// <summary>Raised after this item is marked equipped.</summary>
    public event Action Equipped;

    public string ItemId => itemId;
    public string DisplayName => displayName;
    public PPEType Type => ppeType;
    public PPEEquipSlot EquipSlots => equipSlots;
    public float EquipDistance => equipDistance;
    public float EquipAngleTolerance => equipAngleTolerance;
    public GameObject GuidePrefab => guidePrefab;
    public bool IsEquipped => isEquipped;
    public bool DeactivateOnEquip => deactivateOnEquip;

    public bool UsesSlot(PPEEquipSlot slot)
    {
        return slot != PPEEquipSlot.None && (equipSlots & slot) == slot;
    }

    /// <summary>
    /// Returns true when this item can be equipped (not already equipped,
    /// has a valid id, and the manager does not already track this id).
    /// </summary>
    public bool CanEquip()
    {
        if (isEquipped)
            return false;

        if (string.IsNullOrWhiteSpace(itemId))
            return false;

        var manager = PPEEquipmentManager.Instance;
        if (manager != null && manager.IsEquipped(itemId))
            return false;

        return true;
    }

    /// <summary>
    /// Marks this item equipped, optionally shows visual / plays audio.
    /// Root deactivation is owned by <see cref="PPEEquipmentManager"/>.
    /// </summary>
    public void MarkAsEquipped()
    {
        if (isEquipped)
            return;

        isEquipped = true;

        if (equippedVisual != null)
            equippedVisual.SetActive(true);

        if (equippedAudio != null)
            equippedAudio.Play();

        Equipped?.Invoke();
    }

    /// <summary>
    /// Clears local equipped state and hides optional equipped visual.
    /// Does not reactivate the PPE root (never deactivated by this component).
    /// </summary>
    public void ResetEquipState()
    {
        isEquipped = false;

        if (equippedVisual != null)
            equippedVisual.SetActive(false);
    }

#if UNITY_EDITOR
    /// <summary>Editor helper used by setup tools.</summary>
    public void EditorApplyIdentity(string id, string koreanDisplayName)
    {
        itemId = id;
        displayName = koreanDisplayName;
    }

    /// <summary>Editor helper — assign optional equipped visual (e.g. wearable gloves).</summary>
    public void EditorApplyEquippedVisual(GameObject visual)
    {
        equippedVisual = visual;
    }

    /// <summary>Editor helper for STEP 3 placement metadata.</summary>
    public void EditorApplyPlacementDefinition(
        PPEType type,
        PPEEquipSlot slots,
        float distance,
        float angleTolerance,
        GameObject guide)
    {
        ppeType = type;
        equipSlots = slots;
        equipDistance = Mathf.Max(0.01f, distance);
        equipAngleTolerance = Mathf.Clamp(angleTolerance, 0f, 180f);
        guidePrefab = guide;
    }

    /// <summary>Editor helper for an item that remains visible on its anchor after equip.</summary>
    public void EditorSetDeactivateOnEquip(bool deactivate)
    {
        deactivateOnEquip = deactivate;
    }
#endif
}
