using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Unity.XR.CoreUtils;

/// <summary>
/// Wires INT_WasteContainer_01 for gaze + Grip take-out after chemical gloves are worn.
/// </summary>
public static class WasteContainerPrepareSetup
{
    const string PreferredScenePath = "Assets/Scenes/04_PPE.unity";
    const string ContainerNamePrefix = "INT_WasteContainer";
    const string RouterName = "XR_InteractionConfirmRouter";
    const string GazeDriverName = "CameraGazeSelectDriver";
    const string OutlineMaterialPath = "Assets/1.JSR/Materials/PPE/PPE_Outline.mat";

    public static void SetupWasteContainerTakeOut()
    {
        EnsurePreferredSceneLoaded();

        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[Waste Container] No active loaded scene.");
            return;
        }

        var report = new StringBuilder();
        report.AppendLine("=== Setup Waste Container Gaze+Grip Take-Out ===");
        report.AppendLine($"Scene: {scene.name}");

        var container = FindWasteContainer(scene);
        if (container == null)
        {
            Debug.LogError("[Waste Container] INT_WasteContainer_01 not found in scene.");
            return;
        }

        report.AppendLine($"Container: {GetPath(container.transform)}");

        EnsureCollider(container, report);
        var interactable = EnsureSimpleInteractable(container, report);
        var outline = EnsureOutline(container, report);
        var prepare = EnsurePrepare(container, interactable, report);
        var gaze = EnsureGazeDriver(scene, prepare, report);
        WireRouter(prepare, report);

        // Refresh glove wearable ray cache if present.
        var gloveWearable = Object.FindAnyObjectByType<ChemicalGlovesWearableVisual>(FindObjectsInactive.Include);
        if (gloveWearable != null)
        {
            var so = new SerializedObject(gloveWearable);
            var disableProp = so.FindProperty("disableRayInteractorsWhileWorn");
            if (disableProp != null)
                disableProp.boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(gloveWearable);
            report.AppendLine("Confirmed ChemicalGlovesWearableVisual disables rays while worn.");
        }

        EditorUtility.SetDirty(container);
        EditorUtility.SetDirty(prepare);
        if (outline != null)
            EditorUtility.SetDirty(outline);
        if (gaze != null)
            EditorUtility.SetDirty(gaze);

        EditorSceneManager.MarkSceneDirty(scene);
        if (scene.isDirty && !string.IsNullOrEmpty(scene.path))
            EditorSceneManager.SaveScene(scene);

        Selection.activeGameObject = container;
        report.AppendLine("Done. Gloves on → look at container → Grip → checklist WasteContainer.");
        Debug.Log(report.ToString(), container);
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

