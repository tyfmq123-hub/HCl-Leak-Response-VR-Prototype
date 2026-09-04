using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Restores cabinet props:
/// - PPECabinet = wardrobe (PPE_wardrobe / 61f4325f)
/// - ENV_PPECabinet_03 = three-door locker (75ff6923)
/// Does not touch PPE_Storage.
/// </summary>
public static class PPECabinetRestoreSetup
{
    const string WardrobeAssetPath =
        "Assets/1.JSR/Models/PPE_wardrobe/tripo_convert_61f4325f-bd93-42bf-9a78-5dde4d9c31e3.fbx";
    const string ThreeDoorAssetPath =
        "Assets/1.JSR/Models/ENV_PPECabinet_03/tripo_convert_75ff6923-86fd-444c-86d6-96b69d7998a7.fbx";
    // Newer import folder (if ENV_PPECabinet_03 was moved/renamed).
    const string ThreeDoorFallbackPath =
        "Assets/1.JSR/Models/PPE_Cabinet/tripo_convert_7e32883c-a66d-406b-9cf0-e73ae2a6715d.fbx";

    const string WardrobeName = "PPECabinet";
    const string ThreeDoorName = "ENV_PPECabinet_03";

    public static void RestoreCabinet()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[PPE Cabinet] No active scene.");
            return;
        }

        DestroyRootByName(scene, WardrobeName);
        DestroyRootByName(scene, ThreeDoorName);
        DestroyRootByName(scene, "ENV_PPECabinet");

        // Side-by-side on the back wall, both ~2.2 m tall.
        var wardrobe = SpawnFitted(
            WardrobeAssetPath,
            WardrobeName,
            new Vector3(-1.15f, 0f, 2.35f),
            180f);
        var threeDoor = SpawnFitted(
            ResolveExistingPath(ThreeDoorAssetPath, ThreeDoorFallbackPath),
            ThreeDoorName,
            new Vector3(1.15f, 0f, 2.35f),
            180f);

        EditorSceneManager.MarkSceneDirty(scene);
        if (scene.isDirty && !string.IsNullOrEmpty(scene.path))
            EditorSceneManager.SaveScene(scene);

        if (wardrobe != null)
            Selection.activeObject = wardrobe;

        Debug.Log(
            $"[PPE Cabinet] Restored '{WardrobeName}' and '{ThreeDoorName}'. PPE_Storage unchanged.",
            wardrobe != null ? wardrobe : threeDoor);
    }

    static void DestroyRootByName(Scene scene, string name)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name != name && !root.name.StartsWith(name + " "))
                continue;
            if (root.name == "PPE_Storage")
                continue;
            Undo.DestroyObjectImmediate(root);
        }
    }

    static string ResolveExistingPath(params string[] candidates)
    {
        foreach (var path in candidates)
        {
            if (string.IsNullOrEmpty(path))
                continue;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                return path;
            if (System.IO.File.Exists(path))
                return path;
        }

        return candidates.Length > 0 ? candidates[0] : null;
    }

    static GameObject SpawnFitted(string assetPath, string name, Vector3 position, float yawDegrees)
    {
        if (string.IsNullOrEmpty(assetPath))
        {
            Debug.LogError("[PPE Cabinet] No asset path for " + name);
            return null;
        }

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (model == null)
        {
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            model = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        }

        if (model == null)
        {
            Debug.LogError($"[PPE Cabinet] Missing asset: {assetPath}");
            return null;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        Undo.RegisterCreatedObjectUndo(instance, "Restore " + name);
        instance.name = name;
        instance.transform.position = position;
        instance.transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
        instance.transform.localScale = Vector3.one;

        FitHeightToFloor(instance, position, targetHeight: 2.2f);
        return instance;
    }

    static void FitHeightToFloor(GameObject instance, Vector3 floorPos, float targetHeight)
    {
        var rends = instance.GetComponentsInChildren<Renderer>();
        if (rends == null || rends.Length == 0)
            return;

        var bounds = rends[0].bounds;
        for (var i = 1; i < rends.Length; i++)
            bounds.Encapsulate(rends[i].bounds);

        var height = bounds.size.y;
        if (height <= 0.01f)
            return;

        var mul = Mathf.Clamp(targetHeight / height, 0.5f, 3.5f);
        instance.transform.localScale = Vector3.one * mul;
        instance.transform.position = floorPos;

        rends = instance.GetComponentsInChildren<Renderer>();
        bounds = rends[0].bounds;
        for (var i = 1; i < rends.Length; i++)
            bounds.Encapsulate(rends[i].bounds);
        instance.transform.position += Vector3.up * (-bounds.min.y);
    }
}
