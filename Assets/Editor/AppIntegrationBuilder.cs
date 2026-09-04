#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HClTraining.App;
using HClTraining.SharedXR;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;

public static class AppIntegrationBuilder
{
    const string AppFolder = "Assets/Scenes";
    const string ProfileFolder = "Assets/Resources/App/StageXRProfiles";
    const string ReportPath = "Logs/AppIntegrationBuild.log";
    static readonly Vector3 CommonBagBodyOffset = new Vector3(-0.52f, 0.65f, 0.14f);

    sealed class LoadedScene
    {
        public Scene scene;
        public bool openedByBuilder;
    }

    public static void BuildAll()
    {
        Directory.CreateDirectory("Logs");
        CloseBatchModeUntitledScenes();
        EnsureAssetFolder(AppFolder);
        EnsureAssetFolder(ProfileFolder);

        var ppe = OpenForRead(SceneFlowController.PpeScenePath, Application.isBatchMode);
        var preEntry = OpenForRead(SceneFlowController.PreEntryScenePath);
        var leak = OpenForRead(SceneFlowController.LeakResponseScenePath);

        try
        {
            var ppeOrigin = FindOrigin(ppe.scene);
            var preEntryOrigin = FindOrigin(preEntry.scene);
            var leakOrigin = FindOrigin(leak.scene);
            if (ppeOrigin == null || preEntryOrigin == null || leakOrigin == null)
                throw new InvalidOperationException("공식 Stage 중 하나에서 XR Origin을 찾지 못했습니다.");

            BuildProfile(TrainingStage.PPE, SceneFlowController.PpeScenePath, ppeOrigin);
            BuildProfile(TrainingStage.PreEntry, SceneFlowController.PreEntryScenePath, preEntryOrigin);
            BuildProfile(TrainingStage.LeakResponse, SceneFlowController.LeakResponseScenePath, leakOrigin);
            BuildAppScene(ppe.scene, ppeOrigin, preEntryOrigin, leakOrigin);
            BuildMenuScene<StartMenuPresenter>(SceneFlowController.StartScenePath, "Start");
            BuildMenuScene<CompleteMenuPresenter>(SceneFlowController.CompleteScenePath, "Complete");
            ConfigureBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var report =
                "Integrated App build succeeded\n" +
                $"App={SceneFlowController.AppScenePath}\n" +
                $"Start={SceneFlowController.StartScenePath}\n" +
                $"PPE={SceneFlowController.PpeScenePath}\n" +
                $"PreEntry={SceneFlowController.PreEntryScenePath}\n" +
                $"LeakResponse={SceneFlowController.LeakResponseScenePath}\n" +
                $"Complete={SceneFlowController.CompleteScenePath}\n" +
                "Common XR baseline=PPE; merged missing components/children from PreEntry and LeakResponse\n";
            File.WriteAllText(ReportPath, report);
            Debug.Log("[App Integration Builder] " + report.Replace('\n', ' '));
        }
        finally
        {
            CloseIfBuilderOpened(leak);
            CloseIfBuilderOpened(preEntry);
            CloseIfBuilderOpened(ppe);
        }
    }

