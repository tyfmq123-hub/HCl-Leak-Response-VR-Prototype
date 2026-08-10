using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Replaces a damaged ResponseBag_UnityReady scene instance with a fresh FBX prefab instance,
/// preserving BagContentsRoot (and its content slots) and re-wiring bag components.
/// Tools > PPE > Replace ResponseBag Preserve Contents
/// </summary>
public static class ResponseBagReplacePreserveContentsSetup
{
    const string ModelAssetPath =
        "Assets/PPE/ResponseBag_UnityReady_basecolor/ResponseBag_UnityReady.fbx";
    const string ModelGuid = "88d10f56f6f05194191301d6361717dd";
    const string UnityReadyName = "ResponseBag_UnityReady";
    const string ContentsRootName = "BagContentsRoot";
    const string TriggerPath = "Assets/Scripts/Editor/RunResponseBagReplacePreserveContents.trigger.txt";

    [InitializeOnLoadMethod]
    static void AutoRunIfTriggered()
    {
        if (!File.Exists(TriggerPath))
            return;

        EditorApplication.delayCall += () =>
        {
            if (!File.Exists(TriggerPath))
                return;

            try
            {
                File.Delete(TriggerPath);
                if (File.Exists(TriggerPath + ".meta"))
                    File.Delete(TriggerPath + ".meta");
            }
            catch
            {
                // ignored
            }

            ReplacePreserveContents();
        };
    }

