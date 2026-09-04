using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Configures chemical gloves so they are grabbed by the cuff rim and hang
/// below the VR hand (loose PPE object — not a wearable palm pose).
/// </summary>
public static class GloveGrabPoseSetup
{
    const string PreferredScenePath = "Assets/Scenes/04_PPE.unity";
    const string PrefabPath = "Assets/1.JSR/Prefabs/PPE_ChemicalGloves_Grab.prefab";
    const string GripPointName = "GripPoint";

    // Extra offset past the cuff rim (meters) so the hand sits on the outside
    // edge and does not sink into the opening / overlap the glove palm.
    const float CuffOutsideOffsetMeters = 0.08f;

    public static void FixChemicalGlovesGrabPose()
    {
        EnsurePreferredSceneLoaded();

        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[Glove Grab Pose] No active loaded scene.");
            return;
        }

        var report = new StringBuilder();
        report.AppendLine("=== Fix Chemical Gloves Grab Pose (cuff hang) ===");

        var gloves = FindGlovesRoot(scene);
        if (gloves == null)
        {
            Debug.LogError("[Glove Grab Pose] PPE_Gloves / PPE_ChemicalGloves not found.");
            return;
        }

        report.AppendLine($"Target: {GetPath(gloves.transform)}");

        var grab = gloves.GetComponent<GloveGrabPose>();
        if (grab == null)
            grab = Undo.AddComponent<GloveGrabPose>(gloves);

        var interactionBits = 1;
        var selectMode = InteractableSelectMode.Single;
        var distanceMode = XRBaseInteractable.DistanceCalculationMode.ColliderPosition;

        var simple = gloves.GetComponent<XRSimpleInteractable>();
        if (simple != null)
        {
            interactionBits = simple.interactionLayers.value;
            selectMode = simple.selectMode;
            distanceMode = simple.distanceCalculationMode;
            Undo.DestroyObjectImmediate(simple);
            report.AppendLine(gloves.GetComponent<XRSimpleInteractable>() == null
                ? "Removed XRSimpleInteractable."
                : "WARNING: XRSimpleInteractable still present.");
        }

        foreach (var g in gloves.GetComponents<XRGrabInteractable>())
        {
            if (g is GloveGrabPose)
                continue;
            interactionBits = g.interactionLayers.value;
            selectMode = g.selectMode;
            distanceMode = g.distanceCalculationMode;
            Undo.DestroyObjectImmediate(g);
            report.AppendLine("Removed plain XRGrabInteractable.");
        }

        grab = gloves.GetComponent<GloveGrabPose>();
        if (grab == null)
            grab = Undo.AddComponent<GloveGrabPose>(gloves);

        EnsureRigidbody(gloves, report);
        EnsureCollider(gloves, report);

        // Remove old palm-style attach points from the previous grab pose.
        DestroyChildIfExists(gloves.transform, "GrabAttach_Left", report);
        DestroyChildIfExists(gloves.transform, "GrabAttach_Right", report);

        var grip = EnsureAttach(gloves.transform, GripPointName, report);
        PlaceCuffGripPoint(gloves, grip, report);

        Undo.RecordObject(grab, "Configure GloveGrabPose cuff grip");
        grab.EditorWire(grip);
        grab.interactionLayers = interactionBits;
        grab.selectMode = selectMode;
        grab.distanceCalculationMode = distanceMode;
        grab.useDynamicAttach = false;
        grab.trackPosition = true;
        grab.trackRotation = true;
        grab.movementType = XRGrabInteractable.MovementType.Kinematic;
        grab.throwOnDetach = false;
        grab.predictedVisualsTransform = null;
        EditorUtility.SetDirty(grab);

        SavePrefab(gloves, report);

