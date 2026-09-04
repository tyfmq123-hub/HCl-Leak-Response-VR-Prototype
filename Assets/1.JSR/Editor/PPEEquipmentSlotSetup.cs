using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Authors PPE placement metadata and the Stage-local body-relative anchor frame.</summary>
public static class PPEEquipmentSlotSetup
{
    const string ScenePath = "Assets/Scenes/04_PPE.unity";
    const string BodyAnchorName = "PlayerBodyAnchor";
    const string BodyReferenceName = "PPE_PlayerBodyReference";
    const string AnchorRootName = "PlayerEquipmentAnchors";

    readonly struct PlacementProfile
    {
        public readonly PPEType Type;
        public readonly PPEEquipSlot Slots;
        public readonly float Distance;
        public readonly float AngleTolerance;

        public PlacementProfile(
            PPEType type,
            PPEEquipSlot slots,
            float distance,
            float angleTolerance)
        {
            Type = type;
            Slots = slots;
            Distance = distance;
            AngleTolerance = angleTolerance;
        }
    }

    static readonly Dictionary<string, PlacementProfile> PlacementProfiles =
        new Dictionary<string, PlacementProfile>(StringComparer.OrdinalIgnoreCase)
        {
            { PPEEquipmentManager.IdHelmet, new PlacementProfile(PPEType.Helmet, PPEEquipSlot.Head, 0.35f, 35f) },
            { "Goggles", new PlacementProfile(PPEType.Goggles, PPEEquipSlot.Face, 0.14f, 30f) },
            { "ChemicalFaceShield", new PlacementProfile(PPEType.FaceShield, PPEEquipSlot.Face, 0.35f, 80f) },
            { "FaceShield", new PlacementProfile(PPEType.FaceShield, PPEEquipSlot.Face, 0.35f, 80f) },
            { "FaceRespirator", new PlacementProfile(PPEType.Respirator, PPEEquipSlot.Face, 0.35f, 80f) },
            { "Respirator", new PlacementProfile(PPEType.Respirator, PPEEquipSlot.Face, 0.35f, 80f) },
            { "ChemicalSuit", new PlacementProfile(PPEType.ProtectiveSuit, PPEEquipSlot.Body, 0.45f, 45f) },
            { "ProtectiveSuit", new PlacementProfile(PPEType.ProtectiveSuit, PPEEquipSlot.Body, 0.45f, 45f) },
            { "InnerGloves", new PlacementProfile(PPEType.Gloves, PPEEquipSlot.LeftHand | PPEEquipSlot.RightHand, 0.14f, 40f) },
            { "ChemicalGloves", new PlacementProfile(PPEType.Gloves, PPEEquipSlot.LeftHand | PPEEquipSlot.RightHand, 0.14f, 40f) },
            { "Gloves", new PlacementProfile(PPEType.Gloves, PPEEquipSlot.LeftHand | PPEEquipSlot.RightHand, 0.14f, 40f) },
            { "ChemicalSafetyBoots", new PlacementProfile(PPEType.Boots, PPEEquipSlot.LeftFoot | PPEEquipSlot.RightFoot, 0.20f, 40f) },
            { "Boots", new PlacementProfile(PPEType.Boots, PPEEquipSlot.LeftFoot | PPEEquipSlot.RightFoot, 0.20f, 40f) },
            { "Radio", new PlacementProfile(PPEType.Radio, PPEEquipSlot.Radio, 0.16f, 45f) },
            { "GasDetector", new PlacementProfile(PPEType.GasDetector, PPEEquipSlot.GasDetector, 0.16f, 45f) },
        };

