using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>STEP 8 authoring and static validation for the final PPE placement flow.</summary>
public static class PPEFinalFlowValidationTool
{
    const string ScenePath = "Assets/Scenes/04_PPE.unity";

    readonly struct FlowCase
    {
        public readonly string Label;
        public readonly PPEType Type;
        public readonly PPEEquipSlot Slots;
        public readonly string[] ItemIds;

        public FlowCase(string label, PPEType type, PPEEquipSlot slots, params string[] itemIds)
        {
            Label = label;
            Type = type;
            Slots = slots;
            ItemIds = itemIds;
        }
    }

    static readonly FlowCase[] RequiredFlows =
    {
        new FlowCase("Helmet", PPEType.Helmet, PPEEquipSlot.Head, PPEEquipmentManager.IdHelmet),
        new FlowCase("Respirator", PPEType.Respirator, PPEEquipSlot.Face, "FaceRespirator", "Respirator"),
        new FlowCase("Inner Gloves", PPEType.Gloves, PPEEquipSlot.LeftHand | PPEEquipSlot.RightHand, "InnerGloves"),
        new FlowCase("Outer Gloves", PPEType.Gloves, PPEEquipSlot.LeftHand | PPEEquipSlot.RightHand, "ChemicalGloves", "Gloves"),
        new FlowCase("Boots", PPEType.Boots, PPEEquipSlot.LeftFoot | PPEEquipSlot.RightFoot, "ChemicalSafetyBoots", "Boots"),
        new FlowCase("Protective Suit", PPEType.ProtectiveSuit, PPEEquipSlot.Body, "ChemicalSuit", "ProtectiveSuit"),
    };

    public static void SetupDebugGizmo()
    {
        var scene = SceneManager.GetActiveScene();
        if (!IsTargetScene(scene))
        {
            Debug.LogError($"[PPE Final Validation] Open '{ScenePath}' first.");
            return;
        }

        var manager = UnityEngine.Object.FindAnyObjectByType<PPEPlacementGuideManager>(FindObjectsInactive.Include);
        var anchorSet = UnityEngine.Object.FindAnyObjectByType<PPEEquipmentAnchorSet>(FindObjectsInactive.Include);
        if (manager == null || anchorSet == null)
        {
            Debug.LogError("[PPE Final Validation] Placement manager or anchor set is missing.");
            return;
        }

        var gizmo = manager.GetComponent<PPEPlacementDebugGizmo>();
        if (gizmo == null)
            gizmo = Undo.AddComponent<PPEPlacementDebugGizmo>(manager.gameObject);
        Undo.RecordObject(gizmo, "Wire PPE placement debug gizmo");
        gizmo.EditorWire(manager, anchorSet);
        EditorUtility.SetDirty(gizmo);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeObject = gizmo;
        Debug.Log("[PPE Final Validation] Placement Debug Gizmo is wired. Toggle Draw Debug Gizmos in Inspector.", gizmo);
    }

    public static void ValidateFinalFlow()
    {
        var scene = SceneManager.GetActiveScene();
        if (!IsTargetScene(scene))
        {
            Debug.LogError($"[PPE Final Validation] Open '{ScenePath}' first.");
            return;
        }

        var failures = new List<string>();
        var warnings = new List<string>();
        var report = new StringBuilder("=== PPE STEP 8 FINAL FLOW VALIDATION ===\n");
        var manager = UnityEngine.Object.FindAnyObjectByType<PPEPlacementGuideManager>(FindObjectsInactive.Include);
        var anchorSet = UnityEngine.Object.FindAnyObjectByType<PPEEquipmentAnchorSet>(FindObjectsInactive.Include);
        var bodyReference = UnityEngine.Object.FindAnyObjectByType<PPEPlayerBodyReference>(FindObjectsInactive.Include);
        var gizmo = UnityEngine.Object.FindAnyObjectByType<PPEPlacementDebugGizmo>(FindObjectsInactive.Include);
        var equipables = FindSceneComponents<PPEEquipable>(scene);

        ValidateCore(manager, anchorSet, bodyReference, gizmo, failures, warnings, report);
        for (var i = 0; i < RequiredFlows.Length; i++)
            ValidateFlow(RequiredFlows[i], equipables, manager, anchorSet, failures, warnings, report);

        report.AppendLine();
        report.AppendLine("Runtime test order:");
        report.AppendLine("  1. Grab current sequence PPE and confirm its slot Guide appears.");
        report.AppendLine("  2. Keep rotation invalid inside the distance: Guide must remain Not Ready.");
        report.AppendLine("  3. Match distance + rotation: Guide turns Ready and equips after hold time.");
        report.AppendLine("  4. Confirm state, checklist, and next sequence prompt update.");
        report.AppendLine("  5. Ray-select and press Grip again: wearable PPE must not button-equip.");

        if (warnings.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("WARNINGS:");
            for (var i = 0; i < warnings.Count; i++)
                report.AppendLine("  - " + warnings[i]);
        }

        if (failures.Count > 0)
        {
            report.AppendLine();
            report.AppendLine($"VALIDATION FAIL ({failures.Count}):");
            for (var i = 0; i < failures.Count; i++)
                report.AppendLine("  - " + failures[i]);
            Debug.LogError(report.ToString());
        }
        else
        {
            report.AppendLine();
            report.AppendLine("VALIDATION PASS: all authored PPE placement flows are ready for headset testing.");
            Debug.Log(report.ToString());
        }
    }