    static void CloseBatchModeUntitledScenes()
    {
        if (!Application.isBatchMode)
            return;
        for (var i = SceneManager.sceneCount - 1; i >= 0; i--)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (scene.IsValid() && scene.isLoaded && string.IsNullOrEmpty(scene.path))
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    static void BuildAppScene(Scene ppeScene, XROrigin ppeOrigin, XROrigin preEntryOrigin, XROrigin leakOrigin)
    {
        CloseCleanGeneratedSceneBeforeOverwrite(SceneFlowController.AppScenePath);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            var commonOrigin = UnityEngine.Object.Instantiate(ppeOrigin.gameObject);
            commonOrigin.name = "Common XR Origin";
            SceneManager.MoveGameObjectToScene(commonOrigin, scene);
            commonOrigin.SetActive(true);
            ApplyCommonBagBodyOffset(commonOrigin.transform);

            var objectMap = new Dictionary<UnityEngine.Object, UnityEngine.Object>();
            MapParallelHierarchy(ppeOrigin.transform, commonOrigin.transform, objectMap);
            ClonePpeSharedRoots(ppeScene, ppeOrigin.transform, scene, objectMap);

            MergeMissingOriginContent(preEntryOrigin.transform, commonOrigin.transform, objectMap);
            MergeMissingOriginContent(leakOrigin.transform, commonOrigin.transform, objectMap);
            RemapSerializedReferences(scene, objectMap, commonOrigin.transform);
            ConfigureCommonXrContracts(commonOrigin.transform);

            var appRoot = new GameObject("App");
            SceneManager.MoveGameObjectToScene(appRoot, scene);
            var bootstrap = appRoot.AddComponent<AppBootstrap>();
            var serialized = new SerializedObject(bootstrap);
            serialized.FindProperty("commonXrOrigin").objectReferenceValue = commonOrigin;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            if (!EditorSceneManager.SaveScene(scene, SceneFlowController.AppScenePath))
                throw new InvalidOperationException("App Scene 저장에 실패했습니다.");
        }
        finally
        {
            if (scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    static void ClonePpeSharedRoots(
        Scene ppeScene,
        Transform ppeOrigin,
        Scene appScene,
        Dictionary<UnityEngine.Object, UnityEngine.Object> objectMap)
    {
        var selectedRoots = new HashSet<GameObject>();
        foreach (var root in ppeScene.GetRootGameObjects())
        {
            if (root == ppeOrigin.gameObject || ppeOrigin.IsChildOf(root.transform))
                continue;

            var components = root.GetComponentsInChildren<Component>(true);
            var include = components.Any(component => component != null &&
                (component is EventSystem ||
                 component.GetType().Name == "XRInteractionManager" ||
                 component.GetType().Name == "InputActionManager"));
            if (include)
                selectedRoots.Add(root);
        }

        foreach (var sourceRoot in selectedRoots)
        {
            var clone = UnityEngine.Object.Instantiate(sourceRoot);
            clone.name = sourceRoot.name;
            SceneManager.MoveGameObjectToScene(clone, appScene);
            MapParallelHierarchy(sourceRoot.transform, clone.transform, objectMap);
        }
    }

    static void ApplyCommonBagBodyOffset(Transform commonOrigin)
    {
        var bodyAnchor = commonOrigin != null ? commonOrigin.Find("PlayerBodyAnchor") : null;
        var follower = bodyAnchor != null ? bodyAnchor.GetComponent<PlayerBodyAnchorFollower>() : null;
        if (follower == null)
            throw new InvalidOperationException("Common PlayerBodyAnchorFollower is missing.");

        var serialized = new SerializedObject(follower);
        serialized.FindProperty("localPositionOffset").vector3Value = CommonBagBodyOffset;
        serialized.FindProperty("followCameraHeight").boolValue = false;
        serialized.FindProperty("useStableOriginBodyFrame").boolValue = true;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    static void ConfigureCommonXrContracts(Transform commonOrigin)
    {
        var xrCamera = commonOrigin != null ? commonOrigin.GetComponentInChildren<Camera>(true) : null;
        var move = commonOrigin != null ? commonOrigin.GetComponent<ContinuousMoveProvider>() : null;
        var turn = commonOrigin != null ? commonOrigin.GetComponent<SnapTurnProvider>() : null;
        var vignette = commonOrigin != null
            ? commonOrigin.GetComponentInChildren<TunnelingVignetteController>(true)
            : null;
        if (move == null || turn == null || vignette == null)
            throw new InvalidOperationException("Common XR locomotion/vignette components are incomplete.");

        // Quest screenshots can include pixels outside the asymmetric eye frustum.
        // Never expose the Starter Assets blue camera fallback in those pixels.
        if (xrCamera != null)
            xrCamera.backgroundColor = Color.black;

        var rightMove = move.rightHandMoveInput ??
            new XRInputValueReader<Vector2>("Right Hand Move", XRInputValueReader.InputSourceMode.Unused);
        rightMove.inputSourceMode = XRInputValueReader.InputSourceMode.Unused;
        rightMove.inputActionReference = null;
        move.rightHandMoveInput = rightMove;

        var leftTurn = turn.leftHandTurnInput ??
            new XRInputValueReader<Vector2>("Left Hand Snap Turn", XRInputValueReader.InputSourceMode.Unused);
        leftTurn.inputSourceMode = XRInputValueReader.InputSourceMode.Unused;
        leftTurn.inputActionReference = null;
        turn.leftHandTurnInput = leftTurn;

        vignette.enabled = true;
        if (vignette.GetComponent<ComfortVignetteVisibilityGuard>() == null)
            vignette.gameObject.AddComponent<ComfortVignetteVisibilityGuard>();
        var vignetteRenderer = vignette.GetComponent<MeshRenderer>();
        if (vignetteRenderer != null)
            vignetteRenderer.enabled = false;
        // Aperture 1 is fully open: no comfort mesh is visible while idle.
        // Locomotion providers below still close it during move/snap-turn.
        vignette.defaultParameters = CreateVignetteParameters(1f, 0.25f, 0.04f, 0.10f, 0.02f);
        vignette.locomotionVignetteProviders = new List<LocomotionVignetteProvider>
        {
            new LocomotionVignetteProvider
            {
                locomotionProvider = move,
                enabled = true,
                overrideDefaultParameters = true,
                overrideParameters = CreateVignetteParameters(0.68f, 0.15f, 0.08f, 0.15f, 0.03f),
            },
            new LocomotionVignetteProvider
            {
                locomotionProvider = turn,
                enabled = true,
                overrideDefaultParameters = true,
                overrideParameters = CreateVignetteParameters(0.78f, 0.15f, 0.08f, 0.15f, 0.03f),
            },
        };

        foreach (var side in new[] { "Left", "Right" })
        {
            var controller = commonOrigin.Find($"Camera Offset/{side} Controller");
            var ray = controller != null ? controller.Find("Ray Interactor") : null;
            if (ray == null)
                throw new InvalidOperationException($"Common XR {side} Ray Interactor is missing.");
            ray.localRotation = Quaternion.identity;
        }
    }

    static VignetteParameters CreateVignetteParameters(
        float aperture,
        float feather,
        float easeIn,
        float easeOut,
        float delay)
    {
        return new VignetteParameters
        {
            apertureSize = aperture,
            featheringEffect = feather,
            easeInTime = easeIn,
            easeOutTime = easeOut,
            easeInTimeLock = true,
            easeOutDelayTime = delay,
            vignetteColor = Color.black,
            vignetteColorBlend = Color.black,
        };
    }

    static void MergeMissingOriginContent(
        Transform sourceRoot,
        Transform targetRoot,
        Dictionary<UnityEngine.Object, UnityEngine.Object> objectMap)
    {
        var sourceTransforms = sourceRoot.GetComponentsInChildren<Transform>(true);
        for (var i = 0; i < sourceTransforms.Length; i++)
        {
            var source = sourceTransforms[i];
            var path = GetRelativePath(sourceRoot, source);
            var target = string.IsNullOrEmpty(path) ? targetRoot : targetRoot.Find(path);
            if (target == null)
            {
                var parentPath = GetRelativePath(sourceRoot, source.parent);
                var targetParent = string.IsNullOrEmpty(parentPath) ? targetRoot : targetRoot.Find(parentPath);
                if (targetParent == null)
                    throw new InvalidOperationException($"XR merge parent missing: {parentPath}");
                var go = new GameObject(source.name);
                target = go.transform;
                target.SetParent(targetParent, false);
                target.localPosition = source.localPosition;
                target.localRotation = source.localRotation;
                target.localScale = source.localScale;
                go.layer = source.gameObject.layer;
                go.tag = source.gameObject.tag;
                go.SetActive(false);
            }

            objectMap[source] = target;
            objectMap[source.gameObject] = target.gameObject;

            var sourceComponents = source.GetComponents<Component>();
            var perTypeIndex = new Dictionary<Type, int>();
            for (var componentIndex = 0; componentIndex < sourceComponents.Length; componentIndex++)
            {
                var sourceComponent = sourceComponents[componentIndex];
                if (sourceComponent == null || sourceComponent is Transform)
                    continue;
                var type = sourceComponent.GetType();
                if (!IsXrMergeComponent(type))
                    continue;
                perTypeIndex.TryGetValue(type, out var index);
                perTypeIndex[type] = index + 1;
                var targets = target.GetComponents(type);
                Component targetComponent;
                if (index < targets.Length)
                {
                    targetComponent = targets[index];
                }
                else
                {
                    ComponentUtility.CopyComponent(sourceComponent);
                    if (!ComponentUtility.PasteComponentAsNew(target.gameObject))
                    {
                        Debug.LogWarning($"[App Integration Builder] Could not merge {type.FullName} at {path}.");
                        continue;
                    }
                    targets = target.GetComponents(type);
                    targetComponent = targets[targets.Length - 1];
                    if (targetComponent is Behaviour behaviour)
                        behaviour.enabled = false;
                }
                objectMap[sourceComponent] = targetComponent;
            }
        }
    }

    static bool IsXrMergeComponent(Type type)
    {
        var ns = type.Namespace ?? string.Empty;
        var name = type.Name;
        return ns.StartsWith("UnityEngine.XR", StringComparison.Ordinal) ||
               ns.StartsWith("Unity.XR", StringComparison.Ordinal) ||
               name.IndexOf("Locomotion", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("MoveProvider", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("TurnProvider", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("TeleportationProvider", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Interactor", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("InputActionManager", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("ControllerInputAction", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static void RemapSerializedReferences(
        Scene appScene,
        Dictionary<UnityEngine.Object, UnityEngine.Object> objectMap,
        Transform commonOrigin)
    {
        foreach (var root in appScene.GetRootGameObjects())
        {
            var components = root.GetComponentsInChildren<Component>(true);
            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null)
                    continue;
                var serialized = new SerializedObject(component);
                var iterator = serialized.GetIterator();
                var changed = false;
                if (iterator.Next(true))
                {
                    do
                    {
                        if (iterator.propertyType != SerializedPropertyType.ObjectReference || iterator.objectReferenceValue == null)
                            continue;
                        if (objectMap.TryGetValue(iterator.objectReferenceValue, out var mapped) && mapped != null)
                        {
                            iterator.objectReferenceValue = mapped;
                            changed = true;
                            continue;
                        }

                        if (iterator.objectReferenceValue is Component referencedComponent &&
                            (referencedComponent.GetType().Name == "XRInteractionManager" ||
                             referencedComponent.GetType().Name == "InputActionManager"))
                        {
                            var replacement = FindComponentInScene(appScene, referencedComponent.GetType());
                            if (replacement != null)
                            {
                                iterator.objectReferenceValue = replacement;
                                changed = true;
                            }
                        }
                    }
                    while (iterator.Next(true));
                }
                if (changed)
                    serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }

    static Component FindComponentInScene(Scene scene, Type type)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var component = root.GetComponentInChildren(type, true);
            if (component != null)
                return component;
        }
        return null;
    }

    static void MapParallelHierarchy(
        Transform sourceRoot,
        Transform targetRoot,
        Dictionary<UnityEngine.Object, UnityEngine.Object> objectMap)
    {
        var sourceTransforms = sourceRoot.GetComponentsInChildren<Transform>(true);
        for (var i = 0; i < sourceTransforms.Length; i++)
        {
            var source = sourceTransforms[i];
            var path = GetRelativePath(sourceRoot, source);
            var target = string.IsNullOrEmpty(path) ? targetRoot : targetRoot.Find(path);
            if (target == null)
                continue;
            objectMap[source] = target;
            objectMap[source.gameObject] = target.gameObject;
            var sourceComponents = source.GetComponents<Component>();
            var targetComponents = target.GetComponents<Component>();
            var counts = new Dictionary<Type, int>();
            for (var j = 0; j < sourceComponents.Length; j++)
            {
                var sourceComponent = sourceComponents[j];
                if (sourceComponent == null)
                    continue;
                var type = sourceComponent.GetType();
                counts.TryGetValue(type, out var typeIndex);
                counts[type] = typeIndex + 1;
                var matches = targetComponents.Where(candidate => candidate != null && candidate.GetType() == type).ToArray();
                if (typeIndex < matches.Length)
                    objectMap[sourceComponent] = matches[typeIndex];
            }
        }
    }

    static void BuildProfile(TrainingStage stage, string scenePath, XROrigin origin)
    {
        var states = new List<StageXRProfile.ComponentState>();
        var behaviours = origin.GetComponentsInChildren<Behaviour>(true);
        for (var i = 0; i < behaviours.Length; i++)
        {
            var behaviour = behaviours[i];
            if (behaviour == null)
                continue;
            states.Add(new StageXRProfile.ComponentState
            {
                relativePath = GetRelativePath(origin.transform, behaviour.transform),
                typeName = behaviour.GetType().AssemblyQualifiedName,
                enabled = behaviour.enabled && behaviour.gameObject.activeInHierarchy,
            });
        }

        var assetPath = $"{ProfileFolder}/{stage}.asset";
        var profile = AssetDatabase.LoadAssetAtPath<StageXRProfile>(assetPath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<StageXRProfile>();
            AssetDatabase.CreateAsset(profile, assetPath);
        }
        profile.EditorConfigure(stage, scenePath, origin.transform.position, origin.transform.rotation, states);
        EditorUtility.SetDirty(profile);
    }

    static void BuildMenuScene<T>(string path, string rootName) where T : Component
    {
        CloseCleanGeneratedSceneBeforeOverwrite(path);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            var root = new GameObject(rootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            root.AddComponent<T>();
            if (!EditorSceneManager.SaveScene(scene, path))
                throw new InvalidOperationException($"{rootName} Scene 저장에 실패했습니다.");
        }
        finally
        {
            if (scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    static void ConfigureBuildSettings()
    {
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(SceneFlowController.AppScenePath, true),
            new EditorBuildSettingsScene(SceneFlowController.TitleScenePath, true),
            new EditorBuildSettingsScene(SceneFlowController.StartScenePath, true),
            new EditorBuildSettingsScene(SceneFlowController.PpeScenePath, true),
            new EditorBuildSettingsScene(SceneFlowController.PreEntryScenePath, true),
            new EditorBuildSettingsScene(SceneFlowController.LeakResponseScenePath, true),
            new EditorBuildSettingsScene(SceneFlowController.CompleteScenePath, true),
        };
    }

    static LoadedScene OpenForRead(string path, bool openSingle = false)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Required Scene is missing", path);
        var existing = SceneManager.GetSceneByPath(path);
        if (existing.IsValid() && existing.isLoaded)
            return new LoadedScene { scene = existing, openedByBuilder = false };
        return new LoadedScene
        {
            scene = EditorSceneManager.OpenScene(path, openSingle ? OpenSceneMode.Single : OpenSceneMode.Additive),
            openedByBuilder = true,
        };
    }

    static void CloseCleanGeneratedSceneBeforeOverwrite(string path)
    {
        var existing = SceneManager.GetSceneByPath(path);
        if (!existing.IsValid() || !existing.isLoaded)
            return;
        if (existing.isDirty)
            throw new InvalidOperationException(
                $"Generated Scene '{path}' has unsaved Editor changes and cannot be overwritten safely.");
        EditorSceneManager.CloseScene(existing, true);
    }

    static XROrigin FindOrigin(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var origin = root.GetComponentInChildren<XROrigin>(true);
            if (origin != null)
                return origin;
        }
        return null;
    }

    static void CloseIfBuilderOpened(LoadedScene loaded)
    {
        if (loaded != null && loaded.openedByBuilder && loaded.scene.IsValid() && loaded.scene.isLoaded &&
            SceneManager.sceneCount > 1)
            EditorSceneManager.CloseScene(loaded.scene, true);
    }

    static string GetRelativePath(Transform root, Transform target)
    {
        if (target == null || target == root)
            return string.Empty;
        var parts = new Stack<string>();
        for (var current = target; current != null && current != root; current = current.parent)
            parts.Push(current.name);
        return string.Join("/", parts);
    }

    static void EnsureAssetFolder(string path)
    {
        var parts = path.Split('/');
        var current = parts[0];
        for (var i = 1; i < parts.Length; i++)
        {
            var next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}

[InitializeOnLoad]
public static class AppIntegrationAutomation
{
    const string RequestPath = "Temp/AppIntegrationBuild.request";
    const string DonePath = "Temp/AppIntegrationBuild.done";
    const string FailurePath = "Temp/AppIntegrationBuild.failed";

    static AppIntegrationAutomation()
    {
        EditorApplication.delayCall += TryRun;
    }

    static void TryRun()
    {
        if (!File.Exists(RequestPath) || EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            if (File.Exists(RequestPath))
                EditorApplication.delayCall += TryRun;
            return;
        }

        File.Delete(RequestPath);
        try
        {
            AppIntegrationBuilder.BuildAll();
            IntegrationSceneValidator.Validate();
            File.WriteAllText(DonePath, DateTime.Now.ToString("O"));
            if (File.Exists(FailurePath))
                File.Delete(FailurePath);
        }
        catch (Exception exception)
        {
            File.WriteAllText(FailurePath, exception.ToString());
            Debug.LogException(exception);
        }
    }
}
#endif
