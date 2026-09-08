using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// One-shot editor arrange for response bag interior contents.
/// Tools > PPE > Arrange Response Bag Contents
/// Does not auto-run on Play / domain reload; does not save the scene.
/// </summary>
public static class ResponseBagContentsArrangeSetup
{
    const string ContentsRootName = "BagContentsRoot";
    const string GasSlotName = "GasDetectorSlot";
    const string SockSlotName = "AbsorbentSockSlot";
    const string PadSlotName = "AbsorbentPadSlot";

    const string ExactGasName = "ENV_HClDetector_01";
    const string ExactSockName = "INT_AbsorbentSock_Straight_01";
    // Prefer real pad. Cabinet asset is huge and caused ghost "흡착패드" subtitles.
    const string ExactPadPreferred = "INT_AbsorbentPad_01";
    const string ExactPadFallback = "ENV_PPECabinet_03";

    // Target max world-axis extent after fit (meters).
    const float GasTargetExtent = 0.13f;
    const float SockTargetExtent = 0.28f;
    const float PadTargetExtent = 0.20f;
    const float MaxColliderAxis = 0.45f;

    public static void ArrangeContents()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[Bag Contents Arrange] No active loaded scene.");
            return;
        }

        var report = new StringBuilder();
        report.AppendLine("=== Arrange Response Bag Contents ===");

        var manager = Object.FindAnyObjectByType<ResponseEquipmentManager>(FindObjectsInactive.Include);
        if (manager == null || manager.ResponseBagRoot == null)
        {
            Debug.LogError("[Bag Contents Arrange] ResponseEquipmentManager.responseBagRoot missing.");
            return;
        }

        var bag = manager.ResponseBagRoot;
        if (bag.GetComponentInParent<ResponseBagWearableVisual>(true) != null)
        {
            Debug.LogError("[Bag Contents Arrange] World bag points at wearable — abort.");
            return;
        }

        report.AppendLine("World bag: " + GetPath(bag.transform));

        var gas = FindExact(bag.transform, ExactGasName);
        var sock = FindExact(bag.transform, ExactSockName);
        var pad = FindExact(bag.transform, ExactPadPreferred);
        if (pad == null)
            pad = FindExact(bag.transform, ExactPadFallback);

        if (gas == null || sock == null || pad == null)
        {
            report.AppendLine("ERROR: Missing content models.");
            report.AppendLine("  Gas: " + (gas != null ? GetPath(gas) : "MISSING " + ExactGasName));
            report.AppendLine("  Sock: " + (sock != null ? GetPath(sock) : "MISSING " + ExactSockName));
            report.AppendLine("  Pad: " + (pad != null ? GetPath(pad) : "MISSING " + ExactPadPreferred + " / " + ExactPadFallback));
            Debug.LogError(report.ToString());
            return;
        }

        report.AppendLine("Gas model: " + GetPath(gas));
        report.AppendLine("Sock model: " + GetPath(sock));
        report.AppendLine("Pad model: " + GetPath(pad));

        // Ensure visible for bounds / collider fitting.
        ActivateHierarchy(gas.gameObject);
        ActivateHierarchy(sock.gameObject);
        ActivateHierarchy(pad.gameObject);

        var contentsRoot = EnsureChild(bag.transform, ContentsRootName, report);
        contentsRoot.localPosition = Vector3.zero;
        contentsRoot.localRotation = Quaternion.identity;
        contentsRoot.localScale = Vector3.one;

        var gasSlot = EnsureSlot(contentsRoot, GasSlotName, gas, report);
        var sockSlot = EnsureSlot(contentsRoot, SockSlotName, sock, report);
        var padSlot = EnsureSlot(contentsRoot, PadSlotName, pad, report);

        if (!TryGetBagInteriorBounds(bag.transform, gas, sock, pad, out var interior))
        {
            Debug.LogError("[Bag Contents Arrange] Could not compute Bag_Root bounds.");
            return;
        }

        report.AppendLine(
            $"Bag interior world center={interior.center} size={interior.size}");

        // Compact triangular layout inside the open bag (world offsets from interior).
        // Left / rear-top / bottom-right relative to bag local axes projected via bag root.
        var bagRoot = bag.transform.Find("Bag_Root");
        var axes = bagRoot != null ? bagRoot : bag.transform;
        var right = axes.right;
        var up = axes.up;
        var forward = axes.forward;

        // Prefer interior size for spacing; clamp to avoid protruding.
        var hx = Mathf.Clamp(interior.size.x * 0.22f, 0.04f, 0.12f);
        var hy = Mathf.Clamp(interior.size.y * 0.18f, 0.03f, 0.10f);
        var hz = Mathf.Clamp(interior.size.z * 0.18f, 0.03f, 0.10f);
        var center = interior.center + up * (interior.size.y * 0.05f);

        PlaceSlot(
            gasSlot,
            center - right * hx + up * (hy * 0.15f) + forward * (hz * 0.1f),
            // +180 yaw so the detector face/display points out of the bag opening.
            Quaternion.LookRotation(-forward + right * 0.35f, up) * Quaternion.Euler(-8f, 198f, 0f),
            gas,
            GasTargetExtent,
            report);

        PlaceSlot(
            sockSlot,
            center + up * (hy * 0.85f) - forward * hz,
            Quaternion.LookRotation(right, up) * Quaternion.Euler(0f, 0f, 12f),
            sock,
            SockTargetExtent,
            report);

        PlaceSlot(
            padSlot,
            center + right * hx - up * (hy * 0.55f) + forward * (hz * 0.15f),
            Quaternion.LookRotation(up, -forward) * Quaternion.Euler(0f, 0f, 0f),
            pad,
            PadTargetExtent,
            report);

        var managerReveal = bag.GetComponent<ResponseBagContentsRevealController>();
        if (managerReveal == null)
            managerReveal = bag.GetComponentInChildren<ResponseBagContentsRevealController>(true);

        var equipment = manager;
        var nameLabel = Object.FindAnyObjectByType<ResponseBagItemNameLabel>(FindObjectsInactive.Include);

        ConfigureHover(
            gasSlot,
            ResponseEquipmentManager.IdGasDetector,
            "\uD734\uB300\uC6A9 HCl \uAC00\uC2A4\uCE21\uC815\uAE30",
            gas,
            equipment,
            nameLabel,
            managerReveal,
            report);

        ConfigureHover(
            sockSlot,
            ResponseEquipmentManager.IdAbsorbentSock,
            "\uCF00\uBBF8\uCEEC \uD761\uCC29\uC0AD",
            sock,
            equipment,
            nameLabel,
            managerReveal,
            report);

        ConfigureHover(
            padSlot,
            ResponseEquipmentManager.IdAbsorbentPad,
            "\uD654\uD559\uBB3C\uC9C8\uC6A9 \uD761\uCC29\uD328\uB4DC",
            pad,
            equipment,
            nameLabel,
            managerReveal,
            report);

        RestrictBagColliders(bag, report);

        if (managerReveal != null)
        {
            managerReveal.EditorWire(
                bag,
                gasSlot.gameObject,
                sockSlot.gameObject,
                padSlot.gameObject,
                Camera.main,
                equipment,
                nameLabel,
                new[]
                {
                    gasSlot.GetComponent<ResponseBagContentHoverItem>(),
                    sockSlot.GetComponent<ResponseBagContentHoverItem>(),
                    padSlot.GetComponent<ResponseBagContentHoverItem>(),
                },
                0.4f,
                0.8f);
            EditorUtility.SetDirty(managerReveal);
        }

        report.AppendLine("--- Slot transforms (local under BagContentsRoot) ---");
        AppendSlotReport(gasSlot, report);
        AppendSlotReport(sockSlot, report);
        AppendSlotReport(padSlot, report);

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = contentsRoot.gameObject;
        Debug.Log(report.ToString());
    }

    static void PlaceSlot(
        Transform slot,
        Vector3 worldPos,
        Quaternion worldRot,
        Transform model,
        float targetMaxExtent,
        StringBuilder report)
    {
        Undo.RecordObject(slot, "Arrange slot pose");
        slot.position = worldPos;
        slot.rotation = worldRot;
        slot.localScale = Vector3.one;

        // Keep model at slot origin with identity local — visual size via slot uniform scale.
        Undo.RecordObject(model, "Zero model under slot");
        model.localPosition = Vector3.zero;
        model.localRotation = Quaternion.identity;
        model.localScale = Vector3.one;

        // Temporarily scale 1 to measure raw mesh size under slot orientation.
        var extent = ComputeLocalRendererExtent(model);
        var maxExtent = Mathf.Max(extent.x, extent.y, extent.z);
        if (maxExtent < 0.0001f)
            maxExtent = 1f;

        var uniform = Mathf.Clamp(targetMaxExtent / maxExtent, 0.02f, 8f);
        slot.localScale = Vector3.one * uniform;

        FitBoxCollider(slot, model);
        report.AppendLine(
            $"{slot.name}: worldPos={worldPos} euler={worldRot.eulerAngles} " +
            $"uniformScale={uniform:F3} rawExtent={extent} target={targetMaxExtent}");
    }

    static Transform EnsureSlot(Transform contentsRoot, string slotName, Transform model, StringBuilder report)
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

        if (model.parent != slot)
        {
            Undo.SetTransformParent(model, slot, "Parent under " + slotName);
            report.AppendLine("Reparented " + model.name + " → " + slotName);
        }

        return slot;
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
        report.AppendLine("Created " + name);
        return go.transform;
    }

    static void ConfigureHover(
        Transform slot,
        string itemId,
        string koreanName,
        Transform model,
        ResponseEquipmentManager manager,
        ResponseBagItemNameLabel nameLabel,
        ResponseBagContentsRevealController reveal,
        StringBuilder report)
    {
        var interactable = slot.GetComponent<XRSimpleInteractable>();
        if (interactable == null)
        {
            interactable = Undo.AddComponent<XRSimpleInteractable>(slot.gameObject);
            report.AppendLine("Added XRSimpleInteractable on " + slot.name);
        }
        else
        {
            report.AppendLine("Reused XRSimpleInteractable on " + slot.name);
        }

        var box = slot.GetComponent<BoxCollider>();
        if (box == null)
        {
            box = Undo.AddComponent<BoxCollider>(slot.gameObject);
            report.AppendLine("Added BoxCollider on " + slot.name);
        }

        FitBoxCollider(slot, model);

        var so = new SerializedObject(interactable);
        var colList = so.FindProperty("m_Colliders");
        colList.arraySize = 1;
        colList.GetArrayElementAtIndex(0).objectReferenceValue = box;
        so.ApplyModifiedPropertiesWithoutUndo();

        var outline = slot.GetComponent<PPEInteractableOutline>();
        if (outline == null)
        {
            outline = Undo.AddComponent<PPEInteractableOutline>(slot.gameObject);
            report.AppendLine("Added PPEInteractableOutline on " + slot.name);
        }

        outline.EditorSetHoverYellowRayOnly();
        EditorUtility.SetDirty(outline);

        var bad = slot.GetComponent<PPEOutlineHighlight>();
        if (bad != null)
            Undo.DestroyObjectImmediate(bad);

        var grab = slot.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
        if (grab != null)
            Undo.DestroyObjectImmediate(grab);

        var hover = slot.GetComponent<ResponseBagContentHoverItem>();
        if (hover == null)
        {
            hover = Undo.AddComponent<ResponseBagContentHoverItem>(slot.gameObject);
            report.AppendLine("Added ResponseBagContentHoverItem on " + slot.name);
        }
        else
        {
            report.AppendLine("Reused ResponseBagContentHoverItem on " + slot.name);
        }

        hover.EditorWire(
            itemId,
            koreanName,
            model,
            interactable,
            box,
            outline,
            manager,
            nameLabel,
            reveal);
        EditorUtility.SetDirty(hover);

        report.AppendLine(
            $"  Collider center={box.center} size={box.size}");
    }

    static void FitBoxCollider(Transform slot, Transform model)
    {
        var box = slot.GetComponent<BoxCollider>();
        if (box == null)
            return;

        Undo.RecordObject(box, "Fit slot collider");

        var renderers = model.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
        {
            box.center = Vector3.zero;
            box.size = Vector3.one * 0.12f;
            return;
        }

        Bounds? world = null;
        for (var i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null || !(r is MeshRenderer || r is SkinnedMeshRenderer))
                continue;
            if (!world.HasValue)
                world = r.bounds;
            else
            {
                var b = world.Value;
                b.Encapsulate(r.bounds);
                world = b;
            }
        }

        if (!world.HasValue)
        {
            box.center = Vector3.zero;
            box.size = Vector3.one * 0.12f;
            return;
        }

        var wb = world.Value;
        var lossy = slot.lossyScale;
        box.center = slot.InverseTransformPoint(wb.center);
        box.size = new Vector3(
            AbsDiv(wb.size.x, lossy.x),
            AbsDiv(wb.size.y, lossy.y),
            AbsDiv(wb.size.z, lossy.z));
        box.size = Vector3.Max(box.size, Vector3.one * 0.04f);
        var localMax = Mathf.Max(box.size.x, box.size.y, box.size.z);
        if (localMax > MaxColliderAxis)
            box.size *= MaxColliderAxis / localMax;
    }

    static Vector3 ComputeLocalRendererExtent(Transform model)
    {
        var renderers = model.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return Vector3.one * 0.1f;

        Bounds? world = null;
        for (var i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null || !(r is MeshRenderer || r is SkinnedMeshRenderer))
                continue;
            if (!world.HasValue)
                world = r.bounds;
            else
            {
                var b = world.Value;
                b.Encapsulate(r.bounds);
                world = b;
            }
        }

        return world?.size ?? Vector3.one * 0.1f;
    }

    static bool TryGetBagInteriorBounds(
        Transform bag,
        Transform gas,
        Transform sock,
        Transform pad,
        out Bounds bounds)
    {
        bounds = default;
        var bagRoot = bag.Find("Bag_Root");
        var searchRoot = bagRoot != null ? bagRoot : bag;
        var renderers = searchRoot.GetComponentsInChildren<Renderer>(true);
        var found = false;

        for (var i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null || !(r is MeshRenderer || r is SkinnedMeshRenderer))
                continue;

            // Ignore content meshes if they are somehow under Bag_Root.
            if (IsUnder(r.transform, gas) || IsUnder(r.transform, sock) || IsUnder(r.transform, pad))
                continue;

            if (!found)
            {
                bounds = r.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }

        return found;
    }

    static bool IsUnder(Transform t, Transform ancestor)
    {
        if (ancestor == null)
            return false;
        return t == ancestor || t.IsChildOf(ancestor);
    }

    static void RestrictBagColliders(GameObject bag, StringBuilder report)
    {
        var interactable = bag.GetComponent<XRSimpleInteractable>();
        var bagCol = bag.GetComponent<Collider>();
        if (interactable == null || bagCol == null)
            return;

        var so = new SerializedObject(interactable);
        var colList = so.FindProperty("m_Colliders");
        colList.arraySize = 1;
        colList.GetArrayElementAtIndex(0).objectReferenceValue = bagCol;
        so.ApplyModifiedPropertiesWithoutUndo();
        report.AppendLine("Bag XRSimpleInteractable locked to bag collider only.");
    }

    static void AppendSlotReport(Transform slot, StringBuilder report)
    {
        var box = slot.GetComponent<BoxCollider>();
        report.AppendLine(
            $"{slot.name}: localPos={slot.localPosition} localEuler={slot.localEulerAngles} " +
            $"localScale={slot.localScale}");
        if (box != null)
            report.AppendLine($"  BoxCollider center={box.center} size={box.size}");
    }

    static Transform FindExact(Transform root, string exactName)
    {
        for (var i = 0; i < root.childCount; i++)
        {
            if (root.GetChild(i).name == exactName)
                return root.GetChild(i);
        }

        var all = root.GetComponentsInChildren<Transform>(true);
        for (var i = 0; i < all.Length; i++)
        {
            if (all[i] != root && all[i].name == exactName)
                return all[i];
        }

        return null;
    }

    static void ActivateHierarchy(GameObject go)
    {
        if (go == null)
            return;
        Undo.RecordObject(go, "Activate content");
        go.SetActive(true);
        var t = go.transform;
        while (t != null)
        {
            if (!t.gameObject.activeSelf)
            {
                Undo.RecordObject(t.gameObject, "Activate parent");
                t.gameObject.SetActive(true);
            }

            t = t.parent;
        }
    }

    static float AbsDiv(float a, float b)
    {
        var d = Mathf.Abs(b);
        return d < 0.0001f ? a : a / d;
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
