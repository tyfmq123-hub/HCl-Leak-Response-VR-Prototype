using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Connects the world ResponseBag to ResponseEquipmentManager + checklist + confirm router.
/// Manual menu only — does not mutate the scene on domain reload.
/// </summary>
public static class ResponseEquipmentSetup
{
    const string BagRootName = "ResponseBag";
    const string ManagerName = "Response_EquipmentManager";
    const string RouterName = "XR_InteractionConfirmRouter";
    const string PanelCanvasName = "ResponseBag_ContentsCanvas";
    const string UnityReadyModelPath =
        "Assets/Common/Equipment/ResponseBag_HardShell/ResponseBag_HardShell.fbx";
    const string UnityReadyName = "ResponseBag_HardShell";

    static readonly string[] ExactPreferredNames =
    {
        "ResponseBag_HardShell",
        "ResponseBag_World",
        "ResponseBag",
        "PPE_ResponseBag",
        "ChemicalResponseBag",
        "ResponseBag_Yellow",
    };

    static readonly string[] WearableExcludeNameParts =
    {
        "Wearable",
        "PlayerBodyAnchor",
        "BodyAnchor",
        "VisualOnly",
        "UI",
        "Canvas",
    };

    public static void SetupResponseEquipment()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[Response Bag] No active loaded scene.");
            return;
        }

        var report = new StringBuilder();
        report.AppendLine("=== Response Bag Integration Setup ===");

        var manager = Object.FindAnyObjectByType<ResponseEquipmentManager>(FindObjectsInactive.Include);
        GameObject existingRoot = manager != null ? manager.ResponseBagRoot : null;

        var bag = ResolveWorldBagRoot(existingRoot, report);
        if (bag == null)
        {
            Debug.LogError("[Response Bag Setup] No valid world bag candidate. Assign manually.");
            Debug.Log(report.ToString());
            return;
        }

        manager = EnsureManager(bag, report);
        WirePrepareInteractable(bag, manager, report);
        EnsureConfirmRouter(bag, report);
        DisableLegacyContentsOpener(bag, report);
        HideContentsCanvasIfPresent(report);
        WireChecklistPanel(manager, report);
        ValidateWorldAndWearableBagReferences(bag, report);

        var rayReport = new StringBuilder();
        if (PPERayInteractableSetup.WireRayInteractorSelectAndActivateInputs(rayReport))
            report.AppendLine(rayReport.ToString());

        EditorUtility.SetDirty(manager);
        EditorUtility.SetDirty(bag);
        EditorSceneManager.MarkSceneDirty(scene);
        if (scene.isDirty && !string.IsNullOrEmpty(scene.path))
            EditorSceneManager.SaveScene(scene);

        Selection.activeObject = bag;
        Debug.Log(report.ToString());
    }

    static GameObject ResolveWorldBagRoot(GameObject existingAssigned, StringBuilder report)
    {
        if (existingAssigned != null && IsValidWorldBagCandidate(existingAssigned, out _))
        {
            report.AppendLine(
                $"[Response Bag Setup] Keeping existing world bag: {GetHierarchyPath(existingAssigned.transform)}");
            EnsureBagComponents(existingAssigned);
            return existingAssigned;
        }

        // Priority 1: exact existing PrepareInteractable on a valid world object.
        var prepares = Object.FindObjectsByType<ResponseBagPrepareInteractable>(
            FindObjectsInactive.Include);
        var prepareCandidates = new List<GameObject>();
        for (var i = 0; i < prepares.Length; i++)
        {
            if (prepares[i] == null)
                continue;
            var go = prepares[i].gameObject;
            if (!IsValidWorldBagCandidate(go, out var reason))
            {
                report.AppendLine($"[Response Bag Setup] Wearable/excluded prepare on {GetHierarchyPath(go.transform)} ({reason}).");
                continue;
            }

            prepareCandidates.Add(go);
        }

        if (prepareCandidates.Count == 1)
        {
            EnsureBagComponents(prepareCandidates[0]);
            report.AppendLine(
                $"[Response Bag Setup] World bag assigned: {GetHierarchyPath(prepareCandidates[0].transform)}");
            return prepareCandidates[0];
        }

        if (prepareCandidates.Count > 1)
        {
            report.AppendLine("[Response Bag Setup] Multiple world bag candidates found. Assign manually.");
            for (var i = 0; i < prepareCandidates.Count; i++)
                report.AppendLine("  - " + GetHierarchyPath(prepareCandidates[i].transform));
            return existingAssigned;
        }

        // Priority 2–5: scored search.
        var candidates = CollectWorldBagCandidates(report);
        if (candidates.Count == 0)
        {
            var created = TryCreateUnityReadyOrPlaceholder(report);
            if (created != null)
            {
                report.AppendLine(
                    $"[Response Bag Setup] World bag assigned: {GetHierarchyPath(created.transform)}");
            }

            return created;
        }

        if (candidates.Count > 1)
        {
            // Prefer exact name matches first.
            GameObject exact = null;
            for (var i = 0; i < ExactPreferredNames.Length; i++)
            {
                for (var c = 0; c < candidates.Count; c++)
                {
                    if (candidates[c].name == ExactPreferredNames[i])
                    {
                        exact = candidates[c];
                        break;
                    }
                }

                if (exact != null)
                    break;
            }

            if (exact != null)
            {
                EnsureBagComponents(exact);
                report.AppendLine(
                    $"[Response Bag Setup] World bag assigned (exact name): {GetHierarchyPath(exact.transform)}");
                for (var i = 0; i < candidates.Count; i++)
                {
                    if (candidates[i] != exact)
                        report.AppendLine(
                            $"[Response Bag Setup] Other candidate skipped: {GetHierarchyPath(candidates[i].transform)}");
                }

                return exact;
            }

            report.AppendLine("[Response Bag Setup] Multiple world bag candidates found. Assign manually.");
            for (var i = 0; i < candidates.Count; i++)
                report.AppendLine("  - " + GetHierarchyPath(candidates[i].transform));
            return existingAssigned;
        }

        EnsureBagComponents(candidates[0]);
        report.AppendLine(
            $"[Response Bag Setup] World bag assigned: {GetHierarchyPath(candidates[0].transform)}");
        return candidates[0];
    }

    static List<GameObject> CollectWorldBagCandidates(StringBuilder report)
    {
        var list = new List<GameObject>();
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                var go = transforms[i].gameObject;
                if (!NameLooksLikeBag(go.name))
                    continue;

                if (!IsValidWorldBagCandidate(go, out var reason))
                {
                    report.AppendLine(
                        $"[Response Bag Setup] Wearable bag excluded: {GetHierarchyPath(go.transform)} ({reason})");
                    continue;
                }

                if (!list.Contains(go))
                    list.Add(go);
            }
        }

        return list;
    }

    static bool NameLooksLikeBag(string name)
    {
        for (var i = 0; i < ExactPreferredNames.Length; i++)
        {
            if (name.IndexOf(ExactPreferredNames[i], System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return name.IndexOf("ResponseBag", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static bool IsValidWorldBagCandidate(GameObject go, out string rejectReason)
    {
        rejectReason = null;
        if (go == null)
        {
            rejectReason = "null";
            return false;
        }

        var path = GetHierarchyPath(go.transform);
        var lowerName = go.name;

        for (var i = 0; i < WearableExcludeNameParts.Length; i++)
        {
            if (lowerName.IndexOf(WearableExcludeNameParts[i], System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                rejectReason = "excluded name part " + WearableExcludeNameParts[i];
                return false;
            }
        }

        if (go.GetComponentInParent<ResponseBagWearableVisual>(true) != null)
        {
            rejectReason = "has/under ResponseBagWearableVisual";
            return false;
        }

        if (go.GetComponentInParent<PlayerBodyAnchorFollower>(true) != null)
        {
            rejectReason = "under PlayerBodyAnchor";
            return false;
        }

        if (path.IndexOf("XR Origin", System.StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf("Camera Offset", System.StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf("Main Camera", System.StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf("PlayerBodyAnchor", System.StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf("ResponseBag_Wearable", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            rejectReason = "under XR/player wearable hierarchy";
            return false;
        }

        if (go.GetComponentInParent<Canvas>(true) != null)
        {
            rejectReason = "under Canvas";
            return false;
        }

        // Must look like an interactable world object.
        var hasInteract =
            go.GetComponentInChildren<XRBaseInteractable>(true) != null
            || go.GetComponentInChildren<ResponseBagPrepareInteractable>(true) != null
            || go.GetComponentInChildren<Collider>(true) != null
            || go.GetComponentInChildren<Rigidbody>(true) != null;

        if (!hasInteract)
        {
            rejectReason = "no interactable/collider/rigidbody";
            return false;
        }

        return true;
    }

    static GameObject TryCreateUnityReadyOrPlaceholder(StringBuilder report)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(UnityReadyModelPath);
        if (model != null)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            Undo.RegisterCreatedObjectUndo(instance, "Instantiate ResponseBag_HardShell");
            instance.name = UnityReadyName;
            instance.transform.position = new Vector3(1.94f, 0.35f, 2.14f);
            EnsureBagComponents(instance);
            report.AppendLine("Instantiated ResponseBag_HardShell from FBX.");
            return instance;
        }

        var bag = new GameObject(BagRootName);
        Undo.RegisterCreatedObjectUndo(bag, "Create ResponseBag placeholder");
        bag.transform.position = new Vector3(1.1f, 0.35f, 1.6f);
        EnsureBagComponents(bag);
        report.AppendLine("Created empty ResponseBag placeholder.");
        return bag;
    }

    static void EnsureBagComponents(GameObject bag)
    {
        if (bag.GetComponent<BoxCollider>() == null && bag.GetComponent<Collider>() == null)
        {
            var box = Undo.AddComponent<BoxCollider>(bag);
            box.size = new Vector3(0.45f, 0.35f, 0.55f);
            box.center = new Vector3(0f, 0.175f, 0f);
        }

        if (bag.GetComponent<XRSimpleInteractable>() == null
            && bag.GetComponent<XRBaseInteractable>() == null)
        {
            Undo.AddComponent<XRSimpleInteractable>(bag);
        }

        if (bag.GetComponent<PPEOutlineHighlight>() == null)
            Undo.AddComponent<PPEOutlineHighlight>(bag);

        if (bag.GetComponent<ResponseBagPrepareInteractable>() == null)
            Undo.AddComponent<ResponseBagPrepareInteractable>(bag);

        EnsureBagEquipable(bag);
    }

    static void EnsureBagEquipable(GameObject bag)
    {
        var equipable = bag.GetComponent<PPEEquipable>();
        if (equipable == null)
            equipable = Undo.AddComponent<PPEEquipable>(bag);

        equipable.EditorApplyIdentity(
            PPEEquipmentManager.IdResponseBag,
            "\uD654\uD559\uBB3C\uC9C8 \uB204\uCD9C \uB300\uC751\uAC00\uBC29");

        var so = new SerializedObject(equipable);
        var deactivate = so.FindProperty("deactivateOnEquip");
        if (deactivate != null)
            deactivate.boolValue = true;

        var wearable = Object.FindAnyObjectByType<ResponseBagWearableVisual>(FindObjectsInactive.Include);
        var visualProp = so.FindProperty("equippedVisual");
        if (visualProp != null && wearable != null)
            visualProp.objectReferenceValue = wearable.WearableRoot;

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(equipable);
    }

    static ResponseEquipmentManager EnsureManager(GameObject bag, StringBuilder report)
    {
        var manager = Object.FindAnyObjectByType<ResponseEquipmentManager>(FindObjectsInactive.Include);
        if (manager == null)
        {
            var go = new GameObject(ManagerName);
            Undo.RegisterCreatedObjectUndo(go, "Create Response_EquipmentManager");

            var system = GameObject.Find("PPE_System");
            if (system != null)
                Undo.SetTransformParent(go.transform, system.transform, "Parent response manager");

            manager = Undo.AddComponent<ResponseEquipmentManager>(go);
            report.AppendLine("Created ResponseEquipmentManager under PPE_System.");
        }
        else
        {
            report.AppendLine("Reused existing ResponseEquipmentManager.");
        }

        manager.InitializeResponseBagRoot(bag, cacheOriginalPose: true);
        manager.SetContentsPanel(null);

        var wearable = Object.FindAnyObjectByType<ResponseBagWearableVisual>(FindObjectsInactive.Include);
        if (wearable != null)
        {
            manager.SetWearableVisual(wearable);
            report.AppendLine("Wired ResponseEquipmentManager.wearableVisual.");
        }
        else
        {
            report.AppendLine("WARNING: ResponseBagWearableVisual missing — run Setup Wearable Response Bag.");
        }

        EditorUtility.SetDirty(manager);
        report.AppendLine($"Manager.responseBagRoot = {GetHierarchyPath(bag.transform)}");
        return manager;
    }

    static void WirePrepareInteractable(GameObject bag, ResponseEquipmentManager manager, StringBuilder report)
    {
        var prepare = bag.GetComponent<ResponseBagPrepareInteractable>();
        if (prepare == null)
            prepare = Undo.AddComponent<ResponseBagPrepareInteractable>(bag);

        prepare.EditorWire(manager, bag, bag.GetComponent<XRBaseInteractable>());
        prepare.SetConfirmInputOwnedByRouter(true);
        EditorUtility.SetDirty(prepare);
        report.AppendLine("Wired ResponseBagPrepareInteractable (confirm owned by router).");
    }

    static void EnsureConfirmRouter(GameObject bag, StringBuilder report)
    {
        var router = Object.FindAnyObjectByType<XRInteractionConfirmRouter>(FindObjectsInactive.Include);
        if (router == null)
        {
            var go = new GameObject(RouterName);
            Undo.RegisterCreatedObjectUndo(go, "Create XRInteractionConfirmRouter");

            var system = GameObject.Find("PPE_System");
            if (system != null)
                Undo.SetTransformParent(go.transform, system.transform, "Parent confirm router");

            router = Undo.AddComponent<XRInteractionConfirmRouter>(go);
            report.AppendLine("Created XRInteractionConfirmRouter under PPE_System.");
        }
        else
        {
            report.AppendLine("Reused XRInteractionConfirmRouter.");
        }

        var prepare = bag.GetComponent<ResponseBagPrepareInteractable>();
        var selection = Object.FindAnyObjectByType<PPESelectionManager>(FindObjectsInactive.Include);
        router.EditorWire(prepare, selection);

        EditorUtility.SetDirty(router);
        report.AppendLine("Wired XRInteractionConfirmRouter (ray selection + bag prepare; wearable PPE is placement-only).");
    }

    static void DisableLegacyContentsOpener(GameObject bag, StringBuilder report)
    {
        var legacy = bag.GetComponent<ResponseBagInteractable>();
        if (legacy == null)
            return;

        legacy.EditorWire(null, null);
        legacy.enabled = false;
        EditorUtility.SetDirty(legacy);
        report.AppendLine("Disabled ResponseBagInteractable (contents UI deferred).");
    }

    static void HideContentsCanvasIfPresent(StringBuilder report)
    {
        var canvas = GameObject.Find(PanelCanvasName);
        if (canvas == null)
        {
            var panel = Object.FindAnyObjectByType<ResponseBagContentsPanel>(FindObjectsInactive.Include);
            if (panel != null)
                canvas = panel.gameObject;
        }

        if (canvas == null)
            return;

        if (canvas.activeSelf)
        {
            Undo.RecordObject(canvas, "Hide ResponseBag contents canvas");
            canvas.SetActive(false);
            report.AppendLine("Hid ResponseBag_ContentsCanvas (inspection deferred).");
        }
    }

    static void WireChecklistPanel(ResponseEquipmentManager manager, StringBuilder report)
    {
        var checklist = Object.FindAnyObjectByType<PPEChecklistPanel>(FindObjectsInactive.Include);
        if (checklist == null)
        {
            report.AppendLine("WARNING: PPEChecklistPanel not found.");
            return;
        }

        var so = new SerializedObject(checklist);
        var managerProp = so.FindProperty("responseEquipmentManager");
        if (managerProp != null)
            managerProp.objectReferenceValue = manager;

        var rowsProp = so.FindProperty("rows");
        if (rowsProp != null && rowsProp.isArray)
        {
            for (var i = 0; i < rowsProp.arraySize; i++)
            {
                var row = rowsProp.GetArrayElementAtIndex(i);
                var id = row.FindPropertyRelative("itemId").stringValue;
                if (id == PPEEquipmentManager.IdResponseBag || id == "ResponseBag")
                {
                    row.FindPropertyRelative("source").enumValueIndex =
                        (int)PPEChecklistPanel.ChecklistSource.ResponseBagPrepared;
                }
            }
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(checklist);
        report.AppendLine("Wired PPEChecklistPanel ResponseBag row -> ResponseBagPrepared.");
    }

    static void ValidateWorldAndWearableBagReferences(GameObject worldBag, StringBuilder report)
    {
        var wearable = Object.FindAnyObjectByType<ResponseBagWearableVisual>(FindObjectsInactive.Include);
        GameObject wearableRoot = wearable != null ? wearable.WearableRoot : null;

        if (worldBag == null)
        {
            report.AppendLine("VALIDATION FAIL: worldResponseBagRoot is null.");
            return;
        }

        if (wearableRoot != null && worldBag == wearableRoot)
            report.AppendLine("VALIDATION FAIL: world bag == wearable bag.");

        if (worldBag.GetComponentInParent<PlayerBodyAnchorFollower>(true) != null)
            report.AppendLine("VALIDATION FAIL: world bag is under PlayerBodyAnchor.");

        if (wearableRoot != null)
        {
            var hasVisualController =
                wearableRoot.GetComponent<WearableResponseBagVisualController>() != null
                || wearableRoot.GetComponentInChildren<WearableResponseBagVisualController>(true) != null;

            if (!hasVisualController)
            {
                if (wearableRoot.GetComponentInChildren<XRBaseInteractable>(true) != null)
                    report.AppendLine("VALIDATION FAIL: wearable has XR interactable.");
                if (wearableRoot.GetComponentInChildren<Collider>(true) != null
                    || wearableRoot.GetComponentInChildren<Rigidbody>(true) != null)
                    report.AppendLine("VALIDATION FAIL: wearable has Collider/Rigidbody.");
            }
            else
            {
                report.AppendLine(
                    "Wearable open/close visuals present — XR interactable/collider on open handle allowed.");
            }

            report.AppendLine(
                $"[Response Bag Setup] Wearable path: {GetHierarchyPath(wearableRoot.transform)}");
        }

        report.AppendLine(
            $"[Response Bag Setup] World bag path: {GetHierarchyPath(worldBag.transform)}");
    }

    static string GetHierarchyPath(Transform t)
    {
        if (t == null)
            return "(null)";
        var path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }

        return path;
    }
}
