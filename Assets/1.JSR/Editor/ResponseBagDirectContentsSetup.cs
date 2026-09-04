using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Manual setup for direct 3D response-bag content inspection.
/// Tools > PPE > Setup Direct Response Bag Contents
/// </summary>
public static class ResponseBagDirectContentsSetup
{
    const string ContentsDisplayName = "ContentsDisplay";
    const string GasWrapperName = "GasDetector_Display";
    const string SockWrapperName = "AbsorbentSock_Display";
    const string PadWrapperName = "AbsorbentPad_Display";
    const string NameCanvasName = "ResponseBagItemNameCanvas";
    const string DetectorName = "ResponseBag_CameraGazeDetector";
    const string HangulFontResource = "PPE/PPE_HangulFont";
    const string InteractionLayerName = ResponseBagContentHoverItem.InteractionLayerName;

    const string ExactGasName = "ENV_HClDetector_01";
    const string ExactSockName = "INT_AbsorbentSock_Straight_01";
    const string ExactPadName = "INT_AbsorbentPad_01";
    const string ExactPadFallback = "ENV_PPECabinet_03";

    public static void SetupDirectContents()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[Direct Bag Contents] No active loaded scene.");
            return;
        }

        SetupDirectContentsForScene(scene, saveScene: false);
    }

    public static void SetupDirectContentsBatch()
    {
        const string ScenePath = "Assets/Scenes/04_PPE.unity";
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        if (!scene.IsValid())
        {
            Debug.LogError("[Direct Bag Contents] Failed to open " + ScenePath);
            EditorApplication.Exit(1);
            return;
        }

        SetupDirectContentsForScene(scene, saveScene: true);
        EditorApplication.Exit(0);
    }

    static void SetupDirectContentsForScene(Scene scene, bool saveScene)
    {
        var report = new StringBuilder();
        report.AppendLine("=== Setup Direct Response Bag Contents ===");

        EnsureUnityLayer("ResponseBagContent", report);
        var interactionLayerIndex = EnsureXrInteractionLayer(InteractionLayerName, report);

        var system = GameObject.Find("PPE_System");
        if (system == null)
        {
            system = new GameObject("PPE_System");
            Undo.RegisterCreatedObjectUndo(system, "Create PPE_System");
            report.AppendLine("Created PPE_System.");
        }

        var manager = Object.FindAnyObjectByType<ResponseEquipmentManager>(FindObjectsInactive.Include);
        if (manager == null)
        {
            Debug.LogError("[Direct Bag Contents] ResponseEquipmentManager missing.");
            return;
        }

        var bag = manager.ResponseBagRoot;
        if (bag == null)
        {
            Debug.LogError("[Direct Bag Contents] manager.responseBagRoot is null. Assign ResponseBag_HardShell.");
            return;
        }

        if (bag.GetComponentInParent<ResponseBagWearableVisual>(true) != null
            || bag.name.IndexOf("Wearable", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            Debug.LogError("[Direct Bag Contents] World bag points at wearable — abort.");
            return;
        }

        report.AppendLine("World bag: " + GetPath(bag.transform));

        var gasModel = FindExactDirectChild(bag.transform, ExactGasName);
        var sockModel = FindExactDirectChild(bag.transform, ExactSockName);
        var padModel = FindExactDirectChild(bag.transform, ExactPadName);
        if (padModel == null)
            padModel = FindExactDirectChild(bag.transform, ExactPadFallback);

        // Already wrapped?
        if (gasModel == null)
            gasModel = FindExactNamedDescendant(bag.transform, ExactGasName);
        if (sockModel == null)
            sockModel = FindExactNamedDescendant(bag.transform, ExactSockName);
        if (padModel == null)
        {
            padModel = FindExactNamedDescendant(bag.transform, ExactPadName);
            if (padModel == null)
                padModel = FindExactNamedDescendant(bag.transform, ExactPadFallback);
        }

        if (gasModel == null || sockModel == null || padModel == null)
        {
            report.AppendLine("ERROR: Missing content models under bag.");
            report.AppendLine("  GasDetector (" + ExactGasName + "): " + (gasModel != null ? GetPath(gasModel) : "MISSING"));
            report.AppendLine("  AbsorbentSock (" + ExactSockName + "): " + (sockModel != null ? GetPath(sockModel) : "MISSING"));
            report.AppendLine("  AbsorbentPad (" + ExactPadName + "): " + (padModel != null ? GetPath(padModel) : "MISSING"));
            Debug.LogError(report.ToString());
            return;
        }

        var contentsDisplay = EnsureChild(bag.transform, ContentsDisplayName, report);
        var gasWrapper = EnsureWrapper(contentsDisplay, GasWrapperName, gasModel, report);
        var sockWrapper = EnsureWrapper(contentsDisplay, SockWrapperName, sockModel, report);
        var padWrapper = EnsureWrapper(contentsDisplay, PadWrapperName, padModel, report);

        report.AppendLine("Gas detector path: " + GetPath(gasModel));
        report.AppendLine("Absorbent sock path: " + GetPath(sockModel));
        report.AppendLine("Absorbent pad path: " + GetPath(padModel));

        RestrictBagInteractableColliders(bag, report);

        var camera = FindXrCamera();
        var nameLabel = EnsureNameLabel(system.transform, camera, report);
        var detector = EnsureGazeDetector(system.transform, camera, report);

        var reveal = bag.GetComponent<ResponseBagContentsRevealController>();
        if (reveal == null)
        {
            reveal = Undo.AddComponent<ResponseBagContentsRevealController>(bag);
            report.AppendLine("Added ResponseBagContentsRevealController.");
        }
        else
        {
            report.AppendLine("Reused ResponseBagContentsRevealController.");
        }

        var gasItem = ConfigureHoverItem(
            gasWrapper,
            ResponseEquipmentManager.IdGasDetector,
            "\uD734\uB300\uC6A9 HCl \uAC00\uC2A4\uCE21\uC815\uAE30",
            gasModel,
            manager,
            nameLabel,
            reveal,
            interactionLayerIndex,
            report);

        var sockItem = ConfigureHoverItem(
            sockWrapper,
            ResponseEquipmentManager.IdAbsorbentSock,
            "\uCF00\uBBF8\uCEEC \uD761\uCC29\uC0AD",
            sockModel,
            manager,
            nameLabel,
            reveal,
            interactionLayerIndex,
            report);

        var padItem = ConfigureHoverItem(
            padWrapper,
            ResponseEquipmentManager.IdAbsorbentPad,
            "\uD654\uD559\uBB3C\uC9C8\uC6A9 \uD761\uCC29\uD328\uB4DC",
            padModel,
            manager,
            nameLabel,
            reveal,
            interactionLayerIndex,
            report);

        reveal.EditorWire(
            bag,
            gasWrapper.gameObject,
            sockWrapper.gameObject,
            padWrapper.gameObject,
            camera,
            manager,
            nameLabel,
            new[] { gasItem, sockItem, padItem },
            0.4f,
            0.8f);
        EditorUtility.SetDirty(reveal);

        manager.SetContentsReveal(reveal);
        manager.SetContentsPanel(null);
        var wearable = Object.FindAnyObjectByType<ResponseBagWearableVisual>(FindObjectsInactive.Include);
        if (wearable != null)
            manager.SetWearableVisual(wearable);
        EditorUtility.SetDirty(manager);

        var oldPanelDeleted = DisableLegacyContentsUi(report);
        report.AppendLine("Old contents panel deleted: " + oldPanelDeleted);
        report.AppendLine("Shared name label created/reused: " + GetPath(nameLabel.transform));
        report.AppendLine("Gaze detector: " + GetPath(detector.transform));

        AppendMissingRefs(reveal, gasItem, sockItem, padItem, nameLabel, report);
        EnsureRayInteractorsIncludeLayer(interactionLayerIndex, report);

        // Hide contents in edit mode preview so Play matches Start behaviour intent.
        gasWrapper.gameObject.SetActive(false);
        sockWrapper.gameObject.SetActive(false);
        padWrapper.gameObject.SetActive(false);

        EditorSceneManager.MarkSceneDirty(scene);
        if (saveScene)
            EditorSceneManager.SaveScene(scene);

        Selection.activeObject = bag;
        Debug.Log(report.ToString());
    }

    static Transform FindExactDirectChild(Transform parent, string exactName)
    {
        for (var i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child.name == exactName)
                return child;
        }

        return null;
    }

    static Transform FindExactNamedDescendant(Transform root, string exactName)
    {
        var all = root.GetComponentsInChildren<Transform>(true);
        for (var i = 0; i < all.Length; i++)
        {
            if (all[i] != root && all[i].name == exactName)
                return all[i];
        }

        return null;
    }

    static Transform EnsureChild(Transform parent, string name, StringBuilder report)
    {
        var existing = parent.Find(name);
        if (existing != null)
        {
            report.AppendLine("Reused " + name);
            return existing;
        }

        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        report.AppendLine("Created " + name);
        return go.transform;
    }

    static Transform EnsureWrapper(
        Transform contentsDisplay,
        string wrapperName,
        Transform model,
        StringBuilder report)
    {
        var existing = contentsDisplay.Find(wrapperName);
        Transform wrapper;
        if (existing != null)
        {
            wrapper = existing;
            report.AppendLine("Reused wrapper " + wrapperName);
        }
        else
        {
            var go = new GameObject(wrapperName);
            Undo.RegisterCreatedObjectUndo(go, "Create " + wrapperName);
            wrapper = go.transform;
            wrapper.SetParent(contentsDisplay, false);
            wrapper.localPosition = Vector3.zero;
            wrapper.localRotation = Quaternion.identity;
            wrapper.localScale = Vector3.one;
            report.AppendLine("Created wrapper " + wrapperName);
        }

        if (model.parent != wrapper)
        {
            Undo.SetTransformParent(model, wrapper, "Parent content under " + wrapperName);
            report.AppendLine("Reparented " + model.name + " under " + wrapperName + " (world pose preserved).");
        }

        return wrapper;
    }

    static ResponseBagContentHoverItem ConfigureHoverItem(
        Transform wrapper,
        string itemId,
        string koreanName,
        Transform model,
        ResponseEquipmentManager manager,
        ResponseBagItemNameLabel nameLabel,
        ResponseBagContentsRevealController reveal,
        int interactionLayerIndex,
        StringBuilder report)
    {
        var interactable = wrapper.GetComponent<XRSimpleInteractable>();
        if (interactable == null)
        {
            interactable = Undo.AddComponent<XRSimpleInteractable>(wrapper.gameObject);
            report.AppendLine("Added XRSimpleInteractable on " + wrapper.name);
        }

        var so = new SerializedObject(interactable);
        var layersProp = so.FindProperty("m_InteractionLayers.m_Bits");
        // Default (bit 0) + ResponseBagContent when available — rays work even before layer remap.
        if (interactionLayerIndex >= 0)
            layersProp.longValue = 1L | (1L << interactionLayerIndex);
        else
            layersProp.longValue = 1L;
        so.ApplyModifiedPropertiesWithoutUndo();

        var collider = wrapper.GetComponent<Collider>();
        if (collider == null)
        {
            var box = Undo.AddComponent<BoxCollider>(wrapper.gameObject);
            FitBoxCollider(box, model);
            collider = box;
            report.AppendLine("Added BoxCollider on " + wrapper.name + " size=" + box.size);
        }
        else
        {
            report.AppendLine("Reused Collider on " + wrapper.name);
        }

        // Explicit collider list so bag auto-collect does not steal these.
        so.Update();
        var colList = so.FindProperty("m_Colliders");
        colList.arraySize = 1;
        colList.GetArrayElementAtIndex(0).objectReferenceValue = collider;
        so.ApplyModifiedPropertiesWithoutUndo();

        var outline = wrapper.GetComponent<PPEInteractableOutline>();
        if (outline == null)
        {
            outline = Undo.AddComponent<PPEInteractableOutline>(wrapper.gameObject);
            report.AppendLine("Added PPEInteractableOutline on " + wrapper.name);
        }

        outline.EditorSetHoverYellowRayOnly();
        EditorUtility.SetDirty(outline);

        // Never add PPEOutlineHighlight / grab / prepare on content wrappers.
        var badHighlight = wrapper.GetComponent<PPEOutlineHighlight>();
        if (badHighlight != null)
        {
            Undo.DestroyObjectImmediate(badHighlight);
            report.AppendLine("Removed PPEOutlineHighlight from " + wrapper.name);
        }

        var hover = wrapper.GetComponent<ResponseBagContentHoverItem>();
        if (hover == null)
        {
            hover = Undo.AddComponent<ResponseBagContentHoverItem>(wrapper.gameObject);
            report.AppendLine("Added ResponseBagContentHoverItem on " + wrapper.name);
        }

        hover.EditorWire(
            itemId,
            koreanName,
            model,
            interactable,
            collider,
            outline,
            manager,
            nameLabel,
            reveal);
        EditorUtility.SetDirty(hover);
        return hover;
    }

    static void FitBoxCollider(BoxCollider box, Transform model)
    {
        var renderers = model.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
        {
            box.center = Vector3.zero;
            box.size = Vector3.one * 0.15f;
            return;
        }

        var bounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++)
        {
            if (renderers[i] is MeshRenderer || renderers[i] is SkinnedMeshRenderer)
                bounds.Encapsulate(renderers[i].bounds);
        }

        var worldCenter = bounds.center;
        var worldSize = bounds.size;
        var localCenter = box.transform.InverseTransformPoint(worldCenter);
        var lossy = box.transform.lossyScale;
        var localSize = new Vector3(
            SafeDiv(worldSize.x, Mathf.Abs(lossy.x)),
            SafeDiv(worldSize.y, Mathf.Abs(lossy.y)),
            SafeDiv(worldSize.z, Mathf.Abs(lossy.z)));

        box.center = localCenter;
        box.size = Vector3.Max(localSize, Vector3.one * 0.05f);
    }

    static float SafeDiv(float a, float b) => Mathf.Abs(b) < 0.0001f ? a : a / b;

    static void RestrictBagInteractableColliders(GameObject bag, StringBuilder report)
    {
        var interactable = bag.GetComponent<XRSimpleInteractable>();
        if (interactable == null)
        {
            report.AppendLine("WARNING: Bag missing XRSimpleInteractable.");
            return;
        }

        var bagCollider = bag.GetComponent<Collider>();
        if (bagCollider == null)
        {
            report.AppendLine("WARNING: Bag missing Collider — content colliders may bind to bag interactable.");
            return;
        }

        var so = new SerializedObject(interactable);
        var colList = so.FindProperty("m_Colliders");
        colList.arraySize = 1;
        colList.GetArrayElementAtIndex(0).objectReferenceValue = bagCollider;
        so.ApplyModifiedPropertiesWithoutUndo();
        report.AppendLine("Bag XRSimpleInteractable colliders locked to bag collider only.");
    }

    static ResponseBagItemNameLabel EnsureNameLabel(Transform system, Camera camera, StringBuilder report)
    {
        var existing = Object.FindAnyObjectByType<ResponseBagItemNameLabel>(FindObjectsInactive.Include);
        if (existing != null)
        {
            report.AppendLine("Reused shared name label.");
            return existing;
        }

        var canvasGo = new GameObject(NameCanvasName);
        Undo.RegisterCreatedObjectUndo(canvasGo, "Create item name canvas");
        canvasGo.transform.SetParent(system, false);
        canvasGo.transform.localScale = Vector3.one * 0.001f;

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = camera;

        var rt = canvasGo.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(400f, 80f);

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;

        var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
        Undo.RegisterCreatedObjectUndo(bg, "Create name label bg");
        bg.transform.SetParent(canvasGo.transform, false);
        var bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;
        var bgImage = bg.GetComponent<Image>();
        bgImage.color = new Color(0.08f, 0.09f, 0.11f, 0.82f);

        var border = new GameObject("YellowAccent", typeof(RectTransform), typeof(Image));
        Undo.RegisterCreatedObjectUndo(border, "Create name label accent");
        border.transform.SetParent(bg.transform, false);
        var borderRt = border.GetComponent<RectTransform>();
        borderRt.anchorMin = new Vector2(0f, 0f);
        borderRt.anchorMax = new Vector2(1f, 0f);
        borderRt.pivot = new Vector2(0.5f, 0f);
        borderRt.sizeDelta = new Vector2(0f, 4f);
        borderRt.anchoredPosition = Vector2.zero;
        border.GetComponent<Image>().color = new Color(1f, 0.85f, 0.1f, 1f);

        var textGo = new GameObject("Label", typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(textGo, "Create name label text");
        textGo.transform.SetParent(bg.transform, false);
        var textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(12f, 8f);
        textRt.offsetMax = new Vector2(-12f, -8f);

        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = string.Empty;
        tmp.fontSize = 32f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Ellipsis;

        var font = Resources.Load<TMP_FontAsset>(HangulFontResource);
        if (font != null)
            tmp.font = font;
        else
            report.AppendLine("WARNING: PPE_HangulFont not found at Resources/" + HangulFontResource);

        var label = canvasGo.AddComponent<ResponseBagItemNameLabel>();
        label.EditorWire(canvas, bgRt, tmp, camera);
        canvasGo.SetActive(false);
        report.AppendLine("Created shared ResponseBagItemNameCanvas.");
        return label;
    }

    static ResponseBagCameraGazeDetector EnsureGazeDetector(Transform system, Camera camera, StringBuilder report)
    {
        var existing = Object.FindAnyObjectByType<ResponseBagCameraGazeDetector>(FindObjectsInactive.Include);
        if (existing != null)
        {
            existing.EditorWire(camera, ~0, 3f);
            EditorUtility.SetDirty(existing);
            report.AppendLine("Reused ResponseBag_CameraGazeDetector.");
            return existing;
        }

        var go = new GameObject(DetectorName);
        Undo.RegisterCreatedObjectUndo(go, "Create gaze detector");
        go.transform.SetParent(system, false);
        var detector = go.AddComponent<ResponseBagCameraGazeDetector>();
        detector.EditorWire(camera, ~0, 3f);
        report.AppendLine("Created ResponseBag_CameraGazeDetector.");
        return detector;
    }

    static bool DisableLegacyContentsUi(StringBuilder report)
    {
        var deleted = false;

        deleted |= DestroyNamedRoot("ResponseBagContentsCanvas", report);
        deleted |= DestroyNamedRoot("ResponseBag_ContentsCanvas", report);
        deleted |= DestroyNamedRoot("ResponseBagPreviewStage", report);
        deleted |= DestroyNamedRoot("ResponseBagContentsUIAnchor", report);

        var panels = Object.FindObjectsByType<ResponseBagContentsPanel>(
            FindObjectsInactive.Include);
        for (var i = 0; i < panels.Length; i++)
        {
            if (panels[i] == null)
                continue;
            var go = panels[i].gameObject;
            report.AppendLine("Deleted contents panel GO: " + GetPath(go.transform));
            Undo.DestroyObjectImmediate(go);
            deleted = true;
        }

        var gazeTargets = Object.FindObjectsByType<ResponseBagGazeTarget>(
            FindObjectsInactive.Include);
        for (var i = 0; i < gazeTargets.Length; i++)
        {
            if (gazeTargets[i] == null)
                continue;
            var go = gazeTargets[i].gameObject;
            report.AppendLine("Deleted gaze target: " + GetPath(go.transform));
            Undo.DestroyObjectImmediate(go);
            deleted = true;
        }

        return deleted;
    }

    static bool DestroyNamedRoot(string name, StringBuilder report)
    {
        var t = FindInActiveSceneByName(name);
        if (t == null)
            return false;

        report.AppendLine("Deleted " + GetPath(t));
        Undo.DestroyObjectImmediate(t.gameObject);
        return true;
    }

    static Transform FindInActiveSceneByName(string name)
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid())
            return null;

        var roots = scene.GetRootGameObjects();
        for (var i = 0; i < roots.Length; i++)
        {
            var transforms = roots[i].GetComponentsInChildren<Transform>(true);
            for (var t = 0; t < transforms.Length; t++)
            {
                if (transforms[t].name == name)
                    return transforms[t];
            }
        }

        return null;
    }

    static void AppendMissingRefs(
        ResponseBagContentsRevealController reveal,
        ResponseBagContentHoverItem gas,
        ResponseBagContentHoverItem sock,
        ResponseBagContentHoverItem pad,
        ResponseBagItemNameLabel label,
        StringBuilder report)
    {
        report.AppendLine("--- Missing Inspector references ---");
        if (reveal == null)
            report.AppendLine("Reveal controller: MISSING");
        if (reveal != null && reveal.GasDetectorRoot == null)
            report.AppendLine("GasDetectorRoot: MISSING");
        if (reveal != null && reveal.AbsorbentSockRoot == null)
            report.AppendLine("AbsorbentSockRoot: MISSING");
        if (reveal != null && reveal.AbsorbentPadRoot == null)
            report.AppendLine("AbsorbentPadRoot: MISSING");
        if (gas == null)
            report.AppendLine("Gas hover item: MISSING");
        if (sock == null)
            report.AppendLine("Sock hover item: MISSING");
        if (pad == null)
            report.AppendLine("Pad hover item: MISSING");
        if (label == null)
            report.AppendLine("Shared name label: MISSING");
        if (reveal != null
            && reveal.GasDetectorRoot != null
            && reveal.AbsorbentSockRoot != null
            && reveal.AbsorbentPadRoot != null
            && gas != null && sock != null && pad != null && label != null)
        {
            report.AppendLine("(none)");
        }
    }

    static Camera FindXrCamera()
    {
        var origin = Object.FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
        if (origin != null && origin.Camera != null)
            return origin.Camera;
        return Camera.main;
    }

    static void EnsureUnityLayer(string layerName, StringBuilder report)
    {
        if (LayerMask.NameToLayer(layerName) >= 0)
            return;

        var tagManager = new SerializedObject(
            AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/TagManager.asset"));
        var layers = tagManager.FindProperty("layers");
        for (var i = 8; i < layers.arraySize; i++)
        {
            var sp = layers.GetArrayElementAtIndex(i);
            if (!string.IsNullOrEmpty(sp.stringValue))
                continue;
            sp.stringValue = layerName;
            tagManager.ApplyModifiedProperties();
            report.AppendLine("Created Unity layer: " + layerName + " (index " + i + ")");
            return;
        }

        report.AppendLine("WARNING: Could not create Unity layer " + layerName);
    }

    static int EnsureXrInteractionLayer(string layerName, StringBuilder report)
    {
        var settings = AssetDatabase.LoadAssetAtPath<Object>(
            "Assets/XRI/Settings/Resources/InteractionLayerSettings.asset");
        if (settings == null)
        {
            report.AppendLine("WARNING: InteractionLayerSettings.asset missing.");
            return -1;
        }

        var so = new SerializedObject(settings);
        var names = so.FindProperty("m_LayerNames");
        for (var i = 0; i < names.arraySize; i++)
        {
            if (names.GetArrayElementAtIndex(i).stringValue == layerName)
            {
                report.AppendLine("Reused XR interaction layer '" + layerName + "' index " + i);
                return i;
            }
        }

        for (var i = 1; i < names.arraySize; i++)
        {
            var sp = names.GetArrayElementAtIndex(i);
            if (!string.IsNullOrEmpty(sp.stringValue))
                continue;
            sp.stringValue = layerName;
            so.ApplyModifiedProperties();
            report.AppendLine("Created XR interaction layer '" + layerName + "' index " + i);
            return i;
        }

        report.AppendLine("WARNING: No free XR interaction layer slot for " + layerName);
        return -1;
    }

    static void EnsureRayInteractorsIncludeLayer(int layerIndex, StringBuilder report)
    {
        if (layerIndex < 0)
            return;

        var bit = 1L << layerIndex;
        var interactors = Object.FindObjectsByType<XRBaseInteractor>(
            FindObjectsInactive.Include);

        var updated = 0;
        for (var i = 0; i < interactors.Length; i++)
        {
            var interactor = interactors[i];
            if (interactor == null)
                continue;

            if (interactor.name.IndexOf("Teleport", System.StringComparison.OrdinalIgnoreCase) >= 0)
                continue;

            var so = new SerializedObject(interactor);
            var layers = so.FindProperty("m_InteractionLayers.m_Bits");
            if (layers == null)
                continue;

            var value = layers.longValue;
            if ((value & bit) != 0)
                continue;

            layers.longValue = value | bit;
            so.ApplyModifiedPropertiesWithoutUndo();
            updated++;
        }

        report.AppendLine("Interactors updated with ResponseBagContent layer: " + updated);
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
