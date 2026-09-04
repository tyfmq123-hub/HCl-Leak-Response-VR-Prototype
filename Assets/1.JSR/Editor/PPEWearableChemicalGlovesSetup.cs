using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;

/// <summary>
/// Parents wearable VR hands under Left/Right Controller Visuals (Step_13 style),
/// applies the grip-calibrated Visuals rotations from the animation-test project,
/// hides only Quest meshes while equipped, and wires PPE_Gloves.equippedVisual.
/// </summary>
public static class PPEWearableChemicalGlovesSetup
{
    const string LeftAssetPath = "Assets/Common/VRHands/vr hand_Left.prefab";
    const string RightAssetPath = "Assets/Common/VRHands/vr hand_Right.prefab";

    const string LeftChemicalGlovePath =
        "Assets/1.JSR/Models/PPE_ChemicalGlb/ChemicalGlove_Left_Rigged_JointAligned_Fixed.fbx";
    const string RightChemicalGlovePath =
        "Assets/1.JSR/Models/PPE_ChemicalGlb/ChemicalGlove_Right_Rigged_JointAligned_Fixed.fbx";

    const string HostName = "ChemicalGloves_WearableHost";
    const string LeftWearableName = "PPE_VrHand_Left_Wearable";
    const string RightWearableName = "PPE_VrHand_Right_Wearable";
    const string LegacyLeftWearableName = "ChemicalGlove_Left_Wearable";
    const string LegacyRightWearableName = "ChemicalGlove_Right_Wearable";
    const string PreferredScenePath = "Assets/Scenes/04_PPE.unity";

    // Match shelf world size: ProtectiveGloves visual 0.38 under PPE_Storage 2.15.
    static readonly Vector3 WearableScale = new Vector3(0.817f, 0.817f, 0.817f);

    // Keep Controller/Visuals at identity so Quest XR Controller meshes (Y=180 under
    // Visuals) stay aligned with tracking. Step_13 put the grip calibration on Left
    // Visuals; here we apply that same rotation to the wearable hands instead.
    static readonly Quaternion Step13LeftVisualsRotation = Quaternion.identity;
    static readonly Quaternion Step13RightVisualsRotation = Quaternion.identity;

    static readonly Vector3 LeftFineTunePos = Vector3.zero;
    static readonly Vector3 RightFineTunePos = Vector3.zero;

    // Exact localRotation from Step_13 XR Origin (VR) → Left Controller → Visuals.
    // That frame is what made "proper hold" match the animated hand there.
    static readonly Quaternion LeftHandGripRotation =
        new Quaternion(-0.44794282f, -0.3529363f, 0.5857566f, 0.57591015f);

    // Sagittal mirror of the left grip rotation for the right hand.
    static readonly Quaternion RightHandGripRotation =
        new Quaternion(-0.44794282f, 0.3529363f, -0.5857566f, 0.57591015f);

    // Model-space correction for the imported wearable hand only. Interaction aim
    // is owned by App SharedXRAimPoseDriver and must never copy this rotation.
    static readonly Vector3 WearableModelEulerCorrection = new Vector3(70f, 0f, 0f);

    public static void SetupWearableChemicalGloves()
    {
        SetupWearablesFromPaths(LeftAssetPath, RightAssetPath, "VR Hands");
    }

    public static void ReplacePpeWearableWithVrHands()
    {
        SetupWearablesFromPaths(LeftAssetPath, RightAssetPath, "VR Hands");
    }

    public static void RestoreWearableChemicalGloveFbx()
    {
        SetupWearablesFromPaths(LeftChemicalGlovePath, RightChemicalGlovePath, "Chemical Glove FBX");
    }

    public static void AlignWearableGlovesToXrHands()
    {
        EnsurePreferredSceneLoaded();

        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[Wearable Gloves] No active loaded scene.");
            return;
        }

        var report = new StringBuilder();
        report.AppendLine("=== Align Wearable Gloves To Step_13 Visuals ===");

        if (!TryGetControllers(out var leftController, out var rightController, out var leftVisuals, out var rightVisuals))
            return;

        ApplyStep13VisualsPose(leftVisuals, true, report);
        ApplyStep13VisualsPose(rightVisuals, false, report);

        var leftGlove = FindWearable(leftController, leftVisuals, true);
        var rightGlove = FindWearable(rightController, rightVisuals, false);
        if (leftGlove == null || rightGlove == null)
        {
            Debug.LogError("[Wearable Gloves] Wearable hands missing. Run Replace PPE Wearable With VR Hands first.");
            return;
        }

