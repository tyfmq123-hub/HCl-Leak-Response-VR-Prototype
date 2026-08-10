using System.Collections.Generic;

/// <summary>
/// Official PPE loadout snapshots for cross-scene / standalone field start.
/// Full training flow uses live PPE-room capture; field-only start uses
/// <see cref="FieldReadyComplete"/>.
/// </summary>
public static class PPELoadoutPresets
{
    /// <summary>
    /// Module-select / field-scene standalone: essential PPE + prep gear complete.
    /// Matches a successful end-of-PPE-room sequence.
    /// </summary>
    public static PPELoadoutSnapshot FieldReadyComplete => new PPELoadoutSnapshot
    {
        equippedItemIds = new List<string>
        {
            PPEEquipmentManager.IdInnerGloves,       // InnerGloves
            PPEEquipmentManager.IdChemicalSuit,      // ChemicalSuit
            PPEEquipmentManager.IdChemicalBoots,     // ChemicalSafetyBoots
            PPEEquipmentManager.IdFaceRespirator,    // FaceRespirator
            PPEEquipmentManager.IdResponseBag,       // ResponseBag (stored as Equipped)
            PPEEquipmentManager.IdFaceShield,        // ChemicalFaceShield
            PPEEquipmentManager.IdChemicalGloves,    // ChemicalGloves (outer)
        },
        preparedItemIds = new List<string>
        {
            PPEEquipmentManager.IdWasteContainer,    // WasteContainer (Prepared only)
        },
        sequenceComplete = true,
        bagContentsChecked = true,
    };

    /// <summary>
    /// Replaces any existing DontDestroyOnLoad loadout with <paramref name="snapshot"/>.
    /// Call this when starting a new training run or entering field-only mode from module select.
    /// </summary>
    public static PPELoadoutData ReplaceLoadout(PPELoadoutSnapshot snapshot)
    {
        var data = PPELoadoutData.Ensure();
        data.ApplySnapshot(snapshot); // ApplySnapshot clears first
        return data;
    }

    /// <summary>Clears previous DDOL loadout, then applies <see cref="FieldReadyComplete"/>.</summary>
    public static PPELoadoutData BeginFieldStandaloneWithOfficialPreset()
    {
        return ReplaceLoadout(FieldReadyComplete);
    }

    /// <summary>Clears loadout for a brand-new full training (PPE room first).</summary>
    public static void BeginNewFullTrainingClear()
    {
        if (PPELoadoutData.Instance != null)
            PPELoadoutData.Instance.Clear();
        else
            PPELoadoutData.Ensure().Clear();
    }
}
