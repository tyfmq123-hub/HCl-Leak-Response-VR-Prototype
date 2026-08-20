using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Binds ResponseBag_UnityReady model to ResponseEquipmentManager + prepare interactable.
/// Cleans up mistaken XR components on UI checklist rows.
/// </summary>
public static class ResponseBagUnityReadyBindSetup
{
    const string ModelAssetPath =
        "Assets/Common/Equipment/ResponseBag_UnityReady_basecolor/ResponseBag_UnityReady.fbx";
    const string ModelGuid = "88d10f56f6f05194191301d6361717dd";
    const string BagRootName = "ResponseBag";
    const string UnityReadyName = "ResponseBag_UnityReady";

    [MenuItem("Tools/PPE/Bind ResponseBag UnityReady Model", false, 30)]
    public static void BindUnityReadyModel()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[Response Bag] No active loaded scene.");
            return;
        }

        var report = new StringBuilder();
        report.AppendLine("=== Bind ResponseBag_UnityReady ===");

        RepairMistakenUiBag(report);

        var bag = FindOrInstantiateUnityReady(report);
        EnsureBagComponents(bag, report);

        var manager = Object.FindAnyObjectByType<ResponseEquipmentManager>(FindObjectsInactive.Include);
        if (manager == null)
        {
            Debug.LogError("[Response Bag] ResponseEquipmentManager missing. Run Setup Response Bag Integration first.");
            return;
        }

        manager.InitializeResponseBagRoot(bag, cacheOriginalPose: true);
        manager.SetContentsPanel(null);
        EditorUtility.SetDirty(manager);
        report.AppendLine($"Manager.responseBagRoot = {GetPath(bag.transform)}");

        var prepare = bag.GetComponent<ResponseBagPrepareInteractable>();
        if (prepare == null)
            prepare = Undo.AddComponent<ResponseBagPrepareInteractable>(bag);
        prepare.EditorWire(manager, bag, bag.GetComponent<XRBaseInteractable>());
        prepare.SetConfirmInputOwnedByRouter(true);
        EditorUtility.SetDirty(prepare);
        report.AppendLine("Wired ResponseBagPrepareInteractable.");

        var router = Object.FindAnyObjectByType<XRInteractionConfirmRouter>(FindObjectsInactive.Include);
        if (router == null)
        {
            var go = new GameObject("XR_InteractionConfirmRouter");
            Undo.RegisterCreatedObjectUndo(go, "Create XRInteractionConfirmRouter");
            var system = GameObject.Find("PPE_System");
            if (system != null)
                Undo.SetTransformParent(go.transform, system.transform, "Parent confirm router");
            router = Undo.AddComponent<XRInteractionConfirmRouter>(go);
        }

        var selection = Object.FindAnyObjectByType<PPESelectionManager>(FindObjectsInactive.Include);
        router.EditorWire(prepare, selection);
        report.AppendLine("Wired XRInteractionConfirmRouter.");

        WireChecklist(manager, report);

        EditorUtility.SetDirty(bag);
        EditorSceneManager.MarkSceneDirty(scene);
        if (scene.isDirty && !string.IsNullOrEmpty(scene.path))
            EditorSceneManager.SaveScene(scene);

        Selection.activeObject = bag;
        Debug.Log(report.ToString());
    }

    static void RepairMistakenUiBag(StringBuilder report)
    {
        // Previous empty-placeholder setup sometimes attached XR components to
        // a checklist UI row named "ResponseBag". Restore UI and strip XR.
        foreach (var root in sceneRoots())
        {
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                var go = transforms[i].gameObject;
                if (go.name != "ResponseBag" && go.name != "ResponseBagRow")
                    continue;

                // UI row = has RectTransform and lives under a Canvas.
                if (go.GetComponent<RectTransform>() == null)
                    continue;
                if (go.GetComponentInParent<Canvas>() == null)
                    continue;

                var stripped = false;
                var prepare = go.GetComponent<ResponseBagPrepareInteractable>();
                if (prepare != null)
                {
                    Undo.DestroyObjectImmediate(prepare);
                    stripped = true;
                }

                var legacy = go.GetComponent<ResponseBagInteractable>();
                if (legacy != null)
                {
                    Undo.DestroyObjectImmediate(legacy);
                    stripped = true;
                }

                var simple = go.GetComponent<XRSimpleInteractable>();
                if (simple != null)
                {
                    Undo.DestroyObjectImmediate(simple);
                    stripped = true;
                }

                var box = go.GetComponent<BoxCollider>();
                if (box != null)
                {
                    Undo.DestroyObjectImmediate(box);
                    stripped = true;
                }

                if (go.name == "ResponseBag")
                {
                    Undo.RecordObject(go, "Rename checklist row");
                    go.name = "ResponseBagRow";
                    stripped = true;
                }

                if (stripped)
                    report.AppendLine($"Repaired UI row '{GetPath(go.transform)}' (removed XR/bag components).");
            }
        }
    }

    static GameObject FindOrInstantiateUnityReady(StringBuilder report)
    {
        // Prefer existing instance the user already placed.
        var existing = FindByNameContains(UnityReadyName);
        if (existing == null)
            existing = FindPrefabInstanceByGuid(ModelGuid);

        if (existing != null)
        {
            // Keep visual name recognizable, but also accept rename to ResponseBag.
            report.AppendLine($"Reused scene instance '{existing.name}'.");
            return GetPrefabInstanceRoot(existing);
        }

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelAssetPath);
        if (model == null)
        {
            var path = AssetDatabase.GUIDToAssetPath(ModelGuid);
            model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        if (model == null)
        {
            Debug.LogError($"[Response Bag] Missing model: {ModelAssetPath}");
            return null;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        Undo.RegisterCreatedObjectUndo(instance, "Instantiate ResponseBag_UnityReady");
        instance.name = UnityReadyName;
        instance.transform.position = new Vector3(1.94f, 0.35f, 2.14f);
        instance.transform.rotation = Quaternion.identity;
        report.AppendLine("Instantiated ResponseBag_UnityReady from FBX.");
        return instance;
    }

    static GameObject GetPrefabInstanceRoot(GameObject go)
    {
        if (go == null)
            return null;
        var root = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
        return root != null ? root : go;
    }

    static void EnsureBagComponents(GameObject bag, StringBuilder report)
    {
        if (bag == null)
            return;

        if (bag.GetComponent<Collider>() == null)
        {
            var box = Undo.AddComponent<BoxCollider>(bag);
            FitBoxToRenderers(bag, box);
            report.AppendLine("Added BoxCollider fitted to mesh bounds.");
        }

        if (bag.GetComponent<XRSimpleInteractable>() == null
            && bag.GetComponent<XRBaseInteractable>() == null)
        {
            Undo.AddComponent<XRSimpleInteractable>(bag);
            report.AppendLine("Added XRSimpleInteractable.");
        }

        if (bag.GetComponent<PPEOutlineHighlight>() == null)
        {
            Undo.AddComponent<PPEOutlineHighlight>(bag);
            report.AppendLine("Added PPEOutlineHighlight.");
        }

        if (bag.GetComponent<ResponseBagPrepareInteractable>() == null)
            Undo.AddComponent<ResponseBagPrepareInteractable>(bag);

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
        report.AppendLine("Wired PPEEquipable → ResponseBag_Wearable.");

        var legacy = bag.GetComponent<ResponseBagInteractable>();
        if (legacy != null)
        {
            legacy.enabled = false;
            EditorUtility.SetDirty(legacy);
        }
    }

    static void FitBoxToRenderers(GameObject bag, BoxCollider box)
    {
        var renderers = bag.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0)
        {
            box.size = new Vector3(0.45f, 0.35f, 0.55f);
            box.center = new Vector3(0f, 0.175f, 0f);
            return;
        }

        var bounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        var t = bag.transform;
        box.center = t.InverseTransformPoint(bounds.center);
        var size = t.InverseTransformVector(bounds.size);
        box.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
    }

    static void WireChecklist(ResponseEquipmentManager manager, StringBuilder report)
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
        report.AppendLine("Checklist ResponseBag row still uses ResponseBagPrepared.");
    }

    static GameObject FindByNameContains(string name)
    {
        foreach (var root in sceneRoots())
        {
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                if (transforms[i].name.IndexOf(name, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Skip UI / wearable.
                    if (transforms[i].GetComponent<RectTransform>() != null
                        && transforms[i].GetComponentInParent<Canvas>() != null)
                        continue;
                    if (transforms[i].GetComponentInParent<ResponseBagWearableVisual>() != null)
                        continue;
                    if (transforms[i].GetComponentInParent<PlayerBodyAnchorFollower>() != null)
                        continue;
                    var path = transforms[i].name;
                    var p = transforms[i];
                    while (p.parent != null)
                    {
                        p = p.parent;
                        path = p.name + "/" + path;
                    }

                    if (path.IndexOf("XR Origin", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;

                    return transforms[i].gameObject;
                }
            }
        }

        return GameObject.Find(name);
    }

    static GameObject FindPrefabInstanceByGuid(string guid)
    {
        foreach (var root in sceneRoots())
        {
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                var go = transforms[i].gameObject;
                if (!PrefabUtility.IsPartOfPrefabInstance(go))
                    continue;
                var asset = PrefabUtility.GetCorrespondingObjectFromSource(go);
                if (asset == null)
                    continue;
                var path = AssetDatabase.GetAssetPath(asset);
                if (AssetDatabase.AssetPathToGUID(path) == guid)
                    return PrefabUtility.GetOutermostPrefabInstanceRoot(go);
            }
        }

        return null;
    }

    static GameObject[] sceneRoots() => SceneManager.GetActiveScene().GetRootGameObjects();

    static string GetPath(Transform t)
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
