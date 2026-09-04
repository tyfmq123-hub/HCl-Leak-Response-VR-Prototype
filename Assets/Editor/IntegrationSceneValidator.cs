#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HClTraining.App;
using HClTraining.SharedXR;
using JH.EntrySafety;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using VRProject.Valves;

public static class IntegrationSceneValidator
{
    static readonly (string path, int expectedXrOrigins)[] TargetScenes =
    {
        (SceneFlowController.AppScenePath, 1),
        (SceneFlowController.TitleScenePath, 1),
        (SceneFlowController.StartScenePath, 0),
        (SceneFlowController.PpeScenePath, 1),
        (SceneFlowController.PreEntryScenePath, 1),
        (SceneFlowController.LeakResponseScenePath, 1),
        (SceneFlowController.CompleteScenePath, 0),
    };

    public static void Validate()
    {
        var failures = new List<string>();
        var report = new List<string>();

        ValidateBuildSettings(failures, report);

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

        ValidateStageRequirements(failures, report);

        File.WriteAllText("Logs/AppIntegrationValidation.log",
            string.Join("\n", report) + "\nFailures=" + failures.Count + "\n" + string.Join("\n", failures));

        if (failures.Count != 0)
            throw new InvalidOperationException("Integration scene validation failed:\n" + string.Join("\n", failures));

        Debug.Log("[Integration Scene Validation] All seven Scenes, XR profiles, serialization, and Build Settings passed.");
    }

    public static void RemoveMissingScripts()
    {
        var report = new List<string>();
        var removedTotal = 0;
        foreach (var target in TargetScenes)
        {
            var scene = SceneManager.GetSceneByPath(target.path);
            var openedHere = !scene.IsValid() || !scene.isLoaded;
            if (openedHere)
                scene = EditorSceneManager.OpenScene(target.path, OpenSceneMode.Additive);

            var removedFromScene = 0;
            try
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                    {
                        var gameObject = transform.gameObject;
                        var missingCount = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
                        if (missingCount == 0)
                            continue;

                        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(gameObject);
                        removedFromScene += missingCount;
                    }
                }

                if (removedFromScene > 0)
                    EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (openedHere && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }

            removedTotal += removedFromScene;
            report.Add($"{target.path}: removed missing scripts={removedFromScene}");
        }

