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
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using VRProject.LeakResponse;
using VRProject.LeakResponse.Narration;
using VRProject.ObjectInstallation;
using VRProject.Valves;

[InitializeOnLoad]
public static class AppPlayModeSmokeRunner
{
    const string ActiveKey = "HCl.AppSmoke.Active";
    const string PhaseKey = "HCl.AppSmoke.Phase";
    const string StartedKey = "HCl.AppSmoke.Started";
    const string ReportPath = "Logs/AppPlayModeSmoke.log";
    const string RequestPath = "Temp/AppPlayModeSmoke.request";
    const double TimeoutSeconds = 240d;

    static readonly List<string> report = new List<string>();
    static bool hooked;
    static Camera validatedMainCamera;
    static bool validatedBagBodySpace;

    static AppPlayModeSmokeRunner()
    {
        if (SessionState.GetBool(ActiveKey, false))
            Hook();
        else if (File.Exists(RequestPath))
        {
            File.Delete(RequestPath);
            EditorApplication.delayCall += Run;
        }
    }

    [MenuItem("Tools/HCl Training/Run App PlayMode Smoke")]
    public static void Run()
    {
        Directory.CreateDirectory("Logs");
        report.Clear();
        File.WriteAllText(ReportPath, "Starting App PlayMode smoke...\n");
        SessionState.SetBool(ActiveKey, true);
        SessionState.SetInt(PhaseKey, 0);
        SessionState.SetString(StartedKey, EditorApplication.timeSinceStartup.ToString("R"));
        validatedMainCamera = null;
        validatedBagBodySpace = false;
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
                    if (IsContentLoaded(SceneFlowController.StartScenePath))
                    {
                        ValidateRuntime("Initial Start", TrainingStage.None);
                        Require(UnityEngine.Object.FindAnyObjectByType<StartMenuPresenter>() != null, "Start menu presenter missing.");
                        Require(app.ScenarioFlow.StartTraining(TrainingMode.Full), "Could not start full training.");
                        Advance("Initial Start -> Full/PPE");
                        break;
                    }
                    if (!IsContentLoaded(SceneFlowController.TitleScenePath)) return;
                    ValidateRuntime("Initial Title", TrainingStage.None);
                    var titleMenu = UnityEngine.Object.FindAnyObjectByType<JH.EntrySafety.TitleMenuController>();
                    Require(titleMenu != null, "Title menu controller missing.");
                    titleMenu.SelectEducation();
                    return;

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
            ValidateSharedLocomotion(label, xrRoot, expectedStage);
        }

        ValidateSharedEquipment(label, app);
        if (!validatedBagBodySpace)
        {
            ValidateBagBodySpaceMotion(label, app);
            validatedBagBodySpace = true;
        }
        if (expectedStage == TrainingStage.PPE)
        {
            ValidatePpeRequiredHighlight(label);
            ValidatePpeXChecklist(label);
        }
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
                                    expectedStage == TrainingStage.LeakResponse ||
                                    string.Equals(
                                        NormalizePath(app.SceneFlow.CurrentContentScene.path),
                                        NormalizePath(SceneFlowController.StartScenePath),
                                        StringComparison.OrdinalIgnoreCase)
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
        else if (expectedStage == TrainingStage.LeakResponse)
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

