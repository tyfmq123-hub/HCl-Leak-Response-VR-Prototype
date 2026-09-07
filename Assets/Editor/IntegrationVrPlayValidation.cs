using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HClTraining.App;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRProject.LeakResponse;
using VRProject.LeakResponse.Narration;

/// <summary>Opt-in PlayMode regression harness. Uses the real App-loaded scene and XRI
/// selection/release callbacks. Cue gating is injected explicitly; this is not a headset test.</summary>
[InitializeOnLoad]
public static class IntegrationVrPlayValidation
{
    const string ActiveKey = "HCl.VrRegression.Active";
    const string Folder = "Logs/VRRegression";
    static int phase;
    static double phaseTime;
    static readonly StringBuilder report = new StringBuilder();
    static P102WasteContainerRuntime waste;
    static P102RecoveryTongsRuntime tongs;
    static XRGrabInteractable lidGrab;
    static XRRayInteractor hand;
    static XRInteractionManager manager;
    static Transform lid;
    static Transform bin;
    static Vector3 closedPosition;
    static Quaternion closedRotation;
    static int buttonFrames;
    static bool passed;

    static IntegrationVrPlayValidation()
    {
        if (SessionState.GetBool(ActiveKey, false)) Hook();
    }
    public static void Run()
    {
        Directory.CreateDirectory(Folder);
        SessionState.SetBool(ActiveKey, true);
        report.Clear();
        Hook();
        EditorSceneManager.OpenScene(SceneFlowController.AppScenePath);
        EditorApplication.EnterPlaymode();
    }
    static void Hook()
    {
        phase = 0;
        phaseTime = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= Changed;
        EditorApplication.playModeStateChanged += Changed;
    }
    static void Tick()
    {
        if (!EditorApplication.isPlaying || !SessionState.GetBool(ActiveKey, false)) return;
        try
        {
            if (EditorApplication.timeSinceStartup - phaseTime > 90) throw new Exception("Phase timeout: " + phase);
            var app = AppBootstrap.Instance;
            if (app == null || app.SceneFlow == null || app.SceneFlow.IsTransitioning) return;
            switch (phase)
            {
                case 0:
                    if (UnityEngine.SceneManagement.SceneManager.GetSceneByPath(SceneFlowController.TitleScenePath).isLoaded)
                    {
                        Check(app.ScenarioFlow.StartTraining(TrainingMode.LeakResponseOnly), "Start LeakResponse through App");
                        Next();
                    }
                    break;
                case 1:
                    waste = UnityEngine.Object.FindAnyObjectByType<P102WasteContainerRuntime>();
                    tongs = UnityEngine.Object.FindAnyObjectByType<P102RecoveryTongsRuntime>();
                    if (waste == null || !waste.IsConfigured || tongs == null || Age < 3) return;
                    lid = Field<GameObject>(waste, "lidObject").transform;
                    bin = Field<GameObject>(waste, "bodyObject").transform;
                    closedPosition = Field<Vector3>(waste, "closedPosition");
                    closedRotation = Field<Quaternion>(waste, "closedRotation");
                    DumpScene();
                    Capture("01-runtime-closed");
                    Check(Vector3.Distance(lid.position, closedPosition) < 0.001f, "App runtime lid keeps authored closed position");
                    Check(Quaternion.Angle(lid.rotation, closedRotation) < 0.01f, "App runtime lid keeps authored closed rotation");
                    var drops = UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Exclude)
                        .First(p => p.name == "FallingDrops");
                    report.AppendLine($"Drops actual runtime: rate={drops.emission.rateOverTimeMultiplier}, live={drops.particleCount}");
                    Check(drops.emission.rateOverTimeMultiplier > 0, "Liquid drops still emit after App runtime binding");
                    Field<P102LeakNarrationController>(waste, "narration").StopAll();
                    Invoke(UnityEngine.Object.FindAnyObjectByType<StageLifecycleAdapter>(),
                        "OnLeakCueCompleted", P102LeakNarrationCue.LeakSceneArrival);
                    var handObject = new GameObject("Regression Test Hand");
                    handObject.transform.SetParent(app.MainCamera.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>().transform, false);
                    hand = handObject.AddComponent<XRRayInteractor>();
                    hand.selectInput.inputSourceMode = XRInputButtonReader.InputSourceMode.ManualValue;
                    hand.activateInput.inputSourceMode = XRInputButtonReader.InputSourceMode.ManualValue;
                    hand.selectActionTrigger = XRBaseInputInteractor.InputTriggerType.State;
                    manager = hand.interactionManager;
                    lidGrab = Field<XRGrabInteractable>(waste, "lidGrab");
                    Invoke(waste, "OnCueStarted", P102LeakNarrationCue.ContainerAndLidGhostActivated);
                    hand.transform.position = waste.LidHandleCollider.transform.position;
                    Grip(true);
                    manager.SelectEnter(hand, (IXRSelectInteractable)lidGrab);
                    Check(lidGrab.isSelected, "Lid accepts initial XRI selection");
                    Next();
                    break;
                case 2:
                    if (Age < 0.2) return;
                    Check(lidGrab.isSelected, "Lid selected through XRI");
                    hand.transform.position += Vector3.right * 2f;
                    Next();
                    break;
                case 3:
                    if (Age < 0.2) return;
                    report.AppendLine($"Before missed release: lid={lid.position}, hand={hand.transform.position}, closed={closedPosition}");
                    Grip(false);
                    manager.SelectExit(hand, (IXRSelectInteractable)lidGrab);
                    Next();
                    break;
                case 4:
                    if (Age < 0.6) return;
                    if (Field<bool>(waste, "snappingLid") || Field<Coroutine>(waste, "lidReleaseRoutine") != null) return;
                    Check(lidGrab.enabled, "Lid is ready for retry after missed release (stowed=" + waste.IsLidStowed + ")");
                    Check(Vector3.Distance(lid.position, closedPosition) < 0.001f, "Missed release restores closed pose");
                    hand.transform.position = waste.LidHandleCollider.transform.position;
                    Grip(true);
                    manager.SelectEnter(hand, (IXRSelectInteractable)lidGrab);
                    Next();
                    break;
                case 5:
                    if (Age < 0.2) return;
                    Check(lidGrab.isSelected, "Lid retry retains selection");
                    hand.transform.position += Vector3.down * 0.8f;
                    Next();
                    break;
                case 6:
                    if (Age < 0.2) return;
                    Grip(false);
                    manager.SelectCancel(hand, (IXRSelectInteractable)lidGrab);
                    Next();
                    break;
                case 7:
                    if (Age < 0.3) return;
                    Check(Vector3.Distance(lid.position, closedPosition) < 0.001f, "Cancelled release cannot strand lid in bin");
                    Capture("02-after-cancel");
                    Invoke(tongs, "EnableRecoveryInteraction");
                    var grab = Field<XRGrabInteractable>(tongs, "grabInteractable");
                    hand.transform.position = Field<Transform>(tongs, "handleTransform").position;
                    Grip(true);
                    manager.SelectEnter(hand, (IXRSelectInteractable)grab);
                    buttonFrames = 0;
                    Next();
                    break;
                case 8:
                    if (Age < 0.2) return;
                    Grip((buttonFrames & 1) == 0);
                    hand.activateInput.manualPerformed = (buttonFrames & 2) == 0;
                    Check(tongs.IsCarryLocked && Field<XRGrabInteractable>(tongs, "grabInteractable").isSelected,
                        "Tongs retain selection on grip/trigger sequence " + buttonFrames);
                    if (++buttonFrames < 20) return;
                    Check(!tongs.TryReturnAtGhost(), "Cannot return tongs before final return cue");
                    Invoke(tongs, "OnCueCompleted", P102LeakNarrationCue.AllSocksRecovered);
                    hand.transform.position += Vector3.right * 2f;
                    Next();
                    break;
                case 9:
                    if (Age < 0.2) return;
                    Check(!tongs.TryReturnAtGhost(), "Grip away from return ghost cannot release tongs");
                    var tongsRoot = Field<GameObject>(tongs, "tongsRoot").transform;
                    var ghost = Field<Transform>(tongs, "tongsGhost");
                    hand.transform.position += ghost.position - tongsRoot.position;
                    Next();
                    break;
                case 10:
                    if (Age < 0.3) return;
                    hand.selectInput.QueueManualState(true, 1f, true, false);
                    Next();
                    break;
                case 11:
                    if (Age < 0.5) return;
                    if (!Field<bool>(tongs, "returned")) return;
                    Check(!Field<XRGrabInteractable>(tongs, "grabInteractable").isSelected,
                        "Fresh physical grip at final ghost returns tongs through Update");
                    Check(hand.selectInput.bypass == null && !tongs.IsCarryLocked, "Tongs return restores original input reader");
                    var flange = UnityEngine.Object.FindAnyObjectByType<P102FlangeGazeRuntime>();
                    Invoke(flange, "OnCueStarted", P102LeakNarrationCue.LeakReductionStarted);
                    Invoke(flange, "OnCueCompleted", P102LeakNarrationCue.LeakReductionStarted);
                    Next();
                    break;
                case 12:
                    if (Age < 0.2) return;
                    var gaze = UnityEngine.Object.FindAnyObjectByType<P102FlangeGazeRuntime>();
                    var ui = gaze.CountdownTransform;
                    Vector3 toward = Vector3.ProjectOnPlane(app.MainCamera.transform.position - ui.position, Vector3.up).normalized;
                    Check(Vector3.Dot(-ui.forward, toward) > 0.99f, "Countdown front faces actual App camera after stage teleport");
                    IntegrationVisualAudit.Render(Folder + "/03-flange-countdown.png", ui.position,
                        toward * 1.4f + Vector3.up * 0.15f);
                    passed = true;
                    report.AppendLine("RESULT PASS (injected cues + actual XRI; no HMD)");
                    Finish();
                    break;
            }
        }
        catch (Exception ex) { report.AppendLine("RESULT FAIL\n" + ex); Finish(); }
    }
    static void Grip(bool held)
    {
        hand.selectInput.manualPerformed = held;
        hand.selectInput.manualValue = held ? 1 : 0;
        if (held) hand.selectInput.manualFramePerformed = Time.frameCount;
        else hand.selectInput.manualFrameCompleted = Time.frameCount;
    }
    static void DumpScene()
    {
        report.AppendLine("Runtime lid " + lid.position.ToString("F5") + " / " + lid.eulerAngles);
        foreach (var b in lid.GetComponentsInChildren<Component>(true))
            report.AppendLine("  Lid component " + b.GetType().Name + " @ " + b.name);
        foreach (var m in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude)
                     .SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct())
        {
            var texture = m.mainTexture;
            if (texture == null) continue;
            var path = AssetDatabase.GetAssetPath(texture);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && !importer.mipmapEnabled && importer.textureType == TextureImporterType.Default)
                report.AppendLine("NO WORLD MIPS " + path + " material=" + m.name);
        }
    }
    static void Capture(string name)
    {
        P102RuntimeUtility.TryGetRendererBounds(bin.gameObject, out var bounds);
        IntegrationVisualAudit.Render(Folder + "/" + name + ".png", bounds.center, new Vector3(0, .55f, -2.1f));
    }
    static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
    static void Invoke(object owner, string name, params object[] args) => owner.GetType().GetMethod(name,
        BindingFlags.Instance | BindingFlags.NonPublic).Invoke(owner, args);
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        report.AppendLine("PASS " + message);
    }
    static double Age => EditorApplication.timeSinceStartup - phaseTime;
    static void Next() { phase++; phaseTime = EditorApplication.timeSinceStartup; }
    static void Finish()
    {
        SessionState.SetBool(ActiveKey + ".Passed", passed);
        File.WriteAllText(Folder + "/runtime-validation.txt", report.ToString());
        EditorApplication.update -= Tick;
        EditorApplication.ExitPlaymode();
    }
    static void Changed(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(ActiveKey, false)) return;
        SessionState.SetBool(ActiveKey, false);
        if (Application.isBatchMode) EditorApplication.Exit(SessionState.GetBool(ActiveKey + ".Passed", false) ? 0 : 2);
    }
}