    public static void SetupEquipmentSlots()
    {
        var scene = SceneManager.GetActiveScene();
        if (!IsTargetScene(scene))
        {
            Debug.LogError($"[PPE Equipment Slots] Open '{ScenePath}' before running setup.");
            return;
        }

        var report = new StringBuilder();
        report.AppendLine("=== PPE STEP 3/6 Equipment Slot + Body Reference Setup ===");

        var bodyAnchor = FindTransform(scene, BodyAnchorName);
        if (bodyAnchor == null)
        {
            Debug.LogError(
                "[PPE Equipment Slots] Existing PlayerBodyAnchor is missing. " +
                "The PPE worker will not create or modify an XR Origin.");
            return;
        }

        var origin = bodyAnchor.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>(true);
        if (origin == null || origin.Camera == null)
        {
            Debug.LogError("[PPE Equipment Slots] Existing XR Origin or its Camera reference is missing.");
            return;
        }

        var bodyReference = EnsureBodyReference(origin.transform, report);
        var anchorSet = EnsureAnchorSet(scene, bodyReference, report);
        var bodyReferenceDriver = EnsureBodyReferenceDriver(bodyReference, origin, anchorSet, report);
        var configured = ConfigurePpeMetadata(scene, report);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        report.AppendLine($"Configured PPE placement definitions: {configured}");
        report.AppendLine(Validate(scene, anchorSet, bodyReferenceDriver));
        Debug.Log(report.ToString());
    }

    public static void SetupPpeBodyReference()
    {
        SetupEquipmentSlots();
    }

    public static bool IsSetupRequired()
    {
        var scene = SceneManager.GetActiveScene();
        if (!IsTargetScene(scene))
            return false;

        var bodyAnchor = FindTransform(scene, BodyAnchorName);
        if (bodyAnchor == null)
            return false;

        var bodyReference = FindTransform(scene, BodyReferenceName);
        var root = FindTransform(scene, AnchorRootName);
        if (bodyReference == null || root == null || root.parent != bodyReference ||
            root.GetComponent<PPEEquipmentAnchorSet>() == null)
            return true;

        var driver = bodyReference.GetComponent<PPEPlayerBodyReference>();
        if (driver == null || driver.BodyReferenceSetupVersion < 1)
            return true;

        foreach (var equipable in FindEquipables(scene))
        {
            if (equipable.ItemId == "ResponseBag")
                continue;
            if (equipable.EquipSlots == PPEEquipSlot.None)
                return true;
        }

        return false;
    }

    static Transform EnsureBodyReference(Transform origin, StringBuilder report)
    {
        var bodyReference = origin.Find(BodyReferenceName);
        if (bodyReference != null)
            return bodyReference;

        var go = new GameObject(BodyReferenceName);
        Undo.RegisterCreatedObjectUndo(go, "Create PPE player body reference");
        Undo.SetTransformParent(go.transform, origin, "Parent PPE player body reference");
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        report.AppendLine($"Created XR Origin/{BodyReferenceName}.");
        return go.transform;
    }

