#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Edit-mode smoke checks for Source-based loadout resolve (no Play Mode required).
/// Menu: Tools → PPE → Run Loadout Source Guard Smoke Test
/// </summary>
public static class PPELoadoutSourceGuardSmokeTest
{
    public static void Run()
    {
        var failures = 0;
        failures += Expect(
            PPELoadoutPresets.ShouldApplyFieldReadyPreset(PPELoadoutSource.None),
            "Source=None should allow FieldReady preset");
    failures += Expect(
        !PPELoadoutPresets.ShouldApplyFieldReadyPreset(PPELoadoutSource.PpeSceneResult),
        "Source=PpeSceneResult must not allow FieldReady preset");
    failures += Expect(
        !PPELoadoutPresets.ShouldApplyFieldReadyPreset(PPELoadoutSource.FieldReadyPreset),
        "Source=FieldReadyPreset must not allow FieldReady preset");

        // Draft sync must demote FieldReadyPreset and must not rewrite PpeSceneResult.
        // (CaptureFromManagers needs managers; here we only assert Source guard helpers.)

        var host = new GameObject("PPELoadoutSourceGuardSmokeTest_Temp");
        try
        {
            var data = host.AddComponent<PPELoadoutData>();

            // Partial PPE result (some gear missing) must stay PpeSceneResult and not be treated as unresolved.
            data.ReplaceWithSnapshot(new PPELoadoutSnapshot
            {
                source = PPELoadoutSource.PpeSceneResult,
                equippedItemIds = new List<string> { PPEEquipmentManager.IdInnerGloves },
                preparedItemIds = new List<string>(),
                sequenceComplete = false,
                bagContentsChecked = false,
            });
            failures += Expect(data.HasRecordedPpeResult, "Partial result HasRecordedPpeResult");
            failures += Expect(data.HasResolvedLoadout, "Partial result HasResolvedLoadout");
            failures += Expect(
                !PPELoadoutPresets.ShouldApplyFieldReadyPreset(data.Source),
                "Partial result must not be overwritten by preset");

            // Fully missing gear but still a recorded PPE result.
            data.ReplaceWithSnapshot(new PPELoadoutSnapshot
            {
                source = PPELoadoutSource.PpeSceneResult,
                equippedItemIds = new List<string>(),
                preparedItemIds = new List<string>(),
                sequenceComplete = false,
                bagContentsChecked = false,
            });
            failures += Expect(data.EquippedItemIds.Count == 0, "Empty recorded result has 0 equipped");
            failures += Expect(data.HasRecordedPpeResult, "Empty recorded result still HasRecordedPpeResult");
            failures += Expect(
                !PPELoadoutPresets.ShouldApplyFieldReadyPreset(data.Source),
                "Empty recorded result must not be overwritten by preset");

            // Clear → None
            data.Clear();
            failures += Expect(data.Source == PPELoadoutSource.None, "Clear sets Source=None");
            failures += Expect(!data.HasResolvedLoadout, "Clear clears HasResolvedLoadout");
            failures += Expect(
                PPELoadoutPresets.ShouldApplyFieldReadyPreset(data.Source),
                "After Clear, preset may apply");

            // Field ready preset
            data.ReplaceWithSnapshot(PPELoadoutPresets.FieldReadyComplete);
            failures += Expect(data.Source == PPELoadoutSource.FieldReadyPreset, "FieldReady source");
            failures += Expect(data.HasResolvedLoadout, "FieldReady HasResolvedLoadout");
            failures += Expect(!data.HasRecordedPpeResult, "FieldReady is not PpeSceneResult");
            failures += Expect(
                !PPELoadoutPresets.ShouldApplyFieldReadyPreset(data.Source),
                "FieldReady must not re-apply preset");

            // Bag content ItemIds must not be stored as equipped/prepared.
            data.ReplaceWithSnapshot(new PPELoadoutSnapshot
            {
                source = PPELoadoutSource.PpeSceneResult,
                equippedItemIds = new List<string>
                {
                    ResponseEquipmentManager.IdGasDetector,
                    ResponseEquipmentManager.IdAbsorbentSock,
                    ResponseEquipmentManager.IdAbsorbentPad,
                    PPEEquipmentManager.IdChemicalSuit,
                },
                preparedItemIds = new List<string>
                {
                    ResponseEquipmentManager.IdGasDetector,
                    PPEEquipmentManager.IdWasteContainer,
                },
                bagContentsChecked = true,
            });
            failures += Expect(!data.IsEquipped(ResponseEquipmentManager.IdGasDetector), "GasDetector not equipped");
            failures += Expect(data.IsEquipped(PPEEquipmentManager.IdChemicalSuit), "ChemicalSuit kept");
            failures += Expect(data.IsPrepared(PPEEquipmentManager.IdWasteContainer), "WasteContainer prepared");
            failures += Expect(data.BagContentsChecked, "bagContentsChecked preserved");
        }
        finally
        {
            Object.DestroyImmediate(host);
        }

        if (failures == 0)
            Debug.Log("[PPE Loadout Source Guard] All smoke checks passed.");
        else
            Debug.LogError($"[PPE Loadout Source Guard] {failures} check(s) failed.");
    }

    static int Expect(bool condition, string label)
    {
        if (condition)
            return 0;

        Debug.LogError($"[PPE Loadout Source Guard] FAIL: {label}");
        return 1;
    }
}
#endif