    public static void ValidateHelmetReferenceFlow()
    {
        var scene = SceneManager.GetActiveScene();
        if (!IsTargetScene(scene))
        {
            Debug.LogError($"[PPE Helmet] Open '{ScenePath}' first.");
            return;
        }

        var failures = new List<string>();
        var warnings = new List<string>();
        var report = new StringBuilder("=== PPE HELMET REFERENCE VALIDATION ===\n");
        var placement = UnityEngine.Object.FindAnyObjectByType<PPEPlacementGuideManager>(FindObjectsInactive.Include);
        var anchors = UnityEngine.Object.FindAnyObjectByType<PPEEquipmentAnchorSet>(FindObjectsInactive.Include);
        var sequence = UnityEngine.Object.FindAnyObjectByType<PPESequenceManager>(FindObjectsInactive.Include);
        var checklist = UnityEngine.Object.FindAnyObjectByType<PPEChecklistPanel>(FindObjectsInactive.Include);
        var equipables = FindSceneComponents<PPEEquipable>(scene);

        var helmetFlow = RequiredFlows[0];
        ValidateCore(
            placement,
            anchors,
            UnityEngine.Object.FindAnyObjectByType<PPEPlayerBodyReference>(FindObjectsInactive.Include),
            UnityEngine.Object.FindAnyObjectByType<PPEPlacementDebugGizmo>(FindObjectsInactive.Include),
            failures,
            warnings,
            report);
        ValidateFlow(helmetFlow, equipables, placement, anchors, failures, warnings, report);

        if (!PPESequenceManager.TryGetStep(PPEEquipmentManager.IdHelmet, out var helmetStep)
            || helmetStep != PPESequenceStep.Helmet)
            failures.Add("Helmet ItemId is not mapped to PPESequenceStep.Helmet.");
        if (sequence == null || !sequence.ContainsStep(PPESequenceStep.Helmet))
            failures.Add("PPESequenceManager Sequence Order does not include Helmet.");
        if (checklist == null)
            failures.Add("PPEChecklistPanel is missing.");

        report.AppendLine("Contract: ItemId=Helmet, Type=Helmet, Slot=Head; production default order is unchanged.");
        report.AppendLine("For an isolated reference run, set Sequence Order to Helmet only; restore the production order afterward.");
        if (warnings.Count > 0)
            report.AppendLine("Warnings: " + string.Join(" | ", warnings));
        if (failures.Count > 0)
        {
            report.AppendLine("VALIDATION FAIL: " + string.Join(" | ", failures));
            Debug.LogError(report.ToString());
            return;
        }

        report.AppendLine("STATIC VALIDATION PASS. Complete the runtime hand/grab/placement test on Quest.");
        Debug.Log(report.ToString());
    }

    static void ValidateCore(
        PPEPlacementGuideManager manager,
        PPEEquipmentAnchorSet anchorSet,
        PPEPlayerBodyReference bodyReference,
        PPEPlacementDebugGizmo gizmo,
        List<string> failures,
        List<string> warnings,
        StringBuilder report)
    {
        report.AppendLine("Core:");
        AppendCheck(report, manager != null, "Placement Manager");
        AppendCheck(report, anchorSet != null, "Equipment Anchor Set");
        AppendCheck(report, bodyReference != null, "HMD-height Player Body Reference");
        AppendCheck(report, gizmo != null, "Optional Placement Debug Gizmo");

        if (manager == null)
            failures.Add("PPEPlacementGuideManager is missing.");
        else
        {
            if (!manager.AutoEquipEnabled)
                failures.Add("Placement auto equip is disabled.");
            if (manager.NotReadyMaterial == null || manager.ReadyMaterial == null)
                failures.Add("Guide Not Ready/Ready material reference is missing.");
        }
        if (anchorSet == null)
            failures.Add("PPEEquipmentAnchorSet is missing.");
        if (bodyReference == null)
            failures.Add("PPEPlayerBodyReference is missing.");
        if (gizmo == null)
            warnings.Add("PPEPlacementDebugGizmo is not installed; use the STEP 8 setup menu.");
        else if (!gizmo.DrawDebugGizmos)
            warnings.Add("Placement Debug Gizmos are disabled in Inspector.");
    }

