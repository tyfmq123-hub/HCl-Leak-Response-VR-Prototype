using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Tools > PPE > Setup Wearable Response Bag Visual States
/// Organizes closed/open visuals and contents under the existing wearable bag.
/// Does not auto-save or InitializeOnLoad mutate the scene.
/// </summary>
public static class WearableResponseBagVisualStatesSetup
{
    const string WearableName = "ResponseBag_Wearable";
    const string ClosedName = "BagClosedVisual";
    const string OpenName = "BagOpenVisual";
    const string SharedName = "BagSharedFrame";
    const string ContentsName = "BagContentsRoot";
    const string InteractableName = "BagOpenInteractable";
    const string GasSlot = "GasDetectorSlot";
    const string SockSlot = "AbsorbentSockSlot";
    const string PadSlot = "AbsorbentPadSlot";
    const string ExactGas = "ENV_HClDetector_01";
    const string ExactSock = "INT_AbsorbentSock_Straight_01";
    const string ExactPad = "INT_AbsorbentPad_01";
    const string ZipperGroup = "Bag_ZipperPanel_Group__Toggle";
    const string ZipperMovable = "Bag_ZipperPanel_Movable";
    const string BodyStatic = "Bag_Body_Static";

    static readonly Vector3 DefaultClosedCoverPos = new Vector3(0.001f, -0.149f, 0.209f);
    static readonly Vector3 DefaultClosedCoverEuler = Vector3.zero;
    static readonly Vector3 DefaultOpenCoverPos = new Vector3(0.001f, -0.26f, 0.08f);
    static readonly Vector3 DefaultOpenCoverEuler = new Vector3(62f, 0f, 0f);

    static readonly Vector3 GasLocalPos = new Vector3(0.07f, 0.03f, 0.02f);
    static readonly Vector3 GasLocalEuler = new Vector3(-12f, 200f, 0f);
    const float GasScale = 0.12f;

    static readonly Vector3 SockLocalPos = new Vector3(0f, -0.02f, 0.04f);
    static readonly Vector3 SockLocalEuler = new Vector3(5f, 90f, 0f);
    const float SockScale = 0.22f;

    static readonly Vector3 PadLocalPos = new Vector3(-0.07f, 0.02f, -0.01f);
    static readonly Vector3 PadLocalEuler = new Vector3(80f, 0f, 8f);
    const float PadScale = 0.16f;