    static PPEEquipmentAnchorSet EnsureAnchorSet(Scene scene, Transform bodyReference, StringBuilder report)
    {
        var root = bodyReference.Find(AnchorRootName);
        if (root == null)
        {
            root = FindTransform(scene, AnchorRootName);
            if (root != null)
            {
                Undo.SetTransformParent(root, bodyReference, "Move PPE anchors to player body reference");
                root.localPosition = Vector3.zero;
                root.localRotation = Quaternion.identity;
                root.localScale = Vector3.one;
                report.AppendLine($"Moved {AnchorRootName} under {BodyReferenceName}.");
            }
        }

        if (root == null)
        {
            var go = new GameObject(AnchorRootName);
            Undo.RegisterCreatedObjectUndo(go, "Create PPE equipment anchor root");
            Undo.SetTransformParent(go.transform, bodyReference, "Parent PPE equipment anchors");
            root = go.transform;
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;
            report.AppendLine($"Created {BodyReferenceName}/{AnchorRootName}.");
        }

        var anchors = new Dictionary<PPEEquipSlot, Transform>
        {
            { PPEEquipSlot.Head, EnsureAnchor(root, "Head", new Vector3(0f, 1.78f, 0.04f)) },
            { PPEEquipSlot.Face, EnsureAnchor(root, "Face", new Vector3(0f, 1.60f, 0.13f)) },
            { PPEEquipSlot.LeftHand, EnsureAnchor(root, "LeftHand", new Vector3(-0.32f, 1.02f, 0.10f)) },
            { PPEEquipSlot.RightHand, EnsureAnchor(root, "RightHand", new Vector3(0.32f, 1.02f, 0.10f)) },
            { PPEEquipSlot.Body, EnsureAnchor(root, "Body", new Vector3(0f, 0.935f, 0.02f)) },
            { PPEEquipSlot.LeftFoot, EnsureAnchor(root, "LeftFoot", new Vector3(-0.14f, 0.06f, 0.08f)) },
            { PPEEquipSlot.RightFoot, EnsureAnchor(root, "RightFoot", new Vector3(0.14f, 0.06f, 0.08f)) },
            { PPEEquipSlot.Radio, EnsureAnchor(root, "Radio", new Vector3(-0.18f, 1.054f, 0.05f)) },
            { PPEEquipSlot.GasDetector, EnsureAnchor(root, "GasDetector", new Vector3(0.18f, 0.986f, 0.07f)) },
        };

        var anchorSet = root.GetComponent<PPEEquipmentAnchorSet>();
        if (anchorSet == null)
            anchorSet = Undo.AddComponent<PPEEquipmentAnchorSet>(root.gameObject);

        Undo.RecordObject(anchorSet, "Wire PPE equipment anchors");
        anchorSet.EditorWire(
            anchors[PPEEquipSlot.Head],
            anchors[PPEEquipSlot.Face],
            anchors[PPEEquipSlot.LeftHand],
            anchors[PPEEquipSlot.RightHand],
            anchors[PPEEquipSlot.Body],
            anchors[PPEEquipSlot.LeftFoot],
            anchors[PPEEquipSlot.RightFoot],
            anchors[PPEEquipSlot.Radio],
            anchors[PPEEquipSlot.GasDetector]);
        EditorUtility.SetDirty(anchorSet);
        return anchorSet;
    }

    static PPEPlayerBodyReference EnsureBodyReferenceDriver(
        Transform bodyReference,
        Unity.XR.CoreUtils.XROrigin origin,
        PPEEquipmentAnchorSet anchorSet,
        StringBuilder report)
    {
        var driver = bodyReference.GetComponent<PPEPlayerBodyReference>();
        if (driver == null)
        {
            driver = Undo.AddComponent<PPEPlayerBodyReference>(bodyReference.gameObject);
            report.AppendLine("Added PPEPlayerBodyReference body inference driver.");
        }

        Undo.RecordObject(driver, "Wire PPE player body reference");
        driver.EditorWire(origin, origin.Camera.transform, anchorSet);
        EditorUtility.SetDirty(driver);
        return driver;
    }

