using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>Configures existing wearable PPE roots for XRI direct/ray grabbing.</summary>
public static class PPEHandGrabSetup
{
    const string ScenePath = "Assets/Scenes/04_PPE.unity";

    public static void SetupHandGrab()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded || scene.path != ScenePath)
        {
            Debug.LogError($"[PPE Hand Grab] Open '{ScenePath}' before running setup.");
            return;
        }

        var report = new StringBuilder();
        var changed = 0;
        var configured = 0;

        report.AppendLine("=== PPE STEP 2 Hand Grab Setup ===");
        foreach (var equipable in FindWearablePpe(scene))
        {
            configured++;
            if (Configure(equipable.gameObject, report))
                changed++;
        }

        if (changed > 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        var validation = Validate(scene);
        report.AppendLine($"Configured wearable roots: {configured} (changed: {changed})");
        report.AppendLine(validation);
        Debug.Log(report.ToString());
    }

    public static bool IsSetupRequired()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded || scene.path != ScenePath)
            return false;

        foreach (var equipable in FindWearablePpe(scene))
        {
            var root = equipable.gameObject;
            if (root.GetComponent<PPEGlovePairCoordinator>() != null)
            {
                if (root.GetComponent<PPEHandGrabbable>() != null ||
                    root.GetComponent<XRBaseInteractable>() == null ||
                    CountDirectPairMembers(root) < 2)
                    return true;
                continue;
            }
            if (root.GetComponent<PPEHandGrabbable>() == null ||
                root.GetComponent<Rigidbody>() == null ||
                root.GetComponent<PPEStoredDisplayState>() == null ||
                root.GetComponentInChildren<Collider>(true) == null)
                return true;
        }

        return false;
    }

    static bool Configure(GameObject root, StringBuilder report)
    {
        if (root.GetComponent<PPEGlovePairCoordinator>() != null)
            return ConvertLogicalGlovePairRoot(root, report);

        var changed = false;
        var simple = root.GetComponent<XRSimpleInteractable>();
        var interactionLayers = simple != null ? simple.interactionLayers : default;
        var distanceMode = simple != null
            ? simple.distanceCalculationMode
            : XRBaseInteractable.DistanceCalculationMode.ColliderPosition;
        var sourceColliders = new List<Collider>();

        if (simple != null)
            sourceColliders.AddRange(simple.colliders);
        if (sourceColliders.Count == 0)
            sourceColliders.AddRange(root.GetComponentsInChildren<Collider>(true));

        var rigidbody = root.GetComponent<Rigidbody>();
        if (rigidbody == null)
        {
            rigidbody = Undo.AddComponent<Rigidbody>(root);
            changed = true;
        }

        Undo.RecordObject(rigidbody, "Configure PPE hand grab rigidbody");
        rigidbody.mass = 0.35f;
        // Stored display owns Play Mode rest pose. Invalid release applies gravity
        // through PPEStoredDisplayState.Released, not XRI throw-on-detach.
        rigidbody.useGravity = false;
        rigidbody.isKinematic = true;
        rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        EditorUtility.SetDirty(rigidbody);

        var grab = root.GetComponent<PPEHandGrabbable>();
        if (grab == null)
        {
            grab = Undo.AddComponent<PPEHandGrabbable>(root);
            changed = true;
        }

        Undo.RecordObject(grab, "Configure PPE hand grab interactable");
        if (simple != null)
            grab.interactionLayers = interactionLayers;
        grab.selectMode = InteractableSelectMode.Single;
        grab.distanceCalculationMode = distanceMode;
        grab.movementType = XRGrabInteractable.MovementType.Kinematic;
        grab.trackPosition = true;
        grab.trackRotation = true;
        grab.smoothPosition = true;
        grab.smoothRotation = true;
        grab.attachEaseInTime = 0f;
        grab.useDynamicAttach = true;
        grab.matchAttachPosition = true;
        grab.matchAttachRotation = true;
        grab.throwOnDetach = false;
        grab.forceGravityOnDetach = false;
        grab.colliders.Clear();
        foreach (var collider in sourceColliders)
        {
            if (collider != null && collider.enabled && !collider.isTrigger)
                grab.colliders.Add(collider);
        }
        EditorUtility.SetDirty(grab);

        var stored = root.GetComponent<PPEStoredDisplayState>();
        if (stored == null)
        {
            stored = Undo.AddComponent<PPEStoredDisplayState>(root);
            changed = true;
        }

        Undo.RecordObject(stored, "Configure PPE stored display");
        stored.EditorApplyDefaults();
        EditorUtility.SetDirty(stored);

        var filter = root.GetComponent<PPEDirectGrabProximityFilter>();
        if (filter == null)
        {
            filter = Undo.AddComponent<PPEDirectGrabProximityFilter>(root);
            changed = true;
        }

        Undo.RecordObject(filter, "Configure PPE grab proximity filter");
        filter.EditorApplyDefaults();
        filter.RegisterWithInteractable();
        EditorUtility.SetDirty(filter);

        if (simple != null)
        {
            Undo.DestroyObjectImmediate(simple);
            changed = true;
        }

        EditorUtility.SetDirty(root);
        report.AppendLine($"  {HierarchyPath(root.transform)}: PPEHandGrabbable + stored display ({grab.colliders.Count} colliders)");
        return changed;
    }

    /// <summary>
    /// PPEItem / PPEOutlineHighlight require XRBaseInteractable. Add XRSimpleInteractable
    /// before removing PPEHandGrabbable so Unity will allow the grab component to come off
    /// the logical pair root. Physical grab stays on Left/Right members.
    /// </summary>
    static bool ConvertLogicalGlovePairRoot(GameObject root, StringBuilder report)
    {
        var changed = false;
        var grab = root.GetComponent<PPEHandGrabbable>();
        var simple = root.GetComponent<XRSimpleInteractable>();
        if (simple == null)
        {
            simple = Undo.AddComponent<XRSimpleInteractable>(root);
            changed = true;
        }

        Undo.RecordObject(simple, "Configure glove pair hover interactable");
        if (grab != null)
            simple.interactionLayers = grab.interactionLayers;
        simple.selectMode = InteractableSelectMode.Multiple;
        simple.colliders.Clear();
        EditorUtility.SetDirty(simple);

        if (grab != null)
        {
            Undo.DestroyObjectImmediate(grab);
            changed = true;
        }

        var filter = root.GetComponent<PPEDirectGrabProximityFilter>();
        if (filter != null)
        {
            Undo.DestroyObjectImmediate(filter);
            changed = true;
        }

        var stored = root.GetComponent<PPEStoredDisplayState>();
        if (stored != null)
        {
            Undo.DestroyObjectImmediate(stored);
            changed = true;
        }

        var body = root.GetComponent<Rigidbody>();
        if (body != null)
        {
            Undo.DestroyObjectImmediate(body);
            changed = true;
        }

        EditorUtility.SetDirty(root);
        report.AppendLine(
            $"  {HierarchyPath(root.transform)}: logical pair hover via XRSimpleInteractable; grab is on physical members");
        return changed;
    }

    static string Validate(Scene scene)
    {
        var wearables = FindWearablePpe(scene);
        var valid = 0;
        var failures = new List<string>();

        foreach (var equipable in wearables)
        {
            var root = equipable.gameObject;
            if (root.GetComponent<PPEGlovePairCoordinator>() != null)
            {
                if (root.GetComponent<XRBaseInteractable>() == null)
                    failures.Add(root.name + ": glove pair missing XRBaseInteractable for PPEItem/outline");
                else if (root.GetComponent<PPEHandGrabbable>() != null)
                    failures.Add(root.name + ": logical glove pair still has PPEHandGrabbable (should be hover-only)");
                else if (CountDirectPairMembers(root) < 2)
                    failures.Add(root.name + ": authored left/right pair members missing");
                else
                    valid++;
                continue;
            }
            var grab = root.GetComponent<PPEHandGrabbable>();
            if (grab == null)
                failures.Add(root.name + ": missing PPEHandGrabbable");
            else if (grab.colliders.Count == 0)
                failures.Add(root.name + ": no grab collider");
            else if (root.GetComponent<Rigidbody>() == null)
                failures.Add(root.name + ": missing Rigidbody");
            else if (root.GetComponent<PPEStoredDisplayState>() == null)
                failures.Add(root.name + ": missing PPEStoredDisplayState");
            else if (root.GetComponent<XRSimpleInteractable>() != null)
                failures.Add(root.name + ": legacy XRSimpleInteractable remains");
            else
                valid++;
        }

        var bag = FindEquipable(scene, "ResponseBag");
        if (bag != null && bag.GetComponent<PPEHandGrabbable>() != null)
            failures.Add("ResponseBag: wearable PPE grab was added unexpectedly");

        return failures.Count == 0
            ? $"VALIDATION PASS: {valid}/{wearables.Count} wearable PPE roots are hand/ray grabbable; ResponseBag unchanged."
            : "VALIDATION FAIL:\n  " + string.Join("\n  ", failures);
    }

    static List<PPEEquipable> FindWearablePpe(Scene scene)
    {
        var results = new List<PPEEquipable>();
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var equipable in root.GetComponentsInChildren<PPEEquipable>(true))
            {
                if (!string.Equals(equipable.ItemId, "ResponseBag", System.StringComparison.Ordinal))
                    results.Add(equipable);
            }
        }
        return results;
    }

    static PPEEquipable FindEquipable(Scene scene, string itemId)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var equipable in root.GetComponentsInChildren<PPEEquipable>(true))
            {
                if (string.Equals(equipable.ItemId, itemId, System.StringComparison.Ordinal))
                    return equipable;
            }
        }
        return null;
    }

    static int CountDirectPairMembers(GameObject root)
    {
        var count = 0;
        var members = root.GetComponentsInChildren<PPEGlovePairMember>(true);
        for (var i = 0; i < members.Length; i++)
        {
            if (members[i] != null && members[i].transform.parent == root.transform)
                count++;
        }

        return count;
    }

    static string HierarchyPath(Transform transform)
    {
        var path = transform.name;
        while (transform.parent != null)
        {
            transform = transform.parent;
            path = transform.name + "/" + path;
        }
        return path;
    }
}

/// <summary>
/// Applies this migration once when the PPE scene is already open during script import.
/// The setup is idempotent and never opens or edits another scene.
/// </summary>
[InitializeOnLoad]
static class PPEHandGrabSetupTrigger
{
    const string SessionKey = "PPE.HandGrabSetup.Step2.v2";
    const string TargetScenePath = "Assets/Scenes/04_PPE.unity";

    static PPEHandGrabSetupTrigger()
    {
        EditorApplication.delayCall += TryApply;
        EditorSceneManager.sceneOpened += OnSceneOpened;
    }

    static void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        if (scene.path == TargetScenePath)
            EditorApplication.delayCall += TryApply;
    }

    static void TryApply()
    {
        if (SessionState.GetBool(SessionKey, false) ||
            EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded || scene.path != TargetScenePath)
            return;

        SessionState.SetBool(SessionKey, true);
        if (PPEHandGrabSetup.IsSetupRequired())
            PPEHandGrabSetup.SetupHandGrab();
    }
}