    public static void SetupVisualStates()
    {
        var report = new StringBuilder();
        report.AppendLine("=== Setup Wearable Response Bag Visual States ===");

        var wearableVisual = Object.FindAnyObjectByType<ResponseBagWearableVisual>(FindObjectsInactive.Include);
        if (wearableVisual == null)
        {
            Debug.LogError(
                "[Wearable Visual States] ResponseBagWearableVisual missing. " +
                "Run Tools > PPE > Setup Wearable Response Bag first.");
            return;
        }

        var wearable = wearableVisual.WearableRoot;
        report.AppendLine("Wearable root: " + GetPath(wearable.transform));

        // Temporarily activate to allow child searches / component adds.
        var wasActive = wearable.activeSelf;
        if (!wasActive)
            wearable.SetActive(true);

        var bagVisual = wearable.transform.Find("BagVisual");
        if (bagVisual == null)
            bagVisual = FindDeep(wearable.transform, "BagVisual");

        var shared = EnsureChild(wearable.transform, SharedName, report);
        var closed = EnsureChild(wearable.transform, ClosedName, report);
        var open = EnsureChild(wearable.transform, OpenName, report);
        var contents = EnsureChild(open, ContentsName, report);
        var openHandle = EnsureChild(wearable.transform, InteractableName, report);

        // Prefer keeping bag mesh under shared frame.
        if (bagVisual != null && bagVisual.parent != shared)
        {
            Undo.SetTransformParent(bagVisual, shared, "Parent BagVisual under BagSharedFrame");
            report.AppendLine("Moved BagVisual → BagSharedFrame.");
        }

        var body = FindDeep(wearable.transform, BodyStatic);
        var zipperGroup = FindDeep(wearable.transform, ZipperGroup);
        var zipperMovable = FindDeep(wearable.transform, ZipperMovable);
        Transform frontCover = zipperMovable != null ? zipperMovable : zipperGroup;

        if (body == null)
            report.AppendLine("WARNING: Bag_Body_Static not found — assign BagBackAndFrame manually.");
        else
            report.AppendLine("BagBackAndFrame: " + GetPath(body));

        if (frontCover == null)
            report.AppendLine("WARNING: Zipper / front cover not found — assign BagFrontCover in Inspector.");
        else
            report.AppendLine("BagFrontCover: " + GetPath(frontCover) + " (assign/confirm in Inspector)");

        // Cache closed cover pose from current transform when available.
        Vector3 closedPos = DefaultClosedCoverPos;
        Vector3 closedEuler = DefaultClosedCoverEuler;
        if (frontCover != null)
        {
            closedPos = frontCover.localPosition;
            closedEuler = frontCover.localEulerAngles;
            report.AppendLine(
                $"Captured closed cover localPos={closedPos} localEuler={closedEuler}");
        }

        EnsureContentsSlots(contents, report);
        var hoverItems = WireContentHover(contents.gameObject, report);

        var interactable = EnsureOpenInteractable(openHandle.gameObject, report);
        var outline = openHandle.GetComponent<PPEInteractableOutline>();
        var box = openHandle.GetComponent<BoxCollider>();
        FitOpenHandleCollider(box, shared, body);

        var subtitle = Object.FindAnyObjectByType<ResponseBagItemNameLabel>(FindObjectsInactive.Include);
        if (subtitle == null)
        {
            subtitle = CreateWearableSubtitle(wearable.transform, report);
            report.AppendLine("Created ResponseBagItemNameLabel for wearable open/close.");
        }
        else
            report.AppendLine("Shared subtitle: " + GetPath(subtitle.transform));

        var controller = wearable.GetComponent<WearableResponseBagVisualController>();
        if (controller == null)
            controller = Undo.AddComponent<WearableResponseBagVisualController>(wearable);

        controller.EditorWire(
            wearable,
            closed.gameObject,
            open.gameObject,
            frontCover,
            contents.gameObject,
            interactable,
            box,
            outline,
            subtitle,
            hoverItems,
            closedPos,
            closedEuler,
            DefaultOpenCoverPos,
            DefaultOpenCoverEuler);
        EditorUtility.SetDirty(controller);

        wearableVisual.EditorSetVisualController(controller);
        EditorUtility.SetDirty(wearableVisual);

        // Start closed
        closed.gameObject.SetActive(true);
        open.gameObject.SetActive(false);
        contents.gameObject.SetActive(false);
        if (frontCover != null)
        {
            Undo.RecordObject(frontCover, "Set closed cover pose");
            frontCover.localPosition = closedPos;
            frontCover.localRotation = Quaternion.Euler(closedEuler);
        }

        if (!wasActive)
            wearable.SetActive(false);

        EditorUtility.SetDirty(wearable);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Selection.activeGameObject = wearable;

        report.AppendLine();
        report.AppendLine("Scene marked dirty but NOT auto-saved. Save manually if desired.");
        report.AppendLine("MANUAL CHECK: confirm BagFrontCover + three content meshes in Inspector.");
        Debug.Log(report.ToString());
    }

    static void EnsureContentsSlots(Transform contentsRoot, StringBuilder report)
    {
        var gas = EnsureSlot(contentsRoot, GasSlot, ExactGas, GasLocalPos, GasLocalEuler, GasScale, report);
        var sock = EnsureSlot(contentsRoot, SockSlot, ExactSock, SockLocalPos, SockLocalEuler, SockScale, report);
        var pad = EnsureSlot(contentsRoot, PadSlot, ExactPad, PadLocalPos, PadLocalEuler, PadScale, report);

        // Prefer cloning meshes from world bag if wearable slots are empty.
        TryCopyModelFromWorld(gas, ExactGas, report);
        TryCopyModelFromWorld(sock, ExactSock, report);
        TryCopyModelFromWorld(pad, ExactPad, report);
    }

