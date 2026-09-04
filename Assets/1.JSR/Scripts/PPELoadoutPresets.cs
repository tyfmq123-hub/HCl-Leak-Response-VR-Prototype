using System.Collections.Generic;
using HClTraining.SharedXR;

/// <summary>
/// Official PPE loadout snapshots for cross-scene / standalone field start.
/// Full training flow commits live PPE-room state as <see cref="PPELoadoutSource.PpeSceneResult"/>;
/// field-only start uses <see cref="FieldReadyComplete"/>.
/// </summary>
public static class PPELoadoutPresets
{
    /// <summary>
    /// Module-select / field-scene standalone: essential PPE + prep gear complete.
    /// Matches a successful end-of-PPE-room sequence (donning AND equipment preparation).
    /// Donning complete alone is not equivalent to this snapshot.
    /// </summary>
    public static PPELoadoutSnapshot FieldReadyComplete => new PPELoadoutSnapshot
    {
        source = PPELoadoutSource.FieldReadyPreset,
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
        if (data == null)
            return null;

        data.ApplySnapshot(snapshot); // ApplySnapshot clears first
        if (snapshot != null && snapshot.source == PPELoadoutSource.FieldReadyPreset)
            SessionEquipmentInventory.Ensure()?.ApplyFieldReadyPreset();
        else if (snapshot != null && snapshot.source == PPELoadoutSource.PpeSceneResult)
            PPELoadoutData.SyncSessionInventoryFromPpe(data);
        return data;
    }

    /// <summary>
    /// Clears previous DDOL loadout, then applies <see cref="FieldReadyComplete"/>
    /// with <see cref="PPELoadoutSource.FieldReadyPreset"/>.
    /// Call only for explicit field-only / module-select start — never because lists look empty.
    /// </summary>
    public static PPELoadoutData BeginFieldStandaloneWithOfficialPreset()
    {
        // Source is the only gate callers should use before invoking this.
        // Replacing an existing PpeSceneResult is allowed only when the user explicitly
        // chose field-standalone (module select), not when gear lists are partial.
        return ReplaceLoadout(FieldReadyComplete);
    }

    /// <summary>
    /// Clears loadout for a brand-new full training (PPE room first).
    /// Sets <see cref="PPELoadoutSource.None"/>.
    /// </summary>
    public static void BeginNewFullTrainingClear()
    {
        SessionEquipmentInventory.Ensure()?.BeginNewSession();
        if (PPELoadoutData.Instance != null)
            PPELoadoutData.Instance.Clear();
        else
            PPELoadoutData.Ensure()?.Clear();
    }

    /// <summary>
    /// Explicit PPE-room result finalize (partial / empty allowed).
    /// Sets <see cref="PPELoadoutSource.PpeSceneResult"/> via one snapshot replace.
    /// </summary>
    public static PPELoadoutData CommitPpeSceneResult()
    {
        return PPELoadoutData.CommitPpeSceneResultFromManagers();
    }

    /// <summary>
    /// True only when no resolved loadout exists yet.
    /// Never use list counts / sequenceComplete / singleton presence for this.
    /// </summary>
    public static bool ShouldApplyFieldReadyPreset(PPELoadoutSource source)
    {
        return source == PPELoadoutSource.None;
    }
}