        SeatHandUnderVisuals(leftGlove, leftVisuals, true, report);
        SeatHandUnderVisuals(rightGlove, rightVisuals, false, report);
        ReportSharedAimOwnership(report);

        foreach (var procedural in new[] { leftGlove, rightGlove })
        {
            if (procedural == null) continue;
            foreach (var vg in procedural.GetComponentsInChildren<VRGloveHand>(true))
            {
                Undo.DestroyObjectImmediate(vg);
                report.AppendLine($"Removed VRGloveHand from {procedural.name}.");
            }
        }

        if (!EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            if (scene.isDirty && !string.IsNullOrEmpty(scene.path))
                EditorSceneManager.SaveScene(scene);
        }

        Selection.activeTransform = leftGlove;
        report.AppendLine("Done. Hands use model-only grip correction; shared XR owns pointer aim.");
        Debug.Log(report.ToString(), leftGlove);
    }

    public static void AlignControllerRaysForNaturalGrip()
    {
        EnsurePreferredSceneLoaded();

        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[Wearable Gloves] No active loaded scene.");
            return;
        }

        var report = new StringBuilder();
        report.AppendLine("=== Verify Shared Controller Aim Ownership ===");

        if (!TryGetControllers(out var leftController, out var rightController, out _, out _))
            return;

        ReportSharedAimOwnership(report);

        if (!EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            if (scene.isDirty && !string.IsNullOrEmpty(scene.path))
                EditorSceneManager.SaveScene(scene);
        }

        Debug.Log(report.ToString());
    }

    static void SetupWearablesFromPaths(string leftPath, string rightPath, string label)
    {
        EnsurePreferredSceneLoaded();

        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[Wearable Gloves] No active loaded scene.");
            return;
        }

        var report = new StringBuilder();
        report.AppendLine($"=== Setup Wearable PPE Hands ({label}) ===");
        report.AppendLine($"Scene: {scene.name}");
        report.AppendLine($"Left: {leftPath}");
        report.AppendLine($"Right: {rightPath}");

        var origin = FindXrOrigin();
        if (origin == null)
        {
            Debug.LogError("[Wearable Gloves] XR Origin not found.");
            return;
        }

        if (!TryGetControllers(out var leftController, out var rightController, out var leftVisuals, out var rightVisuals))
            return;

        var leftModel = LoadModel(leftPath, report);
        var rightModel = LoadModel(rightPath, report);
        if (leftModel == null || rightModel == null)
        {
            Debug.LogError(report.ToString());
            return;
        }

        ApplyStep13VisualsPose(leftVisuals, true, report);
        ApplyStep13VisualsPose(rightVisuals, false, report);

        DestroyWearableEverywhere(leftController, leftVisuals, LegacyLeftWearableName, report);
        DestroyWearableEverywhere(rightController, rightVisuals, LegacyRightWearableName, report);
        DestroyWearableEverywhere(leftController, leftVisuals, LeftWearableName, report);
        DestroyWearableEverywhere(rightController, rightVisuals, RightWearableName, report);

        var leftGlove = EnsureGloveUnderVisuals(
            leftVisuals, leftModel, LeftWearableName, true, report);
        var rightGlove = EnsureGloveUnderVisuals(
            rightVisuals, rightModel, RightWearableName, false, report);

        var questMeshes = CollectQuestMeshes(leftVisuals, rightVisuals, leftGlove, rightGlove);

        var host = EnsureHost(origin.transform, report);
        var wearable = host.GetComponent<ChemicalGlovesWearableVisual>();
        if (wearable == null)
            wearable = Undo.AddComponent<ChemicalGlovesWearableVisual>(host);

        wearable.EditorWire(leftGlove, rightGlove, questMeshes);
        EditorUtility.SetDirty(wearable);

        WireShelfEquipable(scene, host, report);
        ReportSharedAimOwnership(report);

        if (!EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            if (scene.isDirty && !string.IsNullOrEmpty(scene.path))
                EditorSceneManager.SaveScene(scene);
        }

        Selection.activeGameObject = host;
        report.AppendLine($"Done. Equip ChemicalGloves → Quest meshes hide, {label} show under Visuals.");
        Debug.Log(report.ToString(), host);
    }

    static bool TryGetControllers(
        out Transform leftController,
        out Transform rightController,
        out Transform leftVisuals,
        out Transform rightVisuals)
    {
        leftController = null;
        rightController = null;
        leftVisuals = null;
        rightVisuals = null;

        var origin = FindXrOrigin();
        if (origin == null)
        {
            Debug.LogError("[Wearable Gloves] XR Origin not found.");
            return false;
        }

        var cameraOffset = origin.transform.Find("Camera Offset");
        if (cameraOffset == null)
        {
            Debug.LogError("[Wearable Gloves] Camera Offset not found.");
            return false;
        }

        leftController = cameraOffset.Find("Left Controller");
        rightController = cameraOffset.Find("Right Controller");
        if (leftController == null || rightController == null)
        {
            Debug.LogError("[Wearable Gloves] Left/Right Controller not found under Camera Offset.");
            return false;
        }

        leftVisuals = FindControllerVisuals(leftController);
        rightVisuals = FindControllerVisuals(rightController);
        if (leftVisuals == null || rightVisuals == null)
        {
            Debug.LogError("[Wearable Gloves] Controller Visuals child not found.");
            return false;
        }

        return true;
    }

    static void EnsurePreferredSceneLoaded()
    {
        var active = SceneManager.GetActiveScene();
        if (active.IsValid() && active.path == PreferredScenePath)
            return;
        if (!System.IO.File.Exists(PreferredScenePath))
            return;
        if (active.IsValid() && !string.IsNullOrEmpty(active.path) &&
            active.path.IndexOf("04_PPE", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return;
        EditorSceneManager.OpenScene(PreferredScenePath, OpenSceneMode.Single);
    }

    static GameObject FindXrOrigin()
    {
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name.StartsWith("XR Origin"))
                return root;
        }

        var xr = Object.FindAnyObjectByType<XROrigin>(FindObjectsInactive.Include);
        return xr != null ? xr.gameObject : null;
    }

    static Transform FindControllerVisuals(Transform controller)
    {
        var visuals = controller.Find("Visuals");
        if (visuals != null)
            return visuals;

        for (int i = 0; i < controller.childCount; i++)
        {
            var child = controller.GetChild(i);
            if (child.name.IndexOf("XR Controller", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return child;
            if (child.name.IndexOf("Visual", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return child;
        }

        return null;
    }

    static Transform FindWearable(Transform controller, Transform visuals, bool left)
    {
        var primary = left ? LeftWearableName : RightWearableName;
        var legacy = left ? LegacyLeftWearableName : LegacyRightWearableName;
        return visuals.Find(primary)
               ?? controller.Find(primary)
               ?? visuals.Find(legacy)
               ?? controller.Find(legacy);
    }

    static GameObject LoadModel(string path, StringBuilder report)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (model == null)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        if (model == null)
            report.AppendLine("ERROR: missing " + path);
        else
            report.AppendLine("Loaded " + path);
        return model;
    }

    static void ApplyStep13VisualsPose(Transform visuals, bool left, StringBuilder report)
    {
        if (visuals == null)
            return;

        Undo.RecordObject(visuals, "Apply Step_13 Visuals pose");
        visuals.localPosition = Vector3.zero;
        visuals.localRotation = left ? Step13LeftVisualsRotation : Step13RightVisualsRotation;
        visuals.localScale = Vector3.one;
        report.AppendLine(
            $"Visuals ({(left ? "L" : "R")}): localEuler={visuals.localEulerAngles}");
        EditorUtility.SetDirty(visuals);
    }

    static GameObject EnsureGloveUnderVisuals(
        Transform visuals,
        GameObject model,
        string name,
        bool left,
        StringBuilder report)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        Undo.RegisterCreatedObjectUndo(instance, "Create " + name);
        instance.name = name;
        Undo.SetTransformParent(instance.transform, visuals, "Parent " + name + " under Visuals");
        instance.transform.localScale = WearableScale;

        foreach (var col in instance.GetComponentsInChildren<Collider>(true))
            Undo.DestroyObjectImmediate(col);

        // Step_13 hands drive curl via Hand.cs + Animator only.
        // VRGloveHand procedural curl fights the animator and mirrors poorly on left.
        foreach (var procedural in instance.GetComponentsInChildren<VRGloveHand>(true))
            Undo.DestroyObjectImmediate(procedural);

        ApplyIdentityUnderVisuals(instance.transform, left, report);

        instance.SetActive(false);
        report.AppendLine($"Created {name} under {visuals.name} (inactive, Hand+Animator only).");
        return instance;
    }

    static void SeatHandUnderVisuals(Transform handRoot, Transform visuals, bool left, StringBuilder report)
    {
        if (handRoot == null || visuals == null)
            return;

        if (handRoot.parent != visuals)
        {
            Undo.SetTransformParent(handRoot, visuals, "Reparent wearable under Visuals");
            report.AppendLine($"Reparented {handRoot.name} → {visuals.name}");
        }

        ApplyIdentityUnderVisuals(handRoot, left, report);
    }

    static void ApplyIdentityUnderVisuals(Transform handRoot, bool left, StringBuilder report)
    {
        Undo.RecordObject(handRoot, "Seat VR hand under Visuals");
        var finePos = left ? LeftFineTunePos : RightFineTunePos;
        // Imported model correction is visual-only and never drives interaction aim.
        var gripRot = left ? LeftHandGripRotation : RightHandGripRotation;
        var aimRot = Quaternion.Euler(WearableModelEulerCorrection) * gripRot;
        handRoot.localPosition = finePos;
        handRoot.localRotation = aimRot;
        handRoot.localScale = WearableScale;
        report.AppendLine(
            $"Seated {handRoot.name} under Visuals (model correction {WearableModelEulerCorrection.x}°). " +
            $"localPos={handRoot.localPosition} localEuler={handRoot.localEulerAngles}");
        EditorUtility.SetDirty(handRoot);
    }

    static void ReportSharedAimOwnership(StringBuilder report)
    {
        report.AppendLine(
            "Ray transforms unchanged. App SharedXRAimPoseDriver uses pointerPosition/pointerRotation.");
    }

    static GameObject[] CollectQuestMeshes(
        Transform leftVisuals,
        Transform rightVisuals,
        GameObject leftGlove,
        GameObject rightGlove)
    {
        var list = new List<GameObject>();
        CollectQuestMeshesUnder(leftVisuals, leftGlove, list);
        CollectQuestMeshesUnder(rightVisuals, rightGlove, list);
        return list.ToArray();
    }

    static void CollectQuestMeshesUnder(Transform visuals, GameObject wearable, List<GameObject> list)
    {
        if (visuals == null)
            return;

        for (var i = 0; i < visuals.childCount; i++)
        {
            var child = visuals.GetChild(i);
            if (child == null)
                continue;
            if (wearable != null && child.gameObject == wearable)
                continue;
            if (child.name.StartsWith("PPE_VrHand_", System.StringComparison.Ordinal) ||
                child.name.StartsWith("ChemicalGlove_", System.StringComparison.Ordinal) ||
                child.name.StartsWith("vr hand_", System.StringComparison.OrdinalIgnoreCase))
                continue;

            list.Add(child.gameObject);
        }
    }

    static void DestroyWearableEverywhere(
        Transform controller,
        Transform visuals,
        string name,
        StringBuilder report)
    {
        DestroyChildIfExists(controller, name, report);
        DestroyChildIfExists(visuals, name, report);
    }

    static void DestroyChildIfExists(Transform parent, string name, StringBuilder report)
    {
        if (parent == null || string.IsNullOrEmpty(name))
            return;
        var existing = parent.Find(name);
        if (existing == null)
            return;
        Undo.DestroyObjectImmediate(existing.gameObject);
        report.AppendLine($"Removed {name} under {parent.name}.");
    }

    static GameObject EnsureHost(Transform xrOrigin, StringBuilder report)
    {
        var existing = xrOrigin.Find(HostName);
        GameObject host;
        if (existing != null)
        {
            host = existing.gameObject;
            report.AppendLine("Reused ChemicalGloves_WearableHost.");
        }
        else
        {
            host = new GameObject(HostName);
            Undo.RegisterCreatedObjectUndo(host, "Create ChemicalGloves_WearableHost");
            Undo.SetTransformParent(host.transform, xrOrigin, "Parent wearable host");
            host.transform.localPosition = Vector3.zero;
            host.transform.localRotation = Quaternion.identity;
            host.transform.localScale = Vector3.one;
            report.AppendLine("Created ChemicalGloves_WearableHost under XR Origin.");
        }

        host.SetActive(false);
        return host;
    }

    static void WireShelfEquipable(Scene scene, GameObject host, StringBuilder report)
    {
        PPEEquipable glovesEquipable = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var eq in root.GetComponentsInChildren<PPEEquipable>(true))
            {
                if (eq == null)
                    continue;
                if (eq.ItemId == PPEEquipmentManager.IdChemicalGloves ||
                    eq.name.IndexOf("PPE_Gloves", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    eq.name.IndexOf("PPE_ChemicalGloves", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    glovesEquipable = eq;
                    break;
                }
            }

            if (glovesEquipable != null)
                break;
        }

        if (glovesEquipable == null)
        {
            report.AppendLine("WARNING: PPE_Gloves / ChemicalGloves PPEEquipable not found — equippedVisual not wired.");
            return;
        }

        Undo.RecordObject(glovesEquipable, "Wire ChemicalGloves equippedVisual");
        glovesEquipable.EditorApplyEquippedVisual(host);
        EditorUtility.SetDirty(glovesEquipable);
        report.AppendLine($"Wired {glovesEquipable.name}.equippedVisual → {HostName}.");
    }
}
