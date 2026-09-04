using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Instantiates the PPE desk FBX and attaches table packing on Response_EquipmentManager.
/// Runtime packing still layouts items if this menu was not run.
/// </summary>
public static class PPEResponseEquipmentTablePackingSetup
{
    const string ScenePath = "Assets/Scenes/04_PPE.unity";
    const string DeskAssetPath = "Assets/1.JSR/Models/PPE_DeskTop/\uC791\uC5C5\uB300+3D+\uBAA8\uB378.fbx";

    public static void Setup()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded || scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var manager = Object.FindAnyObjectByType<ResponseEquipmentManager>(FindObjectsInactive.Include);
        if (manager == null)
        {
            Debug.LogError("[PPE Table Packing] ResponseEquipmentManager missing.");
            return;
        }

        var packing = manager.GetComponent<ResponseBagPackingController>();
        if (packing == null)
            packing = Undo.AddComponent<ResponseBagPackingController>(manager.gameObject);

        var deskAsset = AssetDatabase.LoadAssetAtPath<GameObject>(DeskAssetPath);
        if (deskAsset != null && GameObject.Find(ResponseBagPackingController.DeskName) == null)
        {
            var desk = (GameObject)PrefabUtility.InstantiatePrefab(deskAsset, scene);
            Undo.RegisterCreatedObjectUndo(desk, "Create PPE_DeskTop");
            desk.name = ResponseBagPackingController.DeskName;
        }

        packing.EnsureTableLayout();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[PPE Table Packing] Desk, bag-mouth trigger, packing slots, and table grab items are ready.");
    }
}
