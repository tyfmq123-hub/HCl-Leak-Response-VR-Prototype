#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Unity.XR.CoreUtils;

/// <summary>
/// Builds / updates the persistent ResponseBagGearPanel prefab (thumbnail-based cards).
/// </summary>
public static class ResponseBagGearPanelPrefabBuilder
{
    const string PrefabDir = "Assets/1.JSR/Prefabs";
    const string PrefabPath = ResponseBagGearPanel.PrefabAssetPath;
    const string ResourcesPrefabDir = "Assets/Resources/PPE";
    const string ResourcesPrefabPath = "Assets/1.JSR/Resources/ResponseBagGearPanel.prefab";

    public static void BuildPrefab()
    {
        EnsureFolder("Assets/Prefabs");
        EnsureFolder(PrefabDir);
        EnsureFolder("Assets/Resources");
        EnsureFolder(ResourcesPrefabDir);

        var root = ResponseBagGearPanel.BuildRuntimePanel();
        root.name = "ResponseBagGearPanel";

        // Prefer already-rendered Resources thumbnails if present.
        var panel = root.GetComponent<ResponseBagGearPanel>();
        panel.ApplyFrontOfBagPlacementDefaults();

        // Save under Prefabs, then copy into Resources for runtime Load.
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        if (prefab == null)
        {
            Debug.LogError("[PPE] Failed to save " + PrefabPath);
            return;
        }

        AssetDatabase.DeleteAsset(ResourcesPrefabPath);
        AssetDatabase.CopyAsset(PrefabPath, ResourcesPrefabPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Wire default thumbnail sprites onto the Resources prefab slots if available.
        WireThumbnailSprites(ResourcesPrefabPath);
        WireThumbnailSprites(PrefabPath);

        Debug.Log("[PPE] Prefab ready:\n" + PrefabPath + "\n" + ResourcesPrefabPath);
        EditorUtility.DisplayDialog(
            "PPE Gear Panel Prefab",
            "Saved:\n" + PrefabPath + "\n\nAlso mirrored to Resources for runtime load.\n\n" +
            "If thumbnails are blank, run:\nTools > PPE > Render Response Bag Thumbnails",
            "OK");

        Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
    }

    static void WireThumbnailSprites(string prefabPath)
    {
        var prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            var panel = prefabRoot.GetComponent<ResponseBagGearPanel>();
            if (panel == null)
                return;

            var so = new SerializedObject(panel);
            var slots = so.FindProperty("slots");
            if (slots == null || !slots.isArray)
                return;

            for (var i = 0; i < slots.arraySize; i++)
            {
                var slot = slots.GetArrayElementAtIndex(i);
                var itemId = slot.FindPropertyRelative("itemId")?.stringValue;
                var spriteProp = slot.FindPropertyRelative("previewSprite");
                var sprite = LoadThumb(itemId);
                if (spriteProp != null && sprite != null)
                    spriteProp.objectReferenceValue = sprite;

                var imageProp = slot.FindPropertyRelative("previewImage");
                var image = imageProp?.objectReferenceValue as Image;
                if (image != null && sprite != null)
                {
                    image.sprite = sprite;
                    image.preserveAspect = true;
                    image.color = Color.white;
                }
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    static Sprite LoadThumb(string itemId)
    {
        if (itemId == ResponseEquipmentManager.IdGasDetector)
            return AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/1.JSR/Resources/Thumbnails/ResponseBag_GasDetector.png");
        if (itemId == ResponseEquipmentManager.IdAbsorbentSock)
            return AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/1.JSR/Resources/Thumbnails/ResponseBag_AbsorbentSock.png");
        if (itemId == ResponseEquipmentManager.IdAbsorbentPad)
            return AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/1.JSR/Resources/Thumbnails/ResponseBag_AbsorbentPad.png");
        return null;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        var name = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }
}
#endif