    static Transform EnsureSlot(
        Transform contentsRoot,
        string slotName,
        string modelName,
        Vector3 localPos,
        Vector3 localEuler,
        float uniformScale,
        StringBuilder report)
    {
        var existing = contentsRoot.Find(slotName);
        Transform slot;
        if (existing != null)
        {
            slot = existing;
            report.AppendLine("Reused slot " + slotName);
        }
        else
        {
            var go = new GameObject(slotName);
            Undo.RegisterCreatedObjectUndo(go, "Create " + slotName);
            slot = go.transform;
            slot.SetParent(contentsRoot, false);
            report.AppendLine("Created slot " + slotName);
        }

        // Only apply default pose when the slot was just created or has identity defaults.
        // Do not overwrite carefully tuned poses in later setup runs if scale already customized.
        if (slot.childCount == 0 || ApproximatelyDefault(slot))
        {
            Undo.RecordObject(slot, "Slot default pose");
            slot.localPosition = localPos;
            slot.localRotation = Quaternion.Euler(localEuler);
            slot.localScale = Vector3.one * uniformScale;
        }

        report.AppendLine(
            $"  {slotName}: localPos={slot.localPosition} euler={slot.localEulerAngles} scale={slot.localScale}");
        return slot;
    }

    static bool ApproximatelyDefault(Transform slot)
    {
        return slot.localPosition.sqrMagnitude < 0.0001f
            && slot.localScale == Vector3.one;
    }

    static void TryCopyModelFromWorld(Transform slot, string exactModelName, StringBuilder report)
    {
        if (slot == null)
            return;

        for (var i = 0; i < slot.childCount; i++)
        {
            if (slot.GetChild(i).name.Contains(exactModelName.Split('_')[0])
                || slot.GetChild(i).name == exactModelName)
            {
                report.AppendLine("  Keeping existing model under " + slot.name);
                return;
            }
        }

        var world = FindWorldModel(exactModelName);
        if (world == null)
        {
            report.AppendLine(
                "WARNING: World model '" + exactModelName + "' not found — assign under " + slot.name + " manually.");
            return;
        }

        var clone = Object.Instantiate(world.gameObject, slot, false);
        Undo.RegisterCreatedObjectUndo(clone, "Clone wearable content " + exactModelName);
        clone.name = exactModelName;
        clone.transform.localPosition = Vector3.zero;
        clone.transform.localRotation = Quaternion.identity;
        clone.transform.localScale = Vector3.one;
        StripWorldOnlyComponents(clone);
        report.AppendLine("  Cloned " + exactModelName + " into " + slot.name);
    }

    static Transform FindWorldModel(string exactName)
    {
        var manager = Object.FindAnyObjectByType<ResponseEquipmentManager>(FindObjectsInactive.Include);
        if (manager != null && manager.ResponseBagRoot != null)
        {
            var found = FindExact(manager.ResponseBagRoot.transform, exactName);
            if (found != null)
                return found;
        }

        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name.IndexOf(WearableName, System.StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            var found = FindExact(root.transform, exactName);
            if (found != null
                && found.GetComponentInParent<ResponseBagWearableVisual>(true) == null)
                return found;
        }

        return null;
    }

