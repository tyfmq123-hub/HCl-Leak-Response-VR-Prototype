using System;
using System.IO;
using System.Linq;
using System.Text;
using HClTraining.App;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRProject.LeakResponse.Narration;

/// <summary>Opt-in real-physics regression for the dormant additive Leak scene.
/// Prior tests activated Leak immediately and never waited through PreEntry.</summary>
[InitializeOnLoad]
public static class IntegrationWasteLidLifecycleValidation
{
    const string Key = "HCl.WasteLidLifecycle";
    const string Folder = "Logs/WasteLidLifecycle";
    [Serializable] sealed class AuthoredPose { public Vector3 position; public Quaternion rotation; }
    static readonly StringBuilder report = new StringBuilder();
    static int phase;
    static double since;
    static int fixedFrames;
    static Transform lid;
    static AuthoredPose authored;
    static bool success;

    static IntegrationWasteLidLifecycleValidation()
    {
        if (SessionState.GetBool(Key, false)) Hook();
    }

    public static void Run()
    {
        Directory.CreateDirectory(Folder);
        var scene = EditorSceneManager.OpenScene(SceneFlowController.LeakResponseScenePath);
        var authoredLid = Find(scene, "HazWasteContainer_Lid");
        SessionState.SetString(Key + ".pose", JsonUtility.ToJson(new AuthoredPose
        { position = authoredLid.position, rotation = authoredLid.rotation }));
        SessionState.SetBool(Key, true);
        EditorSceneManager.OpenScene(SceneFlowController.AppScenePath);
        Hook();
        EditorApplication.EnterPlaymode();
    }

    static void Hook()
    {
        phase = 0;
        since = EditorApplication.timeSinceStartup;
        report.Clear();
        authored = JsonUtility.FromJson<AuthoredPose>(SessionState.GetString(Key + ".pose", "{}"));
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= OnPlayMode;
        EditorApplication.playModeStateChanged += OnPlayMode;
    }

    static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying) return;
        try
        {
            if (EditorApplication.timeSinceStartup - since > 90) throw new Exception("Timeout phase " + phase);
            var app = AppBootstrap.Instance;
            if (app == null || app.SceneFlow == null || app.SceneFlow.IsTransitioning) return;
            switch (phase)
            {
                case 0:
                    if (!SceneManager.GetSceneByPath(SceneFlowController.TitleScenePath).isLoaded) return;
                    Require(app.ScenarioFlow.StartTraining(TrainingMode.Full), "Start full course through App");
                    Next();
                    break;
                case 1:
                    if (app.Session.CurrentStage != TrainingStage.PPE) return;
                    var loadout = PPELoadoutPresets.FieldReadyComplete;
                    loadout.source = PPELoadoutSource.PpeSceneResult;
                    PPELoadoutPresets.ReplaceLoadout(loadout);
                    app.Inventory.SyncFromLoadout();
                    app.PPEState.ApplyPersistentState();
                    app.ScenarioFlow.CompleteCurrentStage();
                    Next();
                    break;
                case 2:
                    if (app.Session.CurrentStage != TrainingStage.PreEntry) return;
                    lid = Find(SceneManager.GetSceneByPath(SceneFlowController.LeakResponseScenePath), "HazWasteContainer_Lid");
                    var counter = lid.gameObject.AddComponent<IntegrationWasteLidFixedFrameCounter>();
                    fixedFrames = counter.Count;
                    Record("PreEntry entered");
                    Next();
                    break;
                case 3:
                    if (EditorApplication.timeSinceStartup - since < 6) return;
                    fixedFrames = lid.GetComponent<IntegrationWasteLidFixedFrameCounter>().Count;
                    Record("After dormant wait (6 seconds)");
                    Capture("01-after-preentry-wait.png");
                    Require(fixedFrames >= 100, "At least 100 real FixedUpdate steps before Leak activation");
                    RequirePose("Dormant additive lid remains in authored closed pose");
                    var collisionRoot = Find(SceneManager.GetSceneByPath(SceneFlowController.LeakResponseScenePath),
                        P102WasteContainerPhysics.CollisionRootName);
                    Require(collisionRoot.GetComponentsInChildren<BoxCollider>().Length == 17,
                        "Hollow body has 16 solid walls and a bottom before Leak activation");
                    Require(app.ScenarioFlow.CompletePreEntryAndHandoff(), "Handoff into the already loaded Leak scene");
                    Next();
                    break;
                case 4:
                    if (app.Session.CurrentStage != TrainingStage.LeakResponse) return;
                    if (EditorApplication.timeSinceStartup - since < 3) return;
                    Record("After Leak handoff");
                    RequirePose("Handoff cannot capture a fallen lid as the closed pose");
                    Capture("02-after-leak-handoff.png");
                    var runtime = UnityEngine.Object.FindAnyObjectByType<P102WasteContainerRuntime>();
                    Require(runtime != null && runtime.IsConfigured, "Waste runtime configured");
                    // A later stray physics write must not replace the canonical rest pose.
                    lid.position += Vector3.down * 0.5f;
                    Next();
                    break;
                case 5:
                    if (EditorApplication.timeSinceStartup - since < 0.5) return;
                    RequirePose("Unheld lid recovers from a stray position write");
                    success = true;
                    report.AppendLine("RESULT PASS (real FixedUpdate + injected Stage completion; no HMD)");
                    Finish();
                    break;
            }
        }
        catch (Exception e)
        {
            report.AppendLine("RESULT FAIL: " + e);
            success = false;
            Finish();
        }
    }

    static void Next() { phase++; since = EditorApplication.timeSinceStartup; }
    static Transform Find(Scene scene, string name) => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<Transform>(true)).First(t => t.name == name);
    static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        report.AppendLine("PASS " + message);
    }
    static void RequirePose(string message) => Require(
        Vector3.Distance(lid.position, authored.position) < 0.002f &&
        Quaternion.Angle(lid.rotation, authored.rotation) < 0.05f, message);
    static void Record(string label)
    {
        var rb = lid.GetComponent<Rigidbody>();
        report.AppendLine($"{label}: authored={authored.position:F5}, actual={lid.position:F5}, " +
            $"delta={Vector3.Distance(lid.position, authored.position):F5}m, " +
            $"gravity={rb.useGravity}, kinematic={rb.isKinematic}, fixedSteps={fixedFrames}");
    }
    static void Capture(string file)
    {
        var scene = SceneManager.GetSceneByPath(SceneFlowController.LeakResponseScenePath);
        var body = Find(scene, "HazWasteContainer_Body");
        IntegrationVisualAudit.Render(Folder + "/" + file, body.GetComponent<Renderer>().bounds.center,
            new Vector3(0, 0.25f, -2.7f));
    }
    static void Finish()
    {
        File.WriteAllText(Folder + "/result.txt", report.ToString());
        SessionState.SetBool(Key, false);
        EditorApplication.update -= Tick;
        EditorApplication.ExitPlaymode();
    }
    static void OnPlayMode(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode && !SessionState.GetBool(Key, false))
        {
            EditorApplication.playModeStateChanged -= OnPlayMode;
            if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 2);
        }
    }
}

public sealed class IntegrationWasteLidFixedFrameCounter : MonoBehaviour
{
    public int Count { get; private set; }
    void FixedUpdate() => Count++;
}