    static GameObject FindWasteContainer(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name.Trim().StartsWith(ContainerNamePrefix))
                return PrefabUtility.IsPartOfPrefabInstance(root)
                    ? PrefabUtility.GetOutermostPrefabInstanceRoot(root)
                    : root;

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.Trim().StartsWith(ContainerNamePrefix))
                {
                    var go = t.gameObject;
                    return PrefabUtility.IsPartOfPrefabInstance(go)
                        ? PrefabUtility.GetOutermostPrefabInstanceRoot(go)
                        : go;
                }
            }
        }

        return null;
    }

    static void EnsureCollider(GameObject container, StringBuilder report)
    {
        var cols = container.GetComponentsInChildren<Collider>(true);
        var usable = false;
        for (var i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null && cols[i].enabled && !cols[i].isTrigger)
            {
                usable = true;
                break;
            }
        }

        if (usable)
        {
            report.AppendLine("Collider: reuse existing.");
            return;
        }

        var box = container.GetComponent<BoxCollider>();
        if (box == null)
            box = Undo.AddComponent<BoxCollider>(container);

        FitBoxCollider(box, container);
        box.isTrigger = false;
        box.enabled = true;
        report.AppendLine("Collider: added/fitted BoxCollider on container root.");
    }

    static void FitBoxCollider(BoxCollider box, GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
        {
            box.center = Vector3.zero;
            box.size = new Vector3(0.45f, 0.7f, 0.45f);
            return;
        }

        var world = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                world.Encapsulate(renderers[i].bounds);
        }

        var t = root.transform;
        var min = t.InverseTransformPoint(world.min);
        var max = t.InverseTransformPoint(world.max);
        // Approximate with center/size from world corners
        var c = world.center;
        var e = world.extents;
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
        size.x = Mathf.Max(Mathf.Abs(size.x), 0.05f);
        size.y = Mathf.Max(Mathf.Abs(size.y), 0.05f);
        size.z = Mathf.Max(Mathf.Abs(size.z), 0.05f);
        box.size = size;
    }

    static XRSimpleInteractable EnsureSimpleInteractable(GameObject container, StringBuilder report)
    {
        var simple = container.GetComponent<XRSimpleInteractable>();
        if (simple == null)
        {
            simple = Undo.AddComponent<XRSimpleInteractable>(container);
            report.AppendLine("Added XRSimpleInteractable.");
        }
        else
        {
            report.AppendLine("Reused XRSimpleInteractable.");
        }

        return simple;
    }

    static PPEOutlineHighlight EnsureOutline(GameObject container, StringBuilder report)
    {
        var outline = container.GetComponent<PPEOutlineHighlight>();
        if (outline == null)
        {
            outline = Undo.AddComponent<PPEOutlineHighlight>(container);
            report.AppendLine("Added PPEOutlineHighlight.");
        }
        else
        {
            report.AppendLine("Reused PPEOutlineHighlight.");
        }

        var mat = AssetDatabase.LoadAssetAtPath<Material>(OutlineMaterialPath);
        if (mat == null)
            mat = Resources.Load<Material>("PPE_Outline");
        if (mat != null)
        {
            var so = new SerializedObject(outline);
            var prop = so.FindProperty("outlineMaterialSource");
            if (prop != null)
            {
                prop.objectReferenceValue = mat;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        return outline;
    }

    static WasteContainerPrepareInteractable EnsurePrepare(
        GameObject container,
        XRBaseInteractable interactable,
        StringBuilder report)
    {
        var prepare = container.GetComponent<WasteContainerPrepareInteractable>();
        if (prepare == null)
        {
            prepare = Undo.AddComponent<WasteContainerPrepareInteractable>(container);
            report.AppendLine("Added WasteContainerPrepareInteractable.");
        }
        else
        {
            report.AppendLine("Reused WasteContainerPrepareInteractable.");
        }

        var equipment = Object.FindAnyObjectByType<PPEEquipmentManager>(FindObjectsInactive.Include);
        prepare.EditorWire(equipment, container, interactable);
        EditorUtility.SetDirty(prepare);
        return prepare;
    }

    static CameraGazeSelectDriver EnsureGazeDriver(
        Scene scene,
        WasteContainerPrepareInteractable prepare,
        StringBuilder report)
    {
        var system = GameObject.Find("PPE_System");
        Transform parent = system != null ? system.transform : null;

        CameraGazeSelectDriver driver = Object.FindAnyObjectByType<CameraGazeSelectDriver>(
            FindObjectsInactive.Include);
        GameObject go;
        if (driver != null)
        {
            go = driver.gameObject;
            report.AppendLine("Reused CameraGazeSelectDriver.");
        }
        else
        {
            go = new GameObject(GazeDriverName);
            Undo.RegisterCreatedObjectUndo(go, "Create CameraGazeSelectDriver");
            if (parent != null)
                Undo.SetTransformParent(go.transform, parent, "Parent gaze driver");
            driver = Undo.AddComponent<CameraGazeSelectDriver>(go);
            report.AppendLine("Created CameraGazeSelectDriver under PPE_System.");
        }

        Camera cam = null;
        var xr = Object.FindAnyObjectByType<XROrigin>(FindObjectsInactive.Include);
        if (xr != null)
            cam = xr.Camera;
        if (cam == null)
            cam = Camera.main;

        driver.EditorWire(cam, prepare);
        return driver;
    }

    static void WireRouter(WasteContainerPrepareInteractable prepare, StringBuilder report)
    {
        var router = Object.FindAnyObjectByType<XRInteractionConfirmRouter>(FindObjectsInactive.Include);
        if (router == null)
        {
            var system = GameObject.Find("PPE_System");
            var go = new GameObject(RouterName);
            Undo.RegisterCreatedObjectUndo(go, "Create confirm router");
            if (system != null)
                Undo.SetTransformParent(go.transform, system.transform, "Parent router");
            router = Undo.AddComponent<XRInteractionConfirmRouter>(go);
            report.AppendLine("Created XR_InteractionConfirmRouter.");
        }

        var bag = Object.FindAnyObjectByType<ResponseBagPrepareInteractable>(FindObjectsInactive.Include);
        var selection = Object.FindAnyObjectByType<PPESelectionManager>(FindObjectsInactive.Include);
        router.EditorWire(bag, selection, prepare);
        EditorUtility.SetDirty(router);
        report.AppendLine("Wired confirm router: bag → waste → PPE.");
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
