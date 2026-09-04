using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Fills restored stub hand prefabs with the imported vr hand FBX meshes.
/// Menu: Tools/VR/Fill VR Hand Prefabs From FBX
/// </summary>
public static class VrHandPrefabRestore
{
    const string LeftFbxPath = "Assets/Common/VRHands/vr hand_Left.fbx";
    const string RightFbxPath = "Assets/Common/VRHands/vr hand_Right.fbx";
    const string LeftPrefabPath = "Assets/Common/VRHands/vr hand_Left.prefab";
    const string RightPrefabPath = "Assets/Common/VRHands/vr hand_Right.prefab";

    public static void FillHandPrefabsFromFbx()
    {
        FillOne(LeftFbxPath, LeftPrefabPath, "vr hand_Left");
        FillOne(RightFbxPath, RightPrefabPath, "vr hand_Right");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[VR Hands] Filled vr hand_Left/Right prefabs from FBX. Left/Right Controller nested refs should resolve.");
    }

    static void FillOne(string fbxPath, string prefabPath, string rootName)
    {
        if (!File.Exists(fbxPath))
        {
            Debug.LogError("[VR Hands] Missing FBX: " + fbxPath);
            return;
        }

        if (!File.Exists(prefabPath))
        {
            Debug.LogError("[VR Hands] Missing prefab: " + prefabPath);
            return;
        }

        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        var prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            prefabRoot.name = rootName;

            // Remove previous mesh children (keep root Animator/Transform).
            for (var i = prefabRoot.transform.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(prefabRoot.transform.GetChild(i).gameObject);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(fbx, prefabRoot.transform);
            instance.name = fbx.name;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            // Prefer avatar/animator from the FBX model on the prefab root.
            var rootAnimator = prefabRoot.GetComponent<Animator>();
            var modelAnimator = instance.GetComponentInChildren<Animator>();
            if (rootAnimator != null && modelAnimator != null)
            {
                rootAnimator.avatar = modelAnimator.avatar;
                rootAnimator.runtimeAnimatorController = modelAnimator.runtimeAnimatorController;
            }

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
            Debug.Log($"[VR Hands] Updated {prefabPath} with mesh from {fbxPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }
}