    static void ValidateFlow(
        FlowCase flow,
        List<PPEEquipable> equipables,
        PPEPlacementGuideManager manager,
        PPEEquipmentAnchorSet anchorSet,
        List<string> failures,
        List<string> warnings,
        StringBuilder report)
    {
        var item = FindBestMatch(equipables, flow.ItemIds);
        if (item == null)
        {
            report.AppendLine($"[FAIL] {flow.Label}: PPE object missing; expected ID {string.Join("/", flow.ItemIds)} -> {flow.Slots}");
            failures.Add($"{flow.Label} PPE object/prefab is missing (expected {string.Join("/", flow.ItemIds)}).");
            ValidateAnchorsOnly(flow, anchorSet, failures, report);
            return;
        }

        var valid = true;
        if (item.Type != flow.Type)
        {
            failures.Add($"{flow.Label}: type is {item.Type}, expected {flow.Type}.");
            valid = false;
        }
        if ((item.EquipSlots & flow.Slots) != flow.Slots)
        {
            failures.Add($"{flow.Label}: slots are {item.EquipSlots}, expected {flow.Slots}.");
            valid = false;
        }
        if (item.EquipDistance <= 0f || item.EquipAngleTolerance <= 0f)
        {
            failures.Add($"{flow.Label}: distance/angle tolerance must be positive.");
            valid = false;
        }

        var grabbable = item.GetComponent<PPEHandGrabbable>();
        if (grabbable == null)
        {
            failures.Add($"{flow.Label}: PPEHandGrabbable is missing on '{item.name}'.");
            valid = false;
        }
        if (item.GetComponent<Rigidbody>() == null)
        {
            failures.Add($"{flow.Label}: Rigidbody is missing on '{item.name}'.");
            valid = false;
        }

        valid &= ValidateAnchorsOnly(flow, anchorSet, failures, report, false);
        if (item.GuidePrefab == null && manager != null && !manager.UsePpeVisualFallback)
        {
            failures.Add($"{flow.Label}: no Guide Prefab and fallback visual is disabled.");
            valid = false;
        }
        else if (item.GuidePrefab == null)
        {
            warnings.Add($"{flow.Label}: uses renderer-only fallback Ghost; verify headset scale/orientation.");
        }

        report.AppendLine(
            $"[{(valid ? "PASS" : "FAIL")}] {flow.Label}: {item.ItemId} -> {item.EquipSlots}, " +
            $"distance={item.EquipDistance:0.00}m, angle={item.EquipAngleTolerance:0.#}°, " +
            $"guide={(item.GuidePrefab != null ? item.GuidePrefab.name : "fallback mesh")}");
    }

    static bool ValidateAnchorsOnly(
        FlowCase flow,
        PPEEquipmentAnchorSet anchorSet,
        List<string> failures,
        StringBuilder report,
        bool appendResult = true)
    {
        var valid = anchorSet != null;
        foreach (PPEEquipSlot slot in Enum.GetValues(typeof(PPEEquipSlot)))
        {
            if (slot == PPEEquipSlot.None || (flow.Slots & slot) == 0)
                continue;
            if (anchorSet == null || !anchorSet.TryGetAnchor(slot, out var anchor) || anchor == null)
            {
                failures.Add($"{flow.Label}: {slot} anchor is missing.");
                valid = false;
            }
        }

        if (appendResult)
            report.AppendLine($"       Anchor support: {(valid ? "READY" : "MISSING")} ({flow.Slots})");
        return valid;
    }

    static PPEEquipable FindBestMatch(List<PPEEquipable> equipables, string[] ids)
    {
        PPEEquipable fallback = null;
        for (var i = 0; i < ids.Length; i++)
        {
            for (var e = 0; e < equipables.Count; e++)
            {
                var item = equipables[e];
                if (item == null || !string.Equals(item.ItemId, ids[i], StringComparison.OrdinalIgnoreCase))
                    continue;
                if (item.GetComponent<PPEHandGrabbable>() != null)
                    return item;
                if (fallback == null)
                    fallback = item;
            }
        }
        return fallback;
    }

    static List<T> FindSceneComponents<T>(Scene scene) where T : Component
    {
        var results = new List<T>();
        foreach (var root in scene.GetRootGameObjects())
            results.AddRange(root.GetComponentsInChildren<T>(true));
        return results;
    }

    static void AppendCheck(StringBuilder report, bool pass, string label)
    {
        report.AppendLine($"  [{(pass ? "PASS" : "FAIL")}] {label}");
    }

    static bool IsTargetScene(Scene scene)
    {
        return scene.IsValid() && scene.isLoaded && scene.path == ScenePath;
    }
}
