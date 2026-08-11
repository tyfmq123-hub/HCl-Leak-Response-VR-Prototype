#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using HClTraining.App;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

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
                    app.ScenarioFlow.CompleteCurrentStage();
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

        var origins = UnityEngine.Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Require(origins.Length == 1, $"{label}: active XR Origin count={origins.Length}.");

        var cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        var activeMainCameras = 0;
        for (var i = 0; i < cameras.Length; i++)
            if (cameras[i].enabled && (cameras[i].CompareTag("MainCamera") || cameras[i].name == "Main Camera")) activeMainCameras++;
        Require(activeMainCameras == 1, $"{label}: active Main Camera count={activeMainCameras}.");

        var listeners = UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        var activeListeners = 0;
        for (var i = 0; i < listeners.Length; i++) if (listeners[i].enabled) activeListeners++;
        Require(activeListeners == 1, $"{label}: active AudioListener count={activeListeners}.");

        var systems = UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Require(systems.Length == 1, $"{label}: active EventSystem count={systems.Length}.");

        var contentScenes = 0;
        for (var i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (scene.isLoaded && scene.path != SceneFlowController.AppScenePath)
                contentScenes++;
        }
        Require(contentScenes == 1, $"{label}: simultaneously loaded content Scenes={contentScenes}.");
        report.Add($"PASS {label}: XR=1, MainCamera=1, AudioListener=1, EventSystem=1, ContentScene=1");
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
        Unhook();
        if (Application.isBatchMode)
            EditorApplication.Exit(success ? 0 : 2);
    }
}
#endif
