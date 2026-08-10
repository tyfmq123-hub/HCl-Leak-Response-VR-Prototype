using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Null-safe read API for the field scene. Never throws if loadout is missing or empty.
/// Prefer this over touching <see cref="PPELoadoutData.Instance"/> directly.
/// </summary>
public static class PPELoadoutReader
{
    public static bool HasLoadoutObject => PPELoadoutData.Instance != null;

    /// <summary>Existing loadout, or null if none was created yet (does not create).</summary>
    public static PPELoadoutData TryGet() => PPELoadoutData.Instance;

    /// <summary>Existing loadout, or creates an empty one. Prefer presets for field-only start.</summary>
    public static PPELoadoutData GetOrEmpty() => PPELoadoutData.Ensure();

    public static bool IsEquipped(string itemId)
    {
        var data = PPELoadoutData.Instance;
        return data != null && data.IsEquipped(itemId);
    }

    public static bool IsPrepared(string itemId)
    {
        var data = PPELoadoutData.Instance;
        return data != null && data.IsPrepared(itemId);
    }

    public static bool IsSequenceComplete()
    {
        var data = PPELoadoutData.Instance;
        return data != null && data.IsSequenceComplete;
    }

    public static bool HasCompletedPreparation() => IsSequenceComplete();

    public static bool BagContentsChecked()
    {
        var data = PPELoadoutData.Instance;
        return data != null && data.BagContentsChecked;
    }

    public static IReadOnlyList<string> GetEquippedIds()
    {
        var data = PPELoadoutData.Instance;
        if (data == null)
            return Array.Empty<string>();
        return data.EquippedItemIds;
    }

    public static IReadOnlyList<string> GetPreparedIds()
    {
        var data = PPELoadoutData.Instance;
        if (data == null)
            return Array.Empty<string>();
        return data.PreparedItemIds;
    }

    // ---- Wearable / prep convenience (PPEEquipmentManager ItemIds) ----

    public static bool HasInnerGloves() => IsEquipped(PPEEquipmentManager.IdInnerGloves);
    public static bool HasProtectiveSuit() => IsEquipped(PPEEquipmentManager.IdChemicalSuit);
    public static bool HasChemicalBoots() => IsEquipped(PPEEquipmentManager.IdChemicalBoots);
    public static bool HasRespirator() => IsEquipped(PPEEquipmentManager.IdFaceRespirator);
    public static bool HasFaceProtection() => IsEquipped(PPEEquipmentManager.IdFaceShield);
    public static bool HasProtectiveGloves() => IsEquipped(PPEEquipmentManager.IdChemicalGloves);

    /// <summary>Response bag is stored as Equipped (MirrorResponseBagEquipped). Prepared check is defensive.</summary>
    public static bool HasResponseBag() =>
        IsEquipped(PPEEquipmentManager.IdResponseBag) || IsPrepared(PPEEquipmentManager.IdResponseBag);

    /// <summary>Waste container is stored as Prepared only.</summary>
    public static bool HasWasteContainer() => IsPrepared(PPEEquipmentManager.IdWasteContainer);

    // ---- Bag contents (NOT written to Equipped/Prepared lists) ----

    /// <summary>
    /// Gas detector / absorbent items are bag-inspection ids only.
    /// They are never copied into PPELoadoutData equipped/prepared lists.
    /// Use <see cref="BagContentsChecked"/> after PPE-room Y inspect.
    /// </summary>
    public static bool HasBagContentsGlanceConfirmed() => BagContentsChecked();

    public static string IdGasDetector => ResponseEquipmentManager.IdGasDetector;
    public static string IdAbsorbentSock => ResponseEquipmentManager.IdAbsorbentSock;
    public static string IdAbsorbentPad => ResponseEquipmentManager.IdAbsorbentPad;
}