    static Transform EnsureAnchor(Transform parent, string name, Vector3 localPosition)
    {
        var anchor = parent.Find(name);
        if (anchor == null)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name + " equipment anchor");
            Undo.SetTransformParent(go.transform, parent, "Parent " + name + " equipment anchor");
            anchor = go.transform;
        }

        Undo.RecordObject(anchor, "Configure " + name + " equipment anchor");
        anchor.localPosition = localPosition;
        anchor.localRotation = Quaternion.identity;
        anchor.localScale = Vector3.one;
        EditorUtility.SetDirty(anchor);
        return anchor;
    }

    static int ConfigurePpeMetadata(Scene scene, StringBuilder report)
    {
        var count = 0;
        foreach (var equipable in FindEquipables(scene))
        {
            if (!TryGetProfile(equipable.ItemId, out var profile))
            {
                if (equipable.ItemId != "ResponseBag")
                    report.AppendLine($"  SKIP {equipable.name}: no placement profile for '{equipable.ItemId}'.");
                continue;
            }

            Undo.RecordObject(equipable, "Configure PPE placement definition");
            equipable.EditorApplyPlacementDefinition(
                profile.Type,
                profile.Slots,
                profile.Distance,
                profile.AngleTolerance,
                equipable.GuidePrefab);
            var requireRotation = profile.Type == PPEType.Respirator ||
                                  profile.Type == PPEType.FaceShield ||
                                  profile.Type == PPEType.Helmet ||
                                  profile.Type == PPEType.Goggles;
            equipable.EditorSetUseRotationValidation(requireRotation);
            EditorUtility.SetDirty(equipable);
            count++;
            report.AppendLine(
                $"  {equipable.ItemId}: {profile.Type} -> {profile.Slots}, " +
                $"distance={profile.Distance:0.00}m, angle={profile.AngleTolerance:0.#}deg");
        }
        return count;
    }

    static bool TryGetProfile(string itemId, out PlacementProfile profile)
    {
        profile = default;
        return !string.IsNullOrWhiteSpace(itemId) && PlacementProfiles.TryGetValue(itemId, out profile);
    }

    static string Validate(
        Scene scene,
        PPEEquipmentAnchorSet anchorSet,
        PPEPlayerBodyReference bodyReference)
    {
        var failures = new List<string>();
        var anchorCount = 0;
        foreach (PPEEquipSlot slot in new[]
                 {
                     PPEEquipSlot.Head, PPEEquipSlot.Face,
                     PPEEquipSlot.LeftHand, PPEEquipSlot.RightHand,
                     PPEEquipSlot.Body, PPEEquipSlot.LeftFoot, PPEEquipSlot.RightFoot,
                     PPEEquipSlot.Radio, PPEEquipSlot.GasDetector,
                 })
        {
            if (anchorSet != null && anchorSet.TryGetAnchor(slot, out _))
                anchorCount++;
            else
                failures.Add("missing anchor: " + slot);
        }

        var metadataCount = 0;
        foreach (var equipable in FindEquipables(scene))
        {
            if (equipable.ItemId == "ResponseBag")
                continue;
            if (equipable.EquipSlots == PPEEquipSlot.None)
                failures.Add(equipable.ItemId + ": EquipSlots=None");
            else
                metadataCount++;
        }

        if (bodyReference == null || bodyReference.AnchorSet != anchorSet || bodyReference.XrCamera == null)
            failures.Add("PPE player body reference not fully wired");

        return failures.Count == 0
            ? $"VALIDATION PASS: body reference, anchors={anchorCount}/9, PPE definitions={metadataCount}."
            : "VALIDATION FAIL:\n  " + string.Join("\n  ", failures);
    }

    static List<PPEEquipable> FindEquipables(Scene scene)
    {
        var results = new List<PPEEquipable>();
        foreach (var root in scene.GetRootGameObjects())
            results.AddRange(root.GetComponentsInChildren<PPEEquipable>(true));
        return results;
    }

    static Transform FindTransform(Scene scene, string name)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform.name == name)
                    return transform;
            }
        }
        return null;
    }

    static bool IsTargetScene(Scene scene)
    {
        return scene.IsValid() && scene.isLoaded && scene.path == ScenePath;
    }
}

[InitializeOnLoad]
static class PPEEquipmentSlotSetupTrigger
{
    const string SessionKey = "PPE.EquipmentSlotSetup.Step6.v1";
    const string TargetScenePath = "Assets/Scenes/04_PPE.unity";

    static PPEEquipmentSlotSetupTrigger()
    {
        EditorApplication.delayCall += TryApply;
        EditorSceneManager.sceneOpened += OnSceneOpened;
    }

    static void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        if (scene.path == TargetScenePath)
            EditorApplication.delayCall += TryApply;
    }

    static void TryApply()
    {
        if (SessionState.GetBool(SessionKey, false) ||
            EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded || scene.path != TargetScenePath)
            return;

        SessionState.SetBool(SessionKey, true);
        if (PPEEquipmentSlotSetup.IsSetupRequired())
            PPEEquipmentSlotSetup.SetupEquipmentSlots();
    }
}