    [MenuItem("Tools/PPE/Replace ResponseBag Preserve Contents", false, 31)]
    public static void ReplacePreserveContents()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[Bag Replace] No active loaded scene.");
            return;
        }

        var report = new StringBuilder();
        report.AppendLine("=== Replace ResponseBag_UnityReady (preserve BagContentsRoot) ===");

        var manager = Object.FindAnyObjectByType<ResponseEquipmentManager>(FindObjectsInactive.Include);
        var oldBag = manager != null && manager.ResponseBagRoot != null
            ? manager.ResponseBagRoot
            : FindWorldBagInstance();

        if (oldBag == null)
        {
            Debug.LogError("[Bag Replace] No ResponseBag_UnityReady instance found in the scene.");
            return;
        }

        // Prefer outermost prefab root.
        var oldRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(oldBag);
        if (oldRoot != null)
            oldBag = oldRoot;

        report.AppendLine("Old bag: " + GetPath(oldBag.transform));

        var contents = FindChildRecursive(oldBag.transform, ContentsRootName);
        if (contents == null)
        {
            // Sometimes BagContentsRoot was already detached.
            contents = FindSceneContentsRoot();
        }

        Transform contentsTransform = contents;
        Vector3 contentsLocalPos = Vector3.zero;
        Quaternion contentsLocalRot = Quaternion.identity;
        Vector3 contentsLocalScale = Vector3.one;
        if (contentsTransform != null)
        {
            contentsLocalPos = contentsTransform.localPosition;
            contentsLocalRot = contentsTransform.localRotation;
            contentsLocalScale = contentsTransform.localScale;
            Undo.SetTransformParent(contentsTransform, null, "Detach BagContentsRoot");
            report.AppendLine("Detached BagContentsRoot.");
        }
        else
        {
            report.AppendLine("WARNING: BagContentsRoot not found — bag mesh will be replaced without contents.");
        }

        var worldPos = oldBag.transform.position;
        var worldRot = oldBag.transform.rotation;
        var worldScale = oldBag.transform.localScale;
        var parent = oldBag.transform.parent;

        Undo.DestroyObjectImmediate(oldBag);
        report.AppendLine("Destroyed damaged bag instance.");

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelAssetPath);
        if (model == null)
        {
            var path = AssetDatabase.GUIDToAssetPath(ModelGuid);
            model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        if (model == null)
        {
            Debug.LogError("[Bag Replace] Missing model asset: " + ModelAssetPath);
            if (contentsTransform != null)
                Undo.SetTransformParent(contentsTransform, null, "Leave contents orphaned");
            return;
        }

        var newBag = (GameObject)PrefabUtility.InstantiatePrefab(model);
        Undo.RegisterCreatedObjectUndo(newBag, "Instantiate fresh ResponseBag_UnityReady");
        newBag.name = UnityReadyName;
        if (parent != null)
            Undo.SetTransformParent(newBag.transform, parent, "Parent new bag");
        newBag.transform.position = worldPos;
        newBag.transform.rotation = worldRot;
        newBag.transform.localScale = worldScale;
        report.AppendLine("Instantiated fresh FBX at " + worldPos);

        if (contentsTransform != null)
        {
            Undo.SetTransformParent(contentsTransform, newBag.transform, "Reparent BagContentsRoot");
            contentsTransform.localPosition = contentsLocalPos;
            contentsTransform.localRotation = contentsLocalRot;
            contentsTransform.localScale = contentsLocalScale;
            report.AppendLine("Reparented BagContentsRoot under new bag.");
        }

        EnsureBagComponents(newBag, report);
        WireRevealAndContents(newBag, contentsTransform, manager, report);

        if (manager != null)
        {
            manager.InitializeResponseBagRoot(newBag, cacheOriginalPose: true);
            EditorUtility.SetDirty(manager);
            report.AppendLine("Manager.responseBagRoot reassigned.");
        }

        var prepare = newBag.GetComponent<ResponseBagPrepareInteractable>();
        var router = Object.FindAnyObjectByType<XRInteractionConfirmRouter>(FindObjectsInactive.Include);
        var selection = Object.FindAnyObjectByType<PPESelectionManager>(FindObjectsInactive.Include);
        if (router != null && prepare != null)
        {
            router.EditorWire(prepare, selection);
            EditorUtility.SetDirty(router);
            report.AppendLine("Rewired XRInteractionConfirmRouter.");
        }

        EditorUtility.SetDirty(newBag);
        EditorSceneManager.MarkSceneDirty(scene);
        if (scene.isDirty && !string.IsNullOrEmpty(scene.path))
            EditorSceneManager.SaveScene(scene);

        Selection.activeGameObject = newBag;
        Debug.Log(report.ToString());
    }

    static void EnsureBagComponents(GameObject bag, StringBuilder report)
    {
        if (bag.GetComponent<Collider>() == null)
        {
            var box = Undo.AddComponent<BoxCollider>(bag);
            FitBoxToRenderers(bag, box);
            report.AppendLine("Added BoxCollider.");
        }

        if (bag.GetComponent<XRSimpleInteractable>() == null
            && bag.GetComponent<XRBaseInteractable>() == null)
        {
            Undo.AddComponent<XRSimpleInteractable>(bag);
            report.AppendLine("Added XRSimpleInteractable.");
        }

        var simple = bag.GetComponent<XRSimpleInteractable>();
        var bagCol = bag.GetComponent<Collider>();
        if (simple != null && bagCol != null)
        {
            simple.colliders.Clear();
            simple.colliders.Add(bagCol);
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
        report.AppendLine("Wired PPEEquipable.");

        var manager = Object.FindAnyObjectByType<ResponseEquipmentManager>(FindObjectsInactive.Include);
        var prepare = bag.GetComponent<ResponseBagPrepareInteractable>();
        if (prepare != null)
        {
            prepare.EditorWire(manager, bag, bag.GetComponent<XRBaseInteractable>());
            prepare.SetConfirmInputOwnedByRouter(true);
            EditorUtility.SetDirty(prepare);
            report.AppendLine("Wired ResponseBagPrepareInteractable.");
        }
    }

    static void WireRevealAndContents(
        GameObject bag,
        Transform contentsRoot,
        ResponseEquipmentManager manager,
        StringBuilder report)
    {
        var reveal = bag.GetComponent<ResponseBagContentsRevealController>();
        if (reveal == null)
            reveal = Undo.AddComponent<ResponseBagContentsRevealController>(bag);

        GameObject gas = null;
        GameObject sock = null;
        GameObject pad = null;
        if (contentsRoot != null)
        {
            var gasSlot = contentsRoot.Find("GasDetectorSlot");
            var sockSlot = contentsRoot.Find("AbsorbentSockSlot");
            var padSlot = contentsRoot.Find("AbsorbentPadSlot");
            gas = gasSlot != null ? gasSlot.gameObject : null;
            sock = sockSlot != null ? sockSlot.gameObject : null;
            pad = padSlot != null ? padSlot.gameObject : null;
        }

        var label = Object.FindAnyObjectByType<ResponseBagItemNameLabel>(FindObjectsInactive.Include);
        var hoverItems = bag.GetComponentsInChildren<ResponseBagContentHoverItem>(true);
        reveal.EditorWire(
            bag,
            gas,
            sock,
            pad,
            Camera.main,
            manager,
            label,
            hoverItems,
            0.4f,
            0.8f);
        EditorUtility.SetDirty(reveal);

        if (manager != null)
        {
            manager.SetContentsReveal(reveal);
            EditorUtility.SetDirty(manager);
        }

        report.AppendLine(
            "Wired ResponseBagContentsRevealController " +
            $"(gas={(gas != null)}, sock={(sock != null)}, pad={(pad != null)}).");
    }

    static void FitBoxToRenderers(GameObject bag, BoxCollider box)
    {
        var contents = bag.transform.Find(ContentsRootName);
        var renderers = bag.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0)
        {
            box.size = new Vector3(0.45f, 0.35f, 0.55f);
            box.center = new Vector3(0f, 0.175f, 0f);
            return;
        }

        Bounds? bounds = null;
        for (var i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null)
                continue;
            if (contents != null && (r.transform == contents || r.transform.IsChildOf(contents)))
                continue;

            if (bounds == null)
                bounds = r.bounds;
            else
            {
                var b = bounds.Value;
                b.Encapsulate(r.bounds);
                bounds = b;
            }
        }

        if (bounds == null)
        {
            box.size = new Vector3(0.45f, 0.35f, 0.55f);
            box.center = new Vector3(0f, 0.175f, 0f);
            return;
        }

        var t = bag.transform;
        box.center = t.InverseTransformPoint(bounds.Value.center);
        var size = t.InverseTransformVector(bounds.Value.size);
        box.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
    }

    static GameObject FindWorldBagInstance()
    {
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                var t = transforms[i];
                if (t.name.IndexOf(UnityReadyName, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (t.GetComponentInParent<ResponseBagWearableVisual>(true) != null)
                    continue;
                if (t.GetComponentInParent<Canvas>() != null)
                    continue;
                return PrefabUtility.GetOutermostPrefabInstanceRoot(t.gameObject) ?? t.gameObject;
            }
        }

        return null;
    }

    static Transform FindSceneContentsRoot()
    {
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            var found = FindChildRecursive(root.transform, ContentsRootName);
            if (found != null)
                return found;
        }

        return null;
    }

    static Transform FindChildRecursive(Transform root, string exactName)
    {
        if (root == null)
            return null;
        if (root.name == exactName)
            return root;
        for (var i = 0; i < root.childCount; i++)
        {
            var found = FindChildRecursive(root.GetChild(i), exactName);
            if (found != null)
                return found;
        }

        return null;
    }

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
