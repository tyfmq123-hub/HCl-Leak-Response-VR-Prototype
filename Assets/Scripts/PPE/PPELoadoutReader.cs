using System;
using System.Collections.Generic;

/// <summary>
/// Null-safe read API for the pre-entry validation / field scene.
/// Prefer this over touching <see cref="PPELoadoutData.Instance"/> directly.
/// </summary>
public static class PPELoadoutReader
{
    /// <summary>
    /// DontDestroyOnLoad object exists. Do <b>not</b> treat this as a recorded PPE result.
    /// Use <see cref="HasRecordedPpeResult"/> / <see cref="HasResolvedLoadout"/> instead.
    /// </summary>
    public static bool HasLoadoutObject => PPELoadoutData.Instance != null;

    /// <summary>Existing loadout, or null if none was created yet (does not create).</summary>
    public static PPELoadoutData TryGet() => PPELoadoutData.Instance;

    /// <summary>Existing loadout, or creates an empty one. Prefer presets for field-only start.</summary>
    public static PPELoadoutData GetOrEmpty() => PPELoadoutData.Ensure();

    public static PPELoadoutSource Source
    {
        get
        {
            var data = PPELoadoutData.Instance;
            return data != null ? data.Source : PPELoadoutSource.None;
        }
    }

    /// <summary>True when PPE room explicitly committed a result (lists may be empty/partial).</summary>
    public static bool HasRecordedPpeResult => Source == PPELoadoutSource.PpeSceneResult;

    /// <summary>True when a PPE result or field-ready preset is resolved.</summary>
    public static bool HasResolvedLoadout => Source != PPELoadoutSource.None;

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

    /// <summary>Response bag is stored as Equipped. Prepared check is defensive.</summary>
    public static bool HasResponseBag() =>
        IsEquipped(PPEEquipmentManager.IdResponseBag) || IsPrepared(PPEEquipmentManager.IdResponseBag);

    /// <summary>Waste container is stored as Prepared only. Tongs follow this state (no separate ItemId).</summary>
    public static bool HasWasteContainer() => IsPrepared(PPEEquipmentManager.IdWasteContainer);

    /// <summary>Tongs are a WasteContainer accessory — same Prepared state, no dedicated ItemId.</summary>
    public static bool HasTongs() => HasWasteContainer();

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
