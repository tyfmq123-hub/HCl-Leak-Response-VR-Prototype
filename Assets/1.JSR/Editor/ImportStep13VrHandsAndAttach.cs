using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Batch/menu: reattach Step_13 VR hands under Visuals with grip-calibrated pose.
/// </summary>
public static class ImportStep13VrHandsAndAttach
{
    const string ScenePath = "Assets/Scenes/PPE_PreparationRoom 1.unity";

    [MenuItem("Tools/PPE/Import Step_13 VR Hands And Attach", false, 24)]
    public static void RunFromMenu()
    {
        Run(quitEditor: false);
    }

    public static void RunBatch()
    {
        Run(quitEditor: true);
    }

    static void Run(bool quitEditor)
    {
        try
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset("Assets/Common/VRHands/vr hand_Left.prefab", ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset("Assets/Common/VRHands/vr hand_Right.prefab", ImportAssetOptions.ForceSynchronousImport);

            var left = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Common/VRHands/vr hand_Left.prefab");
            var right = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Common/VRHands/vr hand_Right.prefab");
            if (left == null || right == null)
            {
                Debug.LogError("[Step13 Hands] Prefabs failed to load after import.");
                if (quitEditor)
                    EditorApplication.Exit(1);
                return;
            }

            Debug.Log($"[Step13 Hands] Prefabs OK: {left.name}, {right.name}");

            var active = EditorSceneManager.GetActiveScene();
            if (!active.IsValid() || active.path != ScenePath)
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            PPEWearableChemicalGlovesSetup.ReplacePpeWearableWithVrHands();
            PPEChemicalGlovesStorageSetup.InstallVrHandsIntoStorage();

            var scene = EditorSceneManager.GetActiveScene();
            if (scene.IsValid() && scene.isDirty)
                EditorSceneManager.SaveScene(scene);

            Debug.Log("[Step13 Hands] Wearables under Visuals + storage reattached and scene saved.");
            if (quitEditor)
                EditorApplication.Exit(0);
        }
        catch (System.Exception ex)
        {
            Debug.LogException(ex);
            if (quitEditor)
                EditorApplication.Exit(1);
        }
    }
}
