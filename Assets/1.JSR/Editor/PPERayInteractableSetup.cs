using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Adds XRSimpleInteractable + colliders (if needed) + PPERayInteractionDebug
/// + PPEOutlineHighlight to top-level PPE roots in the active scene.
/// Extend targets via <see cref="PPERayInteractableCatalog.Targets"/>.
/// </summary>
public static class PPERayInteractableSetup
{
    const string XriInputActionsPath =
        "Assets/Samples/XR Interaction Toolkit/3.4.1/Starter Assets/XRI Default Input Actions.inputactions";
    const string OutlineMaterialPath = "Assets/1.JSR/Materials/PPE/PPE_Outline.mat";
    const string OutlineResourcesPath = "Assets/Resources/PPE_Outline.mat";

    public static void SetupRayInteractables()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[PPE Ray Interactables] No active loaded scene.");
            return;
        }

        var report = new StringBuilder();
        report.AppendLine("=== PPE Ray Interactables Setup ===");
        report.AppendLine($"Scene: {scene.name}");
        report.AppendLine($"Catalog targets: {PPERayInteractableCatalog.Targets.Length} (edit PPERayInteractableCatalog.Targets to add more later)");
        report.AppendLine();

        var changed = new List<string>();
        var already = new List<string>();
        var skipped = new List<string>();
        var collidersAdded = new List<string>();

        var roots = FindTopLevelPpeRoots();
        report.AppendLine($"Matched top-level PPE roots: {roots.Count}");

        EnsureSelectionManager(report);
        EnsurePreviewController(report);

        foreach (var root in roots)
        {
            var path = GetHierarchyPath(root.transform);
            PPERayInteractableCatalog.MatchesAnyTarget(root.name, out var entry);
            var linePrefix = $"[{entry.Id}] {path}";

            var objectChanges = new List<string>();

            EnsureUsableCollider(root, objectChanges, collidersAdded, path);
            EnsureSimpleInteractable(root, objectChanges);
            EnsureDebug(root, objectChanges);
            EnsureOutlineHighlight(root, objectChanges);

            if (objectChanges.Count == 0)
            {
                already.Add($"{linePrefix}: already configured");
            }
            else
            {
                changed.Add($"{linePrefix}: {string.Join("; ", objectChanges)}");
                EditorUtility.SetDirty(root);
            }
        }

        var selectWired = TryWireRayInteractorSelectInputs(report);

        if (changed.Count > 0 || selectWired)
            EditorSceneManager.MarkSceneDirty(scene);

        // Persist even when this pass only confirms existing components (editor may already be dirty).
        if (scene.isDirty && !string.IsNullOrEmpty(scene.path))
        {
            EditorSceneManager.SaveScene(scene);
            report.AppendLine($"Scene saved: {scene.path}");
        }

        report.AppendLine();
        report.AppendLine($"Changed objects: {changed.Count}");
        foreach (var c in changed)
            report.AppendLine("  + " + c);

        report.AppendLine($"Already configured: {already.Count}");
        foreach (var a in already)
            report.AppendLine("  · " + a);

        report.AppendLine($"Colliders added: {collidersAdded.Count}");
        foreach (var c in collidersAdded)
            report.AppendLine("  + collider " + c);

        if (skipped.Count > 0)
        {
            report.AppendLine($"Skipped: {skipped.Count}");
            foreach (var s in skipped)
                report.AppendLine("  - " + s);
        }

        report.AppendLine();
        report.AppendLine("Notes:");
        report.AppendLine("  - XRSimpleInteractable only (not grab). No Rigidbody added.");
        report.AppendLine("  - Hover outline via PPEOutlineHighlight (URP inverted-hull). Ray hover only.");
        report.AppendLine("  - Transforms / meshes / original PPE materials were not modified.");
        report.AppendLine("  - To register future PPE: add an Entry to PPERayInteractableCatalog.Targets, then re-run this menu.");

        Debug.Log(report.ToString());

        // Also write a disk report so agent tooling can verify without relying on Editor.log timing.
        try
        {
            var reportPath = "Assets/1.JSR/Editor/PPERayInteractableSetup.last-report.txt";
            System.IO.File.WriteAllText(reportPath, report.ToString());
            AssetDatabase.Refresh();
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[PPE Ray Interactables] Could not write disk report: {ex.Message}");
        }
    }

    static List<GameObject> FindTopLevelPpeRoots()
    {
        var results = new List<GameObject>();
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!IsTopLevelPpeRoot(t))
                    continue;
                results.Add(t.gameObject);
            }
        }

        return results;
    }

    static bool IsTopLevelPpeRoot(Transform t)
    {
        if (t == null)
            return false;

        if (PPERayInteractableCatalog.IsExcludedEnvironmentName(t.name))
            return false;

        if (!PPERayInteractableCatalog.MatchesAnyTarget(t.name, out _))
            return false;

        // Skip child meshes / parts when a parent already matches a PPE target.
        var p = t.parent;
        while (p != null)
        {
            if (!PPERayInteractableCatalog.IsExcludedEnvironmentName(p.name) &&
                PPERayInteractableCatalog.MatchesAnyTarget(p.name, out _))
                return false;
            p = p.parent;
        }

        return true;
    }

    static void EnsureUsableCollider(
        GameObject root,
        List<string> objectChanges,
        List<string> collidersAdded,
        string path)
    {
        if (HasUsableCollider(root))
            return;

        // Prefer repairing a root BoxCollider (e.g. disabled / trigger) before adding another.
        var box = root.GetComponent<BoxCollider>();
        if (box == null)
        {
            box = Undo.AddComponent<BoxCollider>(root);
            objectChanges.Add("added BoxCollider");
            collidersAdded.Add($"{path}: BoxCollider (fitted to renderer bounds)");
        }
        else
        {
            Undo.RecordObject(box, "Enable PPE BoxCollider");
            objectChanges.Add("enabled/refit root BoxCollider");
            collidersAdded.Add($"{path}: BoxCollider enabled + fitted to renderer bounds");
        }

        FitBoxColliderToRenderers(box, root);
        box.isTrigger = false;
        box.enabled = true;
    }

    static bool HasUsableCollider(GameObject root)
    {
        var colliders = root.GetComponentsInChildren<Collider>(true);
        for (var i = 0; i < colliders.Length; i++)
        {
            var c = colliders[i];
            if (c != null && c.enabled && !c.isTrigger)
                return true;
        }

        return false;
    }

    static void FitBoxColliderToRenderers(BoxCollider box, GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
        {
            box.center = Vector3.zero;
            box.size = Vector3.one * 0.25f;
            return;
        }

        var worldBounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                worldBounds.Encapsulate(renderers[i].bounds);
        }

        var t = root.transform;
        var corners = GetWorldCorners(worldBounds);
        var localMin = t.InverseTransformPoint(corners[0]);
        var localMax = localMin;
        for (var i = 1; i < corners.Length; i++)
        {
            var lp = t.InverseTransformPoint(corners[i]);
            localMin = Vector3.Min(localMin, lp);
            localMax = Vector3.Max(localMax, lp);
        }

        box.center = (localMin + localMax) * 0.5f;
        var size = localMax - localMin;
        // Avoid zero-thickness axes from flat/degenerate bounds.
        size.x = Mathf.Max(Mathf.Abs(size.x), 0.01f);
        size.y = Mathf.Max(Mathf.Abs(size.y), 0.01f);
        size.z = Mathf.Max(Mathf.Abs(size.z), 0.01f);

        // Skinned / cabinet meshes often inflate AABBs into room-sized ghost targets.
        const float MaxAxis = 0.55f;
        var maxAxis = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
        if (maxAxis > MaxAxis)
        {
            var shrink = MaxAxis / maxAxis;
            size *= shrink;
        }

        box.size = size;
    }

    static Vector3[] GetWorldCorners(Bounds bounds)
    {
        var c = bounds.center;
        var e = bounds.extents;
        return new[]
        {
            c + new Vector3(-e.x, -e.y, -e.z),
            c + new Vector3(-e.x, -e.y, e.z),
            c + new Vector3(-e.x, e.y, -e.z),
            c + new Vector3(-e.x, e.y, e.z),
            c + new Vector3(e.x, -e.y, -e.z),
            c + new Vector3(e.x, -e.y, e.z),
            c + new Vector3(e.x, e.y, -e.z),
            c + new Vector3(e.x, e.y, e.z),
        };
    }

    static void EnsureSimpleInteractable(GameObject root, List<string> objectChanges)
    {
        if (root.GetComponent<XRSimpleInteractable>() != null)
            return;

        // Prefer Simple over adding another interactable type if a non-simple already exists.
        if (root.GetComponent<XRBaseInteractable>() != null)
        {
            objectChanges.Add("skipped XRSimpleInteractable (other XRBaseInteractable already present)");
            return;
        }

        Undo.AddComponent<XRSimpleInteractable>(root);
        objectChanges.Add("added XRSimpleInteractable");
    }

    static void EnsureDebug(GameObject root, List<string> objectChanges)
    {
        if (root.GetComponent<PPERayInteractionDebug>() != null)
            return;

        if (root.GetComponent<XRBaseInteractable>() == null)
        {
            objectChanges.Add("skipped PPERayInteractionDebug (no interactable)");
            return;
        }

        Undo.AddComponent<PPERayInteractionDebug>(root);
        objectChanges.Add("added PPERayInteractionDebug");
    }

    /// <summary>
    /// Adds hover outline if missing. Safe to re-run; never duplicates the component.
    /// </summary>
    static void EnsureOutlineHighlight(GameObject root, List<string> objectChanges)
    {
        if (root.GetComponent<XRBaseInteractable>() == null)
        {
            objectChanges.Add("skipped PPEOutlineHighlight (no interactable)");
            return;
        }

        // Avoid stacking with the older outline component on the same root.
        var legacy = root.GetComponent<PPEInteractableOutline>();
        if (legacy != null && legacy.enabled)
        {
            Undo.RecordObject(legacy, "Disable legacy PPEInteractableOutline");
            legacy.enabled = false;
            objectChanges.Add("disabled PPEInteractableOutline (replaced by PPEOutlineHighlight)");
        }

        var existing = root.GetComponent<PPEOutlineHighlight>();
        if (existing != null)
        {
            AssignOutlineMaterialSource(existing);
            return;
        }

        var highlight = Undo.AddComponent<PPEOutlineHighlight>(root);
        AssignOutlineMaterialSource(highlight);
        objectChanges.Add("added PPEOutlineHighlight");
    }

    static void EnsureSelectionManager(StringBuilder report)
    {
        var existing = Object.FindAnyObjectByType<PPESelectionManager>(FindObjectsInactive.Include);
        if (existing != null)
        {
            report.AppendLine($"PPE_SelectionManager: already present ({GetHierarchyPath(existing.transform)})");
            return;
        }

        var system = GameObject.Find("PPE_System");
        Transform parent = system != null ? system.transform : null;

        var go = new GameObject("PPE_SelectionManager");
        Undo.RegisterCreatedObjectUndo(go, "Create PPE_SelectionManager");
        if (parent != null)
            Undo.SetTransformParent(go.transform, parent, "Parent PPE_SelectionManager");

        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        Undo.AddComponent<PPESelectionManager>(go);
        report.AppendLine($"PPE_SelectionManager: created under {(parent != null ? parent.name : "scene root")}");
        EditorUtility.SetDirty(go);
    }

    static void EnsurePreviewController(StringBuilder report)
    {
        var selection = Object.FindAnyObjectByType<PPESelectionManager>(FindObjectsInactive.Include);
        var preview = Object.FindAnyObjectByType<PPEPreviewController>(FindObjectsInactive.Include);

        if (preview == null)
        {
            var host = selection != null ? selection.gameObject : null;
            if (host == null)
            {
                report.AppendLine("PPEPreviewController: skipped (no PPE_SelectionManager host)");
                return;
            }

            preview = Undo.AddComponent<PPEPreviewController>(host);
            report.AppendLine($"PPEPreviewController: added on {GetHierarchyPath(host.transform)}");
        }
        else
        {
            report.AppendLine($"PPEPreviewController: already present ({GetHierarchyPath(preview.transform)})");
        }

        if (selection != null)
        {
            var so = new SerializedObject(selection);
            var prop = so.FindProperty("previewController");
            if (prop != null && prop.objectReferenceValue == null)
            {
                prop.objectReferenceValue = preview;
                so.ApplyModifiedProperties();
                report.AppendLine("PPE_SelectionManager: wired previewController reference");
            }
        }

        // Ensure preview anchor exists under XR camera.
        var origin = Object.FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>(FindObjectsInactive.Include);
        var cam = origin != null ? origin.Camera : Camera.main;
        if (cam == null)
        {
            report.AppendLine("PPE_PreviewAnchor: skipped (no XR camera)");
            return;
        }

        var anchor = cam.transform.Find("PPE_PreviewAnchor");
        if (anchor == null)
        {
            var go = new GameObject("PPE_PreviewAnchor");
            Undo.RegisterCreatedObjectUndo(go, "Create PPE_PreviewAnchor");
            Undo.SetTransformParent(go.transform, cam.transform, "Parent PPE_PreviewAnchor");
            go.transform.localPosition = new Vector3(0f, -0.15f, 1.2f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            anchor = go.transform;
            report.AppendLine("PPE_PreviewAnchor: created under XR camera");
        }

        if (preview != null)
        {
            var so = new SerializedObject(preview);
            var prop = so.FindProperty("previewAnchor");
            if (prop != null && prop.objectReferenceValue == null)
            {
                prop.objectReferenceValue = anchor;
                so.ApplyModifiedProperties();
                report.AppendLine("PPEPreviewController: wired previewAnchor reference");
            }
        }
    }

    static void AssignOutlineMaterialSource(PPEOutlineHighlight highlight)
    {
        if (highlight == null)
            return;

        var material = AssetDatabase.LoadAssetAtPath<Material>(OutlineMaterialPath)
                       ?? AssetDatabase.LoadAssetAtPath<Material>(OutlineResourcesPath);
        if (material == null)
            return;

        var so = new SerializedObject(highlight);
        var prop = so.FindProperty("outlineMaterialSource");
        if (prop == null)
            return;

        if (prop.objectReferenceValue == null)
        {
            prop.objectReferenceValue = material;
            so.ApplyModifiedProperties();
        }
    }

    /// <summary>
    /// Public entry so other setup menus can fix unwired Ray Select / Activate.
    /// </summary>
    public static bool WireRayInteractorSelectAndActivateInputs(StringBuilder report = null)
    {
        report ??= new StringBuilder();
        return TryWireRayInteractorSelectInputs(report);
    }

    /// <summary>
    /// Scene Ray Interactors currently ship with Select InputActionReference mode but null refs.
    /// Wire them to XRI Default Input Actions so hover/select events can fire.
    /// </summary>
    static bool TryWireRayInteractorSelectInputs(StringBuilder report)
    {
        var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(XriInputActionsPath);
        if (asset == null)
        {
            report.AppendLine();
            report.AppendLine("Select wiring: SKIPPED — could not load XRI Default Input Actions.");
            report.AppendLine($"  Expected at: {XriInputActionsPath}");
            return false;
        }

        var leftSelect = FindActionReference(asset, "XRI Left Interaction", "Select");
        var leftSelectValue = FindActionReference(asset, "XRI Left Interaction", "Select Value");
        var rightSelect = FindActionReference(asset, "XRI Right Interaction", "Select");
        var rightSelectValue = FindActionReference(asset, "XRI Right Interaction", "Select Value");
        var leftActivate = FindActionReference(asset, "XRI Left Interaction", "Activate");
        var leftActivateValue = FindActionReference(asset, "XRI Left Interaction", "Activate Value");
        var rightActivate = FindActionReference(asset, "XRI Right Interaction", "Activate");
        var rightActivateValue = FindActionReference(asset, "XRI Right Interaction", "Activate Value");

        if (leftSelect == null || rightSelect == null)
        {
            report.AppendLine();
            report.AppendLine("Select wiring: SKIPPED — Select InputActionReference sub-assets not found.");
            return false;
        }

        var anyChange = false;
        var rays = Object.FindObjectsByType<XRRayInteractor>(FindObjectsInactive.Include);
        report.AppendLine();
        report.AppendLine($"Ray Interactors found: {rays.Length}");

        foreach (var ray in rays)
        {
            if (ray == null)
                continue;

            var hand = InferHand(ray.transform);
            InputActionReference select;
            InputActionReference selectValue;
            InputActionReference activate;
            InputActionReference activateValue;
            if (hand == HandSide.Left)
            {
                select = leftSelect;
                selectValue = leftSelectValue ?? leftSelect;
                activate = leftActivate;
                activateValue = leftActivateValue ?? leftActivate;
            }
            else if (hand == HandSide.Right)
            {
                select = rightSelect;
                selectValue = rightSelectValue ?? rightSelect;
                activate = rightActivate;
                activateValue = rightActivateValue ?? rightActivate;
            }
            else
            {
                report.AppendLine($"  · {GetHierarchyPath(ray.transform)}: could not infer Left/Right — left Select wiring skipped");
                continue;
            }

            var changed = AssignActionReferences(ray, "m_SelectInput", select, selectValue);
            if (activate != null)
                changed |= AssignActionReferences(ray, "m_ActivateInput", activate, activateValue ?? activate);

            if (changed)
            {
                anyChange = true;
                report.AppendLine($"  + {GetHierarchyPath(ray.transform)}: wired Select/Activate ({hand})");
                EditorUtility.SetDirty(ray);
            }
            else
            {
                report.AppendLine($"  · {GetHierarchyPath(ray.transform)}: Select/Activate already assigned ({hand})");
            }
        }

        return anyChange;
    }

    enum HandSide
    {
        Unknown,
        Left,
        Right,
    }

    static HandSide InferHand(Transform t)
    {
        while (t != null)
        {
            var n = t.name;
            if (n.IndexOf("Left", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return HandSide.Left;
            if (n.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return HandSide.Right;
            t = t.parent;
        }

        return HandSide.Unknown;
    }

    static bool AssignActionReferences(
        XRRayInteractor ray,
        string inputProperty,
        InputActionReference performed,
        InputActionReference value)
    {
        var so = new SerializedObject(ray);
        var modeProp = so.FindProperty($"{inputProperty}.m_InputSourceMode");
        var performedProp = so.FindProperty($"{inputProperty}.m_InputActionReferencePerformed");
        var valueProp = so.FindProperty($"{inputProperty}.m_InputActionReferenceValue");
        if (modeProp == null || performedProp == null || valueProp == null)
            return false;

        var changed = false;

        // InputSourceMode.InputActionReference == 2
        if (modeProp.intValue != 2)
        {
            modeProp.intValue = 2;
            changed = true;
        }

        if (performed != null && performedProp.objectReferenceValue != performed)
        {
            performedProp.objectReferenceValue = performed;
            changed = true;
        }

        if (value != null && valueProp.objectReferenceValue != value)
        {
            valueProp.objectReferenceValue = value;
            changed = true;
        }

        if (changed)
            so.ApplyModifiedProperties();

        return changed;
    }

    static InputActionReference FindActionReference(InputActionAsset asset, string mapName, string actionName)
    {
        var action = asset.FindAction($"{mapName}/{actionName}", false);
        if (action == null)
            return null;

        var path = AssetDatabase.GetAssetPath(asset);
        var assets = AssetDatabase.LoadAllAssetsAtPath(path);
        foreach (var obj in assets)
        {
            if (obj is InputActionReference reference &&
                reference.action != null &&
                reference.action.id == action.id)
                return reference;
        }

        // Fallback: create a transient reference (works for this editor session / serialization via object ref).
        return InputActionReference.Create(action);
    }

    static string GetHierarchyPath(Transform t)
    {
        var path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }

        return path;
    }
}