    static void StripWorldOnlyComponents(GameObject root)
    {
        DestroyAll<ResponseBagPrepareInteractable>(root);
        DestroyAll<ResponseBagInteractable>(root);
        DestroyAll<PPEEquipable>(root);
        DestroyAll<PPEOutlineHighlight>(root);
        DestroyAll<ResponseBagContentsRevealController>(root);
        DestroyAll<Rigidbody>(root);
        // Leave mesh renderers; hover wiring adds collider/interactable on slot parent.
        var cols = root.GetComponentsInChildren<Collider>(true);
        for (var i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null)
                Undo.DestroyObjectImmediate(cols[i]);
        }
    }

    static void DestroyAll<T>(GameObject root) where T : Component
    {
        var comps = root.GetComponentsInChildren<T>(true);
        for (var i = 0; i < comps.Length; i++)
        {
            if (comps[i] != null)
                Undo.DestroyObjectImmediate(comps[i]);
        }
    }

    static ResponseBagContentHoverItem[] WireContentHover(GameObject contentsRoot, StringBuilder report)
    {
        var subtitle = Object.FindAnyObjectByType<ResponseBagItemNameLabel>(FindObjectsInactive.Include);
        var manager = Object.FindAnyObjectByType<ResponseEquipmentManager>(FindObjectsInactive.Include);

        var slots = new[]
        {
            (contentsRoot.transform.Find(GasSlot), ResponseEquipmentManager.IdGasDetector,
                "\uD734\uB300\uC6A9 HCl \uAC00\uC2A4\uCE21\uC815\uAE30"),
            (contentsRoot.transform.Find(SockSlot), ResponseEquipmentManager.IdAbsorbentSock,
                "\uCF00\uBBF8\uCEEC \uD761\uCC29\uC0AD"),
            (contentsRoot.transform.Find(PadSlot), ResponseEquipmentManager.IdAbsorbentPad,
                "\uD654\uD559\uBB3C\uC9C8\uC6A9 \uD761\uCC29\uD328\uB4DC"),
        };

        var list = new System.Collections.Generic.List<ResponseBagContentHoverItem>();
        for (var i = 0; i < slots.Length; i++)
        {
            var slot = slots[i].Item1;
            if (slot == null)
            {
                report.AppendLine("WARNING: Missing slot for content hover wiring.");
                continue;
            }

            var interactable = slot.GetComponent<XRSimpleInteractable>();
            if (interactable == null)
                interactable = Undo.AddComponent<XRSimpleInteractable>(slot.gameObject);

            var box = slot.GetComponent<BoxCollider>();
            if (box == null)
                box = Undo.AddComponent<BoxCollider>(slot.gameObject);
            FitSlotCollider(box, slot);

            interactable.colliders.Clear();
            interactable.colliders.Add(box);

            var outline = slot.GetComponent<PPEInteractableOutline>();
            if (outline == null)
                outline = Undo.AddComponent<PPEInteractableOutline>(slot.gameObject);
            outline.EditorSetHoverYellowRayOnly();

            var hover = slot.GetComponent<ResponseBagContentHoverItem>();
            if (hover == null)
                hover = Undo.AddComponent<ResponseBagContentHoverItem>(slot.gameObject);

            hover.EditorWire(
                slots[i].Item2,
                slots[i].Item3,
                slot,
                interactable,
                box,
                outline,
                manager,
                subtitle,
                null);
            hover.EditorSetWearableDisplayOnly(true);
            EditorUtility.SetDirty(hover);
            list.Add(hover);
            report.AppendLine("Wired hover on " + slot.name);
        }

        return list.ToArray();
    }

    static void FitSlotCollider(BoxCollider box, Transform slot)
    {
        var renderers = slot.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
        {
            box.center = Vector3.zero;
            box.size = Vector3.one * 0.12f;
            return;
        }

        var bounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        var lossy = slot.lossyScale;
        box.center = slot.InverseTransformPoint(bounds.center);
        box.size = new Vector3(
            SafeDiv(bounds.size.x, lossy.x),
            SafeDiv(bounds.size.y, lossy.y),
            SafeDiv(bounds.size.z, lossy.z));
        box.size = Vector3.Max(box.size, Vector3.one * 0.05f);
    }

    static XRSimpleInteractable EnsureOpenInteractable(GameObject handle, StringBuilder report)
    {
        var box = handle.GetComponent<BoxCollider>();
        if (box == null)
            box = Undo.AddComponent<BoxCollider>(handle);

        var xr = handle.GetComponent<XRSimpleInteractable>();
        if (xr == null)
        {
            xr = Undo.AddComponent<XRSimpleInteractable>(handle);
            report.AppendLine("Added XRSimpleInteractable on BagOpenInteractable.");
        }
        else
            report.AppendLine("Reused XRSimpleInteractable on BagOpenInteractable.");

        xr.colliders.Clear();
        xr.colliders.Add(box);

        var outline = handle.GetComponent<PPEInteractableOutline>();
        if (outline == null)
            outline = Undo.AddComponent<PPEInteractableOutline>(handle);
        outline.EditorSetHoverYellowRayOnly();

        // Remove grab / sticky PPE highlight if any were added by mistake.
        var grab = handle.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
        if (grab != null)
            Undo.DestroyObjectImmediate(grab);
        var sticky = handle.GetComponent<PPEOutlineHighlight>();
        if (sticky != null)
            Undo.DestroyObjectImmediate(sticky);

        return xr;
    }

    static void FitOpenHandleCollider(BoxCollider box, Transform shared, Transform body)
    {
        if (box == null)
            return;

        Bounds? bounds = null;
        if (body != null)
        {
            var renderers = body.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                if (bounds == null)
                    bounds = renderers[i].bounds;
                else
                {
                    var b = bounds.Value;
                    b.Encapsulate(renderers[i].bounds);
                    bounds = b;
                }
            }
        }
        else if (shared != null)
        {
            var renderers = shared.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                if (bounds == null)
                    bounds = renderers[i].bounds;
                else
                {
                    var b = bounds.Value;
                    b.Encapsulate(renderers[i].bounds);
                    bounds = b;
                }
            }
        }

        var t = box.transform;
        if (!bounds.HasValue)
        {
            box.center = new Vector3(0f, 0.05f, 0.05f);
            box.size = new Vector3(0.35f, 0.25f, 0.2f);
            return;
        }

        box.center = t.InverseTransformPoint(bounds.Value.center);
        var size = t.InverseTransformVector(bounds.Value.size);
        box.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
        // Slightly shrink so ray prefers bag flap over contents when both open.
        box.size *= 0.85f;
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

    static Transform FindDeep(Transform root, string exactName)
    {
        if (root == null)
            return null;
        if (root.name == exactName)
            return root;
        var all = root.GetComponentsInChildren<Transform>(true);
        for (var i = 0; i < all.Length; i++)
        {
            if (all[i] != root && all[i].name == exactName)
                return all[i];
        }

        return null;
    }

    static Transform FindExact(Transform root, string exactName)
    {
        return FindDeep(root, exactName);
    }

    static float SafeDiv(float a, float b)
    {
        var d = Mathf.Abs(b);
        return d < 0.0001f ? a : a / d;
    }

    static ResponseBagItemNameLabel CreateWearableSubtitle(Transform wearable, StringBuilder report)
    {
        var system = GameObject.Find("PPE_System");
        if (system == null)
        {
            system = new GameObject("PPE_System");
            Undo.RegisterCreatedObjectUndo(system, "Create PPE_System");
        }

        var canvasGo = new GameObject("ResponseBagItemNameCanvas");
        Undo.RegisterCreatedObjectUndo(canvasGo, "Create wearable subtitle");
        canvasGo.transform.SetParent(system.transform, false);
        canvasGo.transform.localScale = Vector3.one * 0.00115f;

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var cam = Camera.main;
        canvas.worldCamera = cam;

        var rt = canvasGo.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(520f, 72f);
        canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>().dynamicPixelsPerUnit = 10f;

        var bg = new GameObject("Background", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        Undo.RegisterCreatedObjectUndo(bg, "Create subtitle bg");
        bg.transform.SetParent(canvasGo.transform, false);
        var bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;
        bg.GetComponent<UnityEngine.UI.Image>().color = new Color(0.08f, 0.09f, 0.11f, 0.82f);

        var textGo = new GameObject("Label", typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(textGo, "Create subtitle label");
        textGo.transform.SetParent(bg.transform, false);
        var textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(12f, 8f);
        textRt.offsetMax = new Vector2(-12f, -8f);

        var tmp = textGo.AddComponent<TMPro.TextMeshProUGUI>();
        tmp.fontSize = 32f;
        tmp.alignment = TMPro.TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        var font = Resources.Load<TMPro.TMP_FontAsset>("PPE/PPE_HangulFont");
        if (font != null)
            tmp.font = font;

        var label = canvasGo.AddComponent<ResponseBagItemNameLabel>();
        label.EditorWire(canvas, bgRt, tmp, cam);
        canvasGo.SetActive(false);
        report.AppendLine("Subtitle parent path: PPE_System/ResponseBagItemNameCanvas (wearable=" + GetPath(wearable) + ")");
        return label;
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
