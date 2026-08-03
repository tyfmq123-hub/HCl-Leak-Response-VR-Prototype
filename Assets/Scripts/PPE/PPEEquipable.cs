using System;
using UnityEngine;

/// <summary>
/// Per-item equipment metadata on a top-level PPE root.
/// Stores equip state only — does not deactivate objects, change transforms,
/// or drive XR interaction.
/// </summary>
[DisallowMultipleComponent]
public class PPEEquipable : MonoBehaviour
{
    [SerializeField]
    string itemId = "";

    [SerializeField]
    string displayName = "";

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
    public bool IsEquipped => isEquipped;
    public bool DeactivateOnEquip => deactivateOnEquip;

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
#endif
}
