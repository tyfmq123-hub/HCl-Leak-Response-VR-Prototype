using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;

/// <summary>
/// Restores Left/Right Controllers under XR Origin (VR):
/// TrackedPoseDriver + XR Controller visual models + Ray Interactors.
/// </summary>
public static class XRControllerRestoreSetup
{
    const string LeftVisualGuid = "1392f805216c47742996d4742c80721c";
    const string RightVisualGuid = "9f3369e30fbd31f4bb596b1a99babe83";
    const string RayInteractorGuid = "ad818c36731146e994540a1896ad8f24";

    public static void RestoreControllers()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[XR Controllers] No active scene.");
            return;
        }

        var origin = FindXrOrigin();
        if (origin == null)
        {
            Debug.LogError("[XR Controllers] XR Origin (VR) not found.");
            return;
        }

        var cameraOffset = origin.transform.Find("Camera Offset");
        if (cameraOffset == null)
        {
            Debug.LogError("[XR Controllers] Camera Offset not found under XR Origin.");
            return;
        }

        var leftVisual = LoadPrefab(LeftVisualGuid, "XR Controller Left");
        var rightVisual = LoadPrefab(RightVisualGuid, "XR Controller Right");
        var rayPrefab = LoadPrefab(RayInteractorGuid, "Ray Interactor");
        if (leftVisual == null || rightVisual == null || rayPrefab == null)
            return;

        var left = EnsureController(
            cameraOffset,
            "Left Controller",
            "<XRController>{LeftHand}/devicePosition",
            "<XRController>{LeftHand}/deviceRotation",
            "<XRController>{LeftHand}/trackingState",
            leftVisual,
            "XR Controller Left",
            rayPrefab);

        var right = EnsureController(
            cameraOffset,
            "Right Controller",
            "<XRController>{RightHand}/devicePosition",
            "<XRController>{RightHand}/deviceRotation",
            "<XRController>{RightHand}/trackingState",
            rightVisual,
            "XR Controller Right",
            rayPrefab);

        EditorSceneManager.MarkSceneDirty(scene);
        if (scene.isDirty && !string.IsNullOrEmpty(scene.path))
            EditorSceneManager.SaveScene(scene);

        Selection.activeObject = left != null ? left : right;
        Debug.Log(
            "[XR Controllers] Restored Left/Right Controllers with Quest visuals + Ray Interactors under Camera Offset.",
            origin);
    }

    static GameObject FindXrOrigin()
    {
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name.StartsWith("XR Origin", StringComparison.Ordinal))
                return root;
            var xr = root.GetComponentInChildren<XROrigin>(true);
            if (xr != null)
                return xr.gameObject;
        }

        return GameObject.Find("XR Origin (VR)");
    }

    static GameObject LoadPrefab(string guid, string label)
    {
        var path = AssetDatabase.GUIDToAssetPath(guid);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
            Debug.LogError($"[XR Controllers] Missing {label} prefab (guid {guid}, path '{path}').");
        return prefab;
    }

    static GameObject EnsureController(
        Transform cameraOffset,
        string controllerName,
        string positionPath,
        string rotationPath,
        string trackingStatePath,
        GameObject visualPrefab,
        string visualInstanceName,
        GameObject rayPrefab)
    {
        var existing = cameraOffset.Find(controllerName);
        if (existing != null)
            Undo.DestroyObjectImmediate(existing.gameObject);

        var controller = new GameObject(controllerName);
        Undo.RegisterCreatedObjectUndo(controller, "Create " + controllerName);
        Undo.SetTransformParent(controller.transform, cameraOffset, "Parent controller");
        controller.transform.localPosition = Vector3.zero;
        controller.transform.localRotation = Quaternion.identity;
        controller.transform.localScale = Vector3.one;

        var tpd = Undo.AddComponent<TrackedPoseDriver>(controller);
        ConfigureTrackedPose(tpd, positionPath, rotationPath, trackingStatePath);

        var visuals = new GameObject("Visuals");
        Undo.RegisterCreatedObjectUndo(visuals, "Create Visuals");
        Undo.SetTransformParent(visuals.transform, controller.transform, "Parent visuals");
        visuals.transform.localPosition = Vector3.zero;
        // Quest mesh pitch fix used previously in this project (Visuals X +25°).
        visuals.transform.localRotation = Quaternion.Euler(25f, 0f, 0f);
        visuals.transform.localScale = Vector3.one;

        var visualInstance = (GameObject)PrefabUtility.InstantiatePrefab(visualPrefab);
        Undo.RegisterCreatedObjectUndo(visualInstance, "Instantiate controller visual");
        visualInstance.name = visualInstanceName;
        Undo.SetTransformParent(visualInstance.transform, visuals.transform, "Parent visual model");
        visualInstance.transform.localPosition = new Vector3(0f, 0f, -0.05f);
        // Official XR Origin style: model faces tracking correctly with Y 180°.
        visualInstance.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var isRight = visualInstanceName.IndexOf("Right", StringComparison.OrdinalIgnoreCase) >= 0;
        visualInstance.transform.localScale = isRight
            ? new Vector3(-1f, 1f, 1f)
            : Vector3.one;

        var rayInstance = (GameObject)PrefabUtility.InstantiatePrefab(rayPrefab);
        Undo.RegisterCreatedObjectUndo(rayInstance, "Instantiate Ray Interactor");
        rayInstance.name = "Ray Interactor";
        Undo.SetTransformParent(rayInstance.transform, controller.transform, "Parent ray");
        rayInstance.transform.localPosition = Vector3.zero;
        rayInstance.transform.localRotation = Quaternion.identity;
        rayInstance.transform.localScale = Vector3.one;

        // Prefer Input System XR controller pose; wire Ray Select/Activate to XRI Default.
        var rayReport = new System.Text.StringBuilder();
        PPERayInteractableSetup.WireRayInteractorSelectAndActivateInputs(rayReport);
        if (rayReport.Length > 0)
            Debug.Log(rayReport.ToString());

        return controller;
    }

    static void ConfigureTrackedPose(
        TrackedPoseDriver tpd,
        string positionPath,
        string rotationPath,
        string trackingStatePath)
    {
        var so = new SerializedObject(tpd);
        so.FindProperty("m_TrackingType").enumValueIndex = 0;
        so.FindProperty("m_UpdateType").enumValueIndex = 0;
        so.FindProperty("m_IgnoreTrackingState").boolValue = false;

        BindInlineAction(so.FindProperty("m_PositionInput"), "Position", "Vector3", positionPath);
        BindInlineAction(so.FindProperty("m_RotationInput"), "Rotation", "Quaternion", rotationPath);
        BindInlineAction(so.FindProperty("m_TrackingStateInput"), "Tracking State", "Integer", trackingStatePath);

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(tpd);
    }

    static void BindInlineAction(SerializedProperty inputProp, string actionName, string expectedType, string path)
    {
        if (inputProp == null)
            return;

        inputProp.FindPropertyRelative("m_UseReference").boolValue = false;
        var action = inputProp.FindPropertyRelative("m_Action");
        action.FindPropertyRelative("m_Name").stringValue = actionName;
        action.FindPropertyRelative("m_Type").intValue = 0;
        action.FindPropertyRelative("m_ExpectedControlType").stringValue = expectedType;
        action.FindPropertyRelative("m_Id").stringValue = Guid.NewGuid().ToString();
        action.FindPropertyRelative("m_Processors").stringValue = "";
        action.FindPropertyRelative("m_Interactions").stringValue = "";
        action.FindPropertyRelative("m_Flags").intValue = 0;

        var bindings = action.FindPropertyRelative("m_SingletonActionBindings");
        bindings.ClearArray();
        bindings.arraySize = 1;
        var b = bindings.GetArrayElementAtIndex(0);
        b.FindPropertyRelative("m_Id").stringValue = Guid.NewGuid().ToString();
        b.FindPropertyRelative("m_Path").stringValue = path;
        b.FindPropertyRelative("m_Action").stringValue = actionName;
        b.FindPropertyRelative("m_Flags").intValue = 0;
    }
}
