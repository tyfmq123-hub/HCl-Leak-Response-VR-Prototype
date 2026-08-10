#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class IntegrationSceneValidator
{
    static readonly string[] TargetScenes =
    {
        "Assets/Scenes/PPE_PreparationRoom 1.unity",
        "Assets/3.JH/Scenes/Prototype/JH_Test_Scene.unity",
        "Assets/Scenes/SSH_VR_Project.unity",
    };

    public static void Validate()
    {
        var failures = new List<string>();

        foreach (var scenePath in TargetScenes)
        {
            if (!System.IO.File.Exists(scenePath))
            {
                failures.Add($"Scene file does not exist: {scenePath}");
                continue;
            }

            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                failures.Add($"Scene could not be opened: {scenePath}");
                continue;
            }

            var xrOriginCount = 0;
            var missingScriptCount = 0;
            var missingReferences = new List<string>();

            foreach (var root in scene.GetRootGameObjects())
            {
                xrOriginCount += root.GetComponentsInChildren<XROrigin>(true).Length;

                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    var gameObject = transform.gameObject;
                    missingScriptCount += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);

                    foreach (var component in gameObject.GetComponents<Component>())
                    {
                        if (component == null)
                            continue;

                        try
                        {
                            var iterator = new SerializedObject(component).GetIterator();
                            if (!iterator.NextVisible(true))
                                continue;

                            do
                            {
                                if (iterator.propertyType == SerializedPropertyType.ObjectReference &&
                                    iterator.objectReferenceValue == null &&
                                    iterator.objectReferenceInstanceIDValue != 0)
                                {
                                    missingReferences.Add($"{GetHierarchyPath(gameObject)} :: {component.GetType().Name}.{iterator.propertyPath}");
                                }
                            }
                            while (iterator.NextVisible(false));
                        }
                        catch (Exception exception)
                        {
                            failures.Add($"Could not inspect {scenePath} / {GetHierarchyPath(gameObject)} / {component.GetType().Name}: {exception.Message}");
                        }
                    }
                }
            }

            if (xrOriginCount != 1)
                failures.Add($"{scenePath}: expected exactly one XR Origin, found {xrOriginCount}");

            if (missingScriptCount != 0)
                failures.Add($"{scenePath}: found {missingScriptCount} missing script component(s)");

            foreach (var missingReference in missingReferences)
                failures.Add($"{scenePath}: missing reference at {missingReference}");

            Debug.Log($"[Integration Scene Validation] {scenePath}: opened with Single mode; XR Origins={xrOriginCount}; Missing Scripts={missingScriptCount}; Missing References={missingReferences.Count}");
        }

        if (failures.Count != 0)
            throw new InvalidOperationException("Integration scene validation failed:\n" + string.Join("\n", failures));

        Debug.Log("[Integration Scene Validation] All target scenes passed.");
    }

    static string GetHierarchyPath(GameObject gameObject)
    {
        var path = gameObject.name;
        for (var parent = gameObject.transform.parent; parent != null; parent = parent.parent)
            path = parent.name + "/" + path;

        return path;
    }
}
#endif
