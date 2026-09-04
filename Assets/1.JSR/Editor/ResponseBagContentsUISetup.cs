using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// Manual setup for gaze-opened response bag contents UI.
/// Tools > PPE > Setup Response Bag Contents UI
/// </summary>
public static class ResponseBagContentsUISetup
{
    const string CanvasName = "ResponseBagContentsCanvas";
    const string AnchorName = "ResponseBagContentsUIAnchor";
    const string GazeTargetName = "ResponseBag_GazeTarget";
    const string PreviewStageName = "ResponseBagPreviewStage";
    const string DetectorName = "ResponseBag_CameraGazeDetector";
    const string HangulFontResource = "PPE/PPE_HangulFont";

    const string GasDetectorFbx =
        "Assets/Common/Equipment/ENV_HClDetector_01/ENV_HClDetector_01.fbx";
    const string SockFbx =
        "Assets/Common/Equipment/INT_AbsorbentSock_Straight_01/INT_AbsorbentSock_Straight_01.fbx";
    const string PadFbx =
        "Assets/Common/Equipment/INT_AbsorbentPad_01/INT_AbsorbentPad_01.fbx";

    public static void SetupContentsUi()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[Bag Contents UI] No active loaded scene.");
            return;
        }

        var report = new StringBuilder();
        report.AppendLine("=== Setup Response Bag Contents UI ===");

        EnsureLayers(report);

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
            Debug.LogError("[Bag Contents UI] ResponseEquipmentManager missing. Run Response Bag Integration first.");
            return;
        }

        var bag = manager.ResponseBagRoot;
        if (bag == null)
        {
            Debug.LogError("[Bag Contents UI] manager.responseBagRoot is null. Assign world bag first.");
            return;
        }

        if (bag.GetComponentInParent<ResponseBagWearableVisual>(true) != null)
        {
            Debug.LogError("[Bag Contents UI] World bag points at wearable — assign ResponseBag_HardShell instead.");
            return;
        }

        report.AppendLine("World bag: " + GetPath(bag.transform));

        var gazeLayer = LayerMask.NameToLayer(ResponseBagGazeTarget.GazeLayerName);
        var previewLayer = LayerMask.NameToLayer(ResponseBagItemPreviewController.PreviewLayerName);

        var gazeTarget = EnsureGazeTarget(bag, gazeLayer, report);
        var detector = EnsureGazeDetector(system.transform, gazeLayer, report);
        var anchor = EnsureAnchor(system.transform, bag.transform, report);
        var canvasGo = EnsureCanvas(system.transform, anchor, report);
        var panel = EnsurePanelComponent(canvasGo, report);
        var preview = EnsurePreviewStage(system.transform, previewLayer, report);
        WireUi(canvasGo, panel, preview, manager, bag, gazeTarget, report);

        var wearable = Object.FindAnyObjectByType<ResponseBagWearableVisual>(FindObjectsInactive.Include);
        manager.SetContentsPanel(panel);
        manager.SetWearableVisual(wearable);
        EditorUtility.SetDirty(manager);

        if (wearable != null)
            report.AppendLine("Wearable connected: " + GetPath(wearable.transform));
        else
            report.AppendLine("WARNING: Wearable visual not found — assign later.");

        WarnUnassignedPrefabs(panel, report);

        var legacyCanvas = GameObject.Find("ResponseBag_ContentsCanvas");
        if (legacyCanvas != null && legacyCanvas != canvasGo)
        {
            Undo.RecordObject(legacyCanvas, "Hide legacy contents canvas");
            legacyCanvas.SetActive(false);
            report.AppendLine("Hid legacy ResponseBag_ContentsCanvas.");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeObject = canvasGo;
        Debug.Log(report.ToString());
    }

    static void EnsureLayers(StringBuilder report)
    {
        EnsureLayer(ResponseBagGazeTarget.GazeLayerName, report);
        EnsureLayer(ResponseBagItemPreviewController.PreviewLayerName, report);
    }

    static void EnsureLayer(string layerName, StringBuilder report)
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
            report.AppendLine("Created layer: " + layerName + " (index " + i + ")");
            return;
        }

        report.AppendLine("WARNING: Could not create layer " + layerName + " — TagManager full.");
    }

    static GameObject EnsureGazeTarget(GameObject bag, int gazeLayer, StringBuilder report)
    {
        var existing = bag.transform.Find(GazeTargetName);
        GameObject go;
        if (existing != null)
        {
            go = existing.gameObject;
            report.AppendLine("Reused ResponseBag_GazeTarget.");
        }
        else
        {
            go = new GameObject(GazeTargetName);
            Undo.RegisterCreatedObjectUndo(go, "Create GazeTarget");
            Undo.SetTransformParent(go.transform, bag.transform, "Parent GazeTarget");
            go.transform.localPosition = new Vector3(0f, 0.35f, 0f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            report.AppendLine("Created ResponseBag_GazeTarget.");
        }

        if (gazeLayer >= 0)
            go.layer = gazeLayer;

        var box = go.GetComponent<BoxCollider>();
        if (box == null)
            box = Undo.AddComponent<BoxCollider>(go);
        box.isTrigger = true;
        box.size = new Vector3(0.55f, 0.7f, 0.45f);
        box.center = Vector3.zero;

        if (go.GetComponent<ResponseBagGazeTarget>() == null)
            Undo.AddComponent<ResponseBagGazeTarget>(go);

        return go;
    }

    static ResponseBagCameraGazeDetector EnsureGazeDetector(Transform system, int gazeLayer, StringBuilder report)
    {
        var existing = Object.FindAnyObjectByType<ResponseBagCameraGazeDetector>(FindObjectsInactive.Include);
        if (existing != null)
        {
            report.AppendLine("Reused ResponseBagCameraGazeDetector.");
            return existing;
        }

        var go = new GameObject(DetectorName);
        Undo.RegisterCreatedObjectUndo(go, "Create GazeDetector");
        Undo.SetTransformParent(go.transform, system, "Parent GazeDetector");
        var detector = Undo.AddComponent<ResponseBagCameraGazeDetector>(go);

        var mask = gazeLayer >= 0 ? (1 << gazeLayer) : Physics.DefaultRaycastLayers;
        Camera cam = null;
        var origin = Object.FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
        if (origin != null)
            cam = origin.Camera;
        if (cam == null)
            cam = Camera.main;

        detector.EditorWire(cam, mask, 3f);
        report.AppendLine("Created camera-center gaze detector (~20 Hz RaycastNonAlloc).");
        return detector;
    }

    static Transform EnsureAnchor(Transform system, Transform bag, StringBuilder report)
    {
        var existing = system.Find(AnchorName);
        Transform anchor;
        if (existing != null)
        {
            anchor = existing;
            report.AppendLine("Reused ResponseBagContentsUIAnchor.");
        }
        else
        {
            var go = new GameObject(AnchorName);
            Undo.RegisterCreatedObjectUndo(go, "Create UI Anchor");
            Undo.SetTransformParent(go.transform, system, "Parent UI Anchor");
            anchor = go.transform;
            report.AppendLine("Created ResponseBagContentsUIAnchor.");
        }

        // Near bag spawn, but parented under PPE_System (stable).
        anchor.position = bag.position + new Vector3(0f, 1.1f, -0.35f);
        anchor.rotation = Quaternion.identity;
        return anchor;
    }

    static GameObject EnsureCanvas(Transform system, Transform anchor, StringBuilder report)
    {
        var existing = GameObject.Find(CanvasName);
        if (existing == null)
        {
            foreach (var t in system.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == CanvasName)
                {
                    existing = t.gameObject;
                    break;
                }
            }
        }

        if (existing != null)
        {
            report.AppendLine("Reused ResponseBagContentsCanvas.");
            return existing;
        }

        var go = new GameObject(CanvasName);
        Undo.RegisterCreatedObjectUndo(go, "Create Contents Canvas");
        Undo.SetTransformParent(go.transform, system, "Parent Contents Canvas");
        go.transform.position = anchor.position;
        go.transform.rotation = anchor.rotation;
        go.transform.localScale = Vector3.one * 0.001f;

        var canvas = Undo.AddComponent<Canvas>(go);
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(1100f, 700f);

        var scaler = Undo.AddComponent<CanvasScaler>(go);
        scaler.dynamicPixelsPerUnit = 10f;
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

        Undo.AddComponent<GraphicRaycaster>(go);
        Undo.AddComponent<TrackedDeviceGraphicRaycaster>(go);

        report.AppendLine("Created ResponseBagContentsCanvas (World Space).");
        return go;
    }

    static ResponseBagContentsPanel EnsurePanelComponent(GameObject canvasGo, StringBuilder report)
    {
        var panel = canvasGo.GetComponent<ResponseBagContentsPanel>();
        if (panel == null)
            panel = Undo.AddComponent<ResponseBagContentsPanel>(canvasGo);
        report.AppendLine("Ensured ResponseBagContentsPanel.");
        return panel;
    }

    static ResponseBagItemPreviewController EnsurePreviewStage(Transform system, int previewLayer, StringBuilder report)
    {
        var existing = Object.FindAnyObjectByType<ResponseBagItemPreviewController>(FindObjectsInactive.Include);
        Transform stageTf = null;
        if (existing != null)
        {
            stageTf = existing.transform;
            report.AppendLine("Reused ResponseBagPreviewStage.");
        }
        else
        {
            var stage = new GameObject(PreviewStageName);
            Undo.RegisterCreatedObjectUndo(stage, "Create Preview Stage");
            Undo.SetTransformParent(stage.transform, system, "Parent Preview Stage");
            stage.transform.position = new Vector3(50f, 50f, 50f);
            stageTf = stage.transform;
            existing = Undo.AddComponent<ResponseBagItemPreviewController>(stage);
            report.AppendLine("Created ResponseBagPreviewStage.");
        }

        var camTf = stageTf.Find("PreviewCamera");
        Camera cam;
        if (camTf == null)
        {
            var camGo = new GameObject("PreviewCamera");
            Undo.RegisterCreatedObjectUndo(camGo, "Create PreviewCamera");
            Undo.SetTransformParent(camGo.transform, stageTf, "Parent PreviewCamera");
            camGo.transform.localPosition = new Vector3(0f, 0f, -1.2f);
            cam = Undo.AddComponent<Camera>(camGo);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.1f, 0.14f, 1f);
            cam.fieldOfView = 35f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 10f;
            cam.enabled = false;
        }
        else
        {
            cam = camTf.GetComponent<Camera>();
        }

        if (previewLayer >= 0 && cam != null)
            cam.cullingMask = 1 << previewLayer;

        var lightTf = stageTf.Find("PreviewLight");
        Light light;
        if (lightTf == null)
        {
            var lightGo = new GameObject("PreviewLight");
            Undo.RegisterCreatedObjectUndo(lightGo, "Create PreviewLight");
            Undo.SetTransformParent(lightGo.transform, stageTf, "Parent PreviewLight");
            lightGo.transform.localRotation = Quaternion.Euler(35f, -30f, 0f);
            light = Undo.AddComponent<Light>(lightGo);
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.enabled = false;
        }
        else
        {
            light = lightTf.GetComponent<Light>();
        }

        var rootTf = stageTf.Find("PreviewObjectRoot");
        if (rootTf == null)
        {
            var rootGo = new GameObject("PreviewObjectRoot");
            Undo.RegisterCreatedObjectUndo(rootGo, "Create PreviewObjectRoot");
            Undo.SetTransformParent(rootGo.transform, stageTf, "Parent PreviewObjectRoot");
            rootTf = rootGo.transform;
        }

        existing.EditorWire(rootTf, cam, light, null);
        return existing;
    }

    static void WireUi(
        GameObject canvasGo,
        ResponseBagContentsPanel panel,
        ResponseBagItemPreviewController preview,
        ResponseEquipmentManager manager,
        GameObject bag,
        GameObject gazeTarget,
        StringBuilder report)
    {
        var font = Resources.Load<TMP_FontAsset>(HangulFontResource);
        var root = EnsureChild(canvasGo.transform, "PanelRoot");
        var rootRt = root.GetComponent<RectTransform>() ?? Undo.AddComponent<RectTransform>(root.gameObject);
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = Vector2.zero;
        rootRt.offsetMax = Vector2.zero;

        var bg = root.GetComponent<Image>() ?? Undo.AddComponent<Image>(root.gameObject);
        bg.color = new Color(0.12f, 0.16f, 0.22f, 0.96f);

        var title = EnsureTmp(root, "Title", new Vector2(0f, 300f), new Vector2(1000f, 60f), 40,
            "\uD654\uD559\uBB3C\uC9C8 \uB204\uCD9C \uB300\uC751\uAC00\uBC29", font);
        var subtitle = EnsureTmp(root, "Subtitle", new Vector2(0f, 250f), new Vector2(1000f, 40f), 26,
            "\uAC00\uBC29 \uB0B4\uBD80 \uC7A5\uBE44\uB97C \uD655\uC778\uD558\uC2ED\uC2DC\uC624.", font);

        var left = EnsureChild(root, "LeftList");
        var leftRt = left.GetComponent<RectTransform>();
        leftRt.anchoredPosition = new Vector2(-280f, 20f);
        leftRt.sizeDelta = new Vector2(420f, 420f);

        var right = EnsureChild(root, "RightDetail");
        var rightRt = right.GetComponent<RectTransform>();
        rightRt.anchoredPosition = new Vector2(280f, 40f);
        rightRt.sizeDelta = new Vector2(480f, 420f);

        var previewImageGo = EnsureChild(right, "PreviewImage");
        var previewRt = previewImageGo.GetComponent<RectTransform>();
        previewRt.anchoredPosition = new Vector2(0f, 80f);
        previewRt.sizeDelta = new Vector2(360f, 360f);
        var raw = previewImageGo.GetComponent<RawImage>() ?? Undo.AddComponent<RawImage>(previewImageGo.gameObject);
        raw.color = Color.white;

        var nameTmp = EnsureTmp(right, "SelectedName", new Vector2(0f, -130f), new Vector2(460f, 40f), 30, "", font);
        var descTmp = EnsureTmp(right, "SelectedDesc", new Vector2(0f, -200f), new Vector2(460f, 90f), 22, "", font);

        var progress = EnsureTmp(root, "Progress", new Vector2(0f, -260f), new Vector2(500f, 36f), 28,
            "\uD655\uC778 \uC644\uB8CC 0 / 3", font);

        var completeGo = EnsureChild(root, "CompleteButton");
        var completeRt = completeGo.GetComponent<RectTransform>();
        completeRt.anchoredPosition = new Vector2(160f, -310f);
        completeRt.sizeDelta = new Vector2(280f, 64f);
        var completeImg = completeGo.GetComponent<Image>() ?? Undo.AddComponent<Image>(completeGo.gameObject);
        completeImg.color = new Color(0.2f, 0.55f, 0.32f, 1f);
        var completeBtn = completeGo.GetComponent<Button>() ?? Undo.AddComponent<Button>(completeGo.gameObject);
        var completeLabel = EnsureTmp(completeGo, "Label", Vector2.zero, new Vector2(260f, 56f), 30,
            "\uC900\uBE44 \uC644\uB8CC", font);

        var closeGo = EnsureChild(root, "CloseButton");
        var closeRt = closeGo.GetComponent<RectTransform>();
        closeRt.anchoredPosition = new Vector2(500f, 310f);
        closeRt.sizeDelta = new Vector2(56f, 56f);
        var closeImg = closeGo.GetComponent<Image>() ?? Undo.AddComponent<Image>(closeGo.gameObject);
        closeImg.color = new Color(0.35f, 0.2f, 0.2f, 1f);
        var closeBtn = closeGo.GetComponent<Button>() ?? Undo.AddComponent<Button>(closeGo.gameObject);
        EnsureTmp(closeGo, "X", Vector2.zero, new Vector2(50f, 50f), 34, "X", font);

        var defaultEntries = CreateEntriesWithOptionalPrefabs(report);
        var rows = new ResponseBagContentsPanel.ContentRowUi[3];
        for (var i = 0; i < 3; i++)
        {
            var rowGo = EnsureChild(left, "Row_" + i);
            var rowRt = rowGo.GetComponent<RectTransform>();
            rowRt.anchoredPosition = new Vector2(0f, 150f - i * 110f);
            rowRt.sizeDelta = new Vector2(400f, 96f);
            var rowBg = rowGo.GetComponent<Image>() ?? Undo.AddComponent<Image>(rowGo.gameObject);
            rowBg.color = new Color(0.18f, 0.22f, 0.28f, 0.92f);
            var rowBtn = rowGo.GetComponent<Button>() ?? Undo.AddComponent<Button>(rowGo.gameObject);

            var label = EnsureTmp(rowGo, "Label", new Vector2(-20f, 0f), new Vector2(300f, 80f), 26,
                defaultEntries[i].KoreanDisplayName, font);
            var check = EnsureTmp(rowGo, "Check", new Vector2(160f, 0f), new Vector2(50f, 50f), 32, "\u25A1", font);

            var highlightGo = EnsureChild(rowGo, "Highlight");
            var highlightRt = highlightGo.GetComponent<RectTransform>();
            highlightRt.anchorMin = Vector2.zero;
            highlightRt.anchorMax = Vector2.one;
            highlightRt.offsetMin = Vector2.zero;
            highlightRt.offsetMax = Vector2.zero;
            var highlight = highlightGo.GetComponent<Image>() ?? Undo.AddComponent<Image>(highlightGo.gameObject);
            highlight.color = new Color(0.95f, 0.82f, 0.2f, 0.25f);
            highlight.enabled = false;
            highlight.raycastTarget = false;

            rows[i] = new ResponseBagContentsPanel.ContentRowUi
            {
                itemId = defaultEntries[i].ItemId,
                rowButton = rowBtn,
                background = rowBg,
                labelText = label,
                checkText = check,
                highlight = highlight,
            };
        }

        preview.EditorWire(
            preview.transform.Find("PreviewObjectRoot"),
            preview.GetComponentInChildren<Camera>(true),
            preview.GetComponentInChildren<Light>(true),
            raw);

        panel.EditorWire(
            manager,
            root.gameObject,
            rows,
            completeBtn,
            closeBtn,
            completeLabel,
            nameTmp,
            descTmp,
            progress,
            raw,
            preview,
            defaultEntries);

        var gaze = gazeTarget.GetComponent<ResponseBagGazeTarget>();
        if (gaze != null)
            gaze.EditorWire(manager, panel, bag);

        EditorUtility.SetDirty(panel);
        EditorUtility.SetDirty(preview);
        EditorUtility.SetDirty(canvasGo);
        report.AppendLine("Wired contents panel UI + gaze target + preview.");
    }

    static ResponseBagContentEntry[] CreateEntriesWithOptionalPrefabs(StringBuilder report)
    {
        var gas = AssetDatabase.LoadAssetAtPath<GameObject>(GasDetectorFbx);
        var sock = AssetDatabase.LoadAssetAtPath<GameObject>(SockFbx);
        var pad = AssetDatabase.LoadAssetAtPath<GameObject>(PadFbx);

        if (gas == null)
            report.AppendLine("MANUAL: Assign PreviewPrefab GasDetector ← ENV_HClDetector_01.fbx");
        if (sock == null)
            report.AppendLine("MANUAL: Assign PreviewPrefab AbsorbentSock ← INT_AbsorbentSock_Straight_01.fbx");
        if (pad == null)
            report.AppendLine("MANUAL: Assign PreviewPrefab AbsorbentPad ← INT_AbsorbentPad_01.fbx");

        return new[]
        {
            new ResponseBagContentEntry
            {
                ItemId = ResponseEquipmentManager.IdGasDetector,
                KoreanDisplayName = "\uD734\uB300\uC6A9 HCl \uAC00\uC2A4\uCE21\uC815\uAE30",
                KoreanDescription =
                    "\uC8FC\uBCC0 HCl \uB18D\uB3C4\uB97C \uCE21\uC815\uD558\uC5EC \uD604\uC7A5 \uC811\uADFC \uAC00\uB2A5 \uC5EC\uBD80\uC640 \uC791\uC5C5 \uC911 \uB300\uAE30 \uC0C1\uD0DC\uB97C \uD655\uC778\uD569\uB2C8\uB2E4.",
                PreviewPrefab = gas,
            },
            new ResponseBagContentEntry
            {
                ItemId = ResponseEquipmentManager.IdAbsorbentSock,
                KoreanDisplayName = "\uCF00\uBBF8\uCEEC \uD761\uCC29\uC0AD",
                KoreanDescription =
                    "\uB204\uCD9C\uC561\uC758 \uD655\uC0B0 \uACBD\uACC4\uB97C \uB9CC\uB4E4\uACE0 \uBC30\uC218\uAD6C \uB610\uB294 \uBE44\uC624\uC5FC \uAD6C\uC5ED\uC73C\uB85C \uBC88\uC9C0\uB294 \uAC83\uC744 \uBC29\uC9C0\uD569\uB2C8\uB2E4.",
                PreviewPrefab = sock,
            },
            new ResponseBagContentEntry
            {
                ItemId = ResponseEquipmentManager.IdAbsorbentPad,
                KoreanDisplayName = "\uD654\uD559\uBB3C\uC9C8\uC6A9 \uD761\uCC29\uD328\uB4DC",
                KoreanDescription =
                    "\uD761\uCC29\uC0AD \uB0B4\uBD80\uC5D0 \uB0A8\uC544 \uC788\uB294 \uB204\uCD9C\uC561\uC744 \uD761\uC218\uD558\uACE0 \uC794\uB958 \uC624\uC5FC\uC744 \uC81C\uAC70\uD558\uB294 \uB370 \uC0AC\uC6A9\uD569\uB2C8\uB2E4.",
                PreviewPrefab = pad,
            },
        };
    }

    static void WarnUnassignedPrefabs(ResponseBagContentsPanel panel, StringBuilder report)
    {
        var entries = panel.Entries;
        if (entries == null)
            return;
        for (var i = 0; i < entries.Length; i++)
        {
            if (entries[i] == null)
                continue;
            if (entries[i].PreviewPrefab == null)
                report.AppendLine("UNASSIGNED PreviewPrefab: " + entries[i].ItemId);
        }
    }

    static Transform EnsureChild(Transform parent, string name)
    {
        var existing = parent.Find(name);
        if (existing != null)
            return existing;

        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        Undo.SetTransformParent(go.transform, parent, "Parent " + name);
        var rt = go.GetComponent<RectTransform>();
        rt.localScale = Vector3.one;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        return go.transform;
    }

    static TextMeshProUGUI EnsureTmp(
        Transform parent,
        string name,
        Vector2 anchored,
        Vector2 size,
        float fontSize,
        string text,
        TMP_FontAsset font)
    {
        var t = EnsureChild(parent, name);
        var rt = t.GetComponent<RectTransform>();
        rt.anchoredPosition = anchored;
        rt.sizeDelta = size;
        var tmp = t.GetComponent<TextMeshProUGUI>() ?? Undo.AddComponent<TextMeshProUGUI>(t.gameObject);
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        if (font != null)
        {
            tmp.font = font;
            if (font.material != null)
                tmp.fontSharedMaterial = font.material;
        }

        return tmp;
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