        ValidateLeakProgressionActivation(label, leakScene, shouldBeActive, app);
        if (expectedStage == TrainingStage.LeakResponse)
            ValidateLeakEntrySpawn(label, leakScene, app);
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
        ValidatePreEntryXDoesNotRestartNarration(label, preEntryScene, controller, adapter);
    }

    static void ValidatePreEntryXDoesNotRestartNarration(
        string label,
        Scene preEntryScene,
        EntrySafetyScenarioController controller,
        EntrySafetyInputAdapter adapter)
    {
        var callback = typeof(EntrySafetyInputAdapter).GetMethod(
            "OnSharedInputPerformed",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Require(callback != null, $"{label}: PreEntry shared-input callback is missing.");

        var stepBefore = controller.StateMachine != null
            ? controller.StateMachine.CurrentStep
            : default;
        var sources = UnityEngine.Object.FindObjectsByType<AudioSource>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        var playingSources = new List<AudioSource>();
        var samplesBefore = new List<int>();
        for (var i = 0; i < sources.Length; i++)
        {
            if (sources[i] == null || sources[i].gameObject.scene != preEntryScene || !sources[i].isPlaying)
                continue;
            playingSources.Add(sources[i]);
            samplesBefore.Add(sources[i].timeSamples);
        }

        for (var press = 0; press < 3; press++)
            callback.Invoke(adapter, new object[] { SharedXRHand.Left, SharedXRControl.PrimaryButton });

        Require(controller.StateMachine == null || controller.StateMachine.CurrentStep.Equals(stepBefore),
            $"{label}: PreEntry X changed the current Scenario step.");
        for (var i = 0; i < playingSources.Count; i++)
        {
            Require(playingSources[i].isPlaying && playingSources[i].timeSamples >= samplesBefore[i],
                $"{label}: PreEntry X restarted or stopped active TTS '{playingSources[i].name}'.");
        }
        report.Add($"PASS {label}: Left X x3 kept Scenario step and active TTS/subtitle playback position.");
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

        var audioSources = UnityEngine.Object.FindObjectsByType<AudioSource>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (var i = 0; i < audioSources.Length; i++)
        {
            if (audioSources[i] == null || audioSources[i].gameObject.scene != preEntryScene)
                continue;
            Require(!audioSources[i].isPlaying,
                $"{label}: PreEntry TTS '{audioSources[i].name}' continued after LeakResponse handoff.");
        }

        var barriers = UnityEngine.Object.FindObjectsByType<EntrySafetyNoAccessOverlayController>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        var barrierCount = 0;
        for (var i = 0; i < barriers.Length; i++)
        {
            if (barriers[i] == null || barriers[i].gameObject.scene != preEntryScene)
                continue;
            barrierCount++;
            var boundary = barriers[i].PhysicalBoundaryCollider;
            Require(!barriers[i].IsCleared && barriers[i].gameObject.activeInHierarchy &&
                    boundary is BoxCollider box && box.enabled && !box.isTrigger &&
                    box.bounds.size.x >= 15.5f && box.bounds.size.z >= 0.3f,
                $"{label}: PreEntry doorway was not physically re-closed for LeakResponse.");
        }
        Require(barrierCount == 1,
            $"{label}: PreEntry return-boundary count={barrierCount}, expected=1.");
        report.Add($"PASS {label}: PreEntry handoff boundary is visible and physically closed.");
    }

    static void ValidateLeakProgressionActivation(
        string label,
        Scene leakScene,
        bool expectedActive,
        AppBootstrap app)
    {
        var absorptions = UnityEngine.Object.FindObjectsByType<HClAbsorptionVisualController>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        var valves = UnityEngine.Object.FindObjectsByType<ValveWheelClosureController>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        var bagHandlers = UnityEngine.Object.FindObjectsByType<P102SharedBagItemHandler>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        var wasteRuntimes = UnityEngine.Object.FindObjectsByType<P102WasteContainerRuntime>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        var tongsRuntimes = UnityEngine.Object.FindObjectsByType<P102RecoveryTongsRuntime>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        var absorptionCount = 0;
        var valveCount = 0;
        var bagHandlerCount = 0;
        var wasteRuntimeCount = 0;
        var tongsRuntimeCount = 0;
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
            var grab = valves[i].GetComponent<XRGrabInteractable>();
            var transformer = valves[i].GetComponent<ValveWheelGrabTransformer>();
            Require(grab != null && transformer != null,
                $"{label}: Leak valve grab/rotation transformer is missing.");
            Require(grab.selectMode == InteractableSelectMode.Multiple,
                $"{label}: Leak valve is not configured for the transformer-backed Multiple select path.");
            Require(grab.startingSingleGrabTransformers.Count == 0,
                $"{label}: Leak valve still has a stale Single transformer registration.");
            var transformerRegistered = false;
            for (var transformerIndex = 0;
                 transformerIndex < grab.startingMultipleGrabTransformers.Count;
                 transformerIndex++)
            {
                transformerRegistered |= ReferenceEquals(
                    grab.startingMultipleGrabTransformers[transformerIndex], transformer);
            }
            Require(transformerRegistered,
                $"{label}: Leak valve rotation transformer is not registered in the Multiple list.");
            var rimColliderCount = 0;
            for (var colliderIndex = 0; colliderIndex < grab.colliders.Count; colliderIndex++)
            {
                if (grab.colliders[colliderIndex] is CapsuleCollider rim && rim.enabled && rim.isTrigger &&
                    rim.name.StartsWith("RimCollider_", StringComparison.Ordinal))
                {
                    rimColliderCount++;
                }
            }
            Require(rimColliderCount == 10 && grab.colliders.Count == 10 &&
                    grab.attachTransform != null && grab.attachTransform.name == "CenterShaftAttach",
                $"{label}: Leak valve Grab does not cover all ten wheel-rim segments.");
        }
        for (var i = 0; i < bagHandlers.Length; i++)
        {
            if (bagHandlers[i] == null || bagHandlers[i].gameObject.scene != leakScene)
                continue;
            bagHandlerCount++;
            Require(bagHandlers[i].enabled == expectedActive,
                $"{label}: Leak Y bag handler enabled={bagHandlers[i].enabled}, expected={expectedActive}.");
        }
        for (var i = 0; i < wasteRuntimes.Length; i++)
        {
            var waste = wasteRuntimes[i];
            if (waste == null || waste.gameObject.scene != leakScene)
                continue;
            wasteRuntimeCount++;
            Require(waste.IsConfigured && waste.LidHandleCollider != null,
                $"{label}: Waste-container lid handle Grab is not configured.");
            var lidGrab = waste.LidHandleCollider.GetComponentInParent<XRGrabInteractable>(true);
            Require(lidGrab != null && lidGrab.colliders.Count == 1 &&
                    ReferenceEquals(lidGrab.colliders[0], waste.LidHandleCollider) &&
                    lidGrab.useDynamicAttach && !lidGrab.matchAttachRotation &&
                    lidGrab.snapToColliderVolume,
                $"{label}: Waste-container lid does not use the handle-only dynamic Grab path.");
        }
        for (var i = 0; i < tongsRuntimes.Length; i++)
        {
            var tongs = tongsRuntimes[i];
            if (tongs == null || tongs.gameObject.scene != leakScene)
                continue;
            tongsRuntimeCount++;
            Require(tongs.IsConfigured && tongs.HandleGrabCollider != null,
                $"{label}: Recovery-tongs handle Grab is not configured.");
            var tongsGrab = tongs.HandleGrabCollider.GetComponentInParent<XRGrabInteractable>(true);
            Require(tongsGrab != null && tongsGrab.colliders.Count == 1 &&
                    ReferenceEquals(tongsGrab.colliders[0], tongs.HandleGrabCollider) &&
                    tongsGrab.useDynamicAttach && !tongsGrab.matchAttachRotation &&
                    tongsGrab.snapToColliderVolume,
                $"{label}: Recovery tongs do not use the handle-only dynamic Grab path.");
        }

        Require(absorptionCount == 1,
            $"{label}: Leak absorption controller count={absorptionCount}, expected=1.");
        Require(valveCount == 2,
            $"{label}: Leak valve controller count={valveCount}, expected=2.");
        if (expectedActive)
            report.Add($"PASS {label}: both valve Grabs cover all ten wheel-rim segments.");
        var expectedBagHandlerCount = expectedActive ? 1 : 0;
        Require(bagHandlerCount == expectedBagHandlerCount,
            $"{label}: Leak Y bag handler count={bagHandlerCount}, expected={expectedBagHandlerCount}.");
        var expectedRecoveryRuntimeCount = expectedActive ? 1 : 0;
        Require(wasteRuntimeCount == expectedRecoveryRuntimeCount,
            $"{label}: Waste runtime count={wasteRuntimeCount}, expected={expectedRecoveryRuntimeCount}.");
        Require(tongsRuntimeCount == expectedRecoveryRuntimeCount,
            $"{label}: Recovery-tongs runtime count={tongsRuntimeCount}, expected={expectedRecoveryRuntimeCount}.");

        if (expectedActive)
        {
            report.Add($"PASS {label}: lid and tongs use handle-only dynamic Grabs.");
            ValidateLeakBagYRetrieval(label, app, bagHandlers, leakScene);
        }
    }

    static void ValidateLeakBagYRetrieval(
        string label,
        AppBootstrap app,
        P102SharedBagItemHandler[] handlers,
        Scene leakScene)
    {
        P102SharedBagItemHandler handler = null;
        for (var i = 0; i < handlers.Length; i++)
        {
            if (handlers[i] != null && handlers[i].gameObject.scene == leakScene)
            {
                handler = handlers[i];
                break;
            }
        }

        Require(handler != null && app.Inventory != null && app.SharedEquipment != null,
            $"{label}: Leak bag retrieval runtime is incomplete.");
        var narration = GetPrivateField<P102LeakNarrationController>(handler, "narration");
        var sockGroup = GetPrivateField<PreplacedGhostInstallGroup>(handler, "sockGroup");
        var sockSource = GetPrivateField<XRGrabInteractable>(handler, "sockSource");
        var padSource = GetPrivateField<XRGrabInteractable>(handler, "padSource");
        var cueCallback = typeof(P102SharedBagItemHandler).GetMethod(
            "OnCueCompleted",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var inputCallback = typeof(P102SharedBagItemHandler).GetMethod(
            "OnSharedInputPerformed",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Require(narration != null && sockGroup != null && sockSource != null && padSource != null &&
                cueCallback != null && inputCallback != null,
            $"{label}: Leak Y input/state references are incomplete.");
        Require(!sockSource.gameObject.activeInHierarchy && !padSource.gameObject.activeInHierarchy,
            $"{label}: Authored Sock/Pad supplies are visible before response-bag retrieval.");

        var leftHand = app.SharedEquipment.LeftController;
        Require(leftHand != null && app.SharedEquipment.RetrievalZone != null,
            $"{label}: Shared bag left-hand retrieval zone is unavailable.");
        var savedPosition = leftHand.position;
        try
        {
            leftHand.position = app.SharedEquipment.RetrievalZone.bounds.center;
            Require(app.SharedEquipment.IsLeftHandInRetrievalZone(),
                $"{label}: Left hand could not enter the shared bag retrieval zone.");

            narration.StopAll();
            sockSource.enabled = true;
            cueCallback.Invoke(handler, new object[] { P102LeakNarrationCue.AbsorbentSockPlacementActivated });
            inputCallback.Invoke(handler, new object[] { SharedXRHand.Left, SharedXRControl.SecondaryButton });
            var sockBody = sockSource.GetComponent<Rigidbody>();
            Require(app.Inventory.IsDeployed(PersistentEquipmentInventory.AbsorbentSock) &&
                    sockSource.gameObject.activeInHierarchy &&
                    sockSource.name == "INT_AbsorbentSock_Straight_01" &&
                    ReferenceEquals(handler.PresentedSource, sockSource) &&
                    ReferenceEquals(sockSource.transform.parent,
                        app.SharedEquipment.RetrievalAnchor) &&
                    Vector3.Distance(sockSource.transform.position,
                        app.SharedEquipment.RetrievalAnchor.position) < 0.22f &&
                    sockBody != null && sockBody.isKinematic && !sockBody.useGravity,
                $"{label}: Left Y did not present the existing Sock stably at the bag mouth.");
            Require(app.Inventory.TryRecall(PersistentEquipmentInventory.AbsorbentSock),
                $"{label}: Sock runtime regression cleanup failed.");

            Require(sockGroup.TryInstallAll("Sock", out var installedSockCount) && installedSockCount > 0,
                $"{label}: Sock state could not advance to the Pad retrieval gate.");
            narration.StopAll();
            padSource.enabled = true;
            cueCallback.Invoke(handler, new object[] { P102LeakNarrationCue.AbsorbentPadPlacementActivated });
            inputCallback.Invoke(handler, new object[] { SharedXRHand.Left, SharedXRControl.SecondaryButton });
            var padBody = padSource.GetComponent<Rigidbody>();
            Require(app.Inventory.IsDeployed(PersistentEquipmentInventory.AbsorbentPad) &&
                    padSource.gameObject.activeInHierarchy &&
                    padSource.name == "INT_AbsorbentPad_01" &&
                    ReferenceEquals(handler.PresentedSource, padSource) &&
                    ReferenceEquals(padSource.transform.parent,
                        app.SharedEquipment.RetrievalAnchor) &&
                    Vector3.Distance(padSource.transform.position,
                        app.SharedEquipment.RetrievalAnchor.position) < 0.22f &&
                    padBody != null && padBody.isKinematic && !padBody.useGravity,
                $"{label}: Left Y did not present the existing Pad stably at the bag mouth after Sock completion.");
            Require(app.Inventory.TryRecall(PersistentEquipmentInventory.AbsorbentPad),
                $"{label}: Pad runtime regression cleanup failed.");
            report.Add(
                $"PASS {label}: Left Y presents the authored Sock, then Pad at the bag mouth without dropping them.");
        }
        finally
        {
            leftHand.position = savedPosition;
        }
    }

    static T GetPrivateField<T>(object instance, string fieldName) where T : class
    {
        if (instance == null)
            return null;
        var field = instance.GetType().GetField(
            fieldName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        return field?.GetValue(instance) as T;
    }

    static void ValidateSharedLocomotion(string label, Transform xrRoot, TrainingStage stage)
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
        var allowLocomotion =
            stage == TrainingStage.PPE ||
            stage == TrainingStage.PreEntry ||
            stage == TrainingStage.LeakResponse;
        var expectedMove = allowLocomotion ? 1 : 0;
        var expectedTurn = allowLocomotion ? 1 : 0;
        Require(CountActive(moveProviders) == expectedMove,
            $"{label}: active shared ContinuousMoveProvider count={CountActive(moveProviders)}, expected={expectedMove}.");
        Require(CountActive(controllerDrivers) == 1,
            $"{label}: active shared CharacterControllerDriver count is not one.");
        Require(CountActive(snapTurnProviders) == expectedTurn,
            $"{label}: active shared SnapTurnProvider count={CountActive(snapTurnProviders)}, expected={expectedTurn}.");

        var moveProvider = xrRoot.GetComponent<ContinuousMoveProvider>();
        var turnProvider = xrRoot.GetComponent<SnapTurnProvider>();
        Require(moveProvider != null && moveProvider.isActiveAndEnabled == allowLocomotion,
            $"{label}: canonical root Move Provider active state does not match Stage requirements.");
        Require(turnProvider != null && turnProvider.isActiveAndEnabled == allowLocomotion,
            $"{label}: canonical root Snap Turn Provider active state does not match Stage requirements.");
        if (allowLocomotion)
        {
            var moveAction = moveProvider.leftHandMoveInput?.inputActionReference?.action;
            var turnAction = turnProvider.rightHandTurnInput?.inputActionReference?.action;
            Require(moveAction != null && moveAction.enabled,
                $"{label}: left-hand Move input action is missing or disabled.");
            Require(turnAction != null && turnAction.enabled,
                $"{label}: right-hand Snap Turn input action is missing or disabled.");
            Require(moveProvider.rightHandMoveInput != null &&
                    moveProvider.rightHandMoveInput.inputSourceMode ==
                    UnityEngine.XR.Interaction.Toolkit.Inputs.Readers.XRInputValueReader.InputSourceMode.Unused,
                $"{label}: right thumbstick is still connected to movement instead of Snap Turn only.");
            Require(turnProvider.leftHandTurnInput != null &&
                    turnProvider.leftHandTurnInput.inputSourceMode ==
                    UnityEngine.XR.Interaction.Toolkit.Inputs.Readers.XRInputValueReader.InputSourceMode.Unused,
                $"{label}: left thumbstick is still connected to turning instead of movement only.");
        }

        if (stage == TrainingStage.LeakResponse)
        {
            var vignette = xrRoot.GetComponentInChildren<TunnelingVignetteController>(true);
            Require(vignette != null && vignette.isActiveAndEnabled,
                $"{label}: LeakResponse comfort vignette is not active.");
            var visibilityGuards = xrRoot.GetComponentsInChildren<ComfortVignetteVisibilityGuard>(true);
            Require(visibilityGuards.Length == 1 && visibilityGuards[0].gameObject == vignette.gameObject,
                $"{label}: comfort vignette visibility guard is missing, duplicated, or attached to the wrong object.");
            Require(vignette.locomotionVignetteProviders.Count == 2 &&
                    ReferenceEquals(vignette.locomotionVignetteProviders[0].locomotionProvider, moveProvider) &&
                    ReferenceEquals(vignette.locomotionVignetteProviders[1].locomotionProvider, turnProvider),
                $"{label}: comfort vignette is not wired to canonical Move/Snap Turn providers.");
        }
    }

    static int CountActive<T>(T[] behaviours) where T : Behaviour
    {
        var count = 0;
        for (var i = 0; i < behaviours.Length; i++)
            if (behaviours[i] != null && behaviours[i].isActiveAndEnabled) count++;
        return count;
    }

    static void ValidatePpeRequiredHighlight(string label)
    {
        var highlights = UnityEngine.Object.FindObjectsByType<PPEOutlineHighlight>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        var requiredCount = 0;
        for (var i = 0; i < highlights.Length; i++)
            if (highlights[i] != null && highlights[i].gameObject.activeInHierarchy &&
                highlights[i].IsRequiredStep) requiredCount++;
        Require(requiredCount == 1,
            $"{label}: active current-required PPE highlight count={requiredCount}, expected=1.");
    }

    static void ValidatePpeXChecklist(string label)
    {
        var checklist = UnityEngine.Object.FindFirstObjectByType<PPEChecklistPanel>(FindObjectsInactive.Include);
        Require(checklist != null, $"{label}: PPE checklist is missing.");
        var callback = typeof(PPEChecklistPanel).GetMethod(
            "OnSharedInputPerformed",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Require(callback != null, $"{label}: PPE X checklist callback is missing.");

        var before = checklist.IsContentVisible;
        callback.Invoke(checklist, new object[] { SharedXRHand.Left, SharedXRControl.PrimaryButton });
        Require(checklist.IsContentVisible != before,
            $"{label}: PPE Left X no longer toggles the checklist.");
        callback.Invoke(checklist, new object[] { SharedXRHand.Left, SharedXRControl.PrimaryButton });
        Require(checklist.IsContentVisible == before,
            $"{label}: PPE checklist did not return to its prior state after the regression check.");
        report.Add($"PASS {label}: Left X checklist toggle regression.");
    }

    static void ValidateBagBodySpaceMotion(string label, AppBootstrap app)
    {
        var equipment = app.SharedEquipment;
        if (equipment.IsUsingStageAnchor)
        {
            Transform stageAnchor = null;
            var contentScene = app.SceneFlow.CurrentContentScene;
            foreach (var root in contentScene.GetRootGameObjects())
            {
                var candidates = root.GetComponentsInChildren<Transform>(true);
                for (var i = 0; i < candidates.Length; i++)
                {
                    if (candidates[i] != null && candidates[i].name == SharedPlayerEquipment.StageBagAnchorName)
                    {
                        stageAnchor = candidates[i];
                        break;
                    }
                }
                if (stageAnchor != null)
                    break;
            }

            Require(stageAnchor != null, $"{label}: Stage bag placement is active without StageBagAnchor.");
            Require(Vector3.Distance(equipment.BagRoot.position, stageAnchor.position) < 0.002f &&
                    Quaternion.Angle(equipment.BagRoot.rotation, stageAnchor.rotation) < 0.1f,
                $"{label}: shared bag does not match StageBagAnchor pose.");
            Require(equipment.BagRoot.gameObject.scene == app.gameObject.scene,
                $"{label}: shared bag was moved into the Stage Scene and would be destroyed on unload.");
            report.Add($"PASS {label}: shared bag uses the fixed StageBagAnchor while remaining App-owned.");
            return;
        }

        var xrRoot = app.XRProfiles.CommonXrOrigin.transform;
        var follower = equipment.BodyAnchor != null
            ? equipment.BodyAnchor.GetComponent<PlayerBodyAnchorFollower>()
            : null;
        Require(follower != null, $"{label}: shared bag body-space follower is missing.");

        var originalRootPosition = xrRoot.position;
        var originalRootRotation = xrRoot.rotation;
        var originalLocalPosition = equipment.BodyAnchor.localPosition;
        var originalCameraLocalPosition = app.MainCamera.transform.localPosition;
        var originalCameraLocalRotation = app.MainCamera.transform.localRotation;
        xrRoot.SetPositionAndRotation(
            originalRootPosition + new Vector3(1.25f, 0f, -0.75f),
            Quaternion.AngleAxis(30f, Vector3.up) * originalRootRotation);
        follower.SnapToTarget();
        Require(Vector3.Distance(equipment.BodyAnchor.localPosition, originalLocalPosition) < 0.002f,
            $"{label}: shared bag local hip offset drifted after locomotion/snap-turn motion.");

        app.MainCamera.transform.localPosition = originalCameraLocalPosition + Vector3.up * 0.35f;
        app.MainCamera.transform.localRotation = Quaternion.Euler(42f, 37f, 0f) * originalCameraLocalRotation;
        follower.SnapToTarget();
        Require(Vector3.Distance(equipment.BodyAnchor.localPosition, originalLocalPosition) < 0.002f &&
                Quaternion.Angle(equipment.BodyAnchor.localRotation, Quaternion.identity) < 0.1f,
            $"{label}: HMD height/pitch/yaw moved the body-fixed shared bag.");

        var bagBodies = equipment.BagRoot.GetComponentsInChildren<Rigidbody>(true);
        for (var i = 0; i < bagBodies.Length; i++)
            Require(bagBodies[i].isKinematic,
                $"{label}: controller contact can still push dynamic bag Rigidbody '{bagBodies[i].name}'.");

        xrRoot.SetPositionAndRotation(originalRootPosition, originalRootRotation);
        app.MainCamera.transform.localPosition = originalCameraLocalPosition;
        app.MainCamera.transform.localRotation = originalCameraLocalRotation;
        follower.SnapToTarget();
        report.Add(
            $"PASS {label}: shared bag stayed in XR-Origin body space across move/turn and HMD height/pitch/yaw changes.");
    }

    static void ValidateLeakEntrySpawn(string label, Scene leakScene, AppBootstrap app)
    {
        Transform spawn = null;
        foreach (var root in leakScene.GetRootGameObjects())
        {
            var candidates = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < candidates.Length; i++)
                if (candidates[i] != null && candidates[i].name == "LeakResponseEntrySpawn") spawn = candidates[i];
        }

        Require(spawn != null && app.MainCamera != null,
            $"{label}: LeakResponse entry spawn or Shared XR camera is missing.");
        var cameraDelta = Vector3.ProjectOnPlane(app.MainCamera.transform.position - spawn.position, Vector3.up);
        var cameraForward = Vector3.ProjectOnPlane(app.MainCamera.transform.forward, Vector3.up).normalized;
        var spawnForward = Vector3.ProjectOnPlane(spawn.forward, Vector3.up).normalized;
        Require(cameraDelta.magnitude < 0.12f && Vector3.Dot(cameraForward, spawnForward) > 0.98f,
            $"{label}: Shared XR Origin did not land at the authored LeakResponse entry pose/facing.");
        report.Add($"PASS {label}: Shared XR Origin spawned at the authored LeakResponse entry inside the closed boundary.");
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
        if (equipment.BodyAnchor != null)
        {
            var follower = equipment.BodyAnchor.GetComponent<PlayerBodyAnchorFollower>();
            Require(follower != null, $"{label}: shared bag body-space follower is missing.");
            var cameraLocal = equipment.BodyAnchor.parent.InverseTransformPoint(
                app.MainCamera.transform.position);
            var offset = follower.LocalPositionOffset;
            var expectedLocalPosition = new Vector3(
                cameraLocal.x + offset.x,
                offset.y,
                cameraLocal.z + offset.z);
            Require(Vector3.Distance(equipment.BodyAnchor.localPosition, expectedLocalPosition) < 0.002f,
                $"{label}: shared bag body anchor is not at the configured left-hip offset from the tracked player.");
        }
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
        var expectedStandard = stage == TrainingStage.None || stage == TrainingStage.Complete ? 2 : 0;
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
        var expectedUi = stage == TrainingStage.None || stage == TrainingStage.Complete ||
                         (requirements != null && requirements.UIInteraction);
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
            Require(rays[i].enableUIInteraction == expectedUi,
                $"{label}: standard ray UI interaction does not match the active screen requirements.");
            if (expectedUi)
                ValidateButtonReader(label, rays[i].name, "UI Press", rays[i].uiPressInput);
        }

        for (var i = 0; i < nearFar.Length; i++)
        {
            Require(nearFar[i].transform.IsChildOf(commonRoot),
                $"{label}: Near/Far is outside the common XR rig: {nearFar[i].name}.");
            Require(nearFar[i].GetComponent<SharedXRAimPoseDriver>() != null,
                $"{label}: Near/Far has no shared aim-pose driver.");
            var lineVisualRoot = nearFar[i].transform.Find("LineVisual");
            var curveVisual = lineVisualRoot != null
                ? lineVisualRoot.GetComponent<CurveVisualController>()
                : null;
            Require(curveVisual != null,
                $"{label}: Near/Far has no CurveVisualController: {nearFar[i].name}.");
            if (requirements != null && requirements.FarInteraction)
            {
                Require(curveVisual.lineRenderer != null,
                    $"{label}: Near/Far CurveVisualController has no LineRenderer: {nearFar[i].name}.");
                Require(curveVisual.curveInteractionDataProvider != null,
                    $"{label}: Near/Far CurveVisualController has no data provider: {nearFar[i].name}.");
            }
            else
            {
                Require(!lineVisualRoot.gameObject.activeInHierarchy && !curveVisual.isActiveAndEnabled,
                    $"{label}: far pointer visual is active in a near-only Stage: {nearFar[i].name}.");
            }
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
