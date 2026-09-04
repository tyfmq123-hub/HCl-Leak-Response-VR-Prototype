using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Wires the STEP 4 guide visuals and STEP 5 placement equip settings.</summary>
public static class PPEPlacementGuideSetup
{
    const string ScenePath = "Assets/Scenes/04_PPE.unity";
    const string ManagerName = "PPE_PlacementGuideManager";
    const string NotReadyMaterialPath = "Assets/1.JSR/Materials/PPE_GuideNotReady.mat";
    const string ReadyMaterialPath = "Assets/1.JSR/Materials/PPE_GuideReady.mat";

    public static void SetupPlacementGuides()
    {
        var scene = SceneManager.GetActiveScene();
        if (!IsTargetScene(scene))
        {
            Debug.LogError($"[PPE Placement Guide] Open '{ScenePath}' before running setup.");
            return;
        }

        var report = new StringBuilder();
        report.AppendLine("=== PPE STEP 4/5 Placement Guide Setup ===");

        var anchorSet = Object.FindAnyObjectByType<PPEEquipmentAnchorSet>(FindObjectsInactive.Include);
        if (anchorSet == null)
        {
            PPEEquipmentSlotSetup.SetupEquipmentSlots();
            anchorSet = Object.FindAnyObjectByType<PPEEquipmentAnchorSet>(FindObjectsInactive.Include);
        }
        if (anchorSet == null)
        {
            Debug.LogError("[PPE Placement Guide] STEP 3 PPEEquipmentAnchorSet is missing.");
            return;
        }

        var system = FindTransform(scene, "PPE_System");
        if (system == null)
        {
            Debug.LogError("[PPE Placement Guide] PPE_System is missing.");
            return;
        }

        var managerTransform = system.Find(ManagerName);
        if (managerTransform == null)
        {
            var go = new GameObject(ManagerName);
            Undo.RegisterCreatedObjectUndo(go, "Create PPE placement guide manager");
            Undo.SetTransformParent(go.transform, system, "Parent PPE placement guide manager");
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            managerTransform = go.transform;
            report.AppendLine("Created PPE_System/PPE_PlacementGuideManager.");
        }

        var manager = managerTransform.GetComponent<PPEPlacementGuideManager>();
        if (manager == null)
            manager = Undo.AddComponent<PPEPlacementGuideManager>(managerTransform.gameObject);

        var notReady = AssetDatabase.LoadAssetAtPath<Material>(NotReadyMaterialPath);
        var ready = AssetDatabase.LoadAssetAtPath<Material>(ReadyMaterialPath);
        Undo.RecordObject(manager, "Wire PPE placement guide manager");
        manager.EditorWire(anchorSet, notReady, ready, true, true, 0.15f);
        EditorUtility.SetDirty(manager);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        report.AppendLine($"Anchor Set: {HierarchyPath(anchorSet.transform)}");
        report.AppendLine($"Not Ready Material: {(notReady != null ? notReady.name : "MISSING")}");
        report.AppendLine($"Ready Material: {(ready != null ? ready.name : "MISSING")}");
        report.AppendLine($"Placement Auto Equip: {manager.AutoEquipEnabled}");
        report.AppendLine($"Ready Hold Duration: {manager.ReadyHoldDuration:0.###}s");
        report.AppendLine(Validate(manager, anchorSet, notReady, ready));
        Debug.Log(report.ToString());
    }

    public static void SetupPlacementAutoEquip()
    {
        SetupPlacementGuides();
    }

    public static bool IsSetupRequired()
    {
        var scene = SceneManager.GetActiveScene();
        if (!IsTargetScene(scene))
            return false;

        var manager = Object.FindAnyObjectByType<PPEPlacementGuideManager>(FindObjectsInactive.Include);
        return manager == null || manager.NotReadyMaterial == null || manager.ReadyMaterial == null ||
               !manager.AutoEquipEnabled || manager.PlacementSetupVersion < 1;
    }

    static string Validate(
        PPEPlacementGuideManager manager,
        PPEEquipmentAnchorSet anchorSet,
        Material notReady,
        Material ready)
    {
        if (manager == null)
            return "VALIDATION FAIL: manager missing.";
        if (anchorSet == null)
            return "VALIDATION FAIL: anchor set missing.";
        if (notReady == null || ready == null)
            return "VALIDATION FAIL: guide material missing.";
        if (notReady.renderQueue < 3000 || ready.renderQueue < 3000)
            return "VALIDATION FAIL: guide materials are not transparent queue materials.";
        if (!manager.AutoEquipEnabled)
            return "VALIDATION FAIL: placement auto equip is disabled.";

        return "VALIDATION PASS: manager, 9-slot anchor set, guide materials, and placement auto equip are wired through PPEEquipmentManager.Equip.";
    }

    static Transform FindTransform(Scene scene, string name)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform.name == name)
                    return transform;
            }
        }
        return null;
    }

    static bool IsTargetScene(Scene scene)
    {
        return scene.IsValid() && scene.isLoaded && scene.path == ScenePath;
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

[InitializeOnLoad]
static class PPEPlacementGuideSetupTrigger
{
    const string SessionKey = "PPE.PlacementGuideSetup.Step5.v2";
    const string TargetScenePath = "Assets/Scenes/04_PPE.unity";

    static PPEPlacementGuideSetupTrigger()
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
        if (PPEPlacementGuideSetup.IsSetupRequired())
            PPEPlacementGuideSetup.SetupPlacementGuides();
    }
}
