#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using HClTraining.App;
using HClTraining.SharedXR;
using JH.EntrySafety;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using VRProject.LeakResponse;
using VRProject.LeakResponse.Narration;
using VRProject.Valves;

[InitializeOnLoad]
public static class AppPlayModeSmokeRunner
{
    const string ActiveKey = "HCl.AppSmoke.Active";
    const string PhaseKey = "HCl.AppSmoke.Phase";
    const string StartedKey = "HCl.AppSmoke.Started";
    const string ReportPath = "Logs/AppPlayModeSmoke.log";
    const double TimeoutSeconds = 240d;

    static readonly List<string> report = new List<string>();
    static bool hooked;
    static Camera validatedMainCamera;

    static AppPlayModeSmokeRunner()
    {
        if (SessionState.GetBool(ActiveKey, false))
            Hook();
    }

    [MenuItem("Tools/HCl Training/Run App PlayMode Smoke")]
    public static void Run()
    {
        Directory.CreateDirectory("Logs");
        File.WriteAllText(ReportPath, "Starting App PlayMode smoke...\n");
        SessionState.SetBool(ActiveKey, true);
        SessionState.SetInt(PhaseKey, 0);
        SessionState.SetString(StartedKey, EditorApplication.timeSinceStartup.ToString("R"));
        validatedMainCamera = null;
        Hook();
        EditorSceneManager.OpenScene(SceneFlowController.AppScenePath, OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    static void Hook()
    {
        if (hooked)
            return;
        hooked = true;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    static void Unhook()
    {
        if (!hooked)
            return;
        hooked = false;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
    }

    static void Tick()
    {
        if (!SessionState.GetBool(ActiveKey, false) || !EditorApplication.isPlaying)
            return;

        if (!double.TryParse(SessionState.GetString(StartedKey, "0"), out var started))
            started = EditorApplication.timeSinceStartup;
        if (EditorApplication.timeSinceStartup - started > TimeoutSeconds)
        {
            Fail("PlayMode smoke timed out.");
            return;
        }

        try
        {
            var app = AppBootstrap.Instance;
            if (app == null || app.SceneFlow == null || app.SceneFlow.IsTransitioning)
                return;

            var phase = SessionState.GetInt(PhaseKey, 0);
            switch (phase)
            {
                case 0:
                    if (!IsContentLoaded(SceneFlowController.StartScenePath)) return;
                    ValidateRuntime("Initial Start", TrainingStage.None);
                    Require(UnityEngine.Object.FindAnyObjectByType<StartMenuPresenter>() != null, "Start menu presenter missing.");
                    Require(app.ScenarioFlow.StartTraining(TrainingMode.Full), "Could not start full training.");
                    Advance("Initial Start -> Full/PPE");
                    break;

                case 1:
                    if (app.Session.CurrentStage != TrainingStage.PPE) return;
                    ValidateRuntime("Full PPE", TrainingStage.PPE);
                    var fullLoadout = PPELoadoutPresets.FieldReadyComplete;
                    fullLoadout.source = PPELoadoutSource.PpeSceneResult;
                    PPELoadoutPresets.ReplaceLoadout(fullLoadout);
                    app.Inventory.SyncFromLoadout();
                    app.PPEState.ApplyPersistentState();
                    app.ScenarioFlow.CompleteCurrentStage();
                    Advance("Full PPE -> PreEntry");
                    break;

                case 2:
                    if (app.Session.CurrentStage != TrainingStage.PreEntry) return;
                    ValidateRuntime("Full PreEntry", TrainingStage.PreEntry);
                    Require(app.Session.LeftChemicalGloveWorn && app.Session.RightChemicalGloveWorn,
                        "Chemical glove state did not persist into PreEntry.");
                    Require(app.ScenarioFlow.CompletePreEntryAndHandoff(),
                        "Full PreEntry additive handoff request failed.");
                    Advance("Full PreEntry -> LeakResponse");
                    break;

                case 3:
                    if (app.Session.CurrentStage != TrainingStage.LeakResponse) return;
                    ValidateRuntime("Full LeakResponse", TrainingStage.LeakResponse);
                    Require(app.Session.IsEquipmentAcquired(PersistentEquipmentInventory.AbsorbentSock),
                        "Full training loadout lost absorbent Sock.");
                    app.ScenarioFlow.CompleteCurrentStage();
                    Advance("Full LeakResponse -> Complete");
                    break;

                case 4:
                    if (app.Session.CurrentStage != TrainingStage.Complete) return;
                    ValidateRuntime("Full Complete", TrainingStage.Complete);
                    Require(app.Session.CompletedStages.Count == 3, "Full training did not record all three stages.");
                    Require(UnityEngine.Object.FindAnyObjectByType<CompleteMenuPresenter>() != null,
                        "Complete menu presenter missing.");
                    Require(app.ScenarioFlow.RepeatSelectedTraining(), "Repeat full training request failed.");
                    Advance("Complete -> Repeat full training");
                    break;

                case 5:
                    if (app.Session.CurrentStage != TrainingStage.PPE) return;
                    ValidateRuntime("Repeated PPE", TrainingStage.PPE);
                    Require(app.ScenarioFlow.ReturnToStart(), "Return to Start failed.");
                    Advance("Repeated PPE -> Start");
                    break;

                case 6:
                    if (!IsContentLoaded(SceneFlowController.StartScenePath)) return;
                    ValidateRuntime("Returned Start", TrainingStage.None);
                    Require(app.ScenarioFlow.StartTraining(TrainingMode.PPEOnly), "PPE-only start failed.");
                    Advance("Start -> PPE only");
                    break;

                case 7:
                    if (app.Session.CurrentStage != TrainingStage.PPE) return;
                    ValidateRuntime("PPE only", TrainingStage.PPE);
                    app.ScenarioFlow.CompleteCurrentStage();
                    Advance("PPE only -> Complete");
                    break;

                case 8:
                    if (app.Session.CurrentStage != TrainingStage.Complete) return;
                    ValidateRuntime("PPE-only Complete", TrainingStage.Complete);
                    Require(app.ScenarioFlow.ReturnToStart(), "PPE-only return failed.");
                    Advance("PPE-only Complete -> Start");
                    break;

                case 9:
                    if (!IsContentLoaded(SceneFlowController.StartScenePath)) return;
                    Require(app.ScenarioFlow.StartTraining(TrainingMode.PreEntryOnly), "PreEntry-only start failed.");
                    Advance("Start -> PreEntry only");
                    break;

                case 10:
                    if (app.Session.CurrentStage != TrainingStage.PreEntry) return;
                    ValidateRuntime("PreEntry only", TrainingStage.PreEntry);
                    Require(PPELoadoutReader.Source == PPELoadoutSource.FieldReadyPreset,
                        "PreEntry-only did not initialize official field-ready PPE preset.");
                    app.ScenarioFlow.CompleteCurrentStage();
                    Advance("PreEntry only -> Complete");
                    break;

                case 11:
                    if (app.Session.CurrentStage != TrainingStage.Complete) return;
                    Require(app.ScenarioFlow.ReturnToStart(), "PreEntry-only return failed.");
                    Advance("PreEntry-only Complete -> Start");
                    break;

                case 12:
                    if (!IsContentLoaded(SceneFlowController.StartScenePath)) return;
                    Require(app.ScenarioFlow.StartTraining(TrainingMode.LeakResponseOnly), "Leak-only start failed.");
                    Advance("Start -> LeakResponse only");
                    break;

                case 13:
                    if (app.Session.CurrentStage != TrainingStage.LeakResponse) return;
                    ValidateRuntime("LeakResponse only", TrainingStage.LeakResponse);
                    Require(PPELoadoutReader.Source == PPELoadoutSource.FieldReadyPreset,
                        "LeakResponse-only did not initialize official field-ready PPE preset.");
                    app.ScenarioFlow.CompleteCurrentStage();
                    Advance("LeakResponse only -> Complete");
                    break;

                case 14:
                    if (app.Session.CurrentStage != TrainingStage.Complete) return;
                    ValidateRuntime("Leak-only Complete", TrainingStage.Complete);
                    Succeed();
                    break;
            }
        }
        catch (Exception exception)
        {
            Fail(exception.ToString());
        }
    }

    static void ValidateRuntime(string label, TrainingStage expectedStage)
    {
        var app = AppBootstrap.Instance;
        Require(app.Session.CurrentStage == expectedStage,
            $"{label}: current stage {app.Session.CurrentStage}, expected {expectedStage}.");
        Require(app.SharedInput != null && app.SharedInput.isActiveAndEnabled && app.SharedInput.IsReady,
            $"{label}: Shared XR input contract is not active.");
        ValidateSharedInputActions(label, app.SharedInput);
        Require(app.SharedEquipment != null && app.SharedEquipment.isActiveAndEnabled &&
                app.SharedEquipment == SharedPlayerEquipment.Instance,
            $"{label}: Shared player equipment contract is not active or is duplicated.");
        Require(app.SharedEquipment.BodyAnchor != null && app.SharedEquipment.BagRoot != null &&
                app.SharedEquipment.RetrievalZone != null && app.SharedEquipment.RetrievalAnchor != null,
            $"{label}: Shared bag body anchor/retrieval contract is incomplete.");

        var origins = UnityEngine.Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Require(origins.Length == 1, $"{label}: active XR Origin count={origins.Length}.");

        var cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        var activeMainCameras = 0;
        Camera activeMainCamera = null;
        for (var i = 0; i < cameras.Length; i++)
        {
            if (!cameras[i].enabled ||
                (!cameras[i].CompareTag("MainCamera") && cameras[i].name != "Main Camera"))
                continue;
            activeMainCameras++;
            activeMainCamera = cameras[i];
        }
        Require(activeMainCameras == 1, $"{label}: active Main Camera count={activeMainCameras}.");
        Require(ReferenceEquals(activeMainCamera, app.MainCamera),
            $"{label}: active XR Main Camera is not the App-owned camera instance.");
        if (validatedMainCamera == null)
            validatedMainCamera = app.MainCamera;
        else
            Require(ReferenceEquals(validatedMainCamera, app.MainCamera),
                $"{label}: XR Main Camera instance changed across Stage transition.");

        var listeners = UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        var activeListeners = 0;
        for (var i = 0; i < listeners.Length; i++) if (listeners[i].enabled) activeListeners++;
        Require(activeListeners == 1, $"{label}: active AudioListener count={activeListeners}.");

        var systems = UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Require(systems.Length == 1, $"{label}: active EventSystem count={systems.Length}.");

        var interactionManagers = UnityEngine.Object.FindObjectsByType<XRInteractionManager>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Require(interactionManagers.Length == 1,
            $"{label}: active XR Interaction Manager count={interactionManagers.Length}.");

        var inputManagers = UnityEngine.Object.FindObjectsByType<InputActionManager>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Require(inputManagers.Length == 1,
            $"{label}: active Input Action Manager count={inputManagers.Length}.");

        var poseDrivers = UnityEngine.Object.FindObjectsByType<TrackedPoseDriver>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Require(poseDrivers.Length == 3,
            $"{label}: active tracked pose driver count={poseDrivers.Length}, expected camera + two controllers.");

        ValidatePointerOwnership(label, expectedStage, app);

        if (expectedStage == TrainingStage.PPE ||
            expectedStage == TrainingStage.PreEntry ||
            expectedStage == TrainingStage.LeakResponse)
        {
            var xrRoot = app.XRProfiles.CommonXrOrigin.transform;
            RequireInactive(xrRoot, "Camera Offset/Hand Visualizer", label);
            ValidateSharedLocomotion(label, xrRoot);
        }

        ValidateSharedEquipment(label, app);
        if (expectedStage == TrainingStage.PreEntry || expectedStage == TrainingStage.LeakResponse)
            Require(app.SharedEquipment.BagRoot.gameObject.activeInHierarchy,
                $"{label}: shared response bag is not available in the field Stage.");

        var contentScenes = 0;
        for (var i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (scene.isLoaded && scene.path != SceneFlowController.AppScenePath)
                contentScenes++;
        }
        var expectedContentScenes = expectedStage == TrainingStage.PreEntry ||
                                    (expectedStage == TrainingStage.LeakResponse &&
                                     app.Session.SelectedMode == TrainingMode.Full)
            ? 2
            : 1;
        Require(contentScenes == expectedContentScenes,
            $"{label}: simultaneously loaded content Scenes={contentScenes}, expected={expectedContentScenes}.");
        ValidatePreEntryLeakActivation(label, expectedStage, app);
        report.Add($"PASS {label}: XR=1, MainCamera=1, SharedBag=1, AudioListener=1, " +
                   $"EventSystem=1, ContentScenes={contentScenes}");
    }

    static void ValidatePreEntryLeakActivation(string label, TrainingStage expectedStage, AppBootstrap app)
    {
        var leakScene = SceneManager.GetSceneByPath(SceneFlowController.LeakResponseScenePath);
        if (expectedStage != TrainingStage.PreEntry && expectedStage != TrainingStage.LeakResponse)
        {
            Require(!leakScene.IsValid() || !leakScene.isLoaded,
                $"{label}: LeakResponse dependency remained loaded outside PreEntry/LeakResponse.");
            return;
        }

        Require(leakScene.IsValid() && leakScene.isLoaded,
            $"{label}: LeakResponse Scene is not loaded.");
        if (expectedStage == TrainingStage.PreEntry)
        {
            Require(IsContentLoaded(SceneFlowController.PreEntryScenePath),
                $"{label}: PreEntry primary Scene is not loaded with its LeakResponse dependency.");
            ValidatePreEntryInputOwnership(label, app);
        }
        else if (expectedStage == TrainingStage.LeakResponse &&
                 app.Session.SelectedMode == TrainingMode.Full)
        {
            ValidatePreEntryInputReleased(label);
        }

        var shouldBeActive = expectedStage == TrainingStage.LeakResponse;
        var bootstraps = UnityEngine.Object.FindObjectsByType<P102LeakRuntimeBootstrap>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        var narrations = UnityEngine.Object.FindObjectsByType<P102LeakNarrationController>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        var leakBootstrapCount = 0;
        var leakNarrationCount = 0;
        for (var i = 0; i < bootstraps.Length; i++)
        {
            if (bootstraps[i] == null || bootstraps[i].gameObject.scene != leakScene)
                continue;
            leakBootstrapCount++;
            Require(bootstraps[i].IsStageActivated == shouldBeActive,
                $"{label}: Leak bootstrap activation={bootstraps[i].IsStageActivated}, expected={shouldBeActive}.");
        }
        for (var i = 0; i < narrations.Length; i++)
        {
            if (narrations[i] == null || narrations[i].gameObject.scene != leakScene)
                continue;
            leakNarrationCount++;
            Require(narrations[i].IsStageActivated == shouldBeActive,
                $"{label}: Leak narration activation={narrations[i].IsStageActivated}, expected={shouldBeActive}.");
        }

        Require(leakBootstrapCount == 1,
            $"{label}: Leak runtime bootstrap count={leakBootstrapCount}, expected=1.");
        Require(leakNarrationCount == 1,
            $"{label}: Leak narration controller count={leakNarrationCount}, expected=1.");

        ValidateLeakProgressionActivation(label, leakScene, shouldBeActive);
    }

    static void ValidatePreEntryInputOwnership(string label, AppBootstrap app)
    {
        var preEntryScene = SceneManager.GetSceneByPath(SceneFlowController.PreEntryScenePath);
        var controllers = UnityEngine.Object.FindObjectsByType<EntrySafetyScenarioController>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        var adapters = UnityEngine.Object.FindObjectsByType<EntrySafetyInputAdapter>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        EntrySafetyScenarioController controller = null;
        EntrySafetyInputAdapter adapter = null;
        for (var i = 0; i < controllers.Length; i++)
            if (controllers[i] != null && controllers[i].gameObject.scene == preEntryScene) controller = controllers[i];
        for (var i = 0; i < adapters.Length; i++)
            if (adapters[i] != null && adapters[i].gameObject.scene == preEntryScene) adapter = adapters[i];

        Require(controller != null && controller.isActiveAndEnabled && controller.InputHandlersSubscribed,
            $"{label}: PreEntry Y/A input handlers are not subscribed.");
        Require(adapter != null && adapter.isActiveAndEnabled,
            $"{label}: PreEntry Shared XR input adapter is not active.");
        Require(controller.LeakResponseHandoffAllowed == (app.Session.SelectedMode == TrainingMode.Full),
            $"{label}: PreEntry handoff permission does not match the launch context.");
    }

    static void ValidatePreEntryInputReleased(string label)
    {
        var preEntryScene = SceneManager.GetSceneByPath(SceneFlowController.PreEntryScenePath);
        Require(preEntryScene.IsValid() && preEntryScene.isLoaded,
            $"{label}: Full handoff unexpectedly unloaded the PreEntry environment.");
        var controllers = UnityEngine.Object.FindObjectsByType<EntrySafetyScenarioController>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        var adapters = UnityEngine.Object.FindObjectsByType<EntrySafetyInputAdapter>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (var i = 0; i < controllers.Length; i++)
        {
            if (controllers[i] == null || controllers[i].gameObject.scene != preEntryScene)
                continue;
            Require(!controllers[i].isActiveAndEnabled && !controllers[i].InputHandlersSubscribed,
                $"{label}: PreEntry Y/A handler remained active after LeakResponse handoff.");
        }
        for (var i = 0; i < adapters.Length; i++)
        {
            if (adapters[i] == null || adapters[i].gameObject.scene != preEntryScene)
                continue;
            Require(!adapters[i].isActiveAndEnabled,
                $"{label}: PreEntry input adapter remained active after LeakResponse handoff.");
        }
    }

    static void ValidateLeakProgressionActivation(string label, Scene leakScene, bool expectedActive)
    {
        var absorptions = UnityEngine.Object.FindObjectsByType<HClAbsorptionVisualController>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        var valves = UnityEngine.Object.FindObjectsByType<ValveWheelClosureController>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        var bagHandlers = UnityEngine.Object.FindObjectsByType<P102SharedBagItemHandler>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        var absorptionCount = 0;
        var valveCount = 0;
        var bagHandlerCount = 0;
        for (var i = 0; i < absorptions.Length; i++)
        {
            if (absorptions[i] == null || absorptions[i].gameObject.scene != leakScene)
                continue;
            absorptionCount++;
            Require(absorptions[i].enabled == expectedActive,
                $"{label}: Leak absorption progression enabled={absorptions[i].enabled}, expected={expectedActive}.");
        }
        for (var i = 0; i < valves.Length; i++)
        {
            if (valves[i] == null || valves[i].gameObject.scene != leakScene)
                continue;
            valveCount++;
            Require(valves[i].enabled == expectedActive,
                $"{label}: Leak valve progression enabled={valves[i].enabled}, expected={expectedActive}.");
        }
        for (var i = 0; i < bagHandlers.Length; i++)
        {
            if (bagHandlers[i] == null || bagHandlers[i].gameObject.scene != leakScene)
                continue;
            bagHandlerCount++;
            Require(bagHandlers[i].enabled == expectedActive,
                $"{label}: Leak Y bag handler enabled={bagHandlers[i].enabled}, expected={expectedActive}.");
        }

        Require(absorptionCount == 1,
            $"{label}: Leak absorption controller count={absorptionCount}, expected=1.");
        Require(valveCount == 2,
            $"{label}: Leak valve controller count={valveCount}, expected=2.");
        var expectedBagHandlerCount = expectedActive ? 1 : 0;
        Require(bagHandlerCount == expectedBagHandlerCount,
            $"{label}: Leak Y bag handler count={bagHandlerCount}, expected={expectedBagHandlerCount}.");
    }

    static void ValidateSharedLocomotion(string label, Transform xrRoot)
    {
        var bodyTransformers = xrRoot.GetComponentsInChildren<XRBodyTransformer>(true);
        var mediators = xrRoot.GetComponentsInChildren<LocomotionMediator>(true);
        var moveProviders = xrRoot.GetComponentsInChildren<ContinuousMoveProvider>(true);
        var controllerDrivers = xrRoot.GetComponentsInChildren<CharacterControllerDriver>(true);
        var snapTurnProviders = xrRoot.GetComponentsInChildren<SnapTurnProvider>(true);

        Require(CountActive(bodyTransformers) == 1,
            $"{label}: active shared XRBodyTransformer count is not one.");
        Require(CountActive(mediators) == 1,
            $"{label}: active shared LocomotionMediator count is not one.");
        Require(CountActive(moveProviders) == 1,
            $"{label}: active shared ContinuousMoveProvider count is not one.");
        Require(CountActive(controllerDrivers) == 1,
            $"{label}: active shared CharacterControllerDriver count is not one.");
        Require(CountActive(snapTurnProviders) == 1,
            $"{label}: active shared SnapTurnProvider count is not one.");

        var moveProvider = xrRoot.GetComponent<ContinuousMoveProvider>();
        var turnProvider = xrRoot.GetComponent<SnapTurnProvider>();
        Require(moveProvider != null && moveProvider.isActiveAndEnabled,
            $"{label}: canonical root Move Provider is not active.");
        Require(turnProvider != null && turnProvider.isActiveAndEnabled,
            $"{label}: canonical root Snap Turn Provider is not active.");
        var moveAction = moveProvider?.leftHandMoveInput?.inputActionReference?.action;
        var turnAction = turnProvider?.rightHandTurnInput?.inputActionReference?.action;
        Require(moveAction != null && moveAction.enabled,
            $"{label}: left-hand Move input action is missing or disabled.");
        Require(turnAction != null && turnAction.enabled,
            $"{label}: right-hand Snap Turn input action is missing or disabled.");
    }

    static int CountActive<T>(T[] behaviours) where T : Behaviour
    {
        var count = 0;
        for (var i = 0; i < behaviours.Length; i++)
            if (behaviours[i] != null && behaviours[i].isActiveAndEnabled) count++;
        return count;
    }

    static void ValidateSharedEquipment(string label, AppBootstrap app)
    {
        var equipment = app.SharedEquipment;
        Require(equipment != null, $"{label}: SharedPlayerEquipment is missing.");
        if (equipment == null)
            return;

        var commonRoot = app.XRProfiles.CommonXrOrigin.transform;
        Require(equipment.BodyAnchor != null && equipment.BodyAnchor.IsChildOf(commonRoot),
            $"{label}: shared body anchor is outside the common XR rig.");
        Require(equipment.BagRoot != null && equipment.BagRoot.parent == equipment.BodyAnchor,
            $"{label}: shared bag is not directly owned by the body anchor.");
        Require(equipment.RetrievalZone != null &&
                equipment.RetrievalZone.transform.parent == equipment.BodyAnchor,
            $"{label}: shared retrieval zone is not body-relative.");
        Require(equipment.RetrievalAnchor != null && equipment.RetrievalZone != null &&
                equipment.RetrievalAnchor.parent == equipment.RetrievalZone.transform,
            $"{label}: shared retrieval anchor is not owned by the retrieval zone root.");
        if (equipment.BagRoot != null && equipment.RetrievalZone != null)
        {
            Require(Vector3.Distance(
                        equipment.BagRoot.localPosition,
                        equipment.RetrievalZone.transform.localPosition) < 0.001f,
                $"{label}: shared bag visual and retrieval zone body-relative positions diverged.");
        }
    }

    static void ValidatePointerOwnership(string label, TrainingStage stage, AppBootstrap app)
    {
        var expectedStandard = stage == TrainingStage.None ? 2 : 0;
        var expectedNearFar = 0;
        StageXRRequirements requirements = null;
        if (stage != TrainingStage.None && stage != TrainingStage.Complete)
        {
            var activeScene = SceneManager.GetActiveScene();
            var assets = Resources.LoadAll<StageXRRequirements>("SharedXR");
            for (var i = 0; i < assets.Length; i++)
            {
                if (assets[i] != null && string.Equals(
                        NormalizePath(assets[i].ScenePath), NormalizePath(activeScene.path),
                        StringComparison.OrdinalIgnoreCase))
                {
                    requirements = assets[i];
                    break;
                }
            }
            Require(requirements != null, $"{label}: Stage XR requirements asset is missing.");
            expectedStandard = requirements.UseStandardRayPointer ? 2 : 0;
            expectedNearFar = requirements.UseNearFarPointer ? 2 : 0;
        }

        var commonRoot = app.XRProfiles.CommonXrOrigin.transform;
        var rays = UnityEngine.Object.FindObjectsByType<XRRayInteractor>(FindObjectsInactive.Exclude);
        var nearFar = UnityEngine.Object.FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Exclude);
        Require(rays.Length == expectedStandard,
            $"{label}: active standard ray count={rays.Length}, expected={expectedStandard}.");
        Require(nearFar.Length == expectedNearFar,
            $"{label}: active Near/Far count={nearFar.Length}, expected={expectedNearFar}.");

        for (var i = 0; i < rays.Length; i++)
        {
            Require(rays[i].transform.IsChildOf(commonRoot),
                $"{label}: standard ray is outside the common XR rig: {rays[i].name}.");
            Require(rays[i].GetComponent<XRInteractorLineVisual>() != null,
                $"{label}: standard ray has no line visual.");
            Require(rays[i].GetComponent<SharedXRAimPoseDriver>() != null,
                $"{label}: standard ray has no shared aim-pose driver.");
            ValidateInteractorInput(label, rays[i]);
        }

        for (var i = 0; i < nearFar.Length; i++)
        {
            Require(nearFar[i].transform.IsChildOf(commonRoot),
                $"{label}: Near/Far is outside the common XR rig: {nearFar[i].name}.");
            Require(nearFar[i].GetComponent<SharedXRAimPoseDriver>() != null,
                $"{label}: Near/Far has no shared aim-pose driver.");
            ValidateInteractorInput(label, nearFar[i]);
            if (requirements != null)
            {
                Require(nearFar[i].enableNearCasting == requirements.NearInteraction,
                    $"{label}: Near/Far near-casting does not match Stage requirements.");
                Require(nearFar[i].enableFarCasting == requirements.FarInteraction,
                    $"{label}: Near/Far far-casting does not match Stage requirements.");
            }
        }

        ValidateHandPointerCount(label, commonRoot, "Left", expectedStandard + expectedNearFar > 0 ? 1 : 0);
        ValidateHandPointerCount(label, commonRoot, "Right", expectedStandard + expectedNearFar > 0 ? 1 : 0);
    }

    static void ValidateHandPointerCount(string label, Transform commonRoot, string side, int expected)
    {
        var controller = commonRoot.Find($"Camera Offset/{side} Controller");
        Require(controller != null, $"{label}: {side} controller is missing.");
        var count = 0;
        var rays = controller.GetComponentsInChildren<XRRayInteractor>(false);
        for (var i = 0; i < rays.Length; i++)
            if (rays[i] != null && rays[i].isActiveAndEnabled) count++;
        var nearFar = controller.GetComponentsInChildren<NearFarInteractor>(false);
        for (var i = 0; i < nearFar.Length; i++)
            if (nearFar[i] != null && nearFar[i].isActiveAndEnabled) count++;
        Require(count == expected,
            $"{label}: {side} active pointer count={count}, expected={expected}.");
    }

    static void ValidateSharedInputActions(string label, SharedXRInputContract input)
    {
        foreach (SharedXRHand hand in Enum.GetValues(typeof(SharedXRHand)))
        {
            foreach (SharedXRControl control in Enum.GetValues(typeof(SharedXRControl)))
            {
                Require(input.TryGetAction(hand, control, out var action) && action != null,
                    $"{label}: shared input action is missing: {hand}/{control}.");
                Require(action.enabled,
                    $"{label}: shared input action is disabled: {hand}/{control}.");
            }
        }
    }

    static void ValidateInteractorInput(string label, XRBaseInputInteractor interactor)
    {
        Require(interactor != null, $"{label}: active input interactor is null.");
        ValidateButtonReader(label, interactor.name, "Select", interactor.selectInput);
        ValidateButtonReader(label, interactor.name, "Activate", interactor.activateInput);
    }

    static void ValidateButtonReader(
        string label,
        string interactorName,
        string inputName,
        XRInputButtonReader reader)
    {
        Require(reader != null &&
                reader.inputSourceMode == XRInputButtonReader.InputSourceMode.InputActionReference,
            $"{label}: {interactorName} {inputName} reader is not action-reference based.");
        var performed = reader.inputActionReferencePerformed?.action;
        var value = reader.inputActionReferenceValue?.action;
        Require(performed != null && performed.enabled,
            $"{label}: {interactorName} {inputName} performed action is missing or disabled.");
        Require(value != null && value.enabled,
            $"{label}: {interactorName} {inputName} value action is missing or disabled.");
    }

    static string NormalizePath(string value) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Replace('\\', '/');

    static void RequireInactive(Transform root, string relativePath, string label)
    {
        var target = root != null ? root.Find(relativePath) : null;
        Require(target == null || !target.gameObject.activeInHierarchy,
            $"{label}: controller-only XR object is active: {relativePath}.");
    }

    static bool IsContentLoaded(string path)
    {
        var scene = SceneManager.GetSceneByPath(path);
        return scene.IsValid() && scene.isLoaded;
    }

    static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    static void Advance(string message)
    {
        report.Add("FLOW " + message);
        SessionState.SetInt(PhaseKey, SessionState.GetInt(PhaseKey, 0) + 1);
    }

    static void Succeed()
    {
        report.Add("RESULT PASS");
        File.WriteAllText(ReportPath, string.Join("\n", report));
        SessionState.SetInt(PhaseKey, 1000);
        EditorApplication.ExitPlaymode();
    }

    static void Fail(string message)
    {
        report.Add("RESULT FAIL\n" + message);
        File.WriteAllText(ReportPath, string.Join("\n", report));
        SessionState.SetInt(PhaseKey, -1);
        if (EditorApplication.isPlaying)
            EditorApplication.ExitPlaymode();
        else
            FinishEditorExit(false);
    }

    static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(ActiveKey, false) || change != PlayModeStateChange.EnteredEditMode)
            return;
        FinishEditorExit(SessionState.GetInt(PhaseKey, -1) == 1000);
    }

    static void FinishEditorExit(bool success)
    {
        SessionState.SetBool(ActiveKey, false);
        validatedMainCamera = null;
        Unhook();
        if (Application.isBatchMode)
            EditorApplication.Exit(success ? 0 : 2);
    }
}
#endif