        EditorUtility.SetDirty(gloves);
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            if (scene.isDirty && !string.IsNullOrEmpty(scene.path))
                EditorSceneManager.SaveScene(scene);
        }

        Selection.activeGameObject = gloves;
        report.AppendLine("Done. Grab snaps to GripPoint at cuff rim; glove hangs below hand.");
        Debug.Log(report.ToString(), gloves);
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

    static GameObject FindGlovesRoot(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "PPE_Gloves" || t.name == "PPE_ChemicalGloves")
                    return t.gameObject;
            }
        }

        return null;
    }

    static void EnsureRigidbody(GameObject gloves, StringBuilder report)
    {
        var rb = gloves.GetComponent<Rigidbody>();
        if (rb == null)
            rb = Undo.AddComponent<Rigidbody>(gloves);

        Undo.RecordObject(rb, "Configure glove Rigidbody");
        rb.useGravity = false;
        rb.isKinematic = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        report.AppendLine("Rigidbody: kinematic, no gravity.");
    }

    static void EnsureCollider(GameObject gloves, StringBuilder report)
    {
        var cols = gloves.GetComponentsInChildren<Collider>(true);
        for (var i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null && cols[i].enabled && !cols[i].isTrigger)
            {
                report.AppendLine("Collider: reuse existing.");
                return;
            }
        }

        var box = Undo.AddComponent<BoxCollider>(gloves);
        FitBoxToRenderers(box, gloves);
        report.AppendLine("Collider: added fitted BoxCollider.");
    }

    static void DestroyChildIfExists(Transform parent, string name, StringBuilder report)
    {
        var existing = parent.Find(name);
        if (existing == null)
            return;
        Undo.DestroyObjectImmediate(existing.gameObject);
        report.AppendLine($"Removed obsolete {name}.");
    }

    static Transform EnsureAttach(Transform parent, string name, StringBuilder report)
    {
        var existing = parent.Find(name);
        if (existing != null)
        {
            report.AppendLine($"Reused {name}.");
            return existing;
        }

        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        Undo.SetTransformParent(go.transform, parent, "Parent " + name);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        report.AppendLine($"Created {name}.");
        return go.transform;
    }

    /// <summary>
    /// Place GripPoint on the outer cuff/opening rim so the VR hand stays above
    /// the glove and the sleeve + fingers hang downward.
    /// </summary>
    static void PlaceCuffGripPoint(GameObject gloves, Transform grip, StringBuilder report)
    {
        var bounds = GetRendererLocalBounds(gloves.transform);

        // Long axis of these storage gloves is local Z: cuff toward min Z, fingers toward max Z.
        var cuffEdge = new Vector3(bounds.center.x, bounds.center.y, bounds.min.z);

        // Push past the opening rim (outside the sleeve) so the hand does not
        // insert into the cuff / overlap the glove palm.
        cuffEdge.z -= CuffOutsideOffsetMeters;

        Undo.RecordObject(grip, "Pose GripPoint at cuff");
        grip.localPosition = cuffEdge;
        grip.localScale = Vector3.one;

        // Hang orientation: cuff→fingers (+Z) maps to interactor down (-Y) so
        // the glove dangles below the hand instead of aligning finger-to-finger.
        // Euler(-90,0,0): local +Z hangs downward when snapped to the controller.
        grip.localRotation = Quaternion.Euler(-90f, 0f, 0f);

        report.AppendLine(
            $"GripPoint at cuff rim local={cuffEdge} (bounds z=[{bounds.min.z:F3},{bounds.max.z:F3}]), " +
            $"rot=(-90,0,0), outsideOffset={CuffOutsideOffsetMeters:F2}m.");
    }

    static Bounds GetRendererLocalBounds(Transform root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return new Bounds(Vector3.zero, new Vector3(0.2f, 0.1f, 0.3f));

        Bounds? world = null;
        foreach (var r in renderers)
        {
            if (r == null || !r.enabled)
                continue;
            if (r.gameObject.name == "HighlightVisual")
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
            return new Bounds(Vector3.zero, new Vector3(0.2f, 0.1f, 0.3f));

        var wb = world.Value;
        var c = wb.center;
        var e = wb.extents;
        var corners = new[]
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

        var localMin = root.InverseTransformPoint(corners[0]);
        var localMax = localMin;
        for (var i = 1; i < corners.Length; i++)
        {
            var lp = root.InverseTransformPoint(corners[i]);
            localMin = Vector3.Min(localMin, lp);
            localMax = Vector3.Max(localMax, lp);
        }

        var localBounds = new Bounds();
        localBounds.SetMinMax(localMin, localMax);
        return localBounds;
    }

    static void FitBoxToRenderers(BoxCollider box, GameObject root)
    {
        var local = GetRendererLocalBounds(root.transform);
        box.center = local.center;
        box.size = new Vector3(
            Mathf.Max(local.size.x, 0.05f),
            Mathf.Max(local.size.y, 0.05f),
            Mathf.Max(local.size.z, 0.05f));
        box.isTrigger = false;
    }

    static void SavePrefab(GameObject gloves, StringBuilder report)
    {
        var dir = System.IO.Path.GetDirectoryName(PrefabPath);
        if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
            System.IO.Directory.CreateDirectory(dir);

        var prefab = PrefabUtility.SaveAsPrefabAsset(gloves, PrefabPath);
        if (prefab != null)
            report.AppendLine($"Prefab saved: {PrefabPath}");
        else
            report.AppendLine("WARNING: failed to save glove grab prefab.");
    }

    static string GetPath(Transform t)
    {
        if (t == null)
            return "<null>";
        var stack = new System.Collections.Generic.Stack<string>();
        while (t != null)
        {
            stack.Push(t.name);
            t = t.parent;
        }

        return string.Join("/", stack);
    }
}
