using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Unity.XR.CoreUtils;

/// <summary>
/// Creates XR Origin / PlayerBodyAnchor / ResponseBag_Wearable with a visual-only bag copy.
/// Does not wire ResponseBagPrepared or touch the world bag interactable.
/// </summary>
public static class PPEWearableResponseBagSetup
{
    const string AnchorName = "PlayerBodyAnchor";
    const string WearableName = "ResponseBag_Wearable";
    const string VisualChildName = "BagVisual";
    const string UnityReadyName = "ResponseBag_HardShell";
    const string UnityReadyAssetPath =
        "Assets/Common/Equipment/ResponseBag_HardShell/ResponseBag_HardShell.fbx";

    static readonly Vector3 DefaultAnchorOffset = new Vector3(-0.58f, -1.08f, 0.16f);
    static readonly Vector3 DefaultWearableLocalPos = Vector3.zero;
    static readonly Vector3 DefaultWearableLocalEuler = new Vector3(0f, 90f, 8f);
    static readonly Vector3 DefaultWearableLocalScale = new Vector3(0.85f, 0.85f, 0.85f);

    public static void SetupWearableResponseBag()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[Wearable Bag] No active loaded scene.");
            return;
        }

        var report = new StringBuilder();
        report.AppendLine("=== Setup Wearable Response Bag ===");

        var origin = FindXrOrigin();
        if (origin == null)
        {
            Debug.LogError("[Wearable Bag] XR Origin not found.");
            return;
        }

        var camera = ResolveCamera(origin);
        if (camera == null)
        {
            Debug.LogError("[Wearable Bag] Main Camera / XR Camera not found.");
            return;
        }

        report.AppendLine($"XR Origin: {origin.name}");
        report.AppendLine($"XR Camera: {camera.name}");

        var anchor = EnsureAnchor(origin.transform, camera.transform, report);
        var wearable = EnsureWearable(anchor.transform, report);
        EnsureVisualCopy(wearable, report);

        EditorUtility.SetDirty(anchor);
        EditorUtility.SetDirty(wearable);
        EditorSceneManager.MarkSceneDirty(scene);
        if (scene.isDirty && !string.IsNullOrEmpty(scene.path))
            EditorSceneManager.SaveScene(scene);

        Selection.activeObject = wearable;
        Debug.Log(report.ToString());
    }

    static GameObject FindXrOrigin()
    {
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name.StartsWith("XR Origin"))
                return root;
        }

        var xr = Object.FindAnyObjectByType<XROrigin>(FindObjectsInactive.Include);
        return xr != null ? xr.gameObject : null;
    }

    static Camera ResolveCamera(GameObject origin)
    {
        var xr = origin.GetComponent<XROrigin>();
        if (xr != null && xr.Camera != null)
            return xr.Camera;

        var main = origin.GetComponentInChildren<Camera>(true);
        if (main != null)
            return main;

        return Camera.main;
    }

    static GameObject EnsureAnchor(Transform origin, Transform camera, StringBuilder report)
    {
        var existing = origin.Find(AnchorName);
        GameObject anchorGo;
        if (existing != null)
        {
            anchorGo = existing.gameObject;
            report.AppendLine("Reused PlayerBodyAnchor.");
        }
        else
        {
            anchorGo = new GameObject(AnchorName);
            Undo.RegisterCreatedObjectUndo(anchorGo, "Create PlayerBodyAnchor");
            Undo.SetTransformParent(anchorGo.transform, origin, "Parent PlayerBodyAnchor");
            anchorGo.transform.localPosition = Vector3.zero;
            anchorGo.transform.localRotation = Quaternion.identity;
            anchorGo.transform.localScale = Vector3.one;
            report.AppendLine("Created PlayerBodyAnchor under XR Origin.");
        }

        var follower = anchorGo.GetComponent<PlayerBodyAnchorFollower>();
        if (follower == null)
            follower = Undo.AddComponent<PlayerBodyAnchorFollower>(anchorGo);

        follower.EditorWire(camera);

        var so = new SerializedObject(follower);
        so.FindProperty("localPositionOffset").vector3Value = DefaultAnchorOffset;
        so.FindProperty("positionSmoothTime").floatValue = 0.08f;
        so.FindProperty("rotationSmoothSpeed").floatValue = 12f;
        so.FindProperty("followCameraHeight").boolValue = true;
        so.FindProperty("useCameraYawOnly").boolValue = true;
        so.FindProperty("snapOnEnable").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(follower);

        report.AppendLine(
            $"Anchor offset = {DefaultAnchorOffset}, yaw-only follow, smooth pos=0.08 rot=12.");
        return anchorGo;
    }

    static GameObject EnsureWearable(Transform anchor, StringBuilder report)
    {
        var existing = anchor.Find(WearableName);
        GameObject wearableGo;
        if (existing != null)
        {
            wearableGo = existing.gameObject;
            report.AppendLine("Reused ResponseBag_Wearable.");
        }
        else
        {
            wearableGo = new GameObject(WearableName);
            Undo.RegisterCreatedObjectUndo(wearableGo, "Create ResponseBag_Wearable");
            Undo.SetTransformParent(wearableGo.transform, anchor, "Parent wearable");
            report.AppendLine("Created ResponseBag_Wearable under PlayerBodyAnchor.");
        }

        var visual = wearableGo.GetComponent<ResponseBagWearableVisual>();
        if (visual == null)
            visual = Undo.AddComponent<ResponseBagWearableVisual>(wearableGo);

        var so = new SerializedObject(visual);
        so.FindProperty("wearableRoot").objectReferenceValue = wearableGo;
        so.FindProperty("wearableLocalPosition").vector3Value = DefaultWearableLocalPos;
        so.FindProperty("wearableLocalEuler").vector3Value = DefaultWearableLocalEuler;
        so.FindProperty("wearableLocalScale").vector3Value = DefaultWearableLocalScale;
        so.FindProperty("startHidden").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();

        wearableGo.transform.localPosition = DefaultWearableLocalPos;
        wearableGo.transform.localRotation = Quaternion.Euler(DefaultWearableLocalEuler);
        wearableGo.transform.localScale = DefaultWearableLocalScale;
        wearableGo.SetActive(false);

        visual.EditorWire(wearableGo);
        EditorUtility.SetDirty(visual);

        report.AppendLine(
            $"Wearable localPos={DefaultWearableLocalPos}, euler={DefaultWearableLocalEuler}, scale={DefaultWearableLocalScale}, inactive.");
        return wearableGo;
    }

    static void EnsureVisualCopy(GameObject wearable, StringBuilder report)
    {
        var existingVisual = wearable.transform.Find(VisualChildName);
        if (existingVisual != null)
        {
            StripNonVisualComponents(existingVisual.gameObject, report);
            report.AppendLine("Reused existing BagVisual child.");
            return;
        }

        var source = FindWorldBagSource();
        GameObject copy;
        if (source != null)
        {
            copy = Object.Instantiate(source);
            Undo.RegisterCreatedObjectUndo(copy, "Clone bag visual");
            report.AppendLine($"Cloned visual from scene source '{source.name}'.");
        }
        else
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(UnityReadyAssetPath);
            if (model == null)
            {
                report.AppendLine("WARNING: No ResponseBag_HardShell source — wearable has no mesh yet.");
                return;
            }

            copy = (GameObject)PrefabUtility.InstantiatePrefab(model);
            Undo.RegisterCreatedObjectUndo(copy, "Instantiate bag visual");
            report.AppendLine("Instantiated visual from ResponseBag_HardShell.fbx (scene instance untouched).");
        }

        copy.name = VisualChildName;
        Undo.SetTransformParent(copy.transform, wearable.transform, "Parent bag visual");
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localRotation = Quaternion.identity;
        copy.transform.localScale = Vector3.one;
        copy.SetActive(true);

        StripNonVisualComponents(copy, report);
        EditorUtility.SetDirty(copy);
    }

    static GameObject FindWorldBagSource()
    {
        // Prefer UnityReady world bag; skip UI and existing wearable.
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name.StartsWith("XR Origin"))
                continue;

            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                var go = transforms[i].gameObject;
                if (go.name.IndexOf(UnityReadyName, System.StringComparison.OrdinalIgnoreCase) < 0
                    && go.name != "ResponseBag")
                    continue;

                if (go.GetComponent<RectTransform>() != null && go.GetComponentInParent<Canvas>() != null)
                    continue;

                if (go.GetComponentInParent<ResponseBagWearableVisual>() != null)
                    continue;

                if (go.GetComponentInChildren<MeshFilter>(true) == null
                    && go.GetComponentInChildren<SkinnedMeshRenderer>(true) == null)
                    continue;

                return PrefabUtility.IsPartOfPrefabInstance(go)
                    ? PrefabUtility.GetOutermostPrefabInstanceRoot(go)
                    : go;
            }
        }

        return null;
    }

    static void StripNonVisualComponents(GameObject root, StringBuilder report)
    {
        var stripped = 0;

        foreach (var col in root.GetComponentsInChildren<Collider>(true))
        {
            Undo.DestroyObjectImmediate(col);
            stripped++;
        }

        foreach (var rb in root.GetComponentsInChildren<Rigidbody>(true))
        {
            Undo.DestroyObjectImmediate(rb);
            stripped++;
        }

        foreach (var audio in root.GetComponentsInChildren<AudioSource>(true))
        {
            Undo.DestroyObjectImmediate(audio);
            stripped++;
        }

        // Interaction / prepare scripts — dependents before RequireComponent targets.
        stripped += DestroyAll<ResponseBagPrepareInteractable>(root);
        stripped += DestroyAll<ResponseBagInteractable>(root);
        stripped += DestroyAll<PPEInteractableOutline>(root);
        stripped += DestroyAll<PPEOutlineHighlight>(root);
        stripped += DestroyAll<PPEEquipable>(root);
        stripped += DestroyAll<PPEItem>(root);
        stripped += DestroyAll<XRBaseInteractable>(root);

        // Layer default — not Selectable.
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            t.gameObject.layer = 0;

        report.AppendLine($"Stripped {stripped} non-visual component(s) from BagVisual.");
    }

    static int DestroyAll<T>(GameObject root) where T : Component
    {
        var count = 0;
        foreach (var c in root.GetComponentsInChildren<T>(true))
        {
            Undo.DestroyObjectImmediate(c);
            count++;
        }

        return count;
    }
}