        Directory.CreateDirectory("Logs");
        File.WriteAllText(
            "Logs/IntegrationMissingScriptCleanup.log",
            string.Join("\n", report) + $"\nTotal={removedTotal}\n");
        Debug.Log($"[Integration Missing Script Cleanup] Removed {removedTotal} missing scripts.");
    }

    static void ValidateBuildSettings(List<string> failures, List<string> report)
    {
        var buildScenes = EditorBuildSettings.scenes;
        if (buildScenes.Length != TargetScenes.Length)
            failures.Add($"Build Settings scene count must be {TargetScenes.Length}, but was {buildScenes.Length}.");

        for (var i = 0; i < TargetScenes.Length; i++)
        {
            var expectedPath = NormalizePath(TargetScenes[i].path);
            if (i >= buildScenes.Length)
            {
                failures.Add($"Build Settings is missing scene #{i}: {expectedPath}");
                continue;
            }

            var buildScene = buildScenes[i];
            var actualPath = NormalizePath(buildScene.path);

            if (!string.Equals(actualPath, expectedPath, StringComparison.OrdinalIgnoreCase))
                failures.Add($"Build Settings scene #{i} path mismatch: expected {expectedPath}, got {actualPath}.");
            if (!buildScene.enabled)
                failures.Add($"Build Settings scene is disabled: {expectedPath}");
            var assetExists = !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(expectedPath)) &&
                              File.Exists(Path.GetFullPath(expectedPath));
            if (!assetExists)
                failures.Add($"Build Settings scene asset is missing: {expectedPath}");

            report.Add($"BuildSettings[{i}] Path={actualPath}, Enabled={buildScene.enabled}, " +
                       $"AssetExists={assetExists}");
        }
    }

    static void ValidateStageRequirements(List<string> failures, List<string> report)
    {
        var expectedScenes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            SceneFlowController.PpeScenePath,
            SceneFlowController.PreEntryScenePath,
            SceneFlowController.LeakResponseScenePath,
        };
        var declaredScenes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var guids = AssetDatabase.FindAssets("t:StageXRRequirements");
        for (var i = 0; i < guids.Length; i++)
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
            var requirements = AssetDatabase.LoadAssetAtPath<StageXRRequirements>(assetPath);
            if (requirements == null)
                continue;

            var scenePath = NormalizePath(requirements.ScenePath);
            if (string.IsNullOrEmpty(scenePath))
            {
                failures.Add($"Stage XR requirements has no scene path: {assetPath}");
                continue;
            }
            if (!declaredScenes.Add(scenePath))
                failures.Add($"Duplicate Stage XR requirements for scene: {scenePath}");
            if (!File.Exists(scenePath))
                failures.Add($"Stage XR requirements points to a missing scene: {scenePath}");
            if (requirements.UseNearFarPointer && requirements.UseStandardRayPointer)
                failures.Add($"Stage XR requirements activates two pointer visuals: {assetPath}");
            ValidateRequirementPolicy(scenePath, requirements, failures);

            report.Add($"{assetPath}: Scene={scenePath}, StandardRay={requirements.UseStandardRayPointer}, " +
                       $"NearFar={requirements.UseNearFarPointer}, UI={requirements.UIInteraction}, " +
                       $"Grab={requirements.GrabInteraction}, Haptic={requirements.Haptics}, " +
                       $"Comfort={requirements.ComfortVignette}");
        }

        foreach (var expected in expectedScenes)
            if (!declaredScenes.Contains(NormalizePath(expected)))
                failures.Add($"Stage XR requirements missing for scene: {expected}");
    }

    static void ValidateRequirementPolicy(
        string scenePath,
        StageXRRequirements requirements,
        List<string> failures)
    {
        if (string.Equals(scenePath, NormalizePath(SceneFlowController.PpeScenePath),
                StringComparison.OrdinalIgnoreCase))
        {
            if (requirements.FarInteraction || !requirements.NearInteraction ||
                !requirements.UIInteraction || !requirements.GrabInteraction ||
                !requirements.Movement || !requirements.Turning ||
                !requirements.ComfortVignette)
            {
                failures.Add("PPE requirements must declare Near/Far + UI + Grab + Move/Turn + Comfort vignette, without a duplicate Standard Ray.");
            }
        }
        else if (string.Equals(scenePath, NormalizePath(SceneFlowController.PreEntryScenePath),
                     StringComparison.OrdinalIgnoreCase))
        {
            if (requirements.FarInteraction || !requirements.NearInteraction ||
                requirements.UIInteraction || !requirements.GrabInteraction ||
                !requirements.Movement || !requirements.Turning || requirements.ComfortVignette)
            {
                failures.Add("PreEntry requirements must enable near Grab without far ray/UI, while keeping shared movement and turning enabled.");
            }
        }
        else if (string.Equals(scenePath, NormalizePath(SceneFlowController.LeakResponseScenePath),
                     StringComparison.OrdinalIgnoreCase))
        {
            if (requirements.FarInteraction || !requirements.NearInteraction ||
                !requirements.UIInteraction || !requirements.GrabInteraction ||
                !requirements.ComfortVignette)
            {
                failures.Add("LeakResponse requirements must declare Near-only interaction + UI + Grab + Comfort vignette, with the far ray hidden.");
            }
        }
    }

    static string NormalizePath(string value) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Replace('\\', '/');

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
            XROrigin firstOrigin = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                var origins = root.GetComponentsInChildren<XROrigin>(true);
                xrOriginCount += origins.Length;
                if (firstOrigin == null && origins.Length > 0)
                    firstOrigin = origins[0];
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
                                iterator.objectReferenceEntityIdValue != EntityId.None)
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
            if (scenePath == SceneFlowController.AppScenePath)
                ValidateCommonXr(firstOrigin, failures);
            if (scenePath == SceneFlowController.TitleScenePath)
            {
                var titleMenus = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<TitleMenuController>(true)).ToArray();
                if (titleMenus.Length != 1)
                    failures.Add($"TitleScene must contain exactly one TitleMenuController, found {titleMenus.Length}.");
            }
            if (scenePath == SceneFlowController.PreEntryScenePath)
                ValidatePreEntryLocomotion(firstOrigin, failures);
            if (scenePath == SceneFlowController.LeakResponseScenePath)
            {
                ValidateLeakResponseEntrySpawn(scene, failures);
                ValidateLeakValveGrabColliders(scene, failures);
            }

            report.Add($"{scenePath}: XR={xrOriginCount}, MissingScripts={missingScriptCount}, " +
                       $"BrokenReferences={brokenReferenceCount}, AppBootstrap={appBootstrapCount}");
        }
        finally
        {
            if (openedHere && scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    static void ValidatePreEntryLocomotion(XROrigin origin, List<string> failures)
    {
        if (origin == null)
            return;

        var enabledMove = origin.GetComponentsInChildren<ContinuousMoveProvider>(true)
            .Count(provider => provider != null && provider.enabled);
        var enabledTurn = origin.GetComponentsInChildren<SnapTurnProvider>(true)
            .Count(provider => provider != null && provider.enabled);
        var enabledTeleport = origin.GetComponentsInChildren<TeleportationProvider>(true)
            .Count(provider => provider != null && provider.enabled);
        if (enabledMove != 0 || enabledTurn != 0 || enabledTeleport != 0)
        {
            failures.Add(
                $"PreEntry standalone locomotion must be disabled; enabled Move/Turn/Teleport=" +
                $"{enabledMove}/{enabledTurn}/{enabledTeleport}.");
        }
    }

    static void ValidateCommonXr(XROrigin origin, List<string> failures)
    {
        if (origin == null)
            return;
        if (origin.name != "Common XR Origin")
            failures.Add($"App XR Origin must be named 'Common XR Origin', found '{origin.name}'.");

        RequirePath(origin.transform, "Camera Offset/Main Camera", failures);
        var bodyAnchor = RequirePath(origin.transform, "PlayerBodyAnchor", failures);
        var bagRoot = RequirePath(bodyAnchor, "ResponseBag_Wearable", failures);
        if (bodyAnchor != null && bagRoot != null && bagRoot.parent != bodyAnchor)
            failures.Add("Common response bag must be directly owned by PlayerBodyAnchor.");
        if (bodyAnchor != null)
            ValidateSharedBagPose(bodyAnchor, bagRoot, failures);

        var bodyAnchorCount = CountNamedDescendants(origin.transform, "PlayerBodyAnchor");
        var bagCount = CountNamedDescendants(origin.transform, "ResponseBag_Wearable");
        if (bodyAnchorCount != 1)
            failures.Add($"Common XR must contain one PlayerBodyAnchor, found {bodyAnchorCount}.");
        if (bagCount != 1)
            failures.Add($"Common XR must contain one ResponseBag_Wearable, found {bagCount}.");

        ValidateSharedLocomotion(origin.transform, failures);
        ValidateLeakComfortProfile(origin.transform, failures);

        ValidateController(origin.transform, "Left", "XR Controller Left", "Left_NearFarInteractor", failures);
        ValidateController(origin.transform, "Right", "XR Controller Right", "Right_NearFarInteractor", failures);
    }

    static void ValidateSharedBagPose(Transform bodyAnchor, Transform bagRoot, List<string> failures)
    {
        var follower = bodyAnchor.GetComponent<PlayerBodyAnchorFollower>();
        if (follower == null)
        {
            failures.Add("PlayerBodyAnchorFollower is missing from the shared body anchor.");
        }
        else
        {
            var followerObject = new SerializedObject(follower);
            var offset = followerObject.FindProperty("localPositionOffset");
            var followsHeadHeight = followerObject.FindProperty("followCameraHeight");
            var stableBodyFrame = followerObject.FindProperty("useStableOriginBodyFrame");
            var followsHeadYaw = followerObject.FindProperty("useCameraYawOnly");
            var expectedOffset = new Vector3(-0.52f, 0.65f, 0.14f);
            if (offset == null || Vector3.Distance(offset.vector3Value, expectedOffset) > 0.001f)
                failures.Add($"Shared response bag body offset must be {expectedOffset}.");
            if (followsHeadHeight == null || followsHeadHeight.boolValue ||
                stableBodyFrame == null || !stableBodyFrame.boolValue)
                failures.Add("Shared response bag must use the stable XR Origin body frame and ignore tracked HMD height.");
            if (followsHeadYaw == null || followsHeadYaw.boolValue)
                failures.Add("Shared response bag must ignore HMD look yaw and remain fixed to XR Origin/body yaw.");
        }

        if (bagRoot == null)
            return;

        var expectedRotation = Quaternion.Euler(0f, 90f, 8f);
        if (Quaternion.Angle(bagRoot.localRotation, expectedRotation) > 0.5f)
            failures.Add("Shared response bag must run front-to-back along the left hip (Euler 0, 90, 8).");

        var wearable = bagRoot.GetComponent<ResponseBagWearableVisual>();
        if (wearable == null)
        {
            failures.Add("ResponseBagWearableVisual is missing from the shared response bag.");
            return;
        }

        var wearableObject = new SerializedObject(wearable);
        var wearableEuler = wearableObject.FindProperty("wearableLocalEuler");
        if (wearableEuler == null || Vector3.Distance(wearableEuler.vector3Value, new Vector3(0f, 90f, 8f)) > 0.01f)
            failures.Add("ResponseBagWearableVisual pose does not match the shared response bag pose.");

        var equipment = bodyAnchor.GetComponentInParent<SharedPlayerEquipment>(true);
        if (equipment != null && equipment.RetrievalZone != null &&
            Quaternion.Angle(equipment.RetrievalZone.transform.localRotation, bagRoot.localRotation) > 0.5f)
        {
            failures.Add("Shared bag retrieval Zone/Anchor pose is not aligned with the bag visual.");
        }
    }

    static void ValidateLeakComfortProfile(Transform origin, List<string> failures)
    {
        var vignettes = origin.GetComponentsInChildren<TunnelingVignetteController>(true);
        if (vignettes.Length != 1)
        {
            failures.Add($"Common XR must contain exactly one TunnelingVignetteController, found {vignettes.Length}.");
            return;
        }

        var vignette = vignettes[0];
        var visibilityGuards = origin.GetComponentsInChildren<ComfortVignetteVisibilityGuard>(true);
        if (visibilityGuards.Length != 1 || visibilityGuards[0].gameObject != vignette.gameObject)
            failures.Add($"Common XR comfort vignette must contain exactly one co-located visibility guard, found {visibilityGuards.Length}.");
        ValidateVignetteParameters("default", vignette.defaultParameters,
            0.82f, 0.25f, 0.04f, 0.10f, 0.02f, failures);

        var providers = vignette.locomotionVignetteProviders;
        if (providers == null || providers.Count != 2)
        {
            failures.Add($"Shared Comfort must contain LeakResponse Move/Turn providers, found {providers?.Count ?? 0}.");
            return;
        }

        var canonicalMove = origin.GetComponent<ContinuousMoveProvider>();
        var canonicalTurn = origin.GetComponent<SnapTurnProvider>();
        ValidateVignetteProvider("Move", providers[0], canonicalMove, 0.68f, failures);
        ValidateVignetteProvider("Turn", providers[1], canonicalTurn, 0.78f, failures);
    }

        static void ValidateVignetteProvider(
        string label,
        LocomotionVignetteProvider provider,
        LocomotionProvider expectedProvider,
        float aperture,
        List<string> failures)
    {
        if (provider == null || !provider.enabled || !provider.overrideDefaultParameters ||
            provider.locomotionProvider == null || provider.locomotionProvider != expectedProvider)
        {
            failures.Add($"Shared Comfort {label} provider is missing, disabled, or not wired to the canonical App locomotion provider.");
            return;
        }

        ValidateVignetteParameters(label, provider.overrideParameters,
            aperture, 0.15f, 0.08f, 0.15f, 0.03f, failures);
    }

    static void ValidateVignetteParameters(
        string label,
        VignetteParameters parameters,
        float aperture,
        float feather,
        float easeIn,
        float easeOut,
        float delay,
        List<string> failures)
    {
        if (parameters == null ||
            !Nearly(parameters.apertureSize, aperture) ||
            !Nearly(parameters.featheringEffect, feather) ||
            !Nearly(parameters.easeInTime, easeIn) ||
            !Nearly(parameters.easeOutTime, easeOut) ||
            !parameters.easeInTimeLock ||
            !Nearly(parameters.easeOutDelayTime, delay) ||
            !Nearly(parameters.vignetteColor.a, 1f) ||
            !Nearly(parameters.vignetteColorBlend.a, 1f))
        {
            failures.Add($"Shared Comfort {label} parameters do not match the Quest-tested LeakResponse values.");
        }
    }

    static bool Nearly(float left, float right) => Mathf.Abs(left - right) <= 0.0001f;

    static void ValidateLeakResponseEntrySpawn(Scene scene, List<string> failures)
    {
        Transform spawn = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                if (transforms[i] == null || transforms[i].name != "LeakResponseEntrySpawn")
                    continue;
                if (spawn != null)
                    failures.Add("LeakResponse Scene contains more than one LeakResponseEntrySpawn.");
                spawn = transforms[i];
            }
        }

        if (spawn == null)
            failures.Add("LeakResponse Scene is missing the scene-authored LeakResponseEntrySpawn.");
        else if (spawn.parent != null)
            failures.Add("LeakResponseEntrySpawn must be a root scene marker, not part of a movable object hierarchy.");
    }

    static void ValidateLeakValveGrabColliders(Scene scene, List<string> failures)
    {
        var valves = new List<ValveWheelClosureController>();
        foreach (var root in scene.GetRootGameObjects())
            valves.AddRange(root.GetComponentsInChildren<ValveWheelClosureController>(true));

        if (valves.Count != 2)
            failures.Add($"LeakResponse must contain two configured valve wheels, found {valves.Count}.");

        for (var i = 0; i < valves.Count; i++)
        {
            var grab = valves[i] != null ? valves[i].GetComponent<XRGrabInteractable>() : null;
            if (grab == null || grab.colliders.Count != 10 ||
                grab.colliders.Any(collider =>
                    !(collider is CapsuleCollider capsule) || !capsule.enabled || !capsule.isTrigger ||
                    !capsule.name.StartsWith("RimCollider_", StringComparison.Ordinal)) ||
                grab.attachTransform == null ||
                grab.attachTransform.name != "CenterShaftAttach")
            {
                failures.Add($"Valve '{valves[i]?.name}' must expose ten enabled wheel-rim Grab colliders and its fixed attach pose.");
            }
        }
    }

    static void ValidateSharedLocomotion(Transform origin, List<string> failures)
    {
        var bodyTransformers = origin.GetComponents<XRBodyTransformer>();
        var mediators = origin.GetComponents<LocomotionMediator>();
        var moveProviders = origin.GetComponents<ContinuousMoveProvider>();
        var controllerDrivers = GetBehavioursNamed(origin, "CharacterControllerDriver", false);
        var snapTurnProviders = origin.GetComponents<SnapTurnProvider>();

        if (bodyTransformers.Length != 1 || mediators.Length != 1 || moveProviders.Length != 1 ||
            controllerDrivers.Length != 1 || snapTurnProviders.Length != 1)
        {
            failures.Add(
                "Common XR root must own exactly one canonical BodyTransformer/Mediator/Move/" +
                $"CharacterDriver/SnapTurn; found {bodyTransformers.Length}/{mediators.Length}/" +
                $"{moveProviders.Length}/{controllerDrivers.Length}/{snapTurnProviders.Length}.");
            return;
        }

        ValidateLocomotionReader(
            "left Move",
            moveProviders[0].leftHandMoveInput,
            "LeftHand",
            failures);
        ValidateLocomotionReader(
            "right Snap Turn",
            snapTurnProviders[0].rightHandTurnInput,
            "RightHand",
            failures);

        if (moveProviders[0].rightHandMoveInput == null ||
            moveProviders[0].rightHandMoveInput.inputSourceMode != XRInputValueReader.InputSourceMode.Unused ||
            moveProviders[0].rightHandMoveInput.inputActionReference != null)
        {
            failures.Add("Common XR right Move input must be unused; the right thumbstick is reserved for Snap Turn.");
        }

        if (snapTurnProviders[0].leftHandTurnInput == null ||
            snapTurnProviders[0].leftHandTurnInput.inputSourceMode != XRInputValueReader.InputSourceMode.Unused ||
            snapTurnProviders[0].leftHandTurnInput.inputActionReference != null)
        {
            failures.Add("Common XR left Turn input must be unused; the left thumbstick is reserved for movement.");
        }
    }

    static Behaviour[] GetBehavioursNamed(Transform root, string typeName, bool includeChildren)
    {
        var source = includeChildren
            ? root.GetComponentsInChildren<Behaviour>(true)
            : root.GetComponents<Behaviour>();
        return source.Where(component =>
            component != null && string.Equals(component.GetType().Name, typeName, StringComparison.Ordinal)).ToArray();
    }

    static void ValidateLocomotionReader(
        string label,
        XRInputValueReader<Vector2> reader,
        string handToken,
        List<string> failures)
    {
        var action = reader != null &&
                     reader.inputSourceMode == XRInputValueReader.InputSourceMode.InputActionReference
            ? reader.inputActionReference?.action
            : null;
        if (action == null)
        {
            failures.Add($"Common XR {label} reader is not action-reference based.");
            return;
        }

        if (!HasBindingContaining(action, handToken) || !HasBindingContaining(action, "Primary2DAxis"))
            failures.Add($"Common XR {label} action is not bound to the expected {handToken} Primary2DAxis (thumbstick).");
    }

    static int CountNamedDescendants(Transform root, string exactName)
    {
        if (root == null)
            return 0;
        var count = 0;
        var descendants = root.GetComponentsInChildren<Transform>(true);
        for (var i = 0; i < descendants.Length; i++)
            if (descendants[i] != null && descendants[i].name == exactName) count++;
        return count;
    }

    static void ValidateController(
        Transform origin,
        string side,
        string modelName,
        string nearFarName,
        List<string> failures)
    {
        var basePath = $"Camera Offset/{side} Controller";
        RequirePath(origin, basePath, failures);
        RequirePath(origin, $"{basePath}/Visuals/{modelName}", failures);
        var nearFarRoot = RequirePath(origin, $"{basePath}/{nearFarName}", failures);
        RequirePath(nearFarRoot, "LineVisual", failures);
        var rayRoot = RequirePath(origin, $"{basePath}/Ray Interactor", failures);
        if (rayRoot != null && Quaternion.Angle(rayRoot.localRotation, Quaternion.identity) > 0.5f)
            failures.Add($"Common XR {side} Ray Interactor must follow the controller forward rotation.");

        var ray = rayRoot != null ? rayRoot.GetComponent<XRRayInteractor>() : null;
        var nearFar = nearFarRoot != null ? nearFarRoot.GetComponent<NearFarInteractor>() : null;
        if (ray == null)
            failures.Add($"Common XR {side} standard Ray Interactor component is missing.");
        else
            ValidateGripSelectReader(side, "standard Ray", ray.selectInput, failures);
        if (nearFar == null)
            failures.Add($"Common XR {side} NearFarInteractor component is missing.");
        else
            ValidateGripSelectReader(side, "Near/Far", nearFar.selectInput, failures);
    }

    static void ValidateGripSelectReader(
        string side,
        string interactorLabel,
        XRInputButtonReader reader,
        List<string> failures)
    {
        if (reader == null ||
            reader.inputSourceMode != XRInputButtonReader.InputSourceMode.InputActionReference ||
            reader.inputActionReferencePerformed?.action == null ||
            reader.inputActionReferenceValue?.action == null)
        {
            failures.Add($"Common XR {side} {interactorLabel} Select reader is incomplete.");
            return;
        }

        var select = reader.inputActionReferencePerformed.action;
        var value = reader.inputActionReferenceValue.action;
        if (!HasBindingContaining(select, "GripButton"))
            failures.Add($"Common XR {side} {interactorLabel} Select is not bound to GripButton.");
        if (!HasBindingContaining(value, "Grip"))
            failures.Add($"Common XR {side} {interactorLabel} Select Value is not bound to Grip.");
    }

    static bool HasBindingContaining(UnityEngine.InputSystem.InputAction action, string token)
    {
        if (action == null)
            return false;
        for (var i = 0; i < action.bindings.Count; i++)
        {
            var path = action.bindings[i].path;
            if (!string.IsNullOrEmpty(path) &&
                path.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    static Transform RequirePath(Transform root, string relativePath, List<string> failures)
    {
        var target = root != null ? root.Find(relativePath) : null;
        if (target == null)
            failures.Add($"Common XR path is missing: {relativePath}");
        return target;
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
