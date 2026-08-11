#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using HClTraining.App;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class IntegrationSceneValidator
{
    static readonly (string path, int expectedXrOrigins)[] TargetScenes =
    {
        (SceneFlowController.AppScenePath, 1),
        (SceneFlowController.StartScenePath, 0),
        (SceneFlowController.PpeScenePath, 1),
        (SceneFlowController.PreEntryScenePath, 1),
        (SceneFlowController.LeakResponseScenePath, 1),
        (SceneFlowController.CompleteScenePath, 0),
    };

    [MenuItem("Tools/HCl Training/Validate Integrated App")]
    public static void Validate()
    {
        var failures = new List<string>();
        var report = new List<string>();

        if (EditorBuildSettings.scenes.Length == 0 ||
            EditorBuildSettings.scenes[0].path != SceneFlowController.AppScenePath ||
            !EditorBuildSettings.scenes[0].enabled)
        {
            failures.Add("Build Settings의 첫 활성 Scene이 App이 아닙니다.");
        }

        foreach (var target in TargetScenes)
            ValidateScene(target.path, target.expectedXrOrigins, failures, report);

        foreach (TrainingStage stage in new[] { TrainingStage.PPE, TrainingStage.PreEntry, TrainingStage.LeakResponse })
        {
            var profile = AssetDatabase.LoadAssetAtPath<StageXRProfile>(
                $"Assets/Resources/App/StageXRProfiles/{stage}.asset");
            if (profile == null)
                failures.Add($"Stage XR Profile 누락: {stage}");
            else if (profile.ComponentStates.Count == 0)
                failures.Add($"Stage XR Profile이 비어 있습니다: {stage}");
        }

        File.WriteAllText("Logs/AppIntegrationValidation.log",
            string.Join("\n", report) + "\nFailures=" + failures.Count + "\n" + string.Join("\n", failures));

        if (failures.Count != 0)
            throw new InvalidOperationException("Integration scene validation failed:\n" + string.Join("\n", failures));

        Debug.Log("[Integration Scene Validation] All six Scenes, XR profiles, serialization, and Build Settings passed.");
    }

    static void ValidateScene(
        string scenePath,
        int expectedXrOrigins,
        List<string> failures,
        List<string> report)
    {
        if (!File.Exists(scenePath))
        {
            failures.Add($"Scene file does not exist: {scenePath}");
            return;
        }

        var scene = SceneManager.GetSceneByPath(scenePath);
        var openedHere = !scene.IsValid() || !scene.isLoaded;
        if (openedHere)
            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

        try
        {
            var xrOriginCount = 0;
            var missingScriptCount = 0;
            var brokenReferenceCount = 0;
            var appBootstrapCount = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                xrOriginCount += root.GetComponentsInChildren<XROrigin>(true).Length;
                appBootstrapCount += root.GetComponentsInChildren<AppBootstrap>(true).Length;

                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    var gameObject = transform.gameObject;
                    missingScriptCount += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
                    foreach (var component in gameObject.GetComponents<Component>())
                    {
                        if (component == null)
                            continue;
                        var iterator = new SerializedObject(component).GetIterator();
                        if (!iterator.NextVisible(true))
                            continue;
                        do
                        {
                            if (iterator.propertyType == SerializedPropertyType.ObjectReference &&
                                iterator.objectReferenceValue == null &&
                                iterator.objectReferenceInstanceIDValue != 0)
                            {
                                brokenReferenceCount++;
                                failures.Add($"{scenePath}: broken reference at {GetHierarchyPath(gameObject)} :: " +
                                             $"{component.GetType().Name}.{iterator.propertyPath}");
                            }
                        }
                        while (iterator.NextVisible(false));
                    }
                }
            }

            if (xrOriginCount != expectedXrOrigins)
                failures.Add($"{scenePath}: expected XR Origins={expectedXrOrigins}, found {xrOriginCount}");
            if (missingScriptCount != 0)
                failures.Add($"{scenePath}: missing scripts={missingScriptCount}");
            if (scenePath == SceneFlowController.AppScenePath && appBootstrapCount != 1)
                failures.Add($"{scenePath}: expected exactly one AppBootstrap, found {appBootstrapCount}");

            report.Add($"{scenePath}: XR={xrOriginCount}, MissingScripts={missingScriptCount}, " +
                       $"BrokenReferences={brokenReferenceCount}, AppBootstrap={appBootstrapCount}");
        }
        finally
        {
            if (openedHere && scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, true);
        }
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
